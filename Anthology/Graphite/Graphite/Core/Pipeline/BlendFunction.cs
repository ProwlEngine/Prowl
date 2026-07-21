namespace Prowl.Graphite;

/// <summary>
/// How source and destination blend factors combine.
/// </summary>
public enum BlendFunction : byte
{
    /// <summary>
    /// src + dst.
    /// </summary>
    Add,
    /// <summary>
    /// src - dst.
    /// </summary>
    Subtract,
    /// <summary>
    /// dst - src.
    /// </summary>
    ReverseSubtract,
    /// <summary>
    /// min(src, dst).
    /// </summary>
    Minimum,
    /// <summary>
    /// max(src, dst).
    /// </summary>
    Maximum,
}
