using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports 3D model files using the runtime's ModelImporter.
/// Produces a Model with sub-assets (meshes, materials, animations).
/// </summary>
[ImporterFor(".fbx", ".obj", ".gltf", ".glb", ".dae")]
public class EditorModelImporter : AssetImporter
{
    public override int Version => 1;

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
                    GenerateNormals = settings.TryGet("generateNormals", out var gn) && gn.BoolValue,
                    GenerateSmoothNormals = settings.TryGet("generateSmoothNormals", out var gsn) && gsn.BoolValue,
                    CalculateTangentSpace = settings.TryGet("calculateTangents", out var ct) && ct.BoolValue,
                    FlipUVs = settings.TryGet("flipUVs", out var fu) && fu.BoolValue,
                    GlobalScale = settings.TryGet("globalScale", out var gs) && gs.BoolValue,
                    UnitScale = settings.TryGet("unitScale", out var us) ? us.FloatValue : 1.0f,
                };
            }

            var runtimeImporter = new ModelImporter();
            var model = runtimeImporter.Import(new FileInfo(absolutePath), importSettings);
            model.Name = Path.GetFileNameWithoutExtension(absolutePath);
            result.MainAsset = model;

            // Sub-assets: meshes from the model
            // (Materials and animations are part of the Model object itself)
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import model: {absolutePath}\n{ex.Message}");
        }
        return result;
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s["generateNormals"] = new EchoObject(true);
        s["generateSmoothNormals"] = new EchoObject(false);
        s["calculateTangents"] = new EchoObject(true);
        s["flipUVs"] = new EchoObject(false);
        s["globalScale"] = new EchoObject(false);
        s["unitScale"] = new EchoObject(1.0f);
        return s;
    }
}
