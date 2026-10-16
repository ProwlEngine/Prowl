// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using GFilter = Prowl.Graphite.FilterMode;
using GPixelFormat = Prowl.Graphite.PixelFormat;
using GSamplerAddress = Prowl.Graphite.SamplerAddressMode;
using GSampler = Prowl.Graphite.SamplerDescription;

namespace Prowl.Runtime;

/// <summary>
/// A texture's texel format, a subset of Graphite's pixel formats. The numbers are stored in assets, so they never
/// change. Float formats hold floats of that many bits, plain ones normalized unsigned values.
/// </summary>
public enum TextureFormat : byte
{
    RGBA8 = 0,
    R8 = 1,
    RG8 = 2,
    R16 = 3,
    RG16 = 4,
    RGBA16 = 5,
    R16Float = 6,
    RG16Float = 7,
    RGBA16Float = 8,
    R32Float = 9,
    RG32Float = 10,
    RGBA32Float = 11,
    R32Int = 12,
    RG32Int = 13,
    RGBA32Int = 14,
    R32UInt = 15,
    RG32UInt = 16,
    RGBA32UInt = 17,
    RG11B10Float = 18,
    Depth16 = 19,
    Depth32Float = 20,
    Depth24Stencil8 = 21,
    R16SNorm = 22,
}

/// <summary>How a texture is filtered when sampled.</summary>
public enum FilterMode : byte
{
    /// <summary>The nearest texel of the nearest mip.</summary>
    Point,
    /// <summary>Blends the four nearest texels of the nearest mip.</summary>
    Bilinear,
    /// <summary>Blends the four nearest texels of the two nearest mips.</summary>
    Trilinear,
}

/// <summary>What a texture returns when sampled outside 0 to 1 along one axis.</summary>
public enum WrapMode : byte
{
    Repeat,
    Clamp,
    Mirror,
    /// <summary>The texture's border colour.</summary>
    Border,
}

/// <summary>The colour a <see cref="WrapMode.Border"/> axis returns outside the texture.</summary>
public enum TextureBorderColor : byte
{
    TransparentBlack,
    OpaqueBlack,
    OpaqueWhite,
}

/// <summary>Primitive topology of a submesh. LineLoop, TriangleFan and Quads become lists when uploaded.</summary>
public enum Topology { Points, Lines, LineLoop, LineStrip, Triangles, TriangleStrip, TriangleFan, Quads }

/// <summary>Conversions from Prowl's texture settings to Graphite's.</summary>
public static class GraphicsFormats
{
    /// <summary>The Graphite format of a texture format. Only RGBA8 has an sRGB form, other formats ignore the flag.</summary>
    public static GPixelFormat ToPixelFormat(TextureFormat format, bool srgb = false) => format switch
    {
        TextureFormat.RGBA8 => srgb ? GPixelFormat.R8_G8_B8_A8_UNorm_SRgb : GPixelFormat.R8_G8_B8_A8_UNorm,
        TextureFormat.R8 => GPixelFormat.R8_UNorm,
        TextureFormat.RG8 => GPixelFormat.R8_G8_UNorm,
        TextureFormat.R16 => GPixelFormat.R16_UNorm,
        TextureFormat.R16SNorm => GPixelFormat.R16_SNorm,
        TextureFormat.RG16 => GPixelFormat.R16_G16_UNorm,
        TextureFormat.RGBA16 => GPixelFormat.R16_G16_B16_A16_UNorm,
        TextureFormat.R16Float => GPixelFormat.R16_Float,
        TextureFormat.RG16Float => GPixelFormat.R16_G16_Float,
        TextureFormat.RGBA16Float => GPixelFormat.R16_G16_B16_A16_Float,
        TextureFormat.R32Float => GPixelFormat.R32_Float,
        TextureFormat.RG32Float => GPixelFormat.R32_G32_Float,
        TextureFormat.RGBA32Float => GPixelFormat.R32_G32_B32_A32_Float,
        TextureFormat.R32Int => GPixelFormat.R32_SInt,
        TextureFormat.RG32Int => GPixelFormat.R32_G32_SInt,
        TextureFormat.RGBA32Int => GPixelFormat.R32_G32_B32_A32_SInt,
        TextureFormat.R32UInt => GPixelFormat.R32_UInt,
        TextureFormat.RG32UInt => GPixelFormat.R32_G32_UInt,
        TextureFormat.RGBA32UInt => GPixelFormat.R32_G32_B32_A32_UInt,
        TextureFormat.RG11B10Float => GPixelFormat.R11_G11_B10_Float,
        TextureFormat.Depth16 => GPixelFormat.D16_UNorm,
        TextureFormat.Depth32Float => GPixelFormat.D32_Float,
        TextureFormat.Depth24Stencil8 => GPixelFormat.D24_UNorm_S8_UInt,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static bool IsDepth(TextureFormat format) => format is TextureFormat.Depth16 or TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8;

    /// <summary>Whether the format has an sRGB form, so the sRGB flag changes how it samples.</summary>
    public static bool SupportsSrgb(TextureFormat format) => format == TextureFormat.RGBA8;

    /// <summary>Bytes per texel.</summary>
    public static int BytesPerTexel(TextureFormat format) => format switch
    {
        TextureFormat.R8 => 1,
        TextureFormat.RG8 or TextureFormat.R16 or TextureFormat.R16SNorm or TextureFormat.R16Float or TextureFormat.Depth16 => 2,
        TextureFormat.RG16 or TextureFormat.RG16Float or TextureFormat.R32Float or TextureFormat.R32Int or TextureFormat.R32UInt => 4,
        TextureFormat.RGBA16 or TextureFormat.RGBA16Float or TextureFormat.RG32Float or TextureFormat.RG32Int or TextureFormat.RG32UInt => 8,
        TextureFormat.RGBA32Float or TextureFormat.RGBA32Int or TextureFormat.RGBA32UInt => 16,
        _ => 4,
    };

    public static GSamplerAddress ToAddressMode(WrapMode wrap) => wrap switch
    {
        WrapMode.Clamp => GSamplerAddress.Clamp,
        WrapMode.Mirror => GSamplerAddress.Mirror,
        WrapMode.Border => GSamplerAddress.Border,
        _ => GSamplerAddress.Wrap,
    };

    /// <summary>The sampler a texture's filter, wrap and mip count describe.</summary>
    public static GSampler ToSampler(FilterMode filter, WrapMode wrapU, WrapMode wrapV, WrapMode wrapW, uint mipCount,
        uint anisotropy = 0, TextureBorderColor border = TextureBorderColor.TransparentBlack)
    {
        GFilter texel = filter == FilterMode.Point ? GFilter.Point : GFilter.Linear;
        return new GSampler
        {
            AddressModeU = ToAddressMode(wrapU),
            AddressModeV = ToAddressMode(wrapV),
            AddressModeW = ToAddressMode(wrapW),
            MinFilter = texel,
            MagFilter = texel,
            MipFilter = filter == FilterMode.Trilinear ? GFilter.Linear : GFilter.Point,
            MaximumAnisotropy = filter == FilterMode.Point ? 0 : anisotropy,
            MinimumLod = 0,
            MaximumLod = Math.Max(1u, mipCount) - 1,
            BorderColor = (Graphite.SamplerBorderColor)border,
        };
    }

    public static Graphite.PrimitiveTopology ToTopology(Topology topology) => topology switch
    {
        Topology.Points => Graphite.PrimitiveTopology.PointList,
        Topology.Lines or Topology.LineLoop => Graphite.PrimitiveTopology.LineList,
        Topology.LineStrip => Graphite.PrimitiveTopology.LineStrip,
        Topology.TriangleStrip => Graphite.PrimitiveTopology.TriangleStrip,
        _ => Graphite.PrimitiveTopology.TriangleList,
    };
}
