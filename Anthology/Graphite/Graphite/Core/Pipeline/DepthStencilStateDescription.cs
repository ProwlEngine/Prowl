using System;

namespace Prowl.Graphite;

/// <summary>
/// Depth stencil state for a GraphicsProgram.
/// </summary>
public struct DepthStencilStateDescription : IEquatable<DepthStencilStateDescription>
{
    /// <summary>
    /// Depth test on/off.
    /// </summary>
    public bool DepthTestEnabled;
    /// <summary>
    /// Write new depth values to buffer.
    /// </summary>
    public bool DepthWriteEnabled;
    /// <summary>
    /// Comparison used for depth values.
    /// </summary>
    public ComparisonKind DepthComparison;

    /// <summary>
    /// Stencil test on/off.
    /// </summary>
    public bool StencilTestEnabled;
    /// <summary>
    /// Stencil behavior for front-facing pixels.
    /// </summary>
    public StencilBehaviorDescription StencilFront;
    /// <summary>
    /// Stencil behavior for back-facing pixels.
    /// </summary>
    public StencilBehaviorDescription StencilBack;
    /// <summary>
    /// Stencil buffer read mask.
    /// </summary>
    public byte StencilReadMask;
    /// <summary>
    /// Stencil buffer write mask.
    /// </summary>
    public byte StencilWriteMask;
    /// <summary>
    /// Reference value for stencil test.
    /// </summary>
    public uint StencilReference;

    /// <summary>
    /// New depth-stencil state, stencil testing disabled.
    /// </summary>
    /// <param name="depthTestEnabled">Depth test on/off.</param>
    /// <param name="depthWriteEnabled">Write new depth values to buffer.</param>
    /// <param name="comparisonKind">Comparison used for depth values.</param>
    public DepthStencilStateDescription(bool depthTestEnabled, bool depthWriteEnabled, ComparisonKind comparisonKind)
    {
        DepthTestEnabled = depthTestEnabled;
        DepthWriteEnabled = depthWriteEnabled;
        DepthComparison = comparisonKind;

        StencilTestEnabled = false;
        StencilFront = default;
        StencilBack = default;
        StencilReadMask = 0;
        StencilWriteMask = 0;
        StencilReference = 0;
    }

    /// <summary>
    /// New depth-stencil state with full stencil config.
    /// </summary>
    /// <param name="depthTestEnabled">Depth test on/off.</param>
    /// <param name="depthWriteEnabled">Write new depth values to buffer.</param>
    /// <param name="comparisonKind">Comparison used for depth values.</param>
    /// <param name="stencilTestEnabled">Stencil test on/off.</param>
    /// <param name="stencilFront">Stencil behavior for front-facing pixels.</param>
    /// <param name="stencilBack">Stencil behavior for back-facing pixels.</param>
    /// <param name="stencilReadMask">Stencil buffer read mask.</param>
    /// <param name="stencilWriteMask">Stencil buffer write mask.</param>
    /// <param name="stencilReference">Reference value for stencil test.</param>
    public DepthStencilStateDescription(
        bool depthTestEnabled,
        bool depthWriteEnabled,
        ComparisonKind comparisonKind,
        bool stencilTestEnabled,
        StencilBehaviorDescription stencilFront,
        StencilBehaviorDescription stencilBack,
        byte stencilReadMask,
        byte stencilWriteMask,
        uint stencilReference)
    {
        DepthTestEnabled = depthTestEnabled;
        DepthWriteEnabled = depthWriteEnabled;
        DepthComparison = comparisonKind;

        StencilTestEnabled = stencilTestEnabled;
        StencilFront = stencilFront;
        StencilBack = stencilBack;
        StencilReadMask = stencilReadMask;
        StencilWriteMask = stencilWriteMask;
        StencilReference = stencilReference;
    }

    /// <summary>
    /// Depth-only, LessEqual, write on. No stencil.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyLessEqual = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        DepthComparison = ComparisonKind.LessEqual
    };

    /// <summary>
    /// Depth-only, LessEqual, write off (read-only). No stencil.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyLessEqualRead = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = false,
        DepthComparison = ComparisonKind.LessEqual
    };

    /// <summary>
    /// Depth-only, GreaterEqual, write on. No stencil.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyGreaterEqual = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        DepthComparison = ComparisonKind.GreaterEqual
    };

    /// <summary>
    /// Depth-only, GreaterEqual, write off (read-only). No stencil.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyGreaterEqualRead = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = false,
        DepthComparison = ComparisonKind.GreaterEqual
    };

    /// <summary>
    /// Depth test and write off. No stencil.
    /// </summary>
    public static readonly DepthStencilStateDescription Disabled = new()
    {
        DepthTestEnabled = false,
        DepthWriteEnabled = false,
        DepthComparison = ComparisonKind.LessEqual
    };

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if all fields match.</returns>
    public bool Equals(DepthStencilStateDescription other)
    {
        return DepthTestEnabled.Equals(other.DepthTestEnabled)
            && DepthWriteEnabled.Equals(other.DepthWriteEnabled)
            && DepthComparison == other.DepthComparison
            && StencilTestEnabled.Equals(other.StencilTestEnabled)
            && StencilFront.Equals(other.StencilFront)
            && StencilBack.Equals(other.StencilBack)
            && StencilReadMask.Equals(other.StencilReadMask)
            && StencilWriteMask.Equals(other.StencilWriteMask)
            && StencilReference.Equals(other.StencilReference);
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>32-bit hash.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(
                DepthTestEnabled.GetHashCode(),
                DepthWriteEnabled.GetHashCode(),
                (int)DepthComparison,
                StencilTestEnabled.GetHashCode(),
                StencilFront.GetHashCode(),
                StencilBack.GetHashCode(),
                StencilReadMask.GetHashCode(),
                StencilWriteMask.GetHashCode()),
            StencilReference.GetHashCode());
    }
}
