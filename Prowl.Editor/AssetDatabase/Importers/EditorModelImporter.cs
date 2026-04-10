using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports 3D model files using the runtime's ModelImporter.
/// Produces a Model with sub-assets (meshes, materials, animations).
/// </summary>
[ImporterFor(".gltf", ".glb")]
public class EditorModelImporter : AssetImporter
{
    public override int Version => 3; // GLTF-only importer, dropped Assimp

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        var result = new ImportResult();
        try
        {
            ModelImporterSettings? importSettings = null;
            if (settings != null)
            {
                importSettings = new ModelImporterSettings
                {
                    GenerateNormals = !settings.TryGet("generateNormals", out var gn) || gn.BoolValue,
                    GenerateSmoothNormals = settings.TryGet("generateSmoothNormals", out var gsn) && gsn.BoolValue,
                    CalculateTangentSpace = !settings.TryGet("calculateTangents", out var ct) || ct.BoolValue,
                    FlipUVs = !settings.TryGet("flipUVs", out var fu) || fu.BoolValue,
                    UnitScale = settings.TryGet("unitScale", out var us) ? us.FloatValue : 1.0f,
                };
            }

            var runtimeImporter = new ModelImporter();
            var model = runtimeImporter.Import(new FileInfo(absolutePath), importSettings);
            model.Name = Path.GetFileNameWithoutExtension(absolutePath);
            result.MainAsset = model;

            // Extract sub-assets: individual meshes, materials, animations
            var subAssets = new List<EngineObject>();

            for (int i = 0; i < model.Meshes.Count; i++)
            {
                var modelMesh = model.Meshes[i];
                var mesh = modelMesh.Mesh.Res;
                if (mesh != null)
                {
                    mesh.Name = !string.IsNullOrEmpty(modelMesh.Name) ? modelMesh.Name : $"Mesh_{i}";
                    subAssets.Add(mesh);
                }
            }

            for (int i = 0; i < model.Materials.Count; i++)
            {
                var mat = model.Materials[i].Res;
                if (mat != null)
                {
                    if (string.IsNullOrEmpty(mat.Name)) mat.Name = $"Material_{i}";
                    subAssets.Add(mat);
                }
            }

            for (int i = 0; i < model.Animations.Count; i++)
            {
                var clip = model.Animations[i];
                if (clip != null)
                {
                    if (string.IsNullOrEmpty(clip.Name)) clip.Name = $"Animation_{i}";
                    subAssets.Add(clip);
                }
            }

            result.SubAssets = subAssets.ToArray();

            // Post-process: replace inline textures with asset database references
            ResolveTextureReferences(model, absolutePath, result);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import model: {absolutePath}\n{ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Walk all materials in the model and replace inline-loaded textures
    /// with AssetRef references to the texture assets in the project.
    /// This ensures texture import settings (filter, wrap, mipmaps) are respected.
    /// </summary>
    private static void ResolveTextureReferences(Model model, string modelAbsPath, ImportResult result)
    {
        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        string modelDir = Path.GetDirectoryName(modelAbsPath) ?? "";
        string assetsRoot = Project.Current?.AssetsPath ?? "";
        if (string.IsNullOrEmpty(assetsRoot)) return;

        foreach (var matRef in model.Materials)
        {
            var mat = matRef.Res;
            if (mat == null) continue;

            // Get the _textures dictionary from PropertyState via the public getter
            // Check each texture slot and try to resolve to an asset
            ResolveTextureSlot(mat, "_MainTex", modelDir, assetsRoot, db, result);
            ResolveTextureSlot(mat, "_NormalTex", modelDir, assetsRoot, db, result);
            ResolveTextureSlot(mat, "_SurfaceTex", modelDir, assetsRoot, db, result);
            ResolveTextureSlot(mat, "_EmissionTex", modelDir, assetsRoot, db, result);
        }
    }

    private static void ResolveTextureSlot(Material mat, string slotName, string modelDir, string assetsRoot, EditorAssetDatabase db, ImportResult result)
    {
        var tex = mat._properties.GetTexture(slotName);
        if (tex == null || tex.IsDisposed) return;

        // The texture was loaded from a file path — try to find it in the asset database
        string? texPath = tex.AssetPath;
        if (string.IsNullOrEmpty(texPath)) return;

        // Convert absolute path to relative path within the Assets folder
        string relativePath = null;
        if (Path.IsPathRooted(texPath))
        {
            if (texPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                relativePath = Path.GetRelativePath(assetsRoot, texPath).Replace('\\', '/');
        }
        else
        {
            // Relative to model directory — resolve to absolute then to relative
            string absTexPath = Path.GetFullPath(Path.Combine(modelDir, texPath));
            if (absTexPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                relativePath = Path.GetRelativePath(assetsRoot, absTexPath).Replace('\\', '/');
        }

        if (relativePath == null) return;

        var entry = db.GetEntry(relativePath);
        if (entry == null) return;

        // Load the texture from the asset database instead of using the inline one
        var dbTexture = Runtime.AssetDatabase.Get(entry.Guid) as Texture2D;
        if (dbTexture != null)
        {
            mat.SetTexture(slotName, dbTexture);
            result.Dependencies.Add(entry.Guid);
        }
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
