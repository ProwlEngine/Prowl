using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Assets embedded in the runtime assembly, with deterministic GUIDs so they are referenced like any other asset.
/// The asset database asks here before its backend, and never unloads them.
/// </summary>
public static class BuiltInAssets
{
    public struct BuiltInEntry
    {
        public Guid Guid;
        public string Name;
        public string Path; // e.g. "$Default:Standard"
        public Type AssetType;
        public Func<Asset> Loader;
    }

    private static readonly Dictionary<Guid, BuiltInEntry> _entries = new();
    private static readonly object _initLock = new();
    private static volatile bool _initialized;

    public static IReadOnlyDictionary<Guid, BuiltInEntry> Entries
    {
        get
        {
            Initialize();
            return _entries;
        }
    }

    /// <summary>
    /// Generate a deterministic GUID from a built-in asset path.
    /// Always produces the same GUID for the same path string.
    /// </summary>
    public static Guid DeterministicGuid(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        // Set version bits to indicate this is a name-based UUID (version 5-like)
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Initialize and register all built-in assets. Safe to call multiple times.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            RegisterAll();
            _initialized = true;
        }
    }

    private static void RegisterAll()
    {
        // Default shaders: precompiled blobs (Tools/DefaultShaderCompiler), raw parse from an embedded
        // resource, same as every other default type. Most DefaultShader entries have no source .shader
        // file yet, so ParseDefault returning null is expected and just skipped.
        foreach (DefaultShader s in Enum.GetValues<DefaultShader>())
        {
            var shader = s;
            if (!EmbeddedResources.Exists($"Assets/Defaults/Compiled/{shader}.shaderblob"))
                continue;

            Register($"$Default:Shader/{shader}", shader.ToString(), typeof(Shader),
                () => Shader.ParseDefault(shader));
        }

        // Default meshes (parsed directly from embedded OBJ files)
        foreach (DefaultModel m in Enum.GetValues<DefaultModel>())
        {
            var model = m;
            string fileName = model switch
            {
                DefaultModel.Cube => "Cube.obj",
                DefaultModel.Sphere => "Sphere.obj",
                DefaultModel.Cylinder => "Cylinder.obj",
                DefaultModel.Plane => "Plane.obj",
                _ => null
            };
            if (fileName == null) continue;

            // The model itself, so the ID DefaultModels.Load stamps on it actually resolves. Only its
            // mesh was registered before, leaving every default model carrying a dangling AssetID.
            Register($"$Default:Model/{model}", model.ToString(), typeof(PrefabAsset),
                () => DefaultModels.Load(model));

            Register($"$Default:Model/{model}/Mesh/0", model.ToString(), typeof(Mesh),
                () =>
                {
                    using var stream = EmbeddedResources.GetStream($"Assets/Defaults/{fileName}");
                    // Built-in primitives always get lightmap UV2 so they're lightmappable out of the box.
                    var importResult = new AssetImporting.ModelImporter().Import(stream, fileName, new AssetImporting.ModelImporterSettings() { RecalculateNormals = true, GenerateNormals = true, GenerateSmoothNormals = true, CalculateTangentSpace = true, GenerateLightmapUVs = true });
                    return importResult.Meshes.Count > 0 ? importResult.Meshes[0] : new Mesh { Name = model.ToString() };
                });
        }

        // Materials register the raw parse so LoadDefault routes through this cache.
        foreach (DefaultMaterial m in Enum.GetValues<DefaultMaterial>())
        {
            var mat = m;
            Register($"$Default:Material/{mat}", mat.ToString(), typeof(Material),
                () => Material.ParseDefault(mat));
        }

        // Textures same: raw load, shared instance.
        foreach (DefaultTexture t in Enum.GetValues<DefaultTexture>())
        {
            var tex = t;
            Register($"$Default:Texture/{tex}", tex.ToString(), typeof(Texture2D),
                () => Texture2D.ParseDefault(tex));
        }

        // Sprites: built from a default texture, shared instance.
        foreach (DefaultSprite sp in Enum.GetValues<DefaultSprite>())
        {
            var sprite = sp;
            Register($"$Default:Sprite/{sprite}", sprite.ToString(), typeof(Sprite),
                () => Sprite.ParseDefault(sprite));
        }

        // Fonts: raw load, shared instance (fallback for UI text with no font assigned).
        foreach (DefaultFont f in Enum.GetValues<DefaultFont>())
        {
            var font = f;
            Register($"$Default:Font/{font}", font.ToString(), typeof(FontAsset),
                () => FontAsset.ParseDefault(font));
        }
    }

    private static void Register(string path, string name, Type type, Func<Asset> loader)
    {
        var guid = DeterministicGuid(path);
        _entries[guid] = new BuiltInEntry
        {
            Guid = guid,
            Name = name,
            Path = path,
            AssetType = type,
            Loader = loader,
        };
    }

    /// <summary>Builds a built-in asset into the staging copy the database fills its stable object from.</summary>
    internal static bool ReadContent(Guid guid, Asset staging)
    {
        if (!_entries.TryGetValue(guid, out var entry)) return false;

        try
        {
            Asset built = entry.Loader();
            if (built.GetType() != staging.GetType())
            {
                Debug.LogError($"Built-in asset '{entry.Path}' built a {built.GetType().Name}, not the {staging.GetType().Name} it is registered as.");
                return false;
            }
            AssetContent.Move(built, staging);
            GC.SuppressFinalize(built);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load built-in asset '{entry.Path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>The built-in asset with this GUID, loaded.</summary>
    public static T Load<T>(Guid guid) where T : Asset
        => AssetDatabase.Load<T>(guid) ?? throw new InvalidOperationException($"No built-in {typeof(T).Name} has the GUID {guid}.");

    /// <summary>
    /// Find all built-in assets assignable to the given type.
    /// </summary>
    public static IEnumerable<(Guid guid, string name, string path, Type type)> FindAllOfType(Type type)
    {
        foreach (var (guid, entry) in Entries)
        {
            if (type.IsAssignableFrom(entry.AssetType))
                yield return (guid, entry.Name, entry.Path, entry.AssetType);
        }
    }

    /// <summary>Check if a GUID corresponds to a built-in asset.</summary>
    public static bool IsBuiltIn(Guid guid) => Entries.ContainsKey(guid);

    /// <summary>
    /// Get the deterministic GUID for a specific default shader.
    /// </summary>
    public static Guid GuidFor(DefaultShader shader) => DeterministicGuid($"$Default:Shader/{shader}");

    /// <summary>
    /// Get the deterministic GUID for a specific default model. Its mesh is a separate
    /// sub-asset - see <see cref="GuidForMesh"/>.
    /// </summary>
    public static Guid GuidFor(DefaultModel model) => DeterministicGuid($"$Default:Model/{model}");

    /// <summary>
    /// Get the deterministic GUID for a specific default texture.
    /// </summary>
    public static Guid GuidFor(DefaultMaterial material) => DeterministicGuid($"$Default:Material/{material}");

    public static Guid GuidFor(DefaultTexture tex) => DeterministicGuid($"$Default:Texture/{tex}");

    /// <summary>Get the deterministic GUID for a specific default sprite.</summary>
    public static Guid GuidFor(DefaultSprite sprite) => DeterministicGuid($"$Default:Sprite/{sprite}");

    /// <summary>Deterministic GUID for a built-in default font.</summary>
    public static Guid GuidFor(DefaultFont font) => DeterministicGuid($"$Default:Font/{font}");

    /// <summary>
    /// Get the deterministic GUID for a default model's first mesh.
    /// </summary>
    public static Guid GuidForMesh(DefaultModel model, int meshIndex = 0) => DeterministicGuid($"$Default:Model/{model}/Mesh/{meshIndex}");
}
