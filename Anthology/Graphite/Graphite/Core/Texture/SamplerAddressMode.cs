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
    /// Clamps on overflow.
    /// </summary>
    Clamp,
    /// <summary>
    /// Border color on overflow.
    /// </summary>
    Border,
}
