namespace Prowl.Graphite;

/// <summary>
/// Action taken on samples that pass or fail the stencil test.
/// </summary>
public enum StencilOperation : byte
{
    /// <summary>
    /// Keep existing value.
    /// </summary>
    Keep,
    /// <summary>
    /// Set to 0.
    /// </summary>
    Zero,
    /// <summary>
    /// Replace with stencil reference.
    /// </summary>
    Replace,
    /// <summary>
    /// Increment, clamp to max unsigned value.
    /// </summary>
    IncrementAndClamp,
    /// <summary>
    /// Decrement, clamp to 0.
    /// </summary>
    DecrementAndClamp,
    /// <summary>
    /// Bitwise invert.
    /// </summary>
    Invert,
    /// <summary>
    /// Increment, wrap to 0 past max unsigned value.
    /// </summary>
    IncrementAndWrap,
    /// <summary>
    /// Decrement, wrap to max unsigned value if it'd go below 0.
    /// </summary>
    DecrementAndWrap,
}
