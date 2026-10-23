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
/// change. Float formats hold floats of that many bits, plain ones normalized unsigned values. BC and ASTC formats
/// hold compressed blocks the GPU samples directly: BC1 is RGB with cutout alpha, BC3 RGBA, BC4 red, BC5 red and
/// green, and ASTC RGBA at the block size its name gives.
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
    BC1 = 23,
    BC3 = 24,
    BC4 = 25,
    BC5 = 26,
    ASTC4x4 = 27,
    ASTC5x5 = 28,
    ASTC6x6 = 29,
    ASTC8x8 = 30,
    ASTC10x10 = 31,
    ASTC12x12 = 32,
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
    /// <summary>The Graphite format of a texture format. Formats without an sRGB form ignore the flag.</summary>
    public static GPixelFormat ToPixelFormat(TextureFormat format, bool srgb = false) => format switch
    {
        TextureFormat.RGBA8 => srgb ? GPixelFormat.R8_G8_B8_A8_UNorm_SRgb : GPixelFormat.R8_G8_B8_A8_UNorm,
        TextureFormat.BC1 => srgb ? GPixelFormat.BC1_Rgba_UNorm_SRgb : GPixelFormat.BC1_Rgba_UNorm,
        TextureFormat.BC3 => srgb ? GPixelFormat.BC3_UNorm_SRgb : GPixelFormat.BC3_UNorm,
        TextureFormat.BC4 => GPixelFormat.BC4_UNorm,
        TextureFormat.BC5 => GPixelFormat.BC5_UNorm,
        TextureFormat.ASTC4x4 => srgb ? GPixelFormat.ASTC_4x4_UNorm_SRgb : GPixelFormat.ASTC_4x4_UNorm,
        TextureFormat.ASTC5x5 => srgb ? GPixelFormat.ASTC_5x5_UNorm_SRgb : GPixelFormat.ASTC_5x5_UNorm,
        TextureFormat.ASTC6x6 => srgb ? GPixelFormat.ASTC_6x6_UNorm_SRgb : GPixelFormat.ASTC_6x6_UNorm,
        TextureFormat.ASTC8x8 => srgb ? GPixelFormat.ASTC_8x8_UNorm_SRgb : GPixelFormat.ASTC_8x8_UNorm,
        TextureFormat.ASTC10x10 => srgb ? GPixelFormat.ASTC_10x10_UNorm_SRgb : GPixelFormat.ASTC_10x10_UNorm,
        TextureFormat.ASTC12x12 => srgb ? GPixelFormat.ASTC_12x12_UNorm_SRgb : GPixelFormat.ASTC_12x12_UNorm,
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
    public static bool SupportsSrgb(TextureFormat format) => format is TextureFormat.RGBA8 or TextureFormat.BC1 or TextureFormat.BC3
        or >= TextureFormat.ASTC4x4 and <= TextureFormat.ASTC12x12;

    /// <summary>Whether the format holds compressed blocks rather than single texels.</summary>
    public static bool IsCompressed(TextureFormat format) => format is >= TextureFormat.BC1 and <= TextureFormat.ASTC12x12;

    /// <summary>The Aperture layout a compressed format encodes and decodes through.</summary>
    public static Aperture.TextureFormat ToAperture(TextureFormat format, bool srgb = false) => format switch
    {
        TextureFormat.BC1 => srgb ? Aperture.TextureFormat.Bc1UnormSrgb : Aperture.TextureFormat.Bc1Unorm,
        TextureFormat.BC3 => srgb ? Aperture.TextureFormat.Bc3UnormSrgb : Aperture.TextureFormat.Bc3Unorm,
        TextureFormat.BC4 => Aperture.TextureFormat.Bc4Unorm,
        TextureFormat.BC5 => Aperture.TextureFormat.Bc5Unorm,
        TextureFormat.ASTC4x4 => srgb ? Aperture.TextureFormat.Astc4x4UnormSrgb : Aperture.TextureFormat.Astc4x4Unorm,
        TextureFormat.ASTC5x5 => srgb ? Aperture.TextureFormat.Astc5x5UnormSrgb : Aperture.TextureFormat.Astc5x5Unorm,
        TextureFormat.ASTC6x6 => srgb ? Aperture.TextureFormat.Astc6x6UnormSrgb : Aperture.TextureFormat.Astc6x6Unorm,
        TextureFormat.ASTC8x8 => srgb ? Aperture.TextureFormat.Astc8x8UnormSrgb : Aperture.TextureFormat.Astc8x8Unorm,
        TextureFormat.ASTC10x10 => srgb ? Aperture.TextureFormat.Astc10x10UnormSrgb : Aperture.TextureFormat.Astc10x10Unorm,
        TextureFormat.ASTC12x12 => srgb ? Aperture.TextureFormat.Astc12x12UnormSrgb : Aperture.TextureFormat.Astc12x12Unorm,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a compressed format."),
    };

    /// <summary>Bytes a tightly packed area of the format takes, edge blocks counted whole.</summary>
    public static long DataSize(TextureFormat format, uint width, uint height, uint depth = 1)
    {
        if (!IsCompressed(format))
            return (long)width * height * depth * BytesPerTexel(format);

        (uint blockWidth, uint blockHeight) = Graphite.FormatSizeHelpers.GetBlockDimensions(ToPixelFormat(format));
        long blocks = (long)((width + blockWidth - 1) / blockWidth) * ((height + blockHeight - 1) / blockHeight) * depth;
        return blocks * (format is TextureFormat.BC1 or TextureFormat.BC4 ? 8 : 16);
    }

    /// <summary>The same Graphite format without the sRGB decode, or null when it has no sRGB form.</summary>
    internal static GPixelFormat? WithoutSrgb(GPixelFormat format) => format switch
    {
        GPixelFormat.R8_G8_B8_A8_UNorm_SRgb => GPixelFormat.R8_G8_B8_A8_UNorm,
        GPixelFormat.BC1_Rgba_UNorm_SRgb => GPixelFormat.BC1_Rgba_UNorm,
        GPixelFormat.BC3_UNorm_SRgb => GPixelFormat.BC3_UNorm,
        GPixelFormat.ASTC_4x4_UNorm_SRgb => GPixelFormat.ASTC_4x4_UNorm,
        GPixelFormat.ASTC_5x5_UNorm_SRgb => GPixelFormat.ASTC_5x5_UNorm,
        GPixelFormat.ASTC_6x6_UNorm_SRgb => GPixelFormat.ASTC_6x6_UNorm,
        GPixelFormat.ASTC_8x8_UNorm_SRgb => GPixelFormat.ASTC_8x8_UNorm,
        GPixelFormat.ASTC_10x10_UNorm_SRgb => GPixelFormat.ASTC_10x10_UNorm,
        GPixelFormat.ASTC_12x12_UNorm_SRgb => GPixelFormat.ASTC_12x12_UNorm,
        _ => null,
    };

    /// <summary>Bytes per texel, zero for a compressed format, which <see cref="DataSize"/> measures instead.</summary>
    public static int BytesPerTexel(TextureFormat format) => format switch
    {
        _ when IsCompressed(format) => 0,
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
