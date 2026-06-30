namespace Prowl.Clay;

/// <summary>
/// Topology of a <see cref="SubMesh"/>'s index buffer.
/// </summary>
public enum PrimitiveTopology
{
    /// <summary>Each index defines a single point primitive.</summary>
    Points,

    /// <summary>Indices are consumed two at a time as line endpoints.</summary>
    Lines,

    /// <summary>Indices are consumed three at a time as triangles.</summary>
    Triangles,
}
