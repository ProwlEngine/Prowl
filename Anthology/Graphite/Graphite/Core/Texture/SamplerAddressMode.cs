namespace Prowl.Graphite;

/// <summary>
/// Texture coordinate addressing mode.
/// </summary>
public enum SamplerAddressMode : byte
{
    /// <summary>
    /// Wraps on overflow.
    /// </summary>
    Wrap,
    /// <summary>
    /// Mirrors on overflow.
    /// </summary>
    Mirror,
    /// <summary>
    /// Clamps to min/max on overflow.
    /// </summary>
    Clamp,
    /// <summary>
    /// Returns the sampler's border color on overflow.
    /// </summary>
    Border,
}
