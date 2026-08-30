// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>How the bytes after the header are arranged.</summary>
internal enum DdsLayout
{
    Unsupported,

    /// <summary>Channels packed into a fixed number of bits a pixel, picked out by masks.</summary>
    Packed,

    /// <summary>Each channel a whole one, two or four byte value of its own.</summary>
    Components,

    Bc1,
    Bc2,
    Bc3,
    Bc4,
    Bc5,
    Bc6h,
    Bc7,

    /// <summary>Brightness and colour rather than red, green and blue.</summary>
    Video,

    /// <summary>Three floating point channels sharing one word, with no sign bits.</summary>
    PackedFloat,

    /// <summary>One bit a pixel, black or white.</summary>
    Bilevel,

    /// <summary>Green a pixel with red and blue shared between each pair.</summary>
    SharedChroma,

    /// <summary>Ten bit channels over a range that runs past nought and one.</summary>
    ExtendedRange,

    /// <summary>Three mantissas sharing one exponent, packed into a word.</summary>
    SharedExponent,

    /// <summary>Blocks of a size the file chooses, from four by four up to twelve by twelve.</summary>
    Astc,
}

/// <summary>
/// The header, reduced to what the pixel path needs. A file may hold a stack of mip levels and a
/// stack of faces or array slices after them, but the largest level of the first slice is the
/// image, and it always sits first.
/// </summary>
internal struct DdsSurface
{
    private const int Magic = 0x20534444;
    private const int HeaderSize = 124;
    private const uint AlphaPixels = 0x1;
    private const uint FourCc = 0x4;
    private const uint Rgb = 0x40;
    private const uint Luminance = 0x20000;
    private const uint Alpha = 0x2;

    /// <summary>Set where the channels hold a direction rather than a colour, and are signed.</summary>
    private const uint Signature = 0x80000;
    private const uint PixelFormatSize = 32;
    private const uint CubeMap = 0x200;
    private const uint CubeFaces = 0xFC00;

    public int Width;
    public int Height;
    public int DataOffset;

    /// <summary>How many mip levels follow the largest one, itself included.</summary>
    public int MipLevels;

    /// <summary>How many whole textures the file holds: six for a cube map, more for an array.</summary>
    public int Slices;

    /// <summary>How many slices deep a volume texture is, one for a flat one.</summary>
    public int Depth;

    public DdsLayout Layout;

    /// <summary>
    /// What the file calls its layout, numbered the way the newer header numbers it. A file
    /// written the older way names its layout with four letters or a set of masks instead, so
    /// what is kept here is the number the same layout would have had.
    /// </summary>
    public uint DxgiFormat;

    /// <summary>True where the block or channel values are signed rather than unsigned.</summary>
    public bool Signed;

    /// <summary>True where the alpha has already been multiplied into the colour it belongs to.</summary>
    public bool Premultiplied;

    /// <summary>Bits one channel occupies, for <see cref="DdsLayout.Components"/>.</summary>
    public int ComponentBits;

    /// <summary>Channels stored, in the order red, green, blue, alpha.</summary>
    public int ComponentCount;

    /// <summary>True where the channels hold floating point rather than a fraction of full.</summary>
    public bool Float;

    /// <summary>True where the red channel was moved into the alpha slot, as a normal map trick.</summary>
    public bool RedInAlpha;

    /// <summary>Which of the video layouts this is, for <see cref="DdsLayout.Video"/>.</summary>
    public int VideoFormat;

    /// <summary>How wide and tall a block is, for <see cref="DdsLayout.Astc"/>.</summary>
    public int AstcWidth;
    public int AstcHeight;

    /// <summary>Bits one packed pixel occupies, for <see cref="DdsLayout.Packed"/>.</summary>
    public int BitCount;

    public uint RedMask;
    public uint GreenMask;
    public uint BlueMask;
    public uint AlphaMask;

    /// <summary>Set where the three colour channels are stored in the order blue, green, red.</summary>
    public readonly bool HasAlpha => AlphaMask != 0 ||
        Layout is DdsLayout.Bc1 or DdsLayout.Bc2 or DdsLayout.Bc3 or DdsLayout.Bc7 or DdsLayout.Astc;

    public readonly bool HasAlphaChannel => Layout == DdsLayout.Components
        ? ComponentCount == 4
        : HasAlpha;

    /// <summary>Bytes one block occupies, or zero where the layout is not block compressed.</summary>
    public readonly int BlockBytes => Layout switch
    {
        DdsLayout.Bc1 or DdsLayout.Bc4 => 8,
        DdsLayout.Bc2 or DdsLayout.Bc3 or DdsLayout.Bc5 or DdsLayout.Bc6h or DdsLayout.Bc7 => 16,
        DdsLayout.Astc => 16,
        _ => 0,
    };

    /// <summary>Pixels one block covers across, one where the layout is not block compressed.</summary>
    public readonly int BlockWidth => Layout switch
    {
        DdsLayout.Astc => AstcWidth,
        _ => BlockBytes > 0 ? 4 : 1,
    };

    /// <summary>Pixels one block covers down, one where the layout is not block compressed.</summary>
    public readonly int BlockHeight => Layout switch
    {
        DdsLayout.Astc => AstcHeight,
        _ => BlockBytes > 0 ? 4 : 1,
    };

    /// <summary>Bytes one of the video layouts occupies, which depends on how it stores colour.</summary>
    private readonly long VideoBytes
    {
        get
        {
            long pixels = (long)Width * Height;
            long across = (Width + 1) / 2;

            return VideoFormat switch
            {
                100 or 101 => pixels * 4,
                102 => pixels * 8,
                103 => pixels + (across * ((Height + 1) / 2) * 2),
                104 or 105 => (pixels + (across * ((Height + 1) / 2) * 2)) * 2,
                108 or 109 => across * Height * 8,
                _ => across * Height * 4,
            };
        }
    }

    /// <summary>Bytes the largest mip level of the first slice occupies.</summary>
    public readonly long SurfaceBytes
    {
        get
        {
            if (Layout == DdsLayout.Components)
                return (long)Width * Height * ComponentCount * (ComponentBits / 8);

            // Neither the video layouts nor the bilevel one is a bit count a pixel.
            if (Layout == DdsLayout.Video)
                return VideoBytes;

            if (Layout is DdsLayout.PackedFloat or DdsLayout.ExtendedRange or DdsLayout.SharedExponent)
                return (long)Width * Height * 4;

            if (Layout == DdsLayout.SharedChroma)
                return (long)((Width + 1) / 2) * Height * 4;

            if (Layout == DdsLayout.Astc)
            {
                long blocksAcross = (Width + AstcWidth - 1) / AstcWidth;
                long blocksDown = (Height + AstcHeight - 1) / AstcHeight;
                return blocksAcross * blocksDown * 16;
            }

            if (Layout == DdsLayout.Bilevel)
                return (long)((Width + 7) / 8) * Height;

            if (BlockBytes == 0)
                return ((long)Width * BitCount / 8) * Height;

            long across = (Width + 3) / 4;
            long down = (Height + 3) / 4;
            return across * down * BlockBytes;
        }
    }

    public static bool TryRead(ReadOnlySpan<byte> data, out DdsSurface surface, out ApertureError error)
    {
        surface = default;

        if (data.Length < 4 + HeaderSize)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(data) != Magic ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != HeaderSize)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        surface.Height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        surface.Width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

        if (surface.Width <= 0 || surface.Height <= 0)
        {
            error = ApertureError.InvalidDimensions;
            return false;
        }

        // The largest dimension the format allows needs seventeen mip levels, and a cube map
        // naming no face has said nothing, so either is broken rather than large.
        uint depth = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
        uint mipLevels = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
        uint caps2 = BinaryPrimitives.ReadUInt32LittleEndian(data[112..]);

        if (mipLevels > 32 || depth > 65536)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if ((caps2 & CubeMap) != 0 && (caps2 & CubeFaces) == 0)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data[76..]) != PixelFormatSize)
        {
            error = ApertureError.InvalidHeader;
            return false;
        }

        uint pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(data[80..]);
        ReadOnlySpan<byte> fourCc = data.Slice(84, 4);
        surface.BitCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[88..]);
        surface.RedMask = BinaryPrimitives.ReadUInt32LittleEndian(data[92..]);
        surface.GreenMask = BinaryPrimitives.ReadUInt32LittleEndian(data[96..]);
        surface.BlueMask = BinaryPrimitives.ReadUInt32LittleEndian(data[100..]);
        surface.AlphaMask = BinaryPrimitives.ReadUInt32LittleEndian(data[104..]);

        surface.DataOffset = 4 + HeaderSize;
        surface.MipLevels = (int)Math.Clamp(mipLevels == 0 ? 1 : mipLevels, 1, 32);
        surface.Slices = (caps2 & CubeMap) != 0 ? 6 : 1;
        surface.Depth = (int)Math.Max(1, Math.Min(depth, 65536));

        if ((pixelFlags & FourCc) != 0 && fourCc.SequenceEqual("DX10"u8))
        {
            if (data.Length < surface.DataOffset + 20)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            uint dxgi = BinaryPrimitives.ReadUInt32LittleEndian(data[surface.DataOffset..]);
            uint arraySize = BinaryPrimitives.ReadUInt32LittleEndian(data[(surface.DataOffset + 12)..]);

            if (arraySize > 65536)
            {
                error = ApertureError.InvalidHeader;
                return false;
            }

            surface.DataOffset += 20;
            surface.Slices *= (int)Math.Max(1, Math.Min(arraySize, 4096));
            surface.DxgiFormat = dxgi;
            FromDxgi(ref surface, dxgi);
        }
        else if ((pixelFlags & FourCc) != 0)
        {
            FromFourCc(ref surface, fourCc);
            surface.DxgiFormat = FromFourCcNumber(fourCc);
        }
        else if ((pixelFlags & Signature) != 0)
        {
            // A surface holding a direction stores its channels signed.
            surface.Layout = DdsLayout.Components;
            surface.Signed = true;
            surface.ComponentBits = System.Numerics.BitOperations.PopCount(surface.RedMask);
            surface.ComponentCount =
                (surface.RedMask != 0 ? 1 : 0) + (surface.GreenMask != 0 ? 1 : 0) +
                (surface.BlueMask != 0 ? 1 : 0) + (surface.AlphaMask != 0 ? 1 : 0);

            if (surface.ComponentBits is not (8 or 16 or 32) || surface.ComponentCount == 0)
                surface.Layout = DdsLayout.Unsupported;
        }
        else if ((pixelFlags & (Rgb | Luminance | Alpha)) != 0)
        {
            surface.Layout = DdsLayout.Packed;
            surface.DxgiFormat = FromMasks(surface.BitCount, surface.RedMask, surface.GreenMask,
                                           surface.BlueMask, surface.AlphaMask);

            // A luminance surface names only a red mask, and the one channel stands for all three.
            if ((pixelFlags & Luminance) != 0 && surface.GreenMask == 0 && surface.BlueMask == 0)
            {
                surface.GreenMask = surface.RedMask;
                surface.BlueMask = surface.RedMask;
            }

            if ((pixelFlags & AlphaPixels) == 0)
                surface.AlphaMask = 0;

            SwapTenBitRedAndBlue(ref surface);

            if (surface.BitCount is not (8 or 16 or 24 or 32))
                surface.Layout = DdsLayout.Unsupported;
        }

        error = surface.Layout == DdsLayout.Unsupported ? ApertureError.UnsupportedFeature : ApertureError.None;
        return surface.Layout != DdsLayout.Unsupported;
    }

    /// <summary>
    /// The number the newer header would have used for a layout the older one names with four
    /// letters. Every one of these is the same arrangement of bits under a different name.
    /// </summary>
    private static uint FromFourCcNumber(ReadOnlySpan<byte> fourCc)
    {
        if (fourCc.SequenceEqual("DXT1"u8)) return 71;
        if (fourCc.SequenceEqual("DXT2"u8) || fourCc.SequenceEqual("DXT3"u8)) return 74;
        if (fourCc.SequenceEqual("DXT4"u8) || fourCc.SequenceEqual("DXT5"u8)) return 77;
        if (fourCc.SequenceEqual("ATI1"u8) || fourCc.SequenceEqual("BC4U"u8)) return 80;
        if (fourCc.SequenceEqual("BC4S"u8)) return 81;
        if (fourCc.SequenceEqual("ATI2"u8) || fourCc.SequenceEqual("BC5U"u8)) return 83;
        if (fourCc.SequenceEqual("BC5S"u8)) return 84;
        if (fourCc.SequenceEqual("RGBG"u8)) return 68;
        if (fourCc.SequenceEqual("GRGB"u8)) return 69;

        // The remaining four letter names are old Direct3D numbers, mapped separately.
        uint code = (uint)(fourCc[0] | (fourCc[1] << 8) | (fourCc[2] << 16) | (fourCc[3] << 24));
        return code switch
        {
            36 => 11,       // A16B16G16R16
            110 => 13,      // Q16W16V16U16, signed
            111 => 54,      // R16F
            112 => 34,      // G16R16F
            113 => 10,      // A16B16G16R16F
            114 => 41,      // R32F
            115 => 16,      // G32R32F
            116 => 2,       // A32B32G32R32F
            _ => 0,
        };
    }

    /// <summary>The number the newer header would have used for a set of channel masks.</summary>
    private static uint FromMasks(int bits, uint red, uint green, uint blue, uint alpha) =>
        (bits, red, green, blue, alpha) switch
        {
            (32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000) => 28,   // R8G8B8A8_UNORM
            (32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000) => 87,   // B8G8R8A8_UNORM
            (32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0) => 88,            // B8G8R8X8_UNORM
            (32, 0x0000FFFF, 0xFFFF0000, 0, 0) => 35,                     // R16G16_UNORM
            (32, 0xFFFFFFFF, 0, 0, 0) => 56,                              // R32_UNORM as R32_UINT
            (32, 0x000003FF, 0x000FFC00, 0x3FF00000, 0xC0000000) => 24,   // R10G10B10A2_UNORM
            (16, 0x0000F800, 0x000007E0, 0x0000001F, 0) => 85,            // B5G6R5_UNORM
            (16, 0x00007C00, 0x000003E0, 0x0000001F, 0x00008000) => 86,   // B5G5R5A1_UNORM
            (16, 0x00000F00, 0x000000F0, 0x0000000F, 0x0000F000) => 115,  // B4G4R4A4_UNORM
            (16, 0x000000FF, 0, 0, 0x0000FF00) => 49,                     // R8G8_UNORM
            (8, 0x000000FF, 0, 0, 0) => 61,                               // R8_UNORM
            (8, 0, 0, 0, 0x000000FF) => 65,                               // A8_UNORM
            _ => 0,
        };

    private static void FromFourCc(ref DdsSurface surface, ReadOnlySpan<byte> fourCc)
    {
        // Two pairs differ only in premultiplication, so they share a layout until the end.
        surface.Layout =
            fourCc.SequenceEqual("DXT1"u8) ? DdsLayout.Bc1 :
            fourCc.SequenceEqual("DXT2"u8) || fourCc.SequenceEqual("DXT3"u8) ? DdsLayout.Bc2 :
            fourCc.SequenceEqual("DXT4"u8) || fourCc.SequenceEqual("DXT5"u8) ? DdsLayout.Bc3 :
            fourCc.SequenceEqual("ATI1"u8) || fourCc.SequenceEqual("BC4U"u8) ? DdsLayout.Bc4 :
            fourCc.SequenceEqual("ATI2"u8) || fourCc.SequenceEqual("BC5U"u8) ? DdsLayout.Bc5 :
            fourCc.SequenceEqual("BC4S"u8) ? DdsLayout.Bc4 :
            fourCc.SequenceEqual("BC5S"u8) ? DdsLayout.Bc5 :
            fourCc.SequenceEqual("RXGB"u8) ? DdsLayout.Bc3 :
            fourCc.SequenceEqual("BC6H"u8) ? DdsLayout.Bc6h :
            fourCc.SequenceEqual("BC7L"u8) || fourCc.SequenceEqual("BC7\0"u8) ? DdsLayout.Bc7 :
            fourCc.SequenceEqual("UYVY"u8) || fourCc.SequenceEqual("YUY2"u8) ? DdsLayout.Video :
            DdsLayout.Unsupported;

        // A plain number where a name would go, which is how the float layouts are named.
        if (surface.Layout == DdsLayout.Unsupported)
        {
            uint numeric = BinaryPrimitives.ReadUInt32LittleEndian(fourCc);
            (int bits, int count) = numeric switch
            {
                36 => (16, 4),
                110 => (16, 4),
                111 => (16, 1),
                112 => (16, 2),
                113 => (16, 4),
                114 => (32, 1),
                115 => (32, 2),
                116 => (32, 4),
                _ => (0, 0),
            };

            if (bits != 0)
            {
                surface.Layout = DdsLayout.Components;
                surface.ComponentBits = bits;
                surface.ComponentCount = count;
                surface.Float = numeric is >= 111 and <= 116;
                surface.Signed = numeric == 110;
            }
            else if (numeric is 0x47424752 or 0x42475247)
            {
                surface.Layout = DdsLayout.SharedChroma;
                surface.VideoFormat = numeric == 0x47424752 ? 69 : 68;
            }
        }

        surface.Signed = fourCc.SequenceEqual("BC4S"u8) || fourCc.SequenceEqual("BC5S"u8);

        if (surface.Layout == DdsLayout.Video)
            surface.VideoFormat = fourCc.SequenceEqual("YUY2"u8) ? 107 : 106;
        surface.Premultiplied = fourCc.SequenceEqual("DXT2"u8) || fourCc.SequenceEqual("DXT4"u8);

        // This four letter name declares a normal map whose red sits in the alpha slot, where it
        // survives quantisation better than any of the three colour channels do.
        surface.RedInAlpha = fourCc.SequenceEqual("RXGB"u8);
    }

    /// <summary>
    /// Puts back the red and blue of a ten ten ten two surface. Files of this one shape are
    /// written with the two masks the wrong way round often enough that the header cannot be
    /// believed, so the correction is confined to exactly it and applies nowhere else.
    /// </summary>
    private static void SwapTenBitRedAndBlue(ref DdsSurface surface)
    {
        if (surface.BitCount != 32 || surface.GreenMask != 0x000FFC00 || surface.AlphaMask != 0xC0000000)
            return;

        if ((surface.RedMask != 0x3FF00000 || surface.BlueMask != 0x000003FF) &&
            (surface.RedMask != 0x000003FF || surface.BlueMask != 0x3FF00000))
            return;

        (surface.RedMask, surface.BlueMask) = (surface.BlueMask, surface.RedMask);
    }

    private static void FromDxgi(ref DdsSurface surface, uint dxgi)
    {
        switch (dxgi)
        {
            case >= 70 and <= 72:
                surface.Layout = DdsLayout.Bc1;
                return;

            case >= 73 and <= 75:
                surface.Layout = DdsLayout.Bc2;
                return;

            case >= 76 and <= 78:
                surface.Layout = DdsLayout.Bc3;
                return;

            case >= 79 and <= 81:
                surface.Layout = DdsLayout.Bc4;
                surface.Signed = dxgi == 81;
                return;

            case >= 82 and <= 84:
                surface.Layout = DdsLayout.Bc5;
                surface.Signed = dxgi == 84;
                return;

            case >= 94 and <= 96:
                surface.Layout = DdsLayout.Bc6h;
                surface.Signed = dxgi == 96;
                return;

            case >= 97 and <= 99:
                surface.Layout = DdsLayout.Bc7;
                return;

            case 26:
                surface.Layout = DdsLayout.PackedFloat;
                return;

            case 67:
                surface.Layout = DdsLayout.SharedExponent;
                return;

            case >= 134 and <= 188 when ((dxgi - 134) & 3) < 3:
            {
                // The fourteen block sizes run in order, each taking four of the format numbers.
                ReadOnlySpan<byte> sizes =
                [
                    4, 4, 5, 4, 5, 5, 6, 5, 6, 6, 8, 5, 8, 6,
                    8, 8, 10, 5, 10, 6, 10, 8, 10, 10, 12, 10, 12, 12,
                ];

                int index = (int)(dxgi - 134) / 4;
                if (index >= 14)
                    return;

                surface.Layout = DdsLayout.Astc;
                surface.AstcWidth = sizes[index * 2];
                surface.AstcHeight = sizes[(index * 2) + 1];
                return;
            }

            case 68 or 69:
                surface.Layout = DdsLayout.SharedChroma;
                surface.VideoFormat = (int)dxgi;
                return;

            case 89:
                surface.Layout = DdsLayout.ExtendedRange;
                return;

            case 66:
                surface.Layout = DdsLayout.Bilevel;
                return;

            case 100 or 101 or 102 or 103 or 104 or 105 or 107 or 108 or 109 or 106:
                surface.Layout = DdsLayout.Video;
                surface.VideoFormat = (int)dxgi;
                return;
        }

        // Layouts whose channels are whole values rather than fields inside a word.
        (int componentBits, int count, bool floating, bool signedValue) = dxgi switch
        {
            2 => (32, 4, true, false),
            6 => (32, 3, true, false),
            10 => (16, 4, true, false),
            11 => (16, 4, false, false),
            13 => (16, 4, false, true),
            16 => (32, 2, true, false),
            31 => (8, 4, false, true),
            34 => (16, 2, true, false),
            35 => (16, 2, false, false),
            37 => (16, 2, false, true),
            41 => (32, 1, true, false),
            51 => (8, 2, false, true),
            54 => (16, 1, true, false),
            56 => (16, 1, false, false),
            58 => (16, 1, false, true),
            63 => (8, 1, false, true),
            _ => (0, 0, false, false),
        };

        if (componentBits != 0)
        {
            surface.Layout = DdsLayout.Components;
            surface.ComponentBits = componentBits;
            surface.ComponentCount = count;
            surface.Float = floating;
            surface.Signed = signedValue;
            return;
        }

        // Packed colour, stated as the older header states it. The names run from the low end
        // of the word up, so the B of B5G6R5 is the bottom five bits.
        (int bits, uint r, uint g, uint b, uint a) = dxgi switch
        {
            27 or 28 or 29 => (32, 0x000000FFu, 0x0000FF00u, 0x00FF0000u, 0xFF000000u),
            87 or 90 or 91 => (32, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0xFF000000u),
            88 or 92 or 93 => (32, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0u),
            85 => (16, 0xF800u, 0x07E0u, 0x001Fu, 0u),
            86 => (16, 0x7C00u, 0x03E0u, 0x001Fu, 0x8000u),
            115 => (16, 0x0F00u, 0x00F0u, 0x000Fu, 0xF000u),
            191 => (16, 0xF000u, 0x0F00u, 0x00F0u, 0x000Fu),
            48 or 49 or 50 or 51 or 52 => (16, 0x00FFu, 0xFF00u, 0u, 0u),
            24 => (32, 0x000003FFu, 0x000FFC00u, 0x3FF00000u, 0xC0000000u),
            60 or 61 or 62 or 64 => (8, 0xFFu, 0xFFu, 0xFFu, 0u),
            65 => (8, 0u, 0u, 0u, 0xFFu),
            _ => (0, 0u, 0u, 0u, 0u),
        };

        if (bits == 0)
        {
            surface.Layout = DdsLayout.Unsupported;
            return;
        }

        surface.Layout = DdsLayout.Packed;
        surface.BitCount = bits;
        surface.RedMask = r;
        surface.GreenMask = g;
        surface.BlueMask = b;
        surface.AlphaMask = a;
    }
}
