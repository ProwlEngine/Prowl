// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>
/// The layouts a file borrows from video: brightness stored separately from colour and usually at
/// a lower resolution, over a shorter range than the full one, sixteen to two hundred and thirty
/// five at eight bits and the same fractions at ten and sixteen.
/// </summary>
internal static class DdsVideo
{
    /// <summary>
    /// Turns one brightness and its colour pair into colour. The weights are the ones the
    /// standard definition television standard fixes, and the arithmetic runs at the depth the
    /// samples were stored at rather than after narrowing them, which is where the two readings
    /// differ by a step at the edges.
    /// </summary>
    public static uint ToColour(int y, int u, int v, int bits, int alpha)
    {
        int shift = bits - 8;
        double scale = 255.0 / ((1 << bits) - 1);

        double luma = 1.164383 * (y - (16 << shift));
        double cb = u - (128 << shift);
        double cr = v - (128 << shift);

        byte red = Clip((luma + (1.596027 * cr)) * scale);
        byte green = Clip((luma - (0.812968 * cr) - (0.391762 * cb)) * scale);
        byte blue = Clip((luma + (2.017232 * cb)) * scale);

        return ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
    }

    private static byte Clip(double value)
    {
        double rounded = Math.Round(value, MidpointRounding.ToEven);
        return rounded < 0 ? (byte)0 : rounded > 255 ? (byte)255 : (byte)rounded;
    }

    /// <summary>How many bits a sample of each layout holds.</summary>
    public static int Depth(int format) => format switch
    {
        101 or 104 or 108 => 10,
        102 or 105 or 109 => 16,
        _ => 8,
    };

    /// <summary>Every pixel carries its own colour pair, packed one way or another.</summary>
    public static bool TryFull(ReadOnlySpan<byte> data, int at, int width, int height, int format,
                               Span<uint> pixels)
    {
        int size = format == 102 ? 8 : 4;
        int bits = Depth(format);
        long total = (long)width * height * size;

        if (at < 0 || at + total > data.Length)
            return false;

        for (int i = 0; i < width * height; i++)
        {
            int from = at + (i * size);
            int y, u, v, alpha;

            switch (format)
            {
                case 100:
                    v = data[from];
                    u = data[from + 1];
                    y = data[from + 2];
                    alpha = data[from + 3];
                    break;

                case 101:
                {
                    uint packed = BinaryPrimitives.ReadUInt32LittleEndian(data[from..]);
                    u = (int)(packed & 0x3FF);
                    y = (int)((packed >> 10) & 0x3FF);
                    v = (int)((packed >> 20) & 0x3FF);
                    alpha = (int)(packed >> 30) * 85;
                    break;
                }

                default:
                    u = BinaryPrimitives.ReadUInt16LittleEndian(data[from..]);
                    y = BinaryPrimitives.ReadUInt16LittleEndian(data[(from + 2)..]);
                    v = BinaryPrimitives.ReadUInt16LittleEndian(data[(from + 4)..]);
                    alpha = BinaryPrimitives.ReadUInt16LittleEndian(data[(from + 6)..]) >> 8;
                    break;
            }

            pixels[i] = ToColour(y, u, v, bits, alpha);
        }

        return true;
    }

    /// <summary>Two pixels side by side share one colour pair.</summary>
    public static bool TryPaired(ReadOnlySpan<byte> data, int at, int width, int height, int format,
                                 Span<uint> pixels)
    {
        bool wide = format is 108 or 109;
        int bits = Depth(format);
        int pairBytes = wide ? 8 : 4;
        int pairs = (width + 1) / 2;
        long rowBytes = (long)pairs * pairBytes;

        if (at < 0 || at + (rowBytes * height) > data.Length)
            return false;

        for (int row = 0; row < height; row++)
        {
            int from = at + (int)(row * rowBytes);

            for (int pair = 0; pair < pairs; pair++)
            {
                int first, second, u, v;

                if (wide)
                {
                    // The ten bit form leaves its value at the top of each word, so it is shifted
                    // down to the depth the arithmetic runs at.
                    int by = from + (pair * 8);
                    int drop = 16 - bits;

                    first = BinaryPrimitives.ReadUInt16LittleEndian(data[by..]) >> drop;
                    u = BinaryPrimitives.ReadUInt16LittleEndian(data[(by + 2)..]) >> drop;
                    second = BinaryPrimitives.ReadUInt16LittleEndian(data[(by + 4)..]) >> drop;
                    v = BinaryPrimitives.ReadUInt16LittleEndian(data[(by + 6)..]) >> drop;
                }
                else
                {
                    int by = from + (pair * 4);

                    // The two eight bit layouts differ only in which of the four bytes is which.
                    if (format == 107)
                    {
                        first = data[by];
                        u = data[by + 1];
                        second = data[by + 2];
                        v = data[by + 3];
                    }
                    else
                    {
                        u = data[by];
                        first = data[by + 1];
                        v = data[by + 2];
                        second = data[by + 3];
                    }
                }

                int x = pair * 2;
                pixels[(row * width) + x] = ToColour(first, u, v, bits, 255);

                if (x + 1 < width)
                    pixels[(row * width) + x + 1] = ToColour(second, u, v, bits, 255);
            }
        }

        return true;
    }

    /// <summary>
    /// Brightness is a plane of its own and the colour pair follows at half resolution in both
    /// directions, interleaved rather than as two planes.
    /// </summary>
    public static bool TryPlanar(ReadOnlySpan<byte> data, int at, int width, int height, int format,
                                 Span<uint> pixels)
    {
        int size = format == 103 ? 1 : 2;
        int bits = Depth(format);
        int drop = size == 1 ? 0 : 16 - bits;

        int chromaWidth = (width + 1) / 2;
        int chromaHeight = (height + 1) / 2;

        long luma = (long)width * height * size;
        long chroma = (long)chromaWidth * chromaHeight * 2 * size;

        if (at < 0 || at + luma + chroma > data.Length)
            return false;

        int chromaAt = at + (int)luma;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int lumaAt = at + (((y * width) + x) * size);
                int pairAt = chromaAt + ((((y / 2) * chromaWidth) + (x / 2)) * 2 * size);

                int value = Read(data, lumaAt, size, drop);
                int u = Read(data, pairAt, size, drop);
                int v = Read(data, pairAt + size, size, drop);

                pixels[(y * width) + x] = ToColour(value, u, v, bits, 255);
            }
        }

        return true;
    }

    private static int Read(ReadOnlySpan<byte> data, int at, int size, int drop) =>
        size == 1 ? data[at] : BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) >> drop;
}
