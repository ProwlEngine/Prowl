using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

[ImporterFor(".gltf", ".glb", ".obj", ".fbx")]
public class EditorModelImporter : AssetImporter
{
    // 7: Model became a PrefabAsset, which serializes its tree through a backing field.
    // 8: normals now come from Clay, which splits vertices on hard edges.
    private const int BaseVersion = 10;
    public override int Version => BaseVersion + MeshFeatureRegistry.AggregateVersion;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            // The editor's resolver never decodes another asset's pixel data itself: external
            // textures resolve to the already-imported project asset by path/GUID, and embedded ones
            // are registered as sub-assets. Unconditional, not settings-gated: holds for every
            // editor import.
            var importSettings = new ModelImporterSettings { TextureResolver = new EditorModelTextureResolver(ctx) };
            if (ctx.Settings != null)
            {
                var s = ctx.Settings;
                importSettings.GenerateNormals = !s.TryGet("generateNormals", out var gn) || gn.BoolValue;
                importSettings.GenerateSmoothNormals = !s.TryGet("generateSmoothNormals", out var gsn) || gsn.BoolValue;
                importSettings.SmoothNormalsAngleDeg = s.TryGet("smoothNormalsAngle", out var sna) ? sna.FloatValue : 80f;
                importSettings.RecalculateNormals = s.TryGet("recalculateNormals", out var rn) && rn.BoolValue;
                importSettings.CalculateTangentSpace = !s.TryGet("calculateTangents", out var ct) || ct.BoolValue;
                importSettings.FlipUVs = !s.TryGet("flipUVs", out var fu) || fu.BoolValue;
                importSettings.UnitScale = s.TryGet("unitScale", out var us) ? us.FloatValue : 1.0f;
                importSettings.ImportCameras = !s.TryGet("importCameras", out var ic) || ic.BoolValue;
                importSettings.ImportLights = !s.TryGet("importLights", out var il) || il.BoolValue;
                importSettings.AnimationWrapMode = (AnimationWrapMode)(s.TryGet("animationWrapMode", out var awm) ? awm.IntValue : (int)AnimationWrapMode.Loop);
                // Off by default (slow; some models ship their own UV2). The importer runs the
                // unwrap in its post-process so the baked UV2 is captured before serialization.
                importSettings.GenerateLightmapUVs = s.TryGet("generateLightmapUVs", out var glu) && glu.BoolValue;
            }

            // 1. Import creates live meshes, materials, animations, GO hierarchy (+ UV2 if enabled).
            var importer = new ModelImporter();
            var data = importer.Import(new FileInfo(ctx.AbsolutePath), importSettings);

            // 2. Register sub-assets assigns deterministic GUIDs immediately
            // Order: the model file has no stable per-mesh key of its own, and it is read front to back.
            var meshIdentities = new string[data.Meshes.Count];
            for (int i = 0; i < data.Meshes.Count; i++)
                meshIdentities[i] = ctx.AddSubAsset(data.Meshes[i].Name ?? $"Mesh_{i}", data.Meshes[i], SubAssetIdentity.Order);

            for (int i = 0; i < data.Materials.Count; i++)
                ctx.AddSubAsset(data.Materials[i].Name ?? $"Material_{i}", data.Materials[i], SubAssetIdentity.Order);

            for (int i = 0; i < data.Animations.Count; i++)
                ctx.AddSubAsset(data.Animations[i].Name ?? $"Animation_{i}", data.Animations[i], SubAssetIdentity.Order);

            // Note: model-referenced textures (both external and embedded) are already fully
            // resolved by this point - materials carry AssetRefs, and any embedded texture is
            // already registered as a sub-asset - both as side effects of EditorModelTextureResolver
            // running during importer.Import() above.

            // 2b. Generate mesh features (SDF, BVH, Prism, ...) per mesh, registered as sub-assets.
            for (int i = 0; i < data.Meshes.Count; i++)
                MeshFeatureImporter.GenerateAll(data.Meshes[i], ctx.Settings, ctx, meshIdentities[i]);

            // 3. Serialize GO hierarchy sub-assets have correct IDs, AssetRefs serialize as GUIDs.
            //    Tracked (matching SceneImporter/PrefabImporter) so the prefab's own dependency list
            //    reflects what its GameObject hierarchy actually references.
            //    A model is a prefab: dropping one into a scene produces an instance linked back here,
            //    so changing import settings and reimporting updates those instances in place.
            var prefab = new PrefabAsset { Name = ctx.FileName, InstanceType = PrefabInstanceType.Model };
            if (data.RootGO != null)
            {
                // The tree is built fresh from the file on every import, so its identities would be new
                // every time and every instance in the project would lose its overrides on any reimport.
                StabilizeIdentities(data.RootGO);

                var goSerCtx = ImportHelper.CreateTrackingContext(out var goDependencies);
                prefab.GameObjectData = Serializer.Serialize(typeof(object), data.RootGO, goSerCtx);
                foreach (var dep in goDependencies)
                    ctx.AddDependency(dep);
            }

            ctx.SetMainAsset(prefab);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import model: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gives every object identities derived from where it sits rather than from its constructor, so
    /// importing the same file twice produces the same ones.
    /// <para/>
    /// This is what lets an instance survive a reimport. Every override names the object and component it
    /// is on by the identity the asset holds them under, so handing out fresh identities would leave
    /// every instance in the project addressing objects that no longer exist: overrides dropped, objects
    /// below the instance root destroyed and rebuilt, references to them dead. A model is reimported
    /// whenever its file or any of its settings change, so that is otherwise a routine loss.
    /// <para/>
    /// Keying on position means renaming or moving a node reads as a different object and orphans the
    /// overrides on it, which is unavoidable while the source file carries no identities of its own. The
    /// root is keyed as itself rather than by name, since the importer names it after the asset file.
    /// </summary>
    internal static void StabilizeIdentities(GameObject go, string path = "$Root")
    {
        go.SetIdentifier(BuiltInAssets.DeterministicGuid($"$GeneratedPrefab/{path}"));

        var perType = new Dictionary<string, int>();
        foreach (MonoBehaviour component in go.GetComponents<MonoBehaviour>())
        {
            string type = component.GetType().FullName ?? component.GetType().Name;
            perType.TryGetValue(type, out int ordinal);
            perType[type] = ordinal + 1;

            component.Identifier = BuiltInAssets.DeterministicGuid($"$GeneratedPrefab/{path}#{type}#{ordinal}");
        }

        // Siblings can share a name, so the key says which one of those this is.
        var perName = new Dictionary<string, int>();
        foreach (GameObject child in go.Children)
        {
            perName.TryGetValue(child.Name, out int ordinal);
            perName[child.Name] = ordinal + 1;

            StabilizeIdentities(child, ordinal == 0 ? $"{path}/{child.Name}" : $"{path}/{child.Name}[{ordinal}]");
        }
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateNormals"] = new EchoObject(true);
        s["generateSmoothNormals"] = new EchoObject(true);
        s["smoothNormalsAngle"] = new EchoObject(80.0f);
        s["recalculateNormals"] = new EchoObject(false);
        s["calculateTangents"] = new EchoObject(true);
        s["flipUVs"] = new EchoObject(true);
        s["unitScale"] = new EchoObject(1.0f);
        s["importCameras"] = new EchoObject(true);
        s["importLights"] = new EchoObject(true);
        s["animationWrapMode"] = new EchoObject((int)AnimationWrapMode.Loop);
        s["generateLightmapUVs"] = new EchoObject(false);
        MeshFeatureRegistry.PopulateDefaultSettings(s);
        return s;
    }
}

/// <summary>
/// The editor's <see cref="IModelTextureResolver"/>: never decodes another asset's pixel data.
/// An externally referenced texture is resolved purely by path, against the asset database's
/// existing GUID for that file. An embedded texture is registered as a proper sub-asset of the
/// model for the asset database to own and cache (this is the one case that still has to decode -
/// there's no separate file for the asset database to already know about).
/// </summary>
internal sealed class EditorModelTextureResolver : IModelTextureResolver
{
    private readonly ImportContext _ctx;
    private readonly string _assetsRoot;
    private readonly EditorAssetBackend? _db;

    public EditorModelTextureResolver(ImportContext ctx)
    {
        _ctx = ctx;
        // Project.AssetsPath is a plain Path.Combine(RootPath, "Assets"), not run through
        // GetFullPath - sourcePath (below) comes from Clay's own, separately-normalized
        // Path.GetFullPath pipeline. Normalizing both through GetFullPath here means a prefix
        // comparison between them is comparing like with like, even if RootPath itself has a
        // trailing separator or other cosmetic difference GetFullPath would otherwise collapse.
        string assetsRoot = Project.Current?.AssetsPath ?? "";
        _assetsRoot = string.IsNullOrEmpty(assetsRoot) ? "" : Path.GetFullPath(assetsRoot);
        _db = EditorAssetBackend.Instance;
    }

    public AssetRef<Texture2D> ResolveExternal(string sourcePath)
    {
        if (_db == null || string.IsNullOrEmpty(_assetsRoot)) return default;

        // sourcePath is always already a resolved, existing, absolute path (guaranteed by Clay's
        // Texture.SourcePath contract) - a plain prefix check + relative-path computation is enough,
        // no need to re-resolve it against the model's own directory.
        if (!sourcePath.StartsWith(_assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"[Clay] External texture '{sourcePath}' is not under the project's " +
                $"Assets folder '{_assetsRoot}' - using the default fallback texture instead.");
            return default;
        }

        string relativePath = Path.GetRelativePath(_assetsRoot, sourcePath).Replace('\\', '/');
        var entry = _db.GetEntry(relativePath);
        if (entry == null)
        {
            Debug.LogWarning($"[Clay] External texture '{sourcePath}' (resolved to '{relativePath}') has no " +
                "tracked asset entry - using the default fallback texture instead. Has it been imported yet?");
            return default;
        }

        _ctx.AddDependency(entry.Guid);
        return new AssetRef<Texture2D>(entry.Guid);
    }

    public AssetRef<Texture2D> ResolveEmbedded(string? name, byte[] encodedBytes, string? mimeType)
    {
        try
        {
            using var ms = new MemoryStream(encodedBytes);
            var tex = Texture2D.LoadFromStream(ms, generateMipmaps: true);
            tex.Name = string.IsNullOrEmpty(name) ? "EmbeddedTexture" : name;
            _ctx.AddSubAsset(tex.Name, tex, SubAssetIdentity.Order); // assigns tex.AssetID
            return new AssetRef<Texture2D>(tex);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Clay] Failed to load embedded texture '{name ?? "(unnamed)"}': {ex.Message}");
            return default;
        }
    }
}
