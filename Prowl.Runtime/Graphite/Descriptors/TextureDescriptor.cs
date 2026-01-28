// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes how to create a GPU texture.
/// </summary>
public struct TextureDescriptor
{
    /// <summary>Texture dimension type.</summary>
    public TextureDimension Dimension;

    /// <summary>Width in pixels.</summary>
    public uint Width;

    /// <summary>Height in pixels (1 for 1D textures).</summary>
    public uint Height;

    /// <summary>Depth in pixels (1 for 2D, >1 for 3D).</summary>
    public uint Depth;

    /// <summary>Number of mip levels (0 = auto-calculate full chain).</summary>
    public uint MipLevels;

    /// <summary>Number of array layers (1 for non-array, 6 for cubemap).</summary>
    public uint ArrayLayers;

    /// <summary>Pixel format.</summary>
    public TextureFormat Format;

    /// <summary>How the texture will be used.</summary>
    public TextureUsage Usage;

    /// <summary>Multisample count.</summary>
    public SampleCount SampleCount;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    public TextureDescriptor()
    {
        Dimension = TextureDimension.Texture2D;
        Width = 1;
        Height = 1;
        Depth = 1;
        MipLevels = 1;
        ArrayLayers = 1;
        Format = TextureFormat.RGBA8Unorm;
        Usage = TextureUsage.Sampled;
        SampleCount = SampleCount.Count1;
        DebugName = null;
    }

    /// <summary>
    /// Creates a 2D texture descriptor.
    /// </summary>
    public static TextureDescriptor Texture2D(uint width, uint height, TextureFormat format, TextureUsage usage = TextureUsage.Sampled, uint mipLevels = 1) => new()
    {
        Dimension = TextureDimension.Texture2D,
        Width = width,
        Height = height,
        Depth = 1,
        MipLevels = mipLevels,
        ArrayLayers = 1,
        Format = format,
        Usage = usage,
        SampleCount = SampleCount.Count1,
    };

    /// <summary>
    /// Creates a 2D texture array descriptor.
    /// </summary>
    public static TextureDescriptor Texture2DArray(uint width, uint height, uint arrayLayers, TextureFormat format, TextureUsage usage = TextureUsage.Sampled, uint mipLevels = 1) => new()
    {
        Dimension = TextureDimension.Texture2D,
        Width = width,
        Height = height,
        Depth = 1,
        MipLevels = mipLevels,
        ArrayLayers = arrayLayers,
        Format = format,
        Usage = usage,
        SampleCount = SampleCount.Count1,
    };

    /// <summary>
    /// Creates a cubemap texture descriptor.
    /// </summary>
    public static TextureDescriptor Cubemap(uint size, TextureFormat format, TextureUsage usage = TextureUsage.Sampled, uint mipLevels = 1) => new()
    {
        Dimension = TextureDimension.TextureCube,
        Width = size,
        Height = size,
        Depth = 1,
        MipLevels = mipLevels,
        ArrayLayers = 6,
        Format = format,
        Usage = usage,
        SampleCount = SampleCount.Count1,
    };

    /// <summary>
    /// Creates a 3D texture descriptor.
    /// </summary>
    public static TextureDescriptor Texture3D(uint width, uint height, uint depth, TextureFormat format, TextureUsage usage = TextureUsage.Sampled, uint mipLevels = 1) => new()
    {
        Dimension = TextureDimension.Texture3D,
        Width = width,
        Height = height,
        Depth = depth,
        MipLevels = mipLevels,
        ArrayLayers = 1,
        Format = format,
        Usage = usage,
        SampleCount = SampleCount.Count1,
    };

    /// <summary>
    /// Creates a render target texture descriptor.
    /// </summary>
    public static TextureDescriptor RenderTarget(uint width, uint height, TextureFormat format, SampleCount sampleCount = SampleCount.Count1) => new()
    {
        Dimension = TextureDimension.Texture2D,
        Width = width,
        Height = height,
        Depth = 1,
        MipLevels = 1,
        ArrayLayers = 1,
        Format = format,
        Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
        SampleCount = sampleCount,
    };

    /// <summary>
    /// Creates a depth texture descriptor.
    /// </summary>
    public static TextureDescriptor DepthStencil(uint width, uint height, TextureFormat format = TextureFormat.Depth24Plus, SampleCount sampleCount = SampleCount.Count1) => new()
    {
        Dimension = TextureDimension.Texture2D,
        Width = width,
        Height = height,
        Depth = 1,
        MipLevels = 1,
        ArrayLayers = 1,
        Format = format,
        Usage = TextureUsage.DepthStencil,
        SampleCount = sampleCount,
    };

    /// <summary>
    /// Calculates the maximum mip levels for current dimensions.
    /// </summary>
    public readonly uint CalculateMaxMipLevels()
    {
        uint maxDimension = Math.Max(Math.Max(Width, Height), Depth);
        return (uint)Math.Floor(Math.Log2(maxDimension)) + 1;
    }
}

/// <summary>
/// Describes a texture update region.
/// </summary>
public struct TextureUpdateDescriptor
{
    public uint MipLevel;
    public uint ArrayLayer;
    public uint X;
    public uint Y;
    public uint Z;
    public uint Width;
    public uint Height;
    public uint Depth;

    public static TextureUpdateDescriptor FullMip(uint width, uint height, uint mipLevel = 0, uint arrayLayer = 0) => new()
    {
        MipLevel = mipLevel,
        ArrayLayer = arrayLayer,
        X = 0,
        Y = 0,
        Z = 0,
        Width = width,
        Height = height,
        Depth = 1,
    };
}
