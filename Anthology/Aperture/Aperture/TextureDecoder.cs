// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Dds;

namespace Prowl.Aperture;

public static class TextureDecoder
{
    /// <summary>Whether <paramref name="format"/> can be expanded.</summary>
    public static bool CanDecode(TextureFormat format) => format switch
    {
        TextureFormat.Bc1Unorm or TextureFormat.Bc1UnormSrgb => true,
        TextureFormat.Bc2Unorm or TextureFormat.Bc2UnormSrgb => true,
        TextureFormat.Bc3Unorm or TextureFormat.Bc3UnormSrgb => true,
        TextureFormat.Bc4Unorm or TextureFormat.Bc5Unorm => true,
        TextureFormat.Bc7Unorm or TextureFormat.Bc7UnormSrgb => true,
        _ => IsAstc(format),
    };

    /// <summary>Expands one surface into a new tightly packed RGBA8 array.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> blocks, int width, int height, TextureFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        long length = (long)width * height * 4;
        if (length > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(width), "The surface is too large to expand into one array.");

        byte[] rgba = new byte[length];
        Decode(blocks, width, height, format, rgba, width * 4);
        return rgba;
    }

    /// <summary>Expands one surface into RGBA8 rows <paramref name="stride"/> bytes apart.</summary>
    public static void Decode(ReadOnlySpan<byte> blocks, int width, int height, TextureFormat format,
                              Span<byte> destination, int stride)
    {
        if (!CanDecode(format))
            throw new NotSupportedException($"Decoding {format} is not supported.");

        TextureEncoder.TryGetBlockInfo(format, out int blockWidth, out int blockHeight, out int bytesPerBlock);

        int size = TextureEncoder.GetEncodedSize(format, width, height);
        if (blocks.Length < size)
            throw new ArgumentException($"The source needs {size} bytes.", nameof(blocks));

        long rowBytes = (long)width * 4;
        if (stride < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, $"A row of {width} pixels needs {rowBytes} bytes.");

        if (destination.Length < ((long)stride * (height - 1)) + rowBytes)
            throw new ArgumentException("The destination is smaller than the stated size.", nameof(destination));

        int across = (width + blockWidth - 1) / blockWidth;
        int down = (height + blockHeight - 1) / blockHeight;

        Span<uint> colours = stackalloc uint[blockWidth * blockHeight];
        Span<byte> first = stackalloc byte[16];
        Span<byte> second = stackalloc byte[16];

        for (int blockY = 0; blockY < down; blockY++)
        {
            for (int blockX = 0; blockX < across; blockX++)
            {
                ReadOnlySpan<byte> block = blocks.Slice(((blockY * across) + blockX) * bytesPerBlock, bytesPerBlock);
                Expand(format, block, blockWidth, blockHeight, colours, first, second);

                for (int y = 0; y < blockHeight; y++)
                {
                    int row = (blockY * blockHeight) + y;
                    if (row >= height)
                        break;

                    Span<byte> line = destination[(row * stride)..];

                    for (int x = 0; x < blockWidth; x++)
                    {
                        int column = (blockX * blockWidth) + x;
                        if (column >= width)
                            break;

                        uint colour = colours[(y * blockWidth) + x];
                        int to = column * 4;
                        line[to] = (byte)(colour >> 16);
                        line[to + 1] = (byte)(colour >> 8);
                        line[to + 2] = (byte)colour;
                        line[to + 3] = (byte)(colour >> 24);
                    }
                }
            }
        }
    }

    private static void Expand(TextureFormat format, ReadOnlySpan<byte> block, int blockWidth, int blockHeight,
                               Span<uint> colours, Span<byte> first, Span<byte> second)
    {
        switch (format)
        {
            case TextureFormat.Bc1Unorm or TextureFormat.Bc1UnormSrgb:
                BlockCompression.DecodeColour(block, colours, allowTransparent: true);
                return;

            case TextureFormat.Bc2Unorm or TextureFormat.Bc2UnormSrgb:
                BlockCompression.DecodeSharpAlpha(block, first);
                BlockCompression.DecodeColour(block[8..], colours, allowTransparent: false);
                WithAlpha(colours, first);
                return;

            case TextureFormat.Bc3Unorm or TextureFormat.Bc3UnormSrgb:
                BlockCompression.DecodeAlpha(block, first, signed: false);
                BlockCompression.DecodeColour(block[8..], colours, allowTransparent: false);
                WithAlpha(colours, first);
                return;

            case TextureFormat.Bc4Unorm:
                BlockCompression.DecodeAlpha(block, first, signed: false);
                for (int i = 0; i < 16; i++)
                    colours[i] = 0xFF000000u | ((uint)first[i] << 16);
                return;

            case TextureFormat.Bc5Unorm:
                BlockCompression.DecodeAlpha(block, first, signed: false);
                BlockCompression.DecodeAlpha(block[8..], second, signed: false);
                for (int i = 0; i < 16; i++)
                    colours[i] = 0xFF000000u | ((uint)first[i] << 16) | ((uint)second[i] << 8);
                return;

            case TextureFormat.Bc7Unorm or TextureFormat.Bc7UnormSrgb:
                Bc7.Decode(block, colours);
                return;

            default:
                Astc.Decode(block, blockWidth, blockHeight, colours);
                return;
        }
    }

    private static void WithAlpha(Span<uint> colours, ReadOnlySpan<byte> alpha)
    {
        for (int i = 0; i < 16; i++)
            colours[i] = (colours[i] & 0x00FFFFFFu) | ((uint)alpha[i] << 24);
    }

    private static bool IsAstc(TextureFormat format) =>
        format is >= TextureFormat.Astc4x4Unorm and <= TextureFormat.Astc12x12UnormSrgb
        && TextureEncoder.TryGetBlockInfo(format, out _, out _, out _);
}
