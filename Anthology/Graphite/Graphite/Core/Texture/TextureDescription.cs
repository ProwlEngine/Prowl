using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a Texture for creation via ResourceFactory.
/// </summary>
public struct TextureDescription : IEquatable<TextureDescription>
{
    /// <summary>
    /// Width in texels.
    /// </summary>
    public uint Width;
    /// <summary>
    /// Height in texels.
    /// </summary>
    public uint Height;
    /// <summary>
    /// Depth in texels.
    /// </summary>
    public uint Depth;
    /// <summary>
    /// Mipmap level count.
    /// </summary>
    public uint MipLevels;
    /// <summary>
    /// Array layer count.
    /// </summary>
    public uint ArrayLayers;
    /// <summary>
    /// Format of each texel.
    /// </summary>
    public PixelFormat Format;
    /// <summary>
    /// Allowed usages. Set Sampled if sampled in a shader, DepthStencil if used as a depth target,
    /// RenderTarget if used as a color target, Cubemap if it's a 2D cubemap.
    /// </summary>
    public TextureUsage Usage;
    /// <summary>
    /// Texture type to create.
    /// </summary>
    public TextureType Type;
    /// <summary>
    /// Sample count. Count1 means not multisampled.
    /// </summary>
    public TextureSampleCount SampleCount;

    /// <summary>
    /// Makes a non-multisampled TextureDescription.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="depth">Depth in texels.</param>
    /// <param name="mipLevels">Mipmap level count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Format of each texel.</param>
    /// <param name="usage">Allowed usages. Sampled/DepthStencil/RenderTarget/Cubemap as needed.</param>
    /// <param name="type">Texture type to create.</param>
    public TextureDescription(
        uint width,
        uint height,
        uint depth,
        uint mipLevels,
        uint arrayLayers,
        PixelFormat format,
        TextureUsage usage,
        TextureType type)
    {
        Width = width;
        Height = height;
        Depth = depth;
        MipLevels = mipLevels;
        ArrayLayers = arrayLayers;
        Format = format;
        Usage = usage;
        SampleCount = TextureSampleCount.Count1;
        Type = type;
    }

    /// <summary>
    /// Makes a new TextureDescription.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="depth">Depth in texels.</param>
    /// <param name="mipLevels">Mipmap level count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Format of each texel.</param>
    /// <param name="usage">Allowed usages. Sampled/DepthStencil/RenderTarget/Cubemap as needed.</param>
    /// <param name="type">Texture type to create.</param>
    /// <param name="sampleCount">Sample count. Anything but Count1 makes this multisampled.</param>
    public TextureDescription(
        uint width,
        uint height,
        uint depth,
        uint mipLevels,
        uint arrayLayers,
        PixelFormat format,
        TextureUsage usage,
        TextureType type,
        TextureSampleCount sampleCount)
    {
        Width = width;
        Height = height;
        Depth = depth;
        MipLevels = mipLevels;
        ArrayLayers = arrayLayers;
        Format = format;
        Usage = usage;
        Type = type;
        SampleCount = sampleCount;
    }

    /// <summary>
    /// Description for a non-multisampled 1D Texture.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="mipLevels">Mipmap level count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Format of each texel.</param>
    /// <param name="usage">Allowed usages. Sampled/DepthStencil/RenderTarget as needed.</param>
    /// <returns>A TextureDescription for a non-multisampled 1D Texture.</returns>
    public static TextureDescription Texture1D(
        uint width,
        uint mipLevels,
        uint arrayLayers,
        PixelFormat format,
        TextureUsage usage)
    {
        return new TextureDescription(
            width,
            1,
            1,
            mipLevels,
            arrayLayers,
            format,
            usage,
            TextureType.Texture1D,
            TextureSampleCount.Count1);
    }

    /// <summary>
    /// Description for a non-multisampled 2D Texture.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="mipLevels">Mipmap level count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Format of each texel.</param>
    /// <param name="usage">Allowed usages. Sampled/DepthStencil/RenderTarget/Cubemap as needed.</param>
    /// <returns>A TextureDescription for a non-multisampled 2D Texture.</returns>
    public static TextureDescription Texture2D(
        uint width,
        uint height,
        uint mipLevels,
        uint arrayLayers,
        PixelFormat format,
        TextureUsage usage)
    {
        return new TextureDescription(
            width,
            height,
            1,
            mipLevels,
            arrayLayers,
            format,
            usage,
            TextureType.Texture2D,
            TextureSampleCount.Count1);
    }

    /// <summary>
    /// Description for a 2D Texture.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="mipLevels">Mipmap level count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Format of each texel.</param>
    /// <param name="usage">Allowed usages. Sampled/DepthStencil/RenderTarget/Cubemap as needed.</param>
    /// <param name="sampleCount">Sample count. Anything but Count1 makes this multisampled.</param>
    /// <returns>A TextureDescription for a 2D Texture.</returns>
    public static TextureDescription Texture2D(
        uint width,
        uint height,
        uint mipLevels,
        uint arrayLayers,
        PixelFormat format,
        TextureUsage usage,
        TextureSampleCount sampleCount)
    {
        return new TextureDescription(
            width,
            height,
            1,
            mipLevels,
            arrayLayers,
            format,
            usage,
            TextureType.Texture2D,
            sampleCount);
    }

    /// <summary>
    /// Description for a 3D Texture.
    /// </summary>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="depth">Depth in texels.</param>
    /// <param name="mipLevels">Mipmap level count.</param>
    /// <param name="format">Format of each texel.</param>
    /// <param name="usage">Allowed usages. Sampled/DepthStencil/RenderTarget as needed.</param>
    /// <returns>A TextureDescription for a 3D Texture.</returns>
    public static TextureDescription Texture3D(
        uint width,
        uint height,
        uint depth,
        uint mipLevels,
        PixelFormat format,
        TextureUsage usage)
    {
        return new TextureDescription(
            width,
            height,
            depth,
            mipLevels,
            1,
            format,
            usage,
            TextureType.Texture3D,
            TextureSampleCount.Count1);
    }

    /// <summary>
    /// Element-wise equality check.
    /// </summary>
    /// <param name="other">Instance to compare against.</param>
    /// <returns>True if every field matches.</returns>
    public readonly bool Equals(TextureDescription other)
    {
        return Width.Equals(other.Width)
            && Height.Equals(other.Height)
            && Depth.Equals(other.Depth)
            && MipLevels.Equals(other.MipLevels)
            && ArrayLayers.Equals(other.ArrayLayers)
            && Format == other.Format
            && Usage == other.Usage
            && Type == other.Type
            && SampleCount == other.SampleCount;
    }

    /// <summary>
    /// Hash code for this instance.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(
                Width.GetHashCode(),
                Height.GetHashCode(),
                Depth.GetHashCode(),
                MipLevels.GetHashCode(),
                ArrayLayers.GetHashCode(),
                (int)Format,
                (int)Usage,
                (int)Type),
            (int)SampleCount);
    }
}
