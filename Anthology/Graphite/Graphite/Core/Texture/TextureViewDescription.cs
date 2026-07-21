using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a TextureView for creation via ResourceFactory.
/// </summary>
public struct TextureViewDescription : IEquatable<TextureViewDescription>
{
    /// <summary>
    /// Target texture.
    /// </summary>
    public Texture Target;
    /// <summary>
    /// Base mip level in the view. Must be less than target's MipLevels.
    /// </summary>
    public uint BaseMipLevel;
    /// <summary>
    /// Mip levels visible in the view.
    /// </summary>
    public uint MipLevels;
    /// <summary>
    /// Base array layer in the view.
    /// </summary>
    public uint BaseArrayLayer;
    /// <summary>
    /// Array layers visible in the view.
    /// </summary>
    public uint ArrayLayers;
    /// <summary>
    /// Optional format override for the view. Null means use the target's format. If set, must be
    /// compatible with the target's format: same size/component count for uncompressed, same or
    /// sRGB-counterpart for compressed.
    /// </summary>
    public PixelFormat? Format;

    /// <summary>
    /// Constructs a new TextureViewDescription.
    /// </summary>
    /// <param name="target">Target texture. Must have been created with the Sampled usage flag.</param>
    public TextureViewDescription(Texture target)
    {
        Target = target;
        BaseMipLevel = 0;
        MipLevels = target.MipLevels;
        BaseArrayLayer = 0;
        ArrayLayers = target.ArrayLayers;
        Format = target.Format;
    }

    /// <summary>
    /// Constructs a new TextureViewDescription.
    /// </summary>
    /// <param name="target">Target texture. Must have been created with the Sampled usage flag.</param>
    /// <param name="format">Format override. Must be compatible with the target's format.</param>
    public TextureViewDescription(Texture target, PixelFormat format)
    {
        Target = target;
        BaseMipLevel = 0;
        MipLevels = target.MipLevels;
        BaseArrayLayer = 0;
        ArrayLayers = target.ArrayLayers;
        Format = format;
    }

    /// <summary>
    /// Constructs a new TextureViewDescription.
    /// </summary>
    /// <param name="target">Target texture.</param>
    /// <param name="baseMipLevel">Base mip level. Must be less than target's MipLevels.</param>
    /// <param name="mipLevels">Mip levels visible in the view.</param>
    /// <param name="baseArrayLayer">Base array layer.</param>
    /// <param name="arrayLayers">Array layers visible in the view.</param>
    public TextureViewDescription(Texture target, uint baseMipLevel, uint mipLevels, uint baseArrayLayer, uint arrayLayers)
    {
        Target = target;
        BaseMipLevel = baseMipLevel;
        MipLevels = mipLevels;
        BaseArrayLayer = baseArrayLayer;
        ArrayLayers = arrayLayers;
        Format = target.Format;
    }

    /// <summary>
    /// Constructs a new TextureViewDescription.
    /// </summary>
    /// <param name="target">Target texture.</param>
    /// <param name="format">Format override. Must be compatible with the target's format.</param>
    /// <param name="baseMipLevel">Base mip level. Must be less than target's MipLevels.</param>
    /// <param name="mipLevels">Mip levels visible in the view.</param>
    /// <param name="baseArrayLayer">Base array layer.</param>
    /// <param name="arrayLayers">Array layers visible in the view.</param>
    public TextureViewDescription(Texture target, PixelFormat format, uint baseMipLevel, uint mipLevels, uint baseArrayLayer, uint arrayLayers)
    {
        Target = target;
        BaseMipLevel = baseMipLevel;
        MipLevels = mipLevels;
        BaseArrayLayer = baseArrayLayer;
        ArrayLayers = arrayLayers;
        Format = format;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">Instance to compare to.</param>
    /// <returns>True if all fields match.</returns>
    public readonly bool Equals(TextureViewDescription other)
    {
        return Target.Equals(other.Target)
            && BaseMipLevel.Equals(other.BaseMipLevel)
            && MipLevels.Equals(other.MipLevels)
            && BaseArrayLayer.Equals(other.BaseArrayLayer)
            && ArrayLayers.Equals(other.ArrayLayers)
            && Format == other.Format;
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            Target.GetHashCode(),
            BaseMipLevel.GetHashCode(),
            MipLevels.GetHashCode(),
            BaseArrayLayer.GetHashCode(),
            ArrayLayers.GetHashCode(),
            Format?.GetHashCode() ?? 0);
    }
}
