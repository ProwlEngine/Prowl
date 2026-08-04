using System;

namespace Prowl.Graphite;

/// <summary>
/// TextureView creation params.
/// </summary>
public struct TextureViewDescription : IEquatable<TextureViewDescription>
{
    /// <summary>
    /// Target texture.
    /// </summary>
    public Texture Target;
    /// <summary>
    /// Base mip level. Must be under target's MipLevels.
    /// </summary>
    public uint BaseMipLevel;
    /// <summary>
    /// Visible mip levels.
    /// </summary>
    public uint MipLevels;
    /// <summary>
    /// Base array layer.
    /// </summary>
    public uint BaseArrayLayer;
    /// <summary>
    /// Visible array layers.
    /// </summary>
    public uint ArrayLayers;
    /// <summary>
    /// Format override. Null = use target's format. Must stay compatible if set.
    /// </summary>
    public PixelFormat? Format;

    /// <summary>
    /// New TextureViewDescription. Unset fields default from target.
    /// </summary>
    /// <param name="target">Target texture. Needs Sampled usage flag.</param>
    /// <param name="baseMipLevel">Base mip level. Must be under target's MipLevels.</param>
    /// <param name="mipLevels">Visible mip levels.</param>
    /// <param name="baseArrayLayer">Base array layer.</param>
    /// <param name="arrayLayers">Visible array layers.</param>
    /// <param name="format">Format override, must be compatible.</param>
    public TextureViewDescription(Texture target, uint? baseMipLevel = null, uint? mipLevels = null, uint? baseArrayLayer = null, uint? arrayLayers = null, PixelFormat? format = null)
    {
        Target = target;
        BaseMipLevel = baseMipLevel ?? 0;
        MipLevels = mipLevels ?? target.MipLevels;
        BaseArrayLayer = baseArrayLayer ?? 0;
        ArrayLayers = arrayLayers ?? target.ArrayLayers;
        Format = format ?? target.Format;
    }

    /// <summary>
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Other instance.</param>
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
    /// Hash code.
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
