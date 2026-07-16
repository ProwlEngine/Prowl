using System;
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
    private const int BaseVersion = 5;
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
                importSettings.RecalculateNormals = s.TryGet("recalculateNormals", out var rn) && rn.BoolValue;
                importSettings.CalculateTangentSpace = !s.TryGet("calculateTangents", out var ct) || ct.BoolValue;
                importSettings.FlipUVs = !s.TryGet("flipUVs", out var fu) || fu.BoolValue;
                importSettings.UnitScale = s.TryGet("unitScale", out var us) ? us.FloatValue : 1.0f;
                // Off by default (slow; some models ship their own UV2). The importer runs the
                // unwrap in its post-process so the baked UV2 is captured before serialization.
                importSettings.GenerateLightmapUVs = s.TryGet("generateLightmapUVs", out var glu) && glu.BoolValue;
            }

            // 1. Import creates live meshes, materials, animations, GO hierarchy (+ UV2 if enabled).
            var importer = new ModelImporter();
            var data = importer.Import(new FileInfo(ctx.AbsolutePath), importSettings);

            // 2. Register sub-assets assigns deterministic GUIDs immediately
            for (int i = 0; i < data.Meshes.Count; i++)
                ctx.AddSubAsset(data.Meshes[i].Name ?? $"Mesh_{i}", data.Meshes[i]);

            for (int i = 0; i < data.Materials.Count; i++)
                ctx.AddSubAsset(data.Materials[i].Name ?? $"Material_{i}", data.Materials[i]);

            for (int i = 0; i < data.Animations.Count; i++)
                ctx.AddSubAsset(data.Animations[i].Name ?? $"Animation_{i}", data.Animations[i]);

            // Note: model-referenced textures (both external and embedded) are already fully
            // resolved by this point - materials carry AssetRefs, and any embedded texture is
            // already registered as a sub-asset - both as side effects of EditorModelTextureResolver
            // running during importer.Import() above.

            // 2b. Generate mesh features (SDF, BVH, Prism, ...) per mesh, registered as sub-assets.
            for (int i = 0; i < data.Meshes.Count; i++)
                MeshFeatureImporter.GenerateAll(data.Meshes[i], ctx.Settings, ctx);

            // 3. Serialize GO hierarchy sub-assets have correct IDs, AssetRefs serialize as GUIDs.
            //    Tracked (matching SceneImporter/PrefabImporter) so the Model's own dependency list
            //    reflects what its GameObject hierarchy actually references.
            var model = new Model(Path.GetFileNameWithoutExtension(ctx.AbsolutePath));
            if (data.RootGO != null)
            {
                var goSerCtx = ImportHelper.CreateTrackingContext(out var goDependencies);
                model.GameObjectData = Serializer.Serialize(typeof(object), data.RootGO, goSerCtx);
                foreach (var dep in goDependencies)
                    ctx.AddDependency(dep);
            }

            ctx.SetMainAsset(model);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import model: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateNormals"] = new EchoObject(true);
        s["generateSmoothNormals"] = new EchoObject(true);
        s["recalculateNormals"] = new EchoObject(false);
        s["calculateTangents"] = new EchoObject(true);
        s["flipUVs"] = new EchoObject(true);
        s["unitScale"] = new EchoObject(1.0f);
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
        _assetsRoot = Project.Current?.AssetsPath ?? "";
        _db = EditorAssetBackend.Instance;
    }

    public AssetRef<Texture2D> ResolveExternal(string sourcePath)
    {
        if (_db == null || string.IsNullOrEmpty(_assetsRoot)) return default;

        // sourcePath is always already a resolved, existing, absolute path (guaranteed by Clay's
        // Texture.SourcePath contract) - a plain prefix check + relative-path computation is enough,
        // no need to re-resolve it against the model's own directory.
        if (!sourcePath.StartsWith(_assetsRoot, StringComparison.OrdinalIgnoreCase)) return default;

        string relativePath = Path.GetRelativePath(_assetsRoot, sourcePath).Replace('\\', '/');
        var entry = _db.GetEntry(relativePath);
        if (entry == null) return default;

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
            _ctx.AddSubAsset(tex.Name, tex); // assigns tex.AssetID
            return new AssetRef<Texture2D>(tex);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Clay] Failed to load embedded texture '{name ?? "(unnamed)"}': {ex.Message}");
            return default;
        }
    }
}
