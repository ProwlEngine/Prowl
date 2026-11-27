// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture;

/// <summary>
/// The arrangement of bytes a texture is stored in, numbered as DXGI numbers them, which every
/// graphics API publishes a mapping from. A file written the older way names its layout with four
/// letters or channel masks; where that is the same arrangement of bits it is reported as this
/// one. Anything unlisted reports <see cref="Unknown"/> and still hands back its bytes.
/// </summary>
public enum TextureFormat : uint
{
    /// <summary>No layout this library can name, though the bytes are still there.</summary>
    Unknown = 0,

    R32G32B32A32Float = 2,
    R32G32B32A32Uint = 3,
    R32G32B32Float = 6,
    R16G16B16A16Float = 10,
    R16G16B16A16Unorm = 11,
    R32G32Float = 16,
    R10G10B10A2Unorm = 24,
    R11G11B10Float = 26,
    R8G8B8A8Unorm = 28,
    R8G8B8A8UnormSrgb = 29,
    R8G8B8A8Snorm = 31,
    R16G16Float = 34,
    R16G16Unorm = 35,
    R32Float = 41,
    R8G8Unorm = 49,
    R16Float = 54,
    R16Unorm = 56,
    R8Unorm = 61,
    A8Unorm = 65,
    R9G9B9E5SharedExponent = 67,
    R8G8B8G8Unorm = 68,
    G8R8G8B8Unorm = 69,

    Bc1Unorm = 71,
    Bc1UnormSrgb = 72,
    Bc2Unorm = 74,
    Bc2UnormSrgb = 75,
    Bc3Unorm = 77,
    Bc3UnormSrgb = 78,
    Bc4Unorm = 80,
    Bc4Snorm = 81,
    Bc5Unorm = 83,
    Bc5Snorm = 84,

    B5G6R5Unorm = 85,
    B5G5R5A1Unorm = 86,
    B8G8R8A8Unorm = 87,
    B8G8R8X8Unorm = 88,
    B8G8R8A8UnormSrgb = 91,

    Bc6hUf16 = 95,
    Bc6hSf16 = 96,
    Bc7Unorm = 98,
    Bc7UnormSrgb = 99,

    B4G4R4A4Unorm = 115,

    Astc4x4Unorm = 134,
    Astc4x4UnormSrgb = 135,
    Astc5x4Unorm = 138,
    Astc5x4UnormSrgb = 139,
    Astc5x5Unorm = 142,
    Astc5x5UnormSrgb = 143,
    Astc6x5Unorm = 146,
    Astc6x5UnormSrgb = 147,
    Astc6x6Unorm = 150,
    Astc6x6UnormSrgb = 151,
    Astc8x5Unorm = 154,
    Astc8x5UnormSrgb = 155,
    Astc8x6Unorm = 158,
    Astc8x6UnormSrgb = 159,
    Astc8x8Unorm = 162,
    Astc8x8UnormSrgb = 163,
    Astc10x5Unorm = 166,
    Astc10x5UnormSrgb = 167,
    Astc10x6Unorm = 170,
    Astc10x6UnormSrgb = 171,
    Astc10x8Unorm = 174,
    Astc10x8UnormSrgb = 175,
    Astc10x10Unorm = 178,
    Astc10x10UnormSrgb = 179,
    Astc12x10Unorm = 182,
    Astc12x10UnormSrgb = 183,
    Astc12x12Unorm = 186,
    Astc12x12UnormSrgb = 187,
}
