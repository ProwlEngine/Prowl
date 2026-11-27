using System;
using System.Diagnostics;

namespace Prowl.Graphite;

internal static class FormatHelpers
{
    public static int GetElementCount(VertexElementFormat format)
    {
        switch (format)
        {
            case VertexElementFormat.Float1:
            case VertexElementFormat.UInt1:
            case VertexElementFormat.Int1:
            case VertexElementFormat.Half1:
                return 1;
            case VertexElementFormat.Float2:
            case VertexElementFormat.Byte2_Norm:
            case VertexElementFormat.Byte2:
            case VertexElementFormat.SByte2_Norm:
            case VertexElementFormat.SByte2:
            case VertexElementFormat.UShort2_Norm:
            case VertexElementFormat.UShort2:
            case VertexElementFormat.Short2_Norm:
            case VertexElementFormat.Short2:
            case VertexElementFormat.UInt2:
            case VertexElementFormat.Int2:
            case VertexElementFormat.Half2:
                return 2;
            case VertexElementFormat.Float3:
            case VertexElementFormat.UInt3:
            case VertexElementFormat.Int3:
                return 3;
            case VertexElementFormat.Float4:
            case VertexElementFormat.Byte4_Norm:
            case VertexElementFormat.Byte4:
            case VertexElementFormat.SByte4_Norm:
            case VertexElementFormat.SByte4:
            case VertexElementFormat.UShort4_Norm:
            case VertexElementFormat.UShort4:
            case VertexElementFormat.Short4_Norm:
            case VertexElementFormat.Short4:
            case VertexElementFormat.UInt4:
            case VertexElementFormat.Int4:
            case VertexElementFormat.Half4:
                return 4;
            default:
                throw Illegal.Value<VertexElementFormat>();
        }
    }

    internal static uint GetSampleCountUInt32(TextureSampleCount sampleCount)
    {
        switch (sampleCount)
        {
            case TextureSampleCount.Count1:
                return 1;
            case TextureSampleCount.Count2:
                return 2;
            case TextureSampleCount.Count4:
                return 4;
            case TextureSampleCount.Count8:
                return 8;
            case TextureSampleCount.Count16:
                return 16;
            case TextureSampleCount.Count32:
                return 32;
            default:
                throw Illegal.Value<TextureSampleCount>();
        }
    }

    internal static bool IsStencilFormat(PixelFormat format)
    {
        return format == PixelFormat.D24_UNorm_S8_UInt || format == PixelFormat.D32_Float_S8_UInt;
    }

    internal static bool IsCompressedFormat(PixelFormat format)
    {
        return format == PixelFormat.BC1_Rgb_UNorm
            || format == PixelFormat.BC1_Rgb_UNorm_SRgb
            || format == PixelFormat.BC1_Rgba_UNorm
            || format == PixelFormat.BC1_Rgba_UNorm_SRgb
            || format == PixelFormat.BC2_UNorm
            || format == PixelFormat.BC2_UNorm_SRgb
            || format == PixelFormat.BC3_UNorm
            || format == PixelFormat.BC3_UNorm_SRgb
            || format == PixelFormat.BC4_UNorm
            || format == PixelFormat.BC4_SNorm
            || format == PixelFormat.BC5_UNorm
            || format == PixelFormat.BC5_SNorm
            || format == PixelFormat.BC7_UNorm
            || format == PixelFormat.BC7_UNorm_SRgb
            || format == PixelFormat.ETC2_R8_G8_B8_UNorm
            || format == PixelFormat.ETC2_R8_G8_B8_A1_UNorm
            || format == PixelFormat.ETC2_R8_G8_B8_A8_UNorm
            || IsAstcFormat(format);
    }

    internal static bool IsAstcFormat(PixelFormat format) =>
        format >= PixelFormat.ASTC_4x4_UNorm && format <= PixelFormat.ASTC_12x12_UNorm_SRgb;

    /// <summary>Pixels one block covers across and down, one by one for an uncompressed format.</summary>
    public static (uint Width, uint Height) GetBlockDimensions(PixelFormat format) => format switch
    {
            PixelFormat.ASTC_4x4_UNorm or PixelFormat.ASTC_4x4_UNorm_SRgb => (4u, 4u),
            PixelFormat.ASTC_5x4_UNorm or PixelFormat.ASTC_5x4_UNorm_SRgb => (5u, 4u),
            PixelFormat.ASTC_5x5_UNorm or PixelFormat.ASTC_5x5_UNorm_SRgb => (5u, 5u),
            PixelFormat.ASTC_6x5_UNorm or PixelFormat.ASTC_6x5_UNorm_SRgb => (6u, 5u),
            PixelFormat.ASTC_6x6_UNorm or PixelFormat.ASTC_6x6_UNorm_SRgb => (6u, 6u),
            PixelFormat.ASTC_8x5_UNorm or PixelFormat.ASTC_8x5_UNorm_SRgb => (8u, 5u),
            PixelFormat.ASTC_8x6_UNorm or PixelFormat.ASTC_8x6_UNorm_SRgb => (8u, 6u),
            PixelFormat.ASTC_8x8_UNorm or PixelFormat.ASTC_8x8_UNorm_SRgb => (8u, 8u),
            PixelFormat.ASTC_10x5_UNorm or PixelFormat.ASTC_10x5_UNorm_SRgb => (10u, 5u),
            PixelFormat.ASTC_10x6_UNorm or PixelFormat.ASTC_10x6_UNorm_SRgb => (10u, 6u),
            PixelFormat.ASTC_10x8_UNorm or PixelFormat.ASTC_10x8_UNorm_SRgb => (10u, 8u),
            PixelFormat.ASTC_10x10_UNorm or PixelFormat.ASTC_10x10_UNorm_SRgb => (10u, 10u),
            PixelFormat.ASTC_12x10_UNorm or PixelFormat.ASTC_12x10_UNorm_SRgb => (12u, 10u),
            PixelFormat.ASTC_12x12_UNorm or PixelFormat.ASTC_12x12_UNorm_SRgb => (12u, 12u),
        _ when IsCompressedFormat(format) => (4u, 4u),
        _ => (1u, 1u),
    };

    public static uint GetBlockSizeInBytes(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.BC1_Rgb_UNorm:
            case PixelFormat.BC1_Rgb_UNorm_SRgb:
            case PixelFormat.BC1_Rgba_UNorm:
            case PixelFormat.BC1_Rgba_UNorm_SRgb:
            case PixelFormat.BC4_UNorm:
            case PixelFormat.BC4_SNorm:
            case PixelFormat.ETC2_R8_G8_B8_UNorm:
            case PixelFormat.ETC2_R8_G8_B8_A1_UNorm:
                return 8;
            case PixelFormat.BC2_UNorm:
            case PixelFormat.BC2_UNorm_SRgb:
            case PixelFormat.BC3_UNorm:
            case PixelFormat.BC3_UNorm_SRgb:
            case PixelFormat.BC5_UNorm:
            case PixelFormat.BC5_SNorm:
            case PixelFormat.BC7_UNorm:
            case PixelFormat.BC7_UNorm_SRgb:
            case PixelFormat.ETC2_R8_G8_B8_A8_UNorm:
                return 16;
            case var astc when IsAstcFormat(astc):
                return 16;
            default:
                throw Illegal.Value<PixelFormat>();
        }
    }

    internal static bool IsFormatViewCompatible(PixelFormat viewFormat, PixelFormat realFormat)
    {
        if (IsCompressedFormat(realFormat))
        {
            return IsSrgbCounterpart(viewFormat, realFormat);
        }
        else
        {
            return GetViewFamilyFormat(viewFormat) == GetViewFamilyFormat(realFormat);
        }
    }

    private static bool IsSrgbCounterpart(PixelFormat viewFormat, PixelFormat realFormat)
    {
        return GetSrgbCounterpart(viewFormat) == realFormat
            || GetSrgbCounterpart(realFormat) == viewFormat;
    }

    /// <summary>
    /// Returns the sRGB variant of a compressed format, or the format itself when it has none
    /// (BC4, BC5 and ETC2 have no sRGB variants, so those only pair with themselves).
    /// </summary>
    private static PixelFormat GetSrgbCounterpart(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.BC1_Rgb_UNorm => PixelFormat.BC1_Rgb_UNorm_SRgb,
            PixelFormat.BC1_Rgb_UNorm_SRgb => PixelFormat.BC1_Rgb_UNorm,
            PixelFormat.BC1_Rgba_UNorm => PixelFormat.BC1_Rgba_UNorm_SRgb,
            PixelFormat.BC1_Rgba_UNorm_SRgb => PixelFormat.BC1_Rgba_UNorm,
            PixelFormat.BC2_UNorm => PixelFormat.BC2_UNorm_SRgb,
            PixelFormat.BC2_UNorm_SRgb => PixelFormat.BC2_UNorm,
            PixelFormat.BC3_UNorm => PixelFormat.BC3_UNorm_SRgb,
            PixelFormat.BC3_UNorm_SRgb => PixelFormat.BC3_UNorm,
            PixelFormat.BC7_UNorm => PixelFormat.BC7_UNorm_SRgb,
            PixelFormat.BC7_UNorm_SRgb => PixelFormat.BC7_UNorm,
            PixelFormat.ASTC_4x4_UNorm => PixelFormat.ASTC_4x4_UNorm_SRgb,
            PixelFormat.ASTC_4x4_UNorm_SRgb => PixelFormat.ASTC_4x4_UNorm,
            PixelFormat.ASTC_5x4_UNorm => PixelFormat.ASTC_5x4_UNorm_SRgb,
            PixelFormat.ASTC_5x4_UNorm_SRgb => PixelFormat.ASTC_5x4_UNorm,
            PixelFormat.ASTC_5x5_UNorm => PixelFormat.ASTC_5x5_UNorm_SRgb,
            PixelFormat.ASTC_5x5_UNorm_SRgb => PixelFormat.ASTC_5x5_UNorm,
            PixelFormat.ASTC_6x5_UNorm => PixelFormat.ASTC_6x5_UNorm_SRgb,
            PixelFormat.ASTC_6x5_UNorm_SRgb => PixelFormat.ASTC_6x5_UNorm,
            PixelFormat.ASTC_6x6_UNorm => PixelFormat.ASTC_6x6_UNorm_SRgb,
            PixelFormat.ASTC_6x6_UNorm_SRgb => PixelFormat.ASTC_6x6_UNorm,
            PixelFormat.ASTC_8x5_UNorm => PixelFormat.ASTC_8x5_UNorm_SRgb,
            PixelFormat.ASTC_8x5_UNorm_SRgb => PixelFormat.ASTC_8x5_UNorm,
            PixelFormat.ASTC_8x6_UNorm => PixelFormat.ASTC_8x6_UNorm_SRgb,
            PixelFormat.ASTC_8x6_UNorm_SRgb => PixelFormat.ASTC_8x6_UNorm,
            PixelFormat.ASTC_8x8_UNorm => PixelFormat.ASTC_8x8_UNorm_SRgb,
            PixelFormat.ASTC_8x8_UNorm_SRgb => PixelFormat.ASTC_8x8_UNorm,
            PixelFormat.ASTC_10x5_UNorm => PixelFormat.ASTC_10x5_UNorm_SRgb,
            PixelFormat.ASTC_10x5_UNorm_SRgb => PixelFormat.ASTC_10x5_UNorm,
            PixelFormat.ASTC_10x6_UNorm => PixelFormat.ASTC_10x6_UNorm_SRgb,
            PixelFormat.ASTC_10x6_UNorm_SRgb => PixelFormat.ASTC_10x6_UNorm,
            PixelFormat.ASTC_10x8_UNorm => PixelFormat.ASTC_10x8_UNorm_SRgb,
            PixelFormat.ASTC_10x8_UNorm_SRgb => PixelFormat.ASTC_10x8_UNorm,
            PixelFormat.ASTC_10x10_UNorm => PixelFormat.ASTC_10x10_UNorm_SRgb,
            PixelFormat.ASTC_10x10_UNorm_SRgb => PixelFormat.ASTC_10x10_UNorm,
            PixelFormat.ASTC_12x10_UNorm => PixelFormat.ASTC_12x10_UNorm_SRgb,
            PixelFormat.ASTC_12x10_UNorm_SRgb => PixelFormat.ASTC_12x10_UNorm,
            PixelFormat.ASTC_12x12_UNorm => PixelFormat.ASTC_12x12_UNorm_SRgb,
            PixelFormat.ASTC_12x12_UNorm_SRgb => PixelFormat.ASTC_12x12_UNorm,
            _ => format,
        };
    }

    internal static uint GetRegionSize(uint width, uint height, uint depth, PixelFormat format)
    {
        if (!IsCompressedFormat(format))
            return width * height * depth * format.GetSizeInBytes();

        // Edge blocks are whole, so a region narrower than a block still costs one.
        (uint blockWidth, uint blockHeight) = GetBlockDimensions(format);
        return ((width + blockWidth - 1) / blockWidth) * ((height + blockHeight - 1) / blockHeight) * depth * GetBlockSizeInBytes(format);
    }

    internal static TextureSampleCount GetSampleCount(uint samples)
    {
        switch (samples)
        {
            case 1: return TextureSampleCount.Count1;
            case 2: return TextureSampleCount.Count2;
            case 4: return TextureSampleCount.Count4;
            case 8: return TextureSampleCount.Count8;
            case 16: return TextureSampleCount.Count16;
            case 32: return TextureSampleCount.Count32;
            default: throw new RenderException("Unsupported multisample count: " + samples);
        }
    }

    internal static PixelFormat GetViewFamilyFormat(PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.R32_G32_B32_A32_Float:
            case PixelFormat.R32_G32_B32_A32_UInt:
            case PixelFormat.R32_G32_B32_A32_SInt:
                return PixelFormat.R32_G32_B32_A32_Float;
            case PixelFormat.R16_G16_B16_A16_Float:
            case PixelFormat.R16_G16_B16_A16_UNorm:
            case PixelFormat.R16_G16_B16_A16_UInt:
            case PixelFormat.R16_G16_B16_A16_SNorm:
            case PixelFormat.R16_G16_B16_A16_SInt:
                return PixelFormat.R16_G16_B16_A16_Float;
            case PixelFormat.R32_G32_Float:
            case PixelFormat.R32_G32_UInt:
            case PixelFormat.R32_G32_SInt:
                return PixelFormat.R32_G32_Float;
            case PixelFormat.R10_G10_B10_A2_UNorm:
            case PixelFormat.R10_G10_B10_A2_UInt:
                return PixelFormat.R10_G10_B10_A2_UNorm;
            case PixelFormat.R8_G8_B8_A8_UNorm:
            case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
            case PixelFormat.R8_G8_B8_A8_UInt:
            case PixelFormat.R8_G8_B8_A8_SNorm:
            case PixelFormat.R8_G8_B8_A8_SInt:
                return PixelFormat.R8_G8_B8_A8_UNorm;
            case PixelFormat.R16_G16_Float:
            case PixelFormat.R16_G16_UNorm:
            case PixelFormat.R16_G16_UInt:
            case PixelFormat.R16_G16_SNorm:
            case PixelFormat.R16_G16_SInt:
                return PixelFormat.R16_G16_Float;
            case PixelFormat.R32_Float:
            case PixelFormat.R32_UInt:
            case PixelFormat.R32_SInt:
                return PixelFormat.R32_Float;
            case PixelFormat.R8_G8_UNorm:
            case PixelFormat.R8_G8_UInt:
            case PixelFormat.R8_G8_SNorm:
            case PixelFormat.R8_G8_SInt:
                return PixelFormat.R8_G8_UNorm;
            case PixelFormat.R16_Float:
            case PixelFormat.R16_UNorm:
            case PixelFormat.R16_UInt:
            case PixelFormat.R16_SNorm:
            case PixelFormat.R16_SInt:
                return PixelFormat.R16_Float;
            case PixelFormat.R8_UNorm:
            case PixelFormat.R8_UInt:
            case PixelFormat.R8_SNorm:
            case PixelFormat.R8_SInt:
                return PixelFormat.R8_UNorm;
            case PixelFormat.BC1_Rgba_UNorm:
            case PixelFormat.BC1_Rgba_UNorm_SRgb:
            case PixelFormat.BC1_Rgb_UNorm:
            case PixelFormat.BC1_Rgb_UNorm_SRgb:
                return PixelFormat.BC1_Rgba_UNorm;
            case PixelFormat.BC2_UNorm:
            case PixelFormat.BC2_UNorm_SRgb:
                return PixelFormat.BC2_UNorm;
            case PixelFormat.BC3_UNorm:
            case PixelFormat.BC3_UNorm_SRgb:
                return PixelFormat.BC3_UNorm;
            case PixelFormat.BC4_UNorm:
            case PixelFormat.BC4_SNorm:
                return PixelFormat.BC4_UNorm;
            case PixelFormat.BC5_UNorm:
            case PixelFormat.BC5_SNorm:
                return PixelFormat.BC5_UNorm;
            case PixelFormat.B8_G8_R8_A8_UNorm:
            case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
                return PixelFormat.B8_G8_R8_A8_UNorm;
            case PixelFormat.BC7_UNorm:
            case PixelFormat.BC7_UNorm_SRgb:
                return PixelFormat.BC7_UNorm;
            case PixelFormat.ASTC_4x4_UNorm:
            case PixelFormat.ASTC_4x4_UNorm_SRgb:
                return PixelFormat.ASTC_4x4_UNorm;
            case PixelFormat.ASTC_5x4_UNorm:
            case PixelFormat.ASTC_5x4_UNorm_SRgb:
                return PixelFormat.ASTC_5x4_UNorm;
            case PixelFormat.ASTC_5x5_UNorm:
            case PixelFormat.ASTC_5x5_UNorm_SRgb:
                return PixelFormat.ASTC_5x5_UNorm;
            case PixelFormat.ASTC_6x5_UNorm:
            case PixelFormat.ASTC_6x5_UNorm_SRgb:
                return PixelFormat.ASTC_6x5_UNorm;
            case PixelFormat.ASTC_6x6_UNorm:
            case PixelFormat.ASTC_6x6_UNorm_SRgb:
                return PixelFormat.ASTC_6x6_UNorm;
            case PixelFormat.ASTC_8x5_UNorm:
            case PixelFormat.ASTC_8x5_UNorm_SRgb:
                return PixelFormat.ASTC_8x5_UNorm;
            case PixelFormat.ASTC_8x6_UNorm:
            case PixelFormat.ASTC_8x6_UNorm_SRgb:
                return PixelFormat.ASTC_8x6_UNorm;
            case PixelFormat.ASTC_8x8_UNorm:
            case PixelFormat.ASTC_8x8_UNorm_SRgb:
                return PixelFormat.ASTC_8x8_UNorm;
            case PixelFormat.ASTC_10x5_UNorm:
            case PixelFormat.ASTC_10x5_UNorm_SRgb:
                return PixelFormat.ASTC_10x5_UNorm;
            case PixelFormat.ASTC_10x6_UNorm:
            case PixelFormat.ASTC_10x6_UNorm_SRgb:
                return PixelFormat.ASTC_10x6_UNorm;
            case PixelFormat.ASTC_10x8_UNorm:
            case PixelFormat.ASTC_10x8_UNorm_SRgb:
                return PixelFormat.ASTC_10x8_UNorm;
            case PixelFormat.ASTC_10x10_UNorm:
            case PixelFormat.ASTC_10x10_UNorm_SRgb:
                return PixelFormat.ASTC_10x10_UNorm;
            case PixelFormat.ASTC_12x10_UNorm:
            case PixelFormat.ASTC_12x10_UNorm_SRgb:
                return PixelFormat.ASTC_12x10_UNorm;
            case PixelFormat.ASTC_12x12_UNorm:
            case PixelFormat.ASTC_12x12_UNorm_SRgb:
                return PixelFormat.ASTC_12x12_UNorm;
            default:
                return format;
        }
    }
}
