using System;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>
/// How values blend into each color target for a GraphicsProgram.
/// </summary>
public struct BlendStateDescription : IEquatable<BlendStateDescription>
{
    /// <summary>
    /// Constant blend color for BlendFactor/InverseBlendFactor modes. Ignored otherwise.
    /// </summary>
    public Color BlendFactor;
    /// <summary>
    /// Blend attachment states, one per color target.
    /// </summary>
    public BlendAttachmentDescription[] AttachmentStates;
    /// <summary>
    /// Alpha-to-coverage: use fragment alpha for multi-sample coverage.
    /// </summary>
    public bool AlphaToCoverageEnabled;

    /// <summary>
    /// New blend state.
    /// </summary>
    /// <param name="blendFactor">Constant blend color.</param>
    /// <param name="attachmentStates">Blend attachment states.</param>
    public BlendStateDescription(Color blendFactor, params BlendAttachmentDescription[] attachmentStates)
    {
        BlendFactor = blendFactor;
        AttachmentStates = attachmentStates;
        AlphaToCoverageEnabled = false;
    }

    /// <summary>
    /// New blend state.
    /// </summary>
    /// <param name="blendFactor">Constant blend color.</param>
    /// <param name="alphaToCoverageEnabled">Use fragment alpha for multi-sample coverage.</param>
    /// <param name="attachmentStates">Blend attachment states.</param>
    public BlendStateDescription(
        Color blendFactor,
        bool alphaToCoverageEnabled,
        params BlendAttachmentDescription[] attachmentStates)
    {
        BlendFactor = blendFactor;
        AttachmentStates = attachmentStates;
        AlphaToCoverageEnabled = alphaToCoverageEnabled;
    }

    /// <summary>
    /// Single color target, override blend.
    /// </summary>
    public static readonly BlendStateDescription SingleOverrideBlend = new()
    {
        AttachmentStates = [BlendAttachmentDescription.OverrideBlend]
    };

    /// <summary>
    /// Single color target, alpha blend.
    /// </summary>
    public static readonly BlendStateDescription SingleAlphaBlend = new()
    {
        AttachmentStates = [BlendAttachmentDescription.AlphaBlend]
    };

    /// <summary>
    /// Single color target, additive blend.
    /// </summary>
    public static readonly BlendStateDescription SingleAdditiveBlend = new()
    {
        AttachmentStates = [BlendAttachmentDescription.AdditiveBlend]
    };

    /// <summary>
    /// Single color target, blend disabled.
    /// </summary>
    public static readonly BlendStateDescription SingleDisabled = new()
    {
        AttachmentStates = [BlendAttachmentDescription.Disabled]
    };

    /// <summary>
    /// No color targets.
    /// </summary>
    public static readonly BlendStateDescription Empty = new()
    {
        AttachmentStates = Array.Empty<BlendAttachmentDescription>()
    };

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare to.</param>
    /// <returns>True if all fields and array elements match.</returns>
    public bool Equals(BlendStateDescription other)
    {
        return BlendFactor.Equals(other.BlendFactor)
            && AlphaToCoverageEnabled.Equals(other.AlphaToCoverageEnabled)
            && Util.ArrayEqualsEquatable(AttachmentStates, other.AttachmentStates);
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            BlendFactor.GetHashCode(),
            AlphaToCoverageEnabled.GetHashCode(),
            AttachmentStates.ArrayHash());
    }

    internal readonly BlendStateDescription ShallowClone()
    {
        BlendStateDescription result = this;
        result.AttachmentStates = Util.ShallowClone(result.AttachmentStates);
        return result;
    }
}
