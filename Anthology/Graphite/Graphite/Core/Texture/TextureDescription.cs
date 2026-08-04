using System;

namespace Prowl.Graphite;

/// <summary>
/// Texture creation params for ResourceFactory.
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
    /// Mip level count.
    /// </summary>
    public uint MipLevels;
    /// <summary>
    /// Array layer count.
    /// </summary>
    public uint ArrayLayers;
    /// <summary>
    /// Texel format.
    /// </summary>
    public PixelFormat Format;
    /// <summary>
    /// Allowed usages: sampled, depth, render target, cubemap.
    /// </summary>
    public TextureUsage Usage;
    /// <summary>
    /// Texture type.
    /// </summary>
    public TextureType Type;
    /// <summary>
    /// Sample count. Count1 = not multisampled.
    /// </summary>
    public TextureSampleCount SampleCount;

    /// <summary>
    /// Non-multisampled texture desc.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="depth">Depth.</param>
    /// <param name="mipLevels">Mip count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Texel format.</param>
    /// <param name="usage">Allowed usages.</param>
    /// <param name="type">Texture type.</param>
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
    /// Makes a texture desc.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="depth">Depth.</param>
    /// <param name="mipLevels">Mip count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Texel format.</param>
    /// <param name="usage">Allowed usages.</param>
    /// <param name="type">Texture type.</param>
    /// <param name="sampleCount">Sample count, non-Count1 = multisampled.</param>
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
    /// Non-multisampled 1D texture desc.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="mipLevels">Mip count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Texel format.</param>
    /// <param name="usage">Allowed usages.</param>
    /// <returns>1D texture desc.</returns>
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
    /// Non-multisampled 2D texture desc.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="mipLevels">Mip count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Texel format.</param>
    /// <param name="usage">Allowed usages.</param>
    /// <returns>2D texture desc.</returns>
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
    /// 2D texture desc.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="mipLevels">Mip count.</param>
    /// <param name="arrayLayers">Array layer count.</param>
    /// <param name="format">Texel format.</param>
    /// <param name="usage">Allowed usages.</param>
    /// <param name="sampleCount">Sample count, non-Count1 = multisampled.</param>
    /// <returns>2D texture desc.</returns>
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
    /// 3D texture desc.
    /// </summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="depth">Depth.</param>
    /// <param name="mipLevels">Mip count.</param>
    /// <param name="format">Texel format.</param>
    /// <param name="usage">Allowed usages.</param>
    /// <returns>3D texture desc.</returns>
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
    /// Field-by-field equality.
    /// </summary>
    /// <param name="other">Instance to compare.</param>
    /// <returns>True if all fields match.</returns>
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
    /// Hash of this instance.
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
