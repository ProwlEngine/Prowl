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

    /// <summary>Index into the parent scene's material list. -1 for no material.</summary>
    public int MaterialIndex { get; set; } = -1;

    /// <summary>True when the mesh contains primitives that aren't triangles.</summary>
    public PrimitiveKind PrimitiveKinds { get; set; }
}

/// <summary>A single face with N indices (3 for triangles, 2 for lines, 1 for points, N for polygons).</summary>
internal struct IntermediateFace
{
    public int[] Indices;

    public IntermediateFace(int[] indices) { Indices = indices; }
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
