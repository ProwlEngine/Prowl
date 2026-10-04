using System;

namespace Prowl.Graphite;

/// <summary>
/// TextureView creation params.
/// </summary>
public struct TextureViewDescription
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
}
