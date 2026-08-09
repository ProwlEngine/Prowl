// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Echo;

namespace Prowl.Runtime.Resources;

/// <summary>
/// Builds the embedded default models (Cube, Sphere, ...) as prefabs.
/// A model file is imported into a <see cref="PrefabAsset"/> like any other, so these are too; this
/// exists only because the defaults come from embedded resources rather than the asset database.
/// </summary>
public static class DefaultModels
{
    /// <summary>Loads a default embedded model (Cube, Sphere, etc.) as a prefab.</summary>
    public static PrefabAsset Load(DefaultModel model)
    {
        string fileName = model switch
        {
            DefaultModel.Cube => "Cube.obj",
            DefaultModel.Sphere => "Sphere.obj",
            DefaultModel.Cylinder => "Cylinder.obj",
            DefaultModel.Plane => "Plane.obj",
            DefaultModel.SkyDome => "SkyDome.obj",
            _ => throw new ArgumentException($"Unknown default model: {model}")
        };

        string resourcePath = $"Assets/Defaults/{fileName}";
        using Stream stream = EmbeddedResources.GetStream(resourcePath);
        var result = new PrefabAsset { Name = model.ToString(), InstanceType = PrefabInstanceType.Model };

        // Import via the OBJ importer. Embedded defaults have no companion .mtl, so the
        // resulting GO just has a MeshRenderer with an empty Materials list callers that
        // use these meshes (e.g. BuiltInAssets, primitive creators) assign their own material.
        var importResult = new AssetImporting.ModelImporter().Import(stream, fileName,
            new AssetImporting.ModelImporterSettings
            {
                RecalculateNormals = true,
                GenerateNormals = true,
                GenerateSmoothNormals = true,
                CalculateTangentSpace = true
            });

        if (importResult.RootGO != null)
            result.GameObjectData = Serializer.Serialize(typeof(object), importResult.RootGO);

        result.AssetPath = $"$Default:{model}";
        result.AssetID = BuiltInAssets.GuidFor(model);
        return result;
    }
}
