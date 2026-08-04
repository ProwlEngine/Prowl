namespace Prowl.Graphite;

/// <summary>
/// Stencil operation for pass/fail samples.
/// </summary>
public enum StencilOperation : byte
{
    /// <summary>
    /// Keep value.
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
    /// Increment and clamp to max.
    /// </summary>
    IncrementAndClamp,
    /// <summary>
    /// Decrement and clamp to 0.
    /// </summary>
    DecrementAndClamp,
    /// <summary>
    /// Bitwise invert.
    /// </summary>
    Invert,
    /// <summary>
    /// Increment and wrap.
    /// </summary>
    IncrementAndWrap,
    /// <summary>
    /// Decrement and wrap.
    /// </summary>
    DecrementAndWrap,
}
