namespace Prowl.Graphite;

/// <summary>
/// How components get weighted in a blend op.
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
    /// Multiply by matching source color component.
    /// </summary>
    SourceColor,
    /// <summary>
    /// Multiply by 1 - matching source color component.
    /// </summary>
    InverseSourceColor,
    /// <summary>
    /// Multiply by matching destination color component.
    /// </summary>
    DestinationColor,
    /// <summary>
    /// Multiply by 1 - matching destination color component.
    /// </summary>
    InverseDestinationColor,
    /// <summary>
    /// Multiply by matching component of the blend constant.
    /// </summary>
    BlendFactor,
    /// <summary>
    /// Multiply by 1 - matching component of the blend constant.
    /// </summary>
    InverseBlendFactor,
}
