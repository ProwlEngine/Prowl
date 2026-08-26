// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Writable mesh form used through the pipeline. Stores positions/normals/tangents/UVs as
/// independent buffers so individual streams can be added, removed, or replaced cheaply.
/// </summary>
internal sealed class IntermediateMesh
{
    public string Name { get; set; } = string.Empty;

    public List<Float3> Positions { get; } = new();
    public List<Float3>? Normals { get; set; }
    public List<Float4>? Tangents { get; set; }
    public List<Color>? Colors0 { get; set; }
    public List<Float2>?[] UVs { get; } = new List<Float2>?[Mesh.MaxUVChannels];

    public List<IntermediateFace> Faces { get; } = new();

    /// <summary>
    /// Per-vertex joint indices when skinned. Packed as <c>vertexCount * MaxInfluencesPerVertex</c>
    /// integers; the bone indices are local to the owning skin's joint list. <c>null</c> when not
    /// skinned. The <see cref="PostProcess.LimitBoneWeightsStep"/> truncates to 4 influences.
    /// </summary>
    public int[]? VertexJoints { get; set; }

    /// <summary>
    /// Per-vertex bone weights, parallel to <see cref="VertexJoints"/>. <c>null</c> when not skinned.
    /// </summary>
    public float[]? VertexWeights { get; set; }

    /// <summary>Number of bone influences stored per vertex (4 unless JOINTS_1/WEIGHTS_1 were present).</summary>
    public int MaxInfluencesPerVertex { get; set; } = 4;

    /// <summary>Morph targets. Empty when none were authored.</summary>
    public List<IntermediateBlendShape> BlendShapes { get; } = new();

    /// <summary>
    /// Index into the parent scene's material list, -1 for no material. Applies to every face that
    /// does not carry its own, which is the common case: only sources that assign materials per
    /// face (glTF primitives merged into one mesh) set <see cref="IntermediateFace.Material"/>.
    /// </summary>
    public int MaterialIndex { get; set; } = -1;

    /// <summary>The material a face actually draws with, resolving the inherit sentinel.</summary>
    public int MaterialForFace(in IntermediateFace face) =>
        face.Material == IntermediateFace.InheritMaterial ? MaterialIndex : face.Material;

    /// <summary>True when the faces do not all resolve to the same material.</summary>
    public bool HasMixedMaterials()
    {
        if (Faces.Count == 0) return false;

        int first = MaterialForFace(Faces[0]);
        for (int i = 1; i < Faces.Count; i++)
            if (MaterialForFace(Faces[i]) != first)
                return true;
        return false;
    }

    /// <summary>True when the mesh contains primitives that aren't triangles.</summary>
    public PrimitiveKind PrimitiveKinds { get; set; }
}

/// <summary>A single face with N indices (3 for triangles, 2 for lines, 1 for points, N for polygons).</summary>
/// <remarks>
/// The material rides on the face rather than beside it so that every post-process step which
/// reorders, filters or moves faces between meshes carries the assignment along without having to
/// know about it. Only the handful of places that construct a face from scratch have to say what
/// material it belongs to.
/// </remarks>
internal struct IntermediateFace
{
    /// <summary>Sentinel for "no material of my own", deferring to <see cref="IntermediateMesh.MaterialIndex"/>.
    /// Distinct from -1, which is a real value meaning the face has no material at all.</summary>
    public const int InheritMaterial = int.MinValue;

    /// <summary>Sentinel for "the source said nothing about smoothing", which leaves the decision to
    /// the smoothing angle. Distinct from 0, which is an author saying this face smooths with nothing.</summary>
    public const int NoSmoothingGroup = -1;

    public int[] Indices;

    /// <summary>Material index for this face, or <see cref="InheritMaterial"/> to use the mesh's.</summary>
    public int Material;

    /// <summary>
    /// Smoothing group the source assigned, or <see cref="NoSmoothingGroup"/>. Faces sharing a
    /// non-zero group are smoothed together whatever the angle between them; faces in different
    /// groups never are.
    /// </summary>
    public int SmoothingGroup;

    public IntermediateFace(int[] indices)
    {
        Indices = indices;
        Material = InheritMaterial;
        SmoothingGroup = NoSmoothingGroup;
    }

    public IntermediateFace(int[] indices, int material)
    {
        Indices = indices;
        Material = material;
        SmoothingGroup = NoSmoothingGroup;
    }

    public IntermediateFace(int[] indices, int material, int smoothingGroup)
    {
        Indices = indices;
        Material = material;
        SmoothingGroup = smoothingGroup;
    }

    /// <summary>Carries this face's material and smoothing group onto a new set of indices.</summary>
    public readonly IntermediateFace WithIndices(int[] indices) => new(indices, Material, SmoothingGroup);
}

[Flags]
internal enum PrimitiveKind
{
    None = 0,
    Point = 1,
    Line = 2,
    Triangle = 4,
    Polygon = 8,
}

internal sealed class IntermediateBlendShape
{
    public required string Name { get; init; }
    public List<IntermediateBlendShapeFrame> Frames { get; } = new();
}

internal sealed class IntermediateBlendShapeFrame
{
    public float Weight { get; set; } = 100f;
    public required Float3[] DeltaPositions { get; init; }
    public Float3[]? DeltaNormals { get; init; }
    public Float3[]? DeltaTangents { get; init; }
}
