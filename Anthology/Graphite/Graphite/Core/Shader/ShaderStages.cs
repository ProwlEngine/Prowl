using System;

namespace Prowl.Graphite;

/// <summary>
/// Bitmask of shader stages.
/// </summary>
[Flags]
public enum ShaderStages : byte
{
    /// <summary>
    /// No stages.
    /// </summary>
    None = 0,
    /// <summary>
    /// Vertex stage.
    /// </summary>
    Vertex = 1 << 0,
    /// <summary>
    /// Geometry stage.
    /// </summary>
    Geometry = 1 << 1,
    /// <summary>
    /// Tessellation control stage.
    /// </summary>
    TessellationControl = 1 << 2,
    /// <summary>
    /// Tessellation evaluation stage.
    /// </summary>
    TessellationEvaluation = 1 << 3,
    /// <summary>
    /// Fragment stage.
    /// </summary>
    Fragment = 1 << 4,
    /// <summary>
    /// Compute stage.
    /// </summary>
    Compute = 1 << 5,
}
