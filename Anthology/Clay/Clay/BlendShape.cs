using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// A named morph target attached to a <see cref="Mesh"/>. Each frame represents one fully-deformed
/// shape; intermediate weights between two frames are interpolated linearly.
/// </summary>
/// <remarks>
/// glTF morph targets always have a single frame; FBX and some legacy formats can carry multiple.
/// Names come from <c>mesh.extras.targetNames</c> in glTF or the <c>Shape</c> name in FBX.
/// </remarks>
public sealed class BlendShape
{
    /// <summary>Blend-shape name.</summary>
    public required string Name { get; init; }

    /// <summary>One or more deformed frames; weights interpolate between them.</summary>
    public required BlendShapeFrame[] Frames { get; init; }
}

/// <summary>
/// A single fully-deformed sample of a <see cref="BlendShape"/>.
/// </summary>
public sealed class BlendShapeFrame
{
    /// <summary>
    /// Weight at which this frame is reached, on a 0..100 scale (100 = full activation).
    /// </summary>
    public float Weight { get; init; } = 100f;

    /// <summary>Per-vertex position delta to add to the base mesh. Length matches <see cref="Mesh.VertexCount"/>.</summary>
    public required Float3[] DeltaVertices { get; init; }

    /// <summary>Per-vertex normal delta, or <c>null</c> if the source did not provide one.</summary>
    public Float3[]? DeltaNormals { get; init; }

    /// <summary>Per-vertex tangent delta, or <c>null</c> if the source did not provide one.</summary>
    public Float3[]? DeltaTangents { get; init; }
}
