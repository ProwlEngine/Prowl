namespace Prowl.Graphite;

/// <summary>
/// How texels are combined when sampling.
/// </summary>
public enum FilterMode : byte
{
    /// <summary>
    /// Nearest texel.
    /// </summary>
    Point,
    /// <summary>
    /// Blend neighboring texels.
    /// </summary>
    Linear,
}
