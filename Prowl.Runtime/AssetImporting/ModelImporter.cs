// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

public class ModelImporter
{
    private readonly GltfImporter _gltfImporter = new();

    public Model Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        string ext = assetPath.Extension.ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
            return _gltfImporter.Import(assetPath, settings);

        throw new System.NotSupportedException($"Unsupported model format: {ext}. Only .gltf and .glb are supported.");
    }

    public Model Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
            return _gltfImporter.Import(stream, virtualPath, settings);

        // OBJ fallback for embedded default models
        if (ext == ".obj")
            return ObjParser.Parse(stream, Path.GetFileNameWithoutExtension(virtualPath));

        throw new System.NotSupportedException($"Unsupported model format: {ext}. Only .gltf and .glb are supported.");
    }
}
