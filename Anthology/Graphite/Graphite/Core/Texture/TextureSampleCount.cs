namespace Prowl.Graphite;

/// <summary>
/// Sample count for a texture.
/// </summary>
public enum TextureSampleCount : byte
{
    /// <summary>
    /// No multisampling.
    /// </summary>
    Count1,
    /// <summary>
    /// 2 samples.
    /// </summary>
    Count2,
    /// <summary>
    /// 4 samples.
    /// </summary>
    Count4,
    /// <summary>
    /// 8 samples.
    /// </summary>
    Count8,
    /// <summary>
    /// 16 samples.
    /// </summary>
    Count16,
    /// <summary>
    /// 32 samples.
    /// </summary>
    Count32,
}
