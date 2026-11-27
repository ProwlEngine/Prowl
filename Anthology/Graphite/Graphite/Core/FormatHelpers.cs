using System;
using System.Diagnostics;

namespace Prowl.Graphite;

internal static class FormatHelpers
{
    public static int GetElementCount(VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float1 or VertexElementFormat.UInt1 or VertexElementFormat.Int1 or VertexElementFormat.Half1 => 1,
            VertexElementFormat.Float2 or VertexElementFormat.Byte2_Norm or VertexElementFormat.Byte2 or VertexElementFormat.SByte2_Norm or VertexElementFormat.SByte2 or VertexElementFormat.UShort2_Norm or VertexElementFormat.UShort2 or VertexElementFormat.Short2_Norm or VertexElementFormat.Short2 or VertexElementFormat.UInt2 or VertexElementFormat.Int2 or VertexElementFormat.Half2 => 2,
            VertexElementFormat.Float3 or VertexElementFormat.UInt3 or VertexElementFormat.Int3 => 3,
            VertexElementFormat.Float4 or VertexElementFormat.Byte4_Norm or VertexElementFormat.Byte4 or VertexElementFormat.SByte4_Norm or VertexElementFormat.SByte4 or VertexElementFormat.UShort4_Norm or VertexElementFormat.UShort4 or VertexElementFormat.Short4_Norm or VertexElementFormat.Short4 or VertexElementFormat.UInt4 or VertexElementFormat.Int4 or VertexElementFormat.Half4 => 4,
            _ => throw Illegal.Value<VertexElementFormat>(),
        };
    }

    internal static uint GetSampleCountUInt32(TextureSampleCount sampleCount)
    {
        return sampleCount switch
        {
            TextureSampleCount.Count1 => 1,
            TextureSampleCount.Count2 => 2,
            TextureSampleCount.Count4 => 4,
            TextureSampleCount.Count8 => 8,
            TextureSampleCount.Count16 => 16,
            TextureSampleCount.Count32 => 32,
            _ => throw Illegal.Value<TextureSampleCount>(),
        };
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
        return format switch
        {
            PixelFormat.BC1_Rgb_UNorm or PixelFormat.BC1_Rgb_UNorm_SRgb or PixelFormat.BC1_Rgba_UNorm or PixelFormat.BC1_Rgba_UNorm_SRgb or PixelFormat.BC4_UNorm or PixelFormat.BC4_SNorm or PixelFormat.ETC2_R8_G8_B8_UNorm or PixelFormat.ETC2_R8_G8_B8_A1_UNorm => 8,
            PixelFormat.BC2_UNorm or PixelFormat.BC2_UNorm_SRgb or PixelFormat.BC3_UNorm or PixelFormat.BC3_UNorm_SRgb or PixelFormat.BC5_UNorm or PixelFormat.BC5_SNorm or PixelFormat.BC7_UNorm or PixelFormat.BC7_UNorm_SRgb or PixelFormat.ETC2_R8_G8_B8_A8_UNorm => 16,
            var astc when IsAstcFormat(astc) => 16,
            _ => throw Illegal.Value<PixelFormat>(),
        };
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
        return samples switch
        {
            1 => TextureSampleCount.Count1,
            2 => TextureSampleCount.Count2,
            4 => TextureSampleCount.Count4,
            8 => TextureSampleCount.Count8,
            16 => TextureSampleCount.Count16,
            32 => TextureSampleCount.Count32,
            _ => throw new RenderException("Unsupported multisample count: " + samples),
        };
    }

    internal static PixelFormat GetViewFamilyFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R32_G32_B32_A32_Float or PixelFormat.R32_G32_B32_A32_UInt or PixelFormat.R32_G32_B32_A32_SInt => PixelFormat.R32_G32_B32_A32_Float,
            PixelFormat.R16_G16_B16_A16_Float or PixelFormat.R16_G16_B16_A16_UNorm or PixelFormat.R16_G16_B16_A16_UInt or PixelFormat.R16_G16_B16_A16_SNorm or PixelFormat.R16_G16_B16_A16_SInt => PixelFormat.R16_G16_B16_A16_Float,
            PixelFormat.R32_G32_Float or PixelFormat.R32_G32_UInt or PixelFormat.R32_G32_SInt => PixelFormat.R32_G32_Float,
            PixelFormat.R10_G10_B10_A2_UNorm or PixelFormat.R10_G10_B10_A2_UInt => PixelFormat.R10_G10_B10_A2_UNorm,
            PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm_SRgb or PixelFormat.R8_G8_B8_A8_UInt or PixelFormat.R8_G8_B8_A8_SNorm or PixelFormat.R8_G8_B8_A8_SInt => PixelFormat.R8_G8_B8_A8_UNorm,
            PixelFormat.R16_G16_Float or PixelFormat.R16_G16_UNorm or PixelFormat.R16_G16_UInt or PixelFormat.R16_G16_SNorm or PixelFormat.R16_G16_SInt => PixelFormat.R16_G16_Float,
            PixelFormat.R32_Float or PixelFormat.R32_UInt or PixelFormat.R32_SInt => PixelFormat.R32_Float,
            PixelFormat.R8_G8_UNorm or PixelFormat.R8_G8_UInt or PixelFormat.R8_G8_SNorm or PixelFormat.R8_G8_SInt => PixelFormat.R8_G8_UNorm,
            PixelFormat.R16_Float or PixelFormat.R16_UNorm or PixelFormat.R16_UInt or PixelFormat.R16_SNorm or PixelFormat.R16_SInt => PixelFormat.R16_Float,
            PixelFormat.R8_UNorm or PixelFormat.R8_UInt or PixelFormat.R8_SNorm or PixelFormat.R8_SInt => PixelFormat.R8_UNorm,
            PixelFormat.BC1_Rgba_UNorm or PixelFormat.BC1_Rgba_UNorm_SRgb or PixelFormat.BC1_Rgb_UNorm or PixelFormat.BC1_Rgb_UNorm_SRgb => PixelFormat.BC1_Rgba_UNorm,
            PixelFormat.BC2_UNorm or PixelFormat.BC2_UNorm_SRgb => PixelFormat.BC2_UNorm,
            PixelFormat.BC3_UNorm or PixelFormat.BC3_UNorm_SRgb => PixelFormat.BC3_UNorm,
            PixelFormat.BC4_UNorm or PixelFormat.BC4_SNorm => PixelFormat.BC4_UNorm,
            PixelFormat.BC5_UNorm or PixelFormat.BC5_SNorm => PixelFormat.BC5_UNorm,
            PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb => PixelFormat.B8_G8_R8_A8_UNorm,
            PixelFormat.BC7_UNorm or PixelFormat.BC7_UNorm_SRgb => PixelFormat.BC7_UNorm,
            PixelFormat.ASTC_4x4_UNorm or PixelFormat.ASTC_4x4_UNorm_SRgb => PixelFormat.ASTC_4x4_UNorm,
            PixelFormat.ASTC_5x4_UNorm or PixelFormat.ASTC_5x4_UNorm_SRgb => PixelFormat.ASTC_5x4_UNorm,
            PixelFormat.ASTC_5x5_UNorm or PixelFormat.ASTC_5x5_UNorm_SRgb => PixelFormat.ASTC_5x5_UNorm,
            PixelFormat.ASTC_6x5_UNorm or PixelFormat.ASTC_6x5_UNorm_SRgb => PixelFormat.ASTC_6x5_UNorm,
            PixelFormat.ASTC_6x6_UNorm or PixelFormat.ASTC_6x6_UNorm_SRgb => PixelFormat.ASTC_6x6_UNorm,
            PixelFormat.ASTC_8x5_UNorm or PixelFormat.ASTC_8x5_UNorm_SRgb => PixelFormat.ASTC_8x5_UNorm,
            PixelFormat.ASTC_8x6_UNorm or PixelFormat.ASTC_8x6_UNorm_SRgb => PixelFormat.ASTC_8x6_UNorm,
            PixelFormat.ASTC_8x8_UNorm or PixelFormat.ASTC_8x8_UNorm_SRgb => PixelFormat.ASTC_8x8_UNorm,
            PixelFormat.ASTC_10x5_UNorm or PixelFormat.ASTC_10x5_UNorm_SRgb => PixelFormat.ASTC_10x5_UNorm,
            PixelFormat.ASTC_10x6_UNorm or PixelFormat.ASTC_10x6_UNorm_SRgb => PixelFormat.ASTC_10x6_UNorm,
            PixelFormat.ASTC_10x8_UNorm or PixelFormat.ASTC_10x8_UNorm_SRgb => PixelFormat.ASTC_10x8_UNorm,
            PixelFormat.ASTC_10x10_UNorm or PixelFormat.ASTC_10x10_UNorm_SRgb => PixelFormat.ASTC_10x10_UNorm,
            PixelFormat.ASTC_12x10_UNorm or PixelFormat.ASTC_12x10_UNorm_SRgb => PixelFormat.ASTC_12x10_UNorm,
            PixelFormat.ASTC_12x12_UNorm or PixelFormat.ASTC_12x12_UNorm_SRgb => PixelFormat.ASTC_12x12_UNorm,
            _ => format,
        };
    }
}
