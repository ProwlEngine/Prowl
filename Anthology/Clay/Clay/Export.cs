// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// A model to write out, in the same space and conventions as an imported <see cref="Model"/>: left handed, Y up,
/// triangles wound as the importer gives them. Writers convert to the file's own conventions, so a written model reads
/// back as it was given.
/// </summary>
public sealed class ExportModel
{
    public string Name { get; set; } = "Model";
    public List<ExportNode> Nodes { get; } = [];
    public List<ExportMesh> Meshes { get; } = [];
    public List<ExportMaterial> Materials { get; } = [];
    public List<ExportTexture> Textures { get; } = [];
    public List<ExportSkin> Skins { get; } = [];
}

/// <summary>A node: a transform under its parent, and the mesh and skin it draws, if any.</summary>
public sealed class ExportNode
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Index of the parent in <see cref="ExportModel.Nodes"/>, -1 for a node at the top.</summary>
    public int Parent { get; set; } = -1;

    public Float3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Float3 Scale { get; set; } = Float3.One;
    public int Mesh { get; set; } = -1;
    public int Skin { get; set; } = -1;
}

/// <summary>
/// One mesh's vertices, shared by its primitives, each a triangle list drawn with one material. Optional streams are
/// null when absent, otherwise as long as <see cref="Positions"/>.
/// </summary>
public sealed class ExportMesh
{
    public string Name { get; set; } = string.Empty;
    public Float3[] Positions { get; set; } = [];
    public Float3[]? Normals { get; set; }
    public Float4[]? Tangents { get; set; }
    public Float2[]? UV0 { get; set; }
    public Float2[]? UV1 { get; set; }
    public Float4[]? Colors { get; set; }

    /// <summary>Four bone indices a vertex, into the skin's joints.</summary>
    public Int4[]? Joints { get; set; }

    /// <summary>The four weights of <see cref="Joints"/>, adding up to one.</summary>
    public Float4[]? Weights { get; set; }

    public List<ExportPrimitive> Primitives { get; } = [];
    public List<ExportMorphTarget> MorphTargets { get; } = [];
}

/// <summary>A triangle list of a mesh and the index of its material, -1 for none.</summary>
public sealed class ExportPrimitive
{
    public uint[] Indices { get; set; } = [];
    public int Material { get; set; } = -1;
}

/// <summary>A blend shape: how far each vertex moves at full weight, and its weight before any animation, from 0 to 1.</summary>
public sealed class ExportMorphTarget
{
    public string Name { get; set; } = string.Empty;
    public Float3[] DeltaPositions { get; set; } = [];
    public Float3[]? DeltaNormals { get; set; }
    public Float3[]? DeltaTangents { get; set; }
    public float Weight { get; set; }
}

/// <summary>A metallic roughness material. Texture fields index <see cref="ExportModel.Textures"/>, -1 for none.</summary>
public sealed class ExportMaterial
{
    public string Name { get; set; } = string.Empty;
    public Float4 BaseColor { get; set; } = new(1f, 1f, 1f, 1f);
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 1f;
    public Float3 Emission { get; set; }
    public float EmissionStrength { get; set; } = 1f;
    public int BaseColorTexture { get; set; } = -1;

    /// <summary>Metalness in the blue channel and roughness in the green, as the file format keeps them.</summary>
    public int MetallicRoughnessTexture { get; set; } = -1;

    public int NormalTexture { get; set; } = -1;
    public float NormalScale { get; set; } = 1f;
    public int EmissionTexture { get; set; } = -1;
    public Float2 TextureOffset { get; set; }
    public Float2 TextureScale { get; set; } = Float2.One;
    public MaterialAlphaMode AlphaMode { get; set; } = MaterialAlphaMode.Opaque;
    public float AlphaCutoff { get; set; } = 0.5f;
    public bool DoubleSided { get; set; }
}

/// <summary>An image written into the file as it is, a PNG.</summary>
public sealed class ExportTexture
{
    public string Name { get; set; } = string.Empty;
    public byte[] Png { get; set; } = [];
}

/// <summary>A skin: the nodes that are its joints, and for each the matrix from the mesh's space into the joint's at rest.</summary>
public sealed class ExportSkin
{
    public string Name { get; set; } = string.Empty;
    public List<int> Joints { get; } = [];
    public List<Float4x4> InverseBindMatrices { get; } = [];
}
