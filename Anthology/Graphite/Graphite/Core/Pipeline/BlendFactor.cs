namespace Prowl.Graphite;

/// <summary>
/// Component weighting in blend operations.
/// </summary>
public enum BlendFactor : byte
{
    /// <summary>
    /// Multiply by 0.
    /// </summary>
    Zero,
    /// <summary>
    /// Multiply by 1.
    /// </summary>
    One,
    /// <summary>
    /// Multiply by source alpha.
    /// </summary>
    SourceAlpha,
    /// <summary>
    /// Multiply by 1 - source alpha.
    /// </summary>
    InverseSourceAlpha,
    /// <summary>
    /// Multiply by destination alpha.
    /// </summary>
    DestinationAlpha,
    /// <summary>
    /// Multiply by 1 - destination alpha.
    /// </summary>
    InverseDestinationAlpha,
    /// <summary>
    /// Source color.
    /// </summary>
    SourceColor,
    /// <summary>
    /// 1 - source color.
    /// </summary>
    InverseSourceColor,
    /// <summary>
    /// Destination color.
    /// </summary>
    DestinationColor,
    /// <summary>
    /// 1 - destination color.
    /// </summary>
    InverseDestinationColor,
    /// <summary>
    /// Blend constant.
    /// </summary>
    BlendFactor,
    /// <summary>
    /// 1 - blend constant.
    /// </summary>
    InverseBlendFactor,
}
