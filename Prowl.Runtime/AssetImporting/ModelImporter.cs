// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.AssetImporting.Gltf;
using Prowl.Runtime.AssetImporting.Obj;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.AssetImporting;

public struct ModelImporterSettings
{
    /// <summary>Generate normals if the mesh doesn't have them.</summary>
    public bool GenerateNormals = true;

    /// <summary>Use smooth (area-weighted vertex) normals instead of flat/faceted.</summary>
    public bool GenerateSmoothNormals = true;

    /// <summary>Force recalculate normals even if the mesh already has them.</summary>
    public bool RecalculateNormals = false;

    /// <summary>Generate tangent vectors for normal mapping.</summary>
    public bool CalculateTangentSpace = true;

    /// <summary>Flip V texture coordinate (some formats use top-left origin).</summary>
    public bool FlipUVs = true;

    /// <summary>Uniform scale applied to all vertex positions.</summary>
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
    private readonly ObjImporter _objImporter = new();

    public ModelImportResult Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        string ext = assetPath.Extension.ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
            return _gltfImporter.Import(assetPath, settings);
        if (ext == ".obj")
            return _objImporter.Import(assetPath, settings);

        throw new System.NotSupportedException($"Unsupported model format: {ext}");
    }

    public ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
            return _gltfImporter.Import(stream, virtualPath, settings);
        if (ext == ".obj")
            return _objImporter.Import(stream, virtualPath, settings);

        throw new System.NotSupportedException($"Unsupported model format: {ext}");
    }
}
