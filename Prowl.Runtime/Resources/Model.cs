// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Vector;

namespace Prowl.Runtime.Resources;

public class Model : EngineObject
{
    public new string Name { get; set; }
    public List<AssetRef<Material>> Materials { get; set; } = [];
    public List<ModelMesh> Meshes { get; set; } = [];
    public List<AnimationClip> Animations { get; set; } = [];
    public Skeleton Skeleton { get; set; }
    public float UnitScale { get; set; } = 1.0f;

    public Model(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Loads a model from a file (.obj, .fbx, .gltf, etc.)
    /// </summary>
    public static Model LoadFromFile(string filePath, AssetImporting.ModelImporterSettings? settings = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Model file not found: {filePath}");

        var importer = new AssetImporting.ModelImporter();
        Model model = importer.Import(new FileInfo(filePath), settings);
        model.AssetPath = filePath;
        return model;
    }

    /// <summary>
    /// Loads a model from a stream
    /// </summary>
    public static Model LoadFromStream(Stream stream, string virtualPath, AssetImporting.ModelImporterSettings? settings = null)
    {
        var importer = new AssetImporting.ModelImporter();
        Model model = importer.Import(stream, virtualPath, settings);
        model.AssetPath = virtualPath;
        return model;
    }

    /// <summary>
    /// Loads a default embedded model
    /// </summary>
    public static Model LoadDefault(DefaultModel model)
    {
        string fileName = model switch
        {
            DefaultModel.Cube => "Cube.obj",
            DefaultModel.Sphere => "Sphere.obj",
            DefaultModel.Cylinder => "Cylinder.obj",
            DefaultModel.Plane => "Plane.obj",
            DefaultModel.SkyDome => "SkyDome.obj",
            DefaultModel.UnitCube => "1mcube.obj",
            _ => throw new ArgumentException($"Unknown default model: {model}")
        };

        string resourcePath = $"Assets/Defaults/{fileName}";
        using (Stream stream = EmbeddedResources.GetStream(resourcePath))
        {
            var importer = new AssetImporting.ModelImporter();
            Model result = importer.Import(stream, resourcePath);
            result.AssetPath = $"$Default:{model}";
            result.AssetID = BuiltInAssets.GuidFor(model);
            result.Name = model.ToString();
            return result;
        }
    }
}

public class ModelMesh
{
    public string Name { get; set; }
    public AssetRef<Mesh> Mesh { get; set; }
    public AssetRef<Material> Material { get; set; }
    public bool HasBones { get; set; }

    public ModelMesh(string name, Mesh mesh, Material material, bool hasBones = false)
    {
        Name = name;
        Mesh = mesh;
        Material = material;
        HasBones = hasBones;
    }
}
