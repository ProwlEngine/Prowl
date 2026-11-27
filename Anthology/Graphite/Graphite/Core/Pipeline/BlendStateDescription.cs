using System;

using Prowl.Vector;

namespace Prowl.Graphite;

/// <summary>
/// Blend settings for each target.
/// </summary>
public record struct BlendStateDescription
{
    /// <summary>
    /// Constant blend color.
    /// </summary>
    public Color BlendFactor;
    /// <summary>
    /// Per-target blend states.
    /// </summary>
    public BlendAttachmentDescription[] AttachmentStates;
    /// <summary>
    /// MSAA coverage via fragment alpha.
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
    public static BlendStateDescription SingleOverrideBlend => new()
    {
        AttachmentStates = [BlendAttachmentDescription.OverrideBlend]
    };

    /// <summary>
    /// Single color target, alpha blend.
    /// </summary>
    public static BlendStateDescription SingleAlphaBlend => new()
    {
        AttachmentStates = [BlendAttachmentDescription.AlphaBlend]
    };

    /// <summary>
    /// Single color target, additive blend.
    /// </summary>
    public static BlendStateDescription SingleAdditiveBlend => new()
    {
        AttachmentStates = [BlendAttachmentDescription.AdditiveBlend]
    };

    /// <summary>
    /// Single color target, blend disabled.
    /// </summary>
    public static BlendStateDescription SingleDisabled => new()
    {
        AttachmentStates = [BlendAttachmentDescription.Disabled]
    };

    /// <summary>
    /// No color targets.
    /// </summary>
    public static BlendStateDescription Empty => new()
    {
        AttachmentStates = Array.Empty<BlendAttachmentDescription>()
    };

    internal readonly BlendStateDescription ShallowClone()
    {
        BlendStateDescription result = this;
        result.AttachmentStates = Util.ShallowClone(result.AttachmentStates);
        return result;
    }

    /// <inheritdoc/>
    public readonly bool Equals(BlendStateDescription other)
        => BlendFactor.Equals(other.BlendFactor)
        && AlphaToCoverageEnabled == other.AlphaToCoverageEnabled
        && Util.ArrayEqualsEquatable(AttachmentStates, other.AttachmentStates);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
        => HashCode.Combine(BlendFactor, AlphaToCoverageEnabled, AttachmentStates.ArrayHash());
}
