// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.IO;

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
/// Result of a model import live objects ready for the asset database to process.
/// </summary>
public class ModelImportResult
{
    public GameObject? RootGO;
    public List<Mesh> Meshes = [];
    public List<Material> Materials = [];
    public List<AnimationClip> Animations = [];
    /// <summary>
    /// Every Texture2D loaded during import. Textures with a populated <c>AssetPath</c> were
    /// loaded from disk files referenced by the model (and the editor's <c>ResolveTextures</c>
    /// will swap them for AssetRefs into the asset database). Textures with an empty
    /// <c>AssetPath</c> are embedded (GLB inline image, FBX Video::Clip content, data: URI)
    /// and should be registered as sub-assets so they're discoverable in the asset browser
    /// and reusable across materials.
    /// </summary>
    public List<Texture2D> Textures = [];
}

/// <summary>
/// Loads .gltf / .glb / .obj into a fully-baked <see cref="ModelImportResult"/>. Backed by the
/// Prowl.Clay library (the previous in-tree GltfImporter / ObjImporter were retired in favor of
/// this single unified path).
/// </summary>
public class ModelImporter
{
    public ModelImportResult Import(FileInfo assetPath, ModelImporterSettings? settings = null)
        => ClayBackedImporter.Import(assetPath, settings ?? new ModelImporterSettings());

    public ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
        => ClayBackedImporter.Import(stream, virtualPath, settings ?? new ModelImporterSettings());
}
