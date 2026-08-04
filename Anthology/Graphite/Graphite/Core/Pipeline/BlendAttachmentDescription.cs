using System;

namespace Prowl.Graphite;

/// <summary>
/// One attachment's blend behavior.
/// </summary>
public struct BlendAttachmentDescription : IEquatable<BlendAttachmentDescription>
{
    /// <summary>
    /// Blend on/off.
    /// </summary>
    public bool BlendEnabled;
    /// <summary>
    /// Which channels write. Null = all.
    /// </summary>
    public ColorWriteMask? ColorWriteMask;

    /// <summary>
    /// Source color weight.
    /// </summary>
    public BlendFactor SourceColorFactor;
    /// <summary>
    /// Dest color weight.
    /// </summary>
    public BlendFactor DestinationColorFactor;
    /// <summary>
    /// Color combine op.
    /// </summary>
    public BlendFunction ColorFunction;
    /// <summary>
    /// Source alpha weight.
    /// </summary>
    public BlendFactor SourceAlphaFactor;
    /// <summary>
    /// Dest alpha weight.
    /// </summary>
    public BlendFactor DestinationAlphaFactor;
    /// <summary>
    /// Alpha combine op.
    /// </summary>
    public BlendFunction AlphaFunction;

    /// <summary>
    /// New blend attachment desc.
    /// </summary>
    /// <param name="blendEnabled">On/off.</param>
    /// <param name="sourceColorFactor">Source color weight.</param>
    /// <param name="destinationColorFactor">Dest color weight.</param>
    /// <param name="colorFunction">Color combine op.</param>
    /// <param name="sourceAlphaFactor">Source alpha weight.</param>
    /// <param name="destinationAlphaFactor">Dest alpha weight.</param>
    /// <param name="alphaFunction">Alpha combine op.</param>
    public BlendAttachmentDescription(
        bool blendEnabled,
        BlendFactor sourceColorFactor,
        BlendFactor destinationColorFactor,
        BlendFunction colorFunction,
        BlendFactor sourceAlphaFactor,
        BlendFactor destinationAlphaFactor,
        BlendFunction alphaFunction)
    {
        BlendEnabled = blendEnabled;
        SourceColorFactor = sourceColorFactor;
        DestinationColorFactor = destinationColorFactor;
        ColorFunction = colorFunction;
        SourceAlphaFactor = sourceAlphaFactor;
        DestinationAlphaFactor = destinationAlphaFactor;
        AlphaFunction = alphaFunction;
        ColorWriteMask = null;
    }

    /// <summary>
    /// New blend attachment desc.
    /// </summary>
    /// <param name="blendEnabled">On/off.</param>
    /// <param name="colorWriteMask">Which channels write.</param>
    /// <param name="sourceColorFactor">Source color weight.</param>
    /// <param name="destinationColorFactor">Dest color weight.</param>
    /// <param name="colorFunction">Color combine op.</param>
    /// <param name="sourceAlphaFactor">Source alpha weight.</param>
    /// <param name="destinationAlphaFactor">Dest alpha weight.</param>
    /// <param name="alphaFunction">Alpha combine op.</param>
    public BlendAttachmentDescription(
        bool blendEnabled,
        ColorWriteMask colorWriteMask,
        BlendFactor sourceColorFactor,
        BlendFactor destinationColorFactor,
        BlendFunction colorFunction,
        BlendFactor sourceAlphaFactor,
        BlendFactor destinationAlphaFactor,
        BlendFunction alphaFunction)
    {
        BlendEnabled = blendEnabled;
        ColorWriteMask = colorWriteMask;
        SourceColorFactor = sourceColorFactor;
        DestinationColorFactor = destinationColorFactor;
        ColorFunction = colorFunction;
        SourceAlphaFactor = sourceAlphaFactor;
        DestinationAlphaFactor = destinationAlphaFactor;
        AlphaFunction = alphaFunction;
    }

    /// <summary>
    /// Source overwrites dest entirely.
    /// </summary>
    public static readonly BlendAttachmentDescription OverrideBlend = new()
    {
        BlendEnabled = true,
        SourceColorFactor = BlendFactor.One,
        DestinationColorFactor = BlendFactor.Zero,
        ColorFunction = BlendFunction.Add,
        SourceAlphaFactor = BlendFactor.One,
        DestinationAlphaFactor = BlendFactor.Zero,
        AlphaFunction = BlendFunction.Add,
    };

    /// <summary>
    /// Standard alpha blend.
    /// </summary>
    public static readonly BlendAttachmentDescription AlphaBlend = new()
    {
        BlendEnabled = true,
        SourceColorFactor = BlendFactor.SourceAlpha,
        DestinationColorFactor = BlendFactor.InverseSourceAlpha,
        ColorFunction = BlendFunction.Add,
        SourceAlphaFactor = BlendFactor.SourceAlpha,
        DestinationAlphaFactor = BlendFactor.InverseSourceAlpha,
        AlphaFunction = BlendFunction.Add,
    };

    /// <summary>
    /// Additive blend.
    /// </summary>
    public static readonly BlendAttachmentDescription AdditiveBlend = new()
    {
        BlendEnabled = true,
        SourceColorFactor = BlendFactor.SourceAlpha,
        DestinationColorFactor = BlendFactor.One,
        ColorFunction = BlendFunction.Add,
        SourceAlphaFactor = BlendFactor.SourceAlpha,
        DestinationAlphaFactor = BlendFactor.One,
        AlphaFunction = BlendFunction.Add,
    };

    /// <summary>
    /// No blending.
    /// </summary>
    public static readonly BlendAttachmentDescription Disabled = new()
    {
        BlendEnabled = false,
        SourceColorFactor = BlendFactor.One,
        DestinationColorFactor = BlendFactor.Zero,
        ColorFunction = BlendFunction.Add,
        SourceAlphaFactor = BlendFactor.One,
        DestinationAlphaFactor = BlendFactor.Zero,
        AlphaFunction = BlendFunction.Add,
    };

    /// <summary>
    /// Field equality.
    /// </summary>
    /// <param name="other">To compare against.</param>
    /// <returns>True if fields match.</returns>
    public bool Equals(BlendAttachmentDescription other)
    {
        return BlendEnabled.Equals(other.BlendEnabled)
            && ColorWriteMask.Equals(other.ColorWriteMask)
            && SourceColorFactor == other.SourceColorFactor
            && DestinationColorFactor == other.DestinationColorFactor && ColorFunction == other.ColorFunction
            && SourceAlphaFactor == other.SourceAlphaFactor && DestinationAlphaFactor == other.DestinationAlphaFactor
            && AlphaFunction == other.AlphaFunction;
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    /// <returns>32-bit hash.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            BlendEnabled.GetHashCode(),
            ColorWriteMask.GetHashCode(),
            (int)SourceColorFactor,
            (int)DestinationColorFactor,
            (int)ColorFunction,
            (int)SourceAlphaFactor,
            (int)DestinationAlphaFactor,
            (int)AlphaFunction);
    }
}
