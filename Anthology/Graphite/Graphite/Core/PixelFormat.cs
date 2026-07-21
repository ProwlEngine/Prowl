namespace Prowl.Graphite;

/// <summary>
/// Format of data in a texture. Name = components + bits each. Float = signed float, UNorm = unsigned normalized int, SRgb suffix = sRGB storage.
/// </summary>
public enum PixelFormat : byte
{
    /// <summary>
    /// RGBA, 8-bit unsigned normalized each.
    /// </summary>
    R8_G8_B8_A8_UNorm,
    /// <summary>
    /// BGRA, 8-bit unsigned normalized each.
    /// </summary>
    B8_G8_R8_A8_UNorm,
    /// <summary>
    /// Single-channel, 8-bit unsigned normalized.
    /// </summary>
    R8_UNorm,
    /// <summary>
    /// Single-channel, 16-bit unsigned normalized. Usable as depth format.
    /// </summary>
    R16_UNorm,
    /// <summary>
    /// RGBA, 32-bit float each.
    /// </summary>
    R32_G32_B32_A32_Float,
    /// <summary>
    /// Single-channel, 32-bit float. Usable as depth format.
    /// </summary>
    R32_Float,
    /// <summary>
    /// BC3 block compressed.
    /// </summary>
    BC3_UNorm,
    /// <summary>
    /// Depth-stencil: 24-bit unsigned normalized depth, 8-bit unsigned int stencil.
    /// </summary>
    D24_UNorm_S8_UInt,
    /// <summary>
    /// Depth-stencil: 32-bit float depth, 8-bit unsigned int stencil.
    /// </summary>
    D32_Float_S8_UInt,
    /// <summary>
    /// RGBA, 32-bit unsigned int each.
    /// </summary>
    R32_G32_B32_A32_UInt,
    /// <summary>
    /// RG, 8-bit signed normalized each.
    /// </summary>
    R8_G8_SNorm,
    /// <summary>
    /// BC1 block compressed, no alpha.
    /// </summary>
    BC1_Rgb_UNorm,
    /// <summary>
    /// BC1 block compressed, 1-bit alpha.
    /// </summary>
    BC1_Rgba_UNorm,
    /// <summary>
    /// BC2 block compressed.
    /// </summary>
    BC2_UNorm,
    /// <summary>
    /// 32-bit packed unsigned normalized: R bits 0-9, G bits 10-19, B bits 20-29, A bits 30-31.
    /// </summary>
    R10_G10_B10_A2_UNorm,
    /// <summary>
    /// 32-bit packed unsigned int: R bits 0-9, G bits 10-19, B bits 20-29, A bits 30-31.
    /// </summary>
    R10_G10_B10_A2_UInt,
    /// <summary>
    /// 32-bit packed float: R bits 0-10, G bits 11-21, B bits 22-31.
    /// </summary>
    R11_G11_B10_Float,
    /// <summary>
    /// Single-channel, 8-bit signed normalized.
    /// </summary>
    R8_SNorm,
    /// <summary>
    /// Single-channel, 8-bit unsigned int.
    /// </summary>
    R8_UInt,
    /// <summary>
    /// Single-channel, 8-bit signed int.
    /// </summary>
    R8_SInt,
    /// <summary>
    /// Single-channel, 16-bit signed normalized.
    /// </summary>
    R16_SNorm,
    /// <summary>
    /// Single-channel, 16-bit unsigned int.
    /// </summary>
    R16_UInt,
    /// <summary>
    /// Single-channel, 16-bit signed int.
    /// </summary>
    R16_SInt,
    /// <summary>
    /// Single-channel, 16-bit float.
    /// </summary>
    R16_Float,
    /// <summary>
    /// Single-channel, 32-bit unsigned int.
    /// </summary>
    R32_UInt,
    /// <summary>
    /// Single-channel, 32-bit signed int.
    /// </summary>
    R32_SInt,
    /// <summary>
    /// RG, 8-bit unsigned normalized each.
    /// </summary>
    R8_G8_UNorm,
    /// <summary>
    /// RG, 8-bit unsigned int each.
    /// </summary>
    R8_G8_UInt,
    /// <summary>
    /// RG, 8-bit signed int each.
    /// </summary>
    R8_G8_SInt,
    /// <summary>
    /// RG, 16-bit unsigned normalized each.
    /// </summary>
    R16_G16_UNorm,
    /// <summary>
    /// RG, 16-bit signed normalized each.
    /// </summary>
    R16_G16_SNorm,
    /// <summary>
    /// RG, 16-bit unsigned int each.
    /// </summary>
    R16_G16_UInt,
    /// <summary>
    /// RG, 16-bit signed int each.
    /// </summary>
    R16_G16_SInt,
    /// <summary>
    /// RG, 16-bit float each.
    /// </summary>
    R16_G16_Float,
    /// <summary>
    /// RG, 32-bit unsigned int each.
    /// </summary>
    R32_G32_UInt,
    /// <summary>
    /// RG, 32-bit signed int each.
    /// </summary>
    R32_G32_SInt,
    /// <summary>
    /// RG, 32-bit float each.
    /// </summary>
    R32_G32_Float,
    /// <summary>
    /// RGBA, 8-bit signed normalized each.
    /// </summary>
    R8_G8_B8_A8_SNorm,
    /// <summary>
    /// RGBA, 8-bit unsigned int each.
    /// </summary>
    R8_G8_B8_A8_UInt,
    /// <summary>
    /// RGBA, 8-bit signed int each.
    /// </summary>
    R8_G8_B8_A8_SInt,
    /// <summary>
    /// RGBA, 16-bit unsigned normalized each.
    /// </summary>
    R16_G16_B16_A16_UNorm,
    /// <summary>
    /// RGBA, 16-bit signed normalized each.
    /// </summary>
    R16_G16_B16_A16_SNorm,
    /// <summary>
    /// RGBA, 16-bit unsigned int each.
    /// </summary>
    R16_G16_B16_A16_UInt,
    /// <summary>
    /// RGBA, 16-bit signed int each.
    /// </summary>
    R16_G16_B16_A16_SInt,
    /// <summary>
    /// RGBA, 16-bit float each.
    /// </summary>
    R16_G16_B16_A16_Float,
    /// <summary>
    /// RGBA, 32-bit signed int each.
    /// </summary>
    R32_G32_B32_A32_SInt,
    /// <summary>
    /// 64-bit, 4x4 block compressed, unsigned normalized RGB.
    /// </summary>
    ETC2_R8_G8_B8_UNorm,
    /// <summary>
    /// 64-bit, 4x4 block compressed, unsigned normalized RGB + 1-bit alpha.
    /// </summary>
    ETC2_R8_G8_B8_A1_UNorm,
    /// <summary>
    /// 128-bit, 4x4 block compressed, 64 bits unsigned normalized RGB + 64 bits alpha.
    /// </summary>
    ETC2_R8_G8_B8_A8_UNorm,
    /// <summary>
    /// BC4 block compressed, unsigned normalized.
    /// </summary>
    BC4_UNorm,
    /// <summary>
    /// BC4 block compressed, signed normalized.
    /// </summary>
    BC4_SNorm,
    /// <summary>
    /// BC5 block compressed, unsigned normalized.
    /// </summary>
    BC5_UNorm,
    /// <summary>
    /// BC5 block compressed, signed normalized.
    /// </summary>
    BC5_SNorm,
    /// <summary>
    /// BC7 block compressed.
    /// </summary>
    BC7_UNorm,
    /// <summary>
    /// RGBA, 8-bit unsigned normalized each, sRGB.
    /// </summary>
    R8_G8_B8_A8_UNorm_SRgb,
    /// <summary>
    /// BGRA, 8-bit unsigned normalized each, sRGB.
    /// </summary>
    B8_G8_R8_A8_UNorm_SRgb,
    /// <summary>
    /// BC1 block compressed, no alpha, sRGB.
    /// </summary>
    BC1_Rgb_UNorm_SRgb,
    /// <summary>
    /// BC1 block compressed, 1-bit alpha, sRGB.
    /// </summary>
    BC1_Rgba_UNorm_SRgb,
    /// <summary>
    /// BC2 block compressed, sRGB.
    /// </summary>
    BC2_UNorm_SRgb,
    /// <summary>
    /// BC3 block compressed, sRGB.
    /// </summary>
    BC3_UNorm_SRgb,
    /// <summary>
    /// BC7 block compressed, sRGB.
    /// </summary>
    BC7_UNorm_SRgb,
}
