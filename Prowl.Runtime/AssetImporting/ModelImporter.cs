// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.AssetImporting.Gltf;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.AssetImporting;

public struct ModelImporterSettings
{
    public bool GenerateNormals = true;
    public bool GenerateSmoothNormals = false;
    public bool CalculateTangentSpace = true;
    public bool FlipUVs = true;
    public float UnitScale = 1.0f;

    public ModelImporterSettings() { }
}

/// <summary>
/// Result of a model import — live objects ready for the asset database to process.
/// </summary>
public class ModelImportResult
{
    public GameObject? RootGO;
    public List<Mesh> Meshes = [];
    public List<Material> Materials = [];
    public List<AnimationClip> Animations = [];
}

public class ModelImporter
{
    private readonly GltfImporter _gltfImporter = new();

    public ModelImportResult Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        string ext = assetPath.Extension.ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
            return _gltfImporter.Import(assetPath, settings);

        throw new System.NotSupportedException($"Unsupported model format: {ext}");
    }

    public ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
            return _gltfImporter.Import(stream, virtualPath, settings);

        throw new System.NotSupportedException($"Unsupported model format: {ext}");
    }
}
