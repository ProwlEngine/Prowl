using System.Diagnostics;

namespace Prowl.Graphite;

/// <summary>
/// Byte size lookups for graphics formats.
/// </summary>
public static class FormatSizeHelpers
{
    /// <summary>Whether the format stores blocks of pixels rather than one pixel at a time.</summary>
    public static bool IsCompressed(this PixelFormat format) => FormatHelpers.IsCompressedFormat(format);

    /// <summary>Pixels one block covers across and down, one by one for an uncompressed format.</summary>
    public static (uint Width, uint Height) GetBlockDimensions(this PixelFormat format) => FormatHelpers.GetBlockDimensions(format);

    /// <summary>Bytes a tightly packed region of the format takes, partial edge blocks counted whole.</summary>
    public static uint GetRegionSizeInBytes(this PixelFormat format, uint width, uint height, uint depth = 1)
        => FormatHelpers.GetRegionSize(width, height, depth, format);

    /// <summary>
    /// Bytes per uncompressed pixel.
    /// </summary>
    /// <param name="format">Pixel format.</param>
    /// <returns>Bytes per pixel.</returns>
    public static uint GetSizeInBytes(this PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.R8_UNorm:
            case PixelFormat.R8_SNorm:
            case PixelFormat.R8_UInt:
            case PixelFormat.R8_SInt:
                return 1;

            case PixelFormat.R16_UNorm:
            case PixelFormat.D16_UNorm:
            case PixelFormat.R16_SNorm:
            case PixelFormat.R16_UInt:
            case PixelFormat.R16_SInt:
            case PixelFormat.R16_Float:
            case PixelFormat.R8_G8_UNorm:
            case PixelFormat.R8_G8_SNorm:
            case PixelFormat.R8_G8_UInt:
            case PixelFormat.R8_G8_SInt:
                return 2;

            case PixelFormat.R32_UInt:
            case PixelFormat.R32_SInt:
            case PixelFormat.R32_Float:
            case PixelFormat.R16_G16_UNorm:
            case PixelFormat.R16_G16_SNorm:
            case PixelFormat.R16_G16_UInt:
            case PixelFormat.R16_G16_SInt:
            case PixelFormat.R16_G16_Float:
            case PixelFormat.R8_G8_B8_A8_UNorm:
            case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
            case PixelFormat.R8_G8_B8_A8_SNorm:
            case PixelFormat.R8_G8_B8_A8_UInt:
            case PixelFormat.R8_G8_B8_A8_SInt:
            case PixelFormat.B8_G8_R8_A8_UNorm:
            case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
            case PixelFormat.R10_G10_B10_A2_UNorm:
            case PixelFormat.R10_G10_B10_A2_UInt:
            case PixelFormat.R11_G11_B10_Float:
            case PixelFormat.D24_UNorm_S8_UInt:
            case PixelFormat.D32_Float:
                return 4;

            case PixelFormat.D32_Float_S8_UInt:
                return 5;

            case PixelFormat.R16_G16_B16_A16_UNorm:
            case PixelFormat.R16_G16_B16_A16_SNorm:
            case PixelFormat.R16_G16_B16_A16_UInt:
            case PixelFormat.R16_G16_B16_A16_SInt:
            case PixelFormat.R16_G16_B16_A16_Float:
            case PixelFormat.R32_G32_UInt:
            case PixelFormat.R32_G32_SInt:
            case PixelFormat.R32_G32_Float:
                return 8;

            case PixelFormat.R32_G32_B32_A32_Float:
            case PixelFormat.R32_G32_B32_A32_UInt:
            case PixelFormat.R32_G32_B32_A32_SInt:
                return 16;

            case PixelFormat.BC1_Rgb_UNorm:
            case PixelFormat.BC1_Rgb_UNorm_SRgb:
            case PixelFormat.BC1_Rgba_UNorm:
            case PixelFormat.BC1_Rgba_UNorm_SRgb:
            case PixelFormat.BC2_UNorm:
            case PixelFormat.BC2_UNorm_SRgb:
            case PixelFormat.BC3_UNorm:
            case PixelFormat.BC3_UNorm_SRgb:
            case PixelFormat.BC4_UNorm:
            case PixelFormat.BC4_SNorm:
            case PixelFormat.BC5_UNorm:
            case PixelFormat.BC5_SNorm:
            case PixelFormat.BC7_UNorm:
            case PixelFormat.BC7_UNorm_SRgb:
            case PixelFormat.ETC2_R8_G8_B8_UNorm:
            case PixelFormat.ETC2_R8_G8_B8_A1_UNorm:
            case PixelFormat.ETC2_R8_G8_B8_A8_UNorm:
            case var astc when FormatHelpers.IsAstcFormat(astc):
                // GetSizeInBytes should not be used on a compressed format.
                throw Illegal.Value<PixelFormat>();
            default: throw Illegal.Value<PixelFormat>();
        }
    }

    /// <summary>
    /// Bytes per vertex element.
    /// </summary>
    /// <param name="format">Vertex element format.</param>
    /// <returns>Bytes per element.</returns>
    public static uint GetSizeInBytes(this VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Byte2_Norm or VertexElementFormat.Byte2 or VertexElementFormat.SByte2_Norm or VertexElementFormat.SByte2 or VertexElementFormat.Half1 => 2,
            VertexElementFormat.Float1 or VertexElementFormat.UInt1 or VertexElementFormat.Int1 or VertexElementFormat.Byte4_Norm or VertexElementFormat.Byte4 or VertexElementFormat.SByte4_Norm or VertexElementFormat.SByte4 or VertexElementFormat.UShort2_Norm or VertexElementFormat.UShort2 or VertexElementFormat.Short2_Norm or VertexElementFormat.Short2 or VertexElementFormat.Half2 => 4,
            VertexElementFormat.Float2 or VertexElementFormat.UInt2 or VertexElementFormat.Int2 or VertexElementFormat.UShort4_Norm or VertexElementFormat.UShort4 or VertexElementFormat.Short4_Norm or VertexElementFormat.Short4 or VertexElementFormat.Half4 => 8,
            VertexElementFormat.Float3 or VertexElementFormat.UInt3 or VertexElementFormat.Int3 => 12,
            VertexElementFormat.Float4 or VertexElementFormat.UInt4 or VertexElementFormat.Int4 => 16,
            _ => throw Illegal.Value<VertexElementFormat>(),
        };
    }
}
