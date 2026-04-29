using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Registry of built-in assets (embedded in the runtime assembly).
/// Provides deterministic GUIDs so they can be referenced like any other asset.
/// Works at both runtime and editor level.
/// </summary>
public static class BuiltInAssets
{
    public struct BuiltInEntry
    {
        public Guid Guid;
        public string Name;
        public string Path; // e.g. "$Default:Standard"
        public Type AssetType;
        public Func<EngineObject> Loader;
    }

    private static readonly Dictionary<Guid, BuiltInEntry> _entries = new();
    private static readonly Dictionary<Guid, EngineObject> _cache = new();
    private static bool _initialized;

    public static IReadOnlyDictionary<Guid, BuiltInEntry> Entries => _entries;

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
        _initialized = true;

        // Shaders loader calls the raw parse so LoadDefault can route through the cache
        // without recursing through itself.
        foreach (DefaultShader s in Enum.GetValues<DefaultShader>())
        {
            var shader = s;
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
                DefaultModel.SkyDome => "SkyDome.obj",
                _ => null
            };
            if (fileName == null) continue;

            Register($"$Default:Model/{model}/Mesh/0", model.ToString(), typeof(Mesh),
                () =>
                {
                    using var stream = EmbeddedResources.GetStream($"Assets/Defaults/{fileName}");
                    var mesh = AssetImporting.Obj.ObjImporter.ParseMeshOnly(stream, model.ToString(), new AssetImporting.ModelImporterSettings() { RecalculateNormals = true, GenerateNormals = true, GenerateSmoothNormals = true, CalculateTangentSpace = true });
                    mesh.AssetID = GuidForMesh(model);
                    mesh.AssetPath = $"$Default:Mesh/{model}";
                    return mesh;
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
    }

    private static void Register(string path, string name, Type type, Func<EngineObject> loader)
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

    /// <summary>
    /// Try to resolve a built-in asset by GUID. Returns null if not a built-in asset.
    /// Caches loaded assets.
    /// </summary>
    public static EngineObject? Get(Guid guid)
    {
        if (!_entries.TryGetValue(guid, out var entry))
            return null;

        if (_cache.TryGetValue(guid, out var cached) && cached != null && !cached.IsDisposed)
            return cached;

        try
        {
            var obj = entry.Loader();
            if (obj != null)
            {
                obj.AssetID = guid;
                obj.AssetPath = entry.Path;
                obj.Name = entry.Name;
                _cache[guid] = obj;
            }
            return obj;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load built-in asset '{entry.Path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Find all built-in assets assignable to the given type.
    /// </summary>
    public static IEnumerable<(Guid guid, string name, string path, Type type)> FindAllOfType(Type type)
    {
        foreach (var (guid, entry) in _entries)
        {
            if (type.IsAssignableFrom(entry.AssetType))
                yield return (guid, entry.Name, entry.Path, entry.AssetType);
        }
    }

    /// <summary>Check if a GUID corresponds to a built-in asset.</summary>
    public static bool IsBuiltIn(Guid guid) => _entries.ContainsKey(guid);

    /// <summary>
    /// Get the deterministic GUID for a specific default shader.
    /// </summary>
    public static Guid GuidFor(DefaultShader shader) => DeterministicGuid($"$Default:Shader/{shader}");

    /// <summary>
    /// Get the deterministic GUID for a specific default model.
    /// </summary>
    public static Guid GuidFor(DefaultModel model) => DeterministicGuid($"$Default:Model/{model}");

    /// <summary>
    /// Get the deterministic GUID for a specific default texture.
    /// </summary>
    public static Guid GuidFor(DefaultMaterial material) => DeterministicGuid($"$Default:Material/{material}");

    public static Guid GuidFor(DefaultTexture tex) => DeterministicGuid($"$Default:Texture/{tex}");

    /// <summary>
    /// Get the deterministic GUID for a default model's first mesh.
    /// </summary>
    public static Guid GuidForMesh(DefaultModel model, int meshIndex = 0) => DeterministicGuid($"$Default:Model/{model}/Mesh/{meshIndex}");
}
