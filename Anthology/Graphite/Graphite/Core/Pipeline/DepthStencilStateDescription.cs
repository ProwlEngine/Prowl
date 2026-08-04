using System;

namespace Prowl.Graphite;

/// <summary>
/// Depth stencil state for a program.
/// </summary>
public struct DepthStencilStateDescription : IEquatable<DepthStencilStateDescription>
{
    /// <summary>
    /// Depth test on/off.
    /// </summary>
    public bool DepthTestEnabled;
    /// <summary>
    /// Write depth to buffer.
    /// </summary>
    public bool DepthWriteEnabled;
    /// <summary>
    /// Depth compare op.
    /// </summary>
    public ComparisonKind DepthComparison;

    /// <summary>
    /// Stencil test on/off.
    /// </summary>
    public bool StencilTestEnabled;
    /// <summary>
    /// Front-face stencil behavior.
    /// </summary>
    public StencilBehaviorDescription StencilFront;
    /// <summary>
    /// Back-face stencil behavior.
    /// </summary>
    public StencilBehaviorDescription StencilBack;
    /// <summary>
    /// Stencil read mask.
    /// </summary>
    public byte StencilReadMask;
    /// <summary>
    /// Stencil write mask.
    /// </summary>
    public byte StencilWriteMask;
    /// <summary>
    /// Stencil ref value.
    /// </summary>
    public uint StencilReference;

    /// <summary>
    /// No stencil, just depth.
    /// </summary>
    /// <param name="depthTestEnabled">Depth test on/off.</param>
    /// <param name="depthWriteEnabled">Write depth to buffer.</param>
    /// <param name="comparisonKind">Depth compare op.</param>
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
    /// Full depth + stencil config.
    /// </summary>
    /// <param name="depthTestEnabled">Depth test on/off.</param>
    /// <param name="depthWriteEnabled">Write depth to buffer.</param>
    /// <param name="comparisonKind">Depth compare op.</param>
    /// <param name="stencilTestEnabled">Stencil test on/off.</param>
    /// <param name="stencilFront">Front-face stencil behavior.</param>
    /// <param name="stencilBack">Back-face stencil behavior.</param>
    /// <param name="stencilReadMask">Stencil read mask.</param>
    /// <param name="stencilWriteMask">Stencil write mask.</param>
    /// <param name="stencilReference">Stencil ref value.</param>
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
    /// Depth-only, LessEqual, write on.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyLessEqual = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        DepthComparison = ComparisonKind.LessEqual
    };

    /// <summary>
    /// Depth-only, LessEqual, read-only.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyLessEqualRead = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = false,
        DepthComparison = ComparisonKind.LessEqual
    };

    /// <summary>
    /// Depth-only, GreaterEqual, write on.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyGreaterEqual = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        DepthComparison = ComparisonKind.GreaterEqual
    };

    /// <summary>
    /// Depth-only, GreaterEqual, read-only.
    /// </summary>
    public static readonly DepthStencilStateDescription DepthOnlyGreaterEqualRead = new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = false,
        DepthComparison = ComparisonKind.GreaterEqual
    };

    /// <summary>
    /// Everything off.
    /// </summary>
    public static readonly DepthStencilStateDescription Disabled = new()
    {
        DepthTestEnabled = false,
        DepthWriteEnabled = false,
        DepthComparison = ComparisonKind.LessEqual
    };

    /// <summary>
    /// Field-by-field equality check.
    /// </summary>
    /// <param name="other">Other instance.</param>
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
    /// Hash of all fields.
    /// </summary>
    /// <returns>Hash code.</returns>
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
