using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

[ImporterFor(".gltf", ".glb")]
public class EditorModelImporter : AssetImporter
{
    public override int Version => 4;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            ModelImporterSettings? importSettings = null;
            if (ctx.Settings != null)
            {
                var s = ctx.Settings;
                importSettings = new ModelImporterSettings
                {
                    GenerateNormals = !s.TryGet("generateNormals", out var gn) || gn.BoolValue,
                    GenerateSmoothNormals = s.TryGet("generateSmoothNormals", out var gsn) && gsn.BoolValue,
                    CalculateTangentSpace = !s.TryGet("calculateTangents", out var ct) || ct.BoolValue,
                    FlipUVs = !s.TryGet("flipUVs", out var fu) || fu.BoolValue,
                    UnitScale = s.TryGet("unitScale", out var us) ? us.FloatValue : 1.0f,
                };
            }

            // 1. Import — creates live meshes, materials, animations, GO hierarchy
            var importer = new ModelImporter();
            var data = importer.Import(new FileInfo(ctx.AbsolutePath), importSettings);

            // 2. Register sub-assets — assigns deterministic GUIDs immediately
            for (int i = 0; i < data.Meshes.Count; i++)
                ctx.AddSubAsset(data.Meshes[i].Name ?? $"Mesh_{i}", data.Meshes[i]);

            for (int i = 0; i < data.Materials.Count; i++)
                ctx.AddSubAsset(data.Materials[i].Name ?? $"Material_{i}", data.Materials[i]);

            for (int i = 0; i < data.Animations.Count; i++)
                ctx.AddSubAsset(data.Animations[i].Name ?? $"Animation_{i}", data.Animations[i]);

            // 3. Resolve inline textures to asset DB references
            ResolveTextures(data, ctx);

            // 4. Serialize GO hierarchy — sub-assets have correct IDs, AssetRefs serialize as GUIDs
            var model = new Model(Path.GetFileNameWithoutExtension(ctx.AbsolutePath));
            if (data.RootGO != null)
                model.GameObjectData = Serializer.Serialize(typeof(object), data.RootGO);

            ctx.SetMainAsset(model);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import model: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
    }

    private static void ResolveTextures(ModelImportResult data, ImportContext ctx)
    {
        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        string modelDir = Path.GetDirectoryName(ctx.AbsolutePath) ?? "";
        string assetsRoot = Project.Current?.AssetsPath ?? "";
        if (string.IsNullOrEmpty(assetsRoot)) return;

        foreach (var mat in data.Materials)
        {
            if (mat == null) continue;
            ResolveSlot(mat, "_MainTex", modelDir, assetsRoot, db, ctx);
            ResolveSlot(mat, "_NormalTex", modelDir, assetsRoot, db, ctx);
            ResolveSlot(mat, "_SurfaceTex", modelDir, assetsRoot, db, ctx);
            ResolveSlot(mat, "_EmissionTex", modelDir, assetsRoot, db, ctx);
        }
    }

    private static void ResolveSlot(Material mat, string slot, string modelDir, string assetsRoot, EditorAssetDatabase db, ImportContext ctx)
    {
        var tex = mat._properties.GetTexture(slot);
        if (tex == null || tex.IsDisposed) return;

        string? texPath = tex.AssetPath;
        if (string.IsNullOrEmpty(texPath)) return;

        string? relativePath = null;
        if (Path.IsPathRooted(texPath))
        {
            if (texPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                relativePath = Path.GetRelativePath(assetsRoot, texPath).Replace('\\', '/');
        }
        else
        {
            string abs = Path.GetFullPath(Path.Combine(modelDir, texPath));
            if (abs.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                relativePath = Path.GetRelativePath(assetsRoot, abs).Replace('\\', '/');
        }

        if (relativePath == null) return;

        var entry = db.GetEntry(relativePath);
        if (entry == null) return;

        // Don't load the texture — just set the AssetRef by GUID.
        // The texture may not be imported yet, but the GUID is assigned.
        // At runtime, AssetRef lazy-loads via AssetDatabase.Get().
        mat.SetTexture(slot, new AssetRef<Texture2D>(entry.Guid));
        ctx.AddDependency(entry.Guid);
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateNormals"] = new EchoObject(true);
        s["generateSmoothNormals"] = new EchoObject(false);
        s["calculateTangents"] = new EchoObject(true);
        s["flipUVs"] = new EchoObject(true);
        s["unitScale"] = new EchoObject(1.0f);
        return s;
    }
}
