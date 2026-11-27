namespace Prowl.Graphite;

/// <summary>
/// How the rasterizer reads a vertex sequence.
/// </summary>
public enum PrimitiveTopology : byte
{
    /// <summary>
    /// Isolated triangles, 3 verts each.
    /// </summary>
    TriangleList,
    /// <summary>
    /// Connected triangles.
    /// </summary>
    TriangleStrip,
    /// <summary>
    /// Isolated line segments, 2 verts each.
    /// </summary>
    LineList,
    /// <summary>
    /// Connected line segments.
    /// </summary>
    LineStrip,
    /// <summary>
    /// Isolated points.
    /// </summary>
    PointList,
}
