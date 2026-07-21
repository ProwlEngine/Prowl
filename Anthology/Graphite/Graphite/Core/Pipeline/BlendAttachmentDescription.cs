using System;

namespace Prowl.Graphite;

/// <summary>
/// Blend behavior for one color attachment.
/// </summary>
public struct BlendAttachmentDescription : IEquatable<BlendAttachmentDescription>
{
    /// <summary>
    /// Blending on/off for this attachment.
    /// </summary>
    public bool BlendEnabled;
    /// <summary>
    /// Which color channels get written. Null means All.
    /// </summary>
    public ColorWriteMask? ColorWriteMask;

    /// <summary>
    /// Source color weight.
    /// </summary>
    public BlendFactor SourceColorFactor;
    /// <summary>
    /// Destination color weight.
    /// </summary>
    public BlendFactor DestinationColorFactor;
    /// <summary>
    /// How source/dest color factors combine.
    /// </summary>
    public BlendFunction ColorFunction;
    /// <summary>
    /// Source alpha weight.
    /// </summary>
    public BlendFactor SourceAlphaFactor;
    /// <summary>
    /// Destination alpha weight.
    /// </summary>
    public BlendFactor DestinationAlphaFactor;
    /// <summary>
    /// How source/dest alpha factors combine.
    /// </summary>
    public BlendFunction AlphaFunction;

    /// <summary>
    /// New blend attachment description.
    /// </summary>
    /// <param name="blendEnabled">Blending on/off.</param>
    /// <param name="sourceColorFactor">Source color weight.</param>
    /// <param name="destinationColorFactor">Destination color weight.</param>
    /// <param name="colorFunction">How color factors combine.</param>
    /// <param name="sourceAlphaFactor">Source alpha weight.</param>
    /// <param name="destinationAlphaFactor">Destination alpha weight.</param>
    /// <param name="alphaFunction">How alpha factors combine.</param>
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
    /// New blend attachment description.
    /// </summary>
    /// <param name="blendEnabled">Blending on/off.</param>
    /// <param name="colorWriteMask">Which color channels get written.</param>
    /// <param name="sourceColorFactor">Source color weight.</param>
    /// <param name="destinationColorFactor">Destination color weight.</param>
    /// <param name="colorFunction">How color factors combine.</param>
    /// <param name="sourceAlphaFactor">Source alpha weight.</param>
    /// <param name="destinationAlphaFactor">Destination alpha weight.</param>
    /// <param name="alphaFunction">How alpha factors combine.</param>
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
    /// Source fully overwrites dest.
    /// Settings:
    ///     BlendEnabled = true
    ///     ColorWriteMask = null
    ///     SourceColorFactor = BlendFactor.One
    ///     DestinationColorFactor = BlendFactor.Zero
    ///     ColorFunction = BlendFunction.Add
    ///     SourceAlphaFactor = BlendFactor.One
    ///     DestinationAlphaFactor = BlendFactor.Zero
    ///     AlphaFunction = BlendFunction.Add
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
    /// Standard alpha blend, source and dest mix inversely by alpha.
    /// Settings:
    ///     BlendEnabled = true
    ///     ColorWriteMask = null
    ///     SourceColorFactor = BlendFactor.SourceAlpha
    ///     DestinationColorFactor = BlendFactor.InverseSourceAlpha
    ///     ColorFunction = BlendFunction.Add
    ///     SourceAlphaFactor = BlendFactor.SourceAlpha
    ///     DestinationAlphaFactor = BlendFactor.InverseSourceAlpha
    ///     AlphaFunction = BlendFunction.Add
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
    /// Additive blend, source adds to dest weighted by its alpha.
    /// Settings:
    ///     BlendEnabled = true
    ///     ColorWriteMask = null
    ///     SourceColorFactor = BlendFactor.SourceAlpha
    ///     DestinationColorFactor = BlendFactor.One
    ///     ColorFunction = BlendFunction.Add
    ///     SourceAlphaFactor = BlendFactor.SourceAlpha
    ///     DestinationAlphaFactor = BlendFactor.One
    ///     AlphaFunction = BlendFunction.Add
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
    /// Settings:
    ///     BlendEnabled = false
    ///     ColorWriteMask = null
    ///     SourceColorFactor = BlendFactor.One
    ///     DestinationColorFactor = BlendFactor.Zero
    ///     ColorFunction = BlendFunction.Add
    ///     SourceAlphaFactor = BlendFactor.One
    ///     DestinationAlphaFactor = BlendFactor.Zero
    ///     AlphaFunction = BlendFunction.Add
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
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if all fields match.</returns>
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
    /// Hash code for this instance.
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
