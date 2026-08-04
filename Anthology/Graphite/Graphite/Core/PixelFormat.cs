namespace Prowl.Graphite;

/// <summary>
/// Texture pixel formats. Name encodes components + bits. Float = signed float, UNorm = unsigned normalized, SRgb = sRGB.
/// </summary>
public enum PixelFormat : byte
{
    /// <summary>
    /// RGBA, 8-bit unorm each.
    /// </summary>
    R8_G8_B8_A8_UNorm,
    /// <summary>
    /// BGRA, 8-bit unorm each.
    /// </summary>
    B8_G8_R8_A8_UNorm,
    /// <summary>
    /// 1 channel, 8-bit unorm.
    /// </summary>
    R8_UNorm,
    /// <summary>
    /// 1 channel, 16-bit unorm. Can be a depth format.
    /// </summary>
    R16_UNorm,
    /// <summary>
    /// RGBA, 32-bit float each.
    /// </summary>
    R32_G32_B32_A32_Float,
    /// <summary>
    /// 1 channel, 32-bit float. Can be a depth format.
    /// </summary>
    R32_Float,
    /// <summary>
    /// BC3 compressed.
    /// </summary>
    BC3_UNorm,
    /// <summary>
    /// Depth-stencil, 24-bit unorm depth + 8-bit uint stencil.
    /// </summary>
    D24_UNorm_S8_UInt,
    /// <summary>
    /// Depth-stencil, 32-bit float depth + 8-bit uint stencil.
    /// </summary>
    D32_Float_S8_UInt,
    /// <summary>
    /// RGBA, 32-bit uint each.
    /// </summary>
    R32_G32_B32_A32_UInt,
    /// <summary>
    /// RG, 8-bit snorm each.
    /// </summary>
    R8_G8_SNorm,
    /// <summary>
    /// BC1 compressed, no alpha.
    /// </summary>
    BC1_Rgb_UNorm,
    /// <summary>
    /// BC1 compressed, 1-bit alpha.
    /// </summary>
    BC1_Rgba_UNorm,
    /// <summary>
    /// BC2 compressed.
    /// </summary>
    BC2_UNorm,
    /// <summary>
    /// Packed 32-bit unorm: R 0-9, G 10-19, B 20-29, A 30-31.
    /// </summary>
    R10_G10_B10_A2_UNorm,
    /// <summary>
    /// Packed 32-bit uint: R 0-9, G 10-19, B 20-29, A 30-31.
    /// </summary>
    R10_G10_B10_A2_UInt,
    /// <summary>
    /// Packed 32-bit float: R 0-10, G 11-21, B 22-31.
    /// </summary>
    R11_G11_B10_Float,
    /// <summary>
    /// 1 channel, 8-bit snorm.
    /// </summary>
    R8_SNorm,
    /// <summary>
    /// 1 channel, 8-bit uint.
    /// </summary>
    R8_UInt,
    /// <summary>
    /// 1 channel, 8-bit sint.
    /// </summary>
    R8_SInt,
    /// <summary>
    /// 1 channel, 16-bit snorm.
    /// </summary>
    R16_SNorm,
    /// <summary>
    /// 1 channel, 16-bit uint.
    /// </summary>
    R16_UInt,
    /// <summary>
    /// 1 channel, 16-bit sint.
    /// </summary>
    R16_SInt,
    /// <summary>
    /// 1 channel, 16-bit float.
    /// </summary>
    R16_Float,
    /// <summary>
    /// 1 channel, 32-bit uint.
    /// </summary>
    R32_UInt,
    /// <summary>
    /// 1 channel, 32-bit sint.
    /// </summary>
    R32_SInt,
    /// <summary>
    /// RG, 8-bit unorm each.
    /// </summary>
    R8_G8_UNorm,
    /// <summary>
    /// RG, 8-bit uint each.
    /// </summary>
    R8_G8_UInt,
    /// <summary>
    /// RG, 8-bit sint each.
    /// </summary>
    R8_G8_SInt,
    /// <summary>
    /// RG, 16-bit unorm each.
    /// </summary>
    R16_G16_UNorm,
    /// <summary>
    /// RG, 16-bit snorm each.
    /// </summary>
    R16_G16_SNorm,
    /// <summary>
    /// RG, 16-bit uint each.
    /// </summary>
    R16_G16_UInt,
    /// <summary>
    /// RG, 16-bit sint each.
    /// </summary>
    R16_G16_SInt,
    /// <summary>
    /// RG, 16-bit float each.
    /// </summary>
    R16_G16_Float,
    /// <summary>
    /// RG, 32-bit uint each.
    /// </summary>
    R32_G32_UInt,
    /// <summary>
    /// RG, 32-bit sint each.
    /// </summary>
    R32_G32_SInt,
    /// <summary>
    /// RG, 32-bit float each.
    /// </summary>
    R32_G32_Float,
    /// <summary>
    /// RGBA, 8-bit snorm each.
    /// </summary>
    R8_G8_B8_A8_SNorm,
    /// <summary>
    /// RGBA, 8-bit uint each.
    /// </summary>
    R8_G8_B8_A8_UInt,
    /// <summary>
    /// RGBA, 8-bit sint each.
    /// </summary>
    R8_G8_B8_A8_SInt,
    /// <summary>
    /// RGBA, 16-bit unorm each.
    /// </summary>
    R16_G16_B16_A16_UNorm,
    /// <summary>
    /// RGBA, 16-bit snorm each.
    /// </summary>
    R16_G16_B16_A16_SNorm,
    /// <summary>
    /// RGBA, 16-bit uint each.
    /// </summary>
    R16_G16_B16_A16_UInt,
    /// <summary>
    /// RGBA, 16-bit sint each.
    /// </summary>
    R16_G16_B16_A16_SInt,
    /// <summary>
    /// RGBA, 16-bit float each.
    /// </summary>
    R16_G16_B16_A16_Float,
    /// <summary>
    /// RGBA, 32-bit sint each.
    /// </summary>
    R32_G32_B32_A32_SInt,
    /// <summary>
    /// ETC2, 64-bit, 4x4 block, unorm RGB.
    /// </summary>
    ETC2_R8_G8_B8_UNorm,
    /// <summary>
    /// ETC2, 64-bit, 4x4 block, unorm RGB + 1-bit alpha.
    /// </summary>
    ETC2_R8_G8_B8_A1_UNorm,
    /// <summary>
    /// ETC2, 128-bit, 4x4 block, RGB + alpha.
    /// </summary>
    ETC2_R8_G8_B8_A8_UNorm,
    /// <summary>
    /// BC4 compressed, unorm.
    /// </summary>
    BC4_UNorm,
    /// <summary>
    /// BC4 compressed, snorm.
    /// </summary>
    BC4_SNorm,
    /// <summary>
    /// BC5 compressed, unorm.
    /// </summary>
    BC5_UNorm,
    /// <summary>
    /// BC5 compressed, snorm.
    /// </summary>
    BC5_SNorm,
    /// <summary>
    /// BC7 compressed.
    /// </summary>
    BC7_UNorm,
    /// <summary>
    /// RGBA, 8-bit unorm each, sRGB.
    /// </summary>
    R8_G8_B8_A8_UNorm_SRgb,
    /// <summary>
    /// BGRA, 8-bit unorm each, sRGB.
    /// </summary>
    B8_G8_R8_A8_UNorm_SRgb,
    /// <summary>
    /// BC1 compressed, no alpha, sRGB.
    /// </summary>
    BC1_Rgb_UNorm_SRgb,
    /// <summary>
    /// BC1 compressed, 1-bit alpha, sRGB.
    /// </summary>
    BC1_Rgba_UNorm_SRgb,
    /// <summary>
    /// BC2 compressed, sRGB.
    /// </summary>
    BC2_UNorm_SRgb,
    /// <summary>
    /// BC3 compressed, sRGB.
    /// </summary>
    BC3_UNorm_SRgb,
    /// <summary>
    /// BC7 compressed, sRGB.
    /// </summary>
    BC7_UNorm_SRgb,
}
