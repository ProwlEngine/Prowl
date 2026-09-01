// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>Turns the largest mip level of the first slice into pixels.</summary>
internal static class DdsImageReader
{
    /// <summary>Pulls one channel out of a packed pixel and stretches it to a byte.</summary>
    private readonly struct Channel
    {
        private readonly int _shift;
        private readonly uint _mask;
        private readonly uint _max;
        private readonly byte[] _table;
        private readonly bool _present;

        public Channel(uint mask)
        {
            if (mask == 0)
            {
                _shift = 0;
                _mask = 0;
                _max = 0;
                _table = UnormScale.Absent;
                _present = false;
                return;
            }

            int shift = BitOperations.TrailingZeroCount(mask);
            int bits = BitOperations.PopCount(mask);

            _shift = shift;
            _mask = bits >= 32 ? uint.MaxValue : (1u << bits) - 1;
            _max = _mask;
            _table = bits <= 8 ? UnormScale.Table(bits) : UnormScale.Absent;
            _present = true;
        }

        public readonly bool IsPresent => _present;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly byte Extract(uint pixel)
        {
            uint value = (pixel >> _shift) & _mask;

            // Too wide for a table worth building, so it is scaled outright.
            return _max <= 255 ? _table[value] : (byte)((((ulong)value * 255) + (_max / 2)) / _max);
        }
    }

    /// <summary>Whether the data present could hold the surface the header declares.</summary>
    public static bool CanDescribe(int available, in DdsSurface surface) => surface.SurfaceBytes <= available;

    public static bool TryDecode(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                 Span<byte> destination, int stride, bool flip, out ApertureError error)
    {
        error = ApertureError.None;

        return surface.Layout switch
        {
            DdsLayout.Packed => DecodePacked(data, surface, channels, destination, stride, flip),
            DdsLayout.Components => DecodeComponents(data, surface, channels, destination, stride, flip),
            DdsLayout.Video => DecodeVideo(data, surface, channels, destination, stride, flip),
            DdsLayout.PackedFloat => DecodePackedFloat(data, surface, channels, destination, stride, flip),
            DdsLayout.Bilevel => DecodeBilevel(data, surface, channels, destination, stride, flip),
            DdsLayout.SharedExponent => DecodeSharedExponent(data, surface, channels, destination, stride, flip),
            DdsLayout.Astc => DecodeAstc(data, surface, channels, destination, stride, flip),
            DdsLayout.SharedChroma => DecodeShared(data, surface, channels, destination, stride, flip),
            DdsLayout.ExtendedRange => DecodeExtended(data, surface, channels, destination, stride, flip),
            _ => DecodeBlocks(data, surface, channels, destination, stride, flip),
        };
    }

    /// <summary>
    /// Green a pixel with red and blue shared between each pair, which is the same saving the
    /// video layouts make but applied to colour rather than to brightness.
    /// </summary>
    private static bool DecodeShared(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                     Span<byte> destination, int stride, bool flip)
    {
        int pairs = (surface.Width + 1) / 2;
        long rowBytes = (long)pairs * 4;

        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        // The names describe the word rather than the bytes.
        bool greenFirst = surface.VideoFormat == 69;

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int pair = 0; pair < pairs; pair++)
            {
                int from = pair * 4;
                byte firstGreen = greenFirst ? source[from] : source[from + 1];
                byte red = greenFirst ? source[from + 1] : source[from];
                byte secondGreen = greenFirst ? source[from + 2] : source[from + 3];
                byte blue = greenFirst ? source[from + 3] : source[from + 2];

                for (int half = 0; half < 2; half++)
                {
                    int x = (pair * 2) + half;
                    if (x >= surface.Width)
                        break;

                    int at = x * channels;
                    row[at] = red;
                    row[at + 1] = half == 0 ? firstGreen : secondGreen;
                    row[at + 2] = blue;

                    if (channels == 4)
                        row[at + 3] = 255;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Ten bit channels whose range runs a little below nought and a little above one, so that a
    /// picture can hold the values a display cannot show without clipping them away.
    /// </summary>
    private static bool DecodeExtended(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                       Span<byte> destination, int stride, bool flip)
    {
        long rowBytes = (long)surface.Width * 4;
        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int x = 0; x < surface.Width; x++)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
                int at = x * channels;

                row[at] = ToByte((((packed & 0x3FF) - 384f) / 510f));
                row[at + 1] = ToByte(((((packed >> 10) & 0x3FF) - 384f) / 510f));
                row[at + 2] = ToByte(((((packed >> 20) & 0x3FF) - 384f) / 510f));

                if (channels == 4)
                    row[at + 3] = (byte)((packed >> 30) * 85);
            }
        }

        return true;
    }

    /// <summary>
    /// Walks the blocks of a surface whose block size the file chose. A block at the right or
    /// bottom edge is whole in the file and read short, as with every other block layout.
    /// </summary>
    private static bool DecodeAstc(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                   Span<byte> destination, int stride, bool flip)
    {
        int blockWidth = surface.AstcWidth;
        int blockHeight = surface.AstcHeight;

        if (blockWidth <= 0 || blockHeight <= 0)
            return false;

        int across = (surface.Width + blockWidth - 1) / blockWidth;
        int down = (surface.Height + blockHeight - 1) / blockHeight;

        long total = (long)across * down * 16;
        if (surface.DataOffset + total > data.Length)
            return false;

        Span<uint> block = stackalloc uint[12 * 12];

        for (int blockY = 0; blockY < down; blockY++)
        {
            for (int blockX = 0; blockX < across; blockX++)
            {
                int at = surface.DataOffset + ((((blockY * across) + blockX) * 16));
                Astc.Decode(data.Slice(at, 16), blockWidth, blockHeight, block);

                for (int y = 0; y < blockHeight; y++)
                {
                    int row = (blockY * blockHeight) + y;
                    if (row >= surface.Height)
                        break;

                    int target = flip ? surface.Height - 1 - row : row;
                    Span<byte> line = destination.Slice(target * stride, surface.Width * channels);

                    for (int x = 0; x < blockWidth; x++)
                    {
                        int column = (blockX * blockWidth) + x;
                        if (column >= surface.Width)
                            break;

                        uint colour = block[(y * blockWidth) + x];
                        int to = column * channels;

                        line[to] = (byte)(colour >> 16);
                        line[to + 1] = (byte)(colour >> 8);
                        line[to + 2] = (byte)colour;

                        if (channels == 4)
                            line[to + 3] = (byte)(colour >> 24);
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Reads one of the layouts borrowed from video and turns it into colour.</summary>
    private static bool DecodeVideo(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                    Span<byte> destination, int stride, bool flip)
    {
        long total = (long)surface.Width * surface.Height;
        if (total <= 0 || total > int.MaxValue / 4)
            return false;

        uint[] pixels = new uint[(int)total];
        int format = surface.VideoFormat;

        bool read = format switch
        {
            100 or 101 or 102 => DdsVideo.TryFull(data, surface.DataOffset, surface.Width,
                                                  surface.Height, format, pixels),
            103 or 104 or 105 => DdsVideo.TryPlanar(data, surface.DataOffset, surface.Width,
                                                    surface.Height, format, pixels),
            _ => DdsVideo.TryPaired(data, surface.DataOffset, surface.Width, surface.Height,
                                    format, pixels),
        };

        if (!read)
            return false;

        Place(pixels, surface, channels, destination, stride, flip);
        return true;
    }

    /// <summary>
    /// Three floating point channels in one word, with no sign and a shared layout of five
    /// exponent bits each. It is the cheapest way a file can hold values past one.
    /// </summary>
    private static bool DecodePackedFloat(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                          Span<byte> destination, int stride, bool flip)
    {
        long rowBytes = (long)surface.Width * 4;
        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int x = 0; x < surface.Width; x++)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
                int at = x * channels;

                row[at] = ToByte(Small(packed & 0x7FF, 6));
                row[at + 1] = ToByte(Small((packed >> 11) & 0x7FF, 6));
                row[at + 2] = ToByte(Small((packed >> 22) & 0x3FF, 5));

                if (channels == 4)
                    row[at + 3] = 255;
            }
        }

        return true;
    }

    /// <summary>
    /// Three nine bit mantissas over one five bit exponent. It cannot hold a channel far brighter
    /// than its neighbours, but for a picture of light where the three move together it is the
    /// cheapest form that still runs past one.
    /// </summary>
    private static bool DecodeSharedExponent(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                             Span<byte> destination, int stride, bool flip)
    {
        long rowBytes = (long)surface.Width * 4;
        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int x = 0; x < surface.Width; x++)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
                float scale = MathF.Pow(2f, (int)(packed >> 27) - 24);
                int at = x * channels;

                row[at] = ToByte((packed & 0x1FF) * scale);
                row[at + 1] = ToByte(((packed >> 9) & 0x1FF) * scale);
                row[at + 2] = ToByte(((packed >> 18) & 0x1FF) * scale);

                if (channels == 4)
                    row[at + 3] = 255;
            }
        }

        return true;
    }

    /// <summary>Expands one of the short floating point channels, which have no sign bit.</summary>
    private static float Small(uint bits, int mantissaBits)
    {
        int exponent = (int)(bits >> mantissaBits);
        uint mantissa = bits & ((1u << mantissaBits) - 1);

        if (exponent == 0)
            return mantissa / (float)(1 << mantissaBits) / 16384f;

        if (exponent == 31)
            return mantissa == 0 ? float.PositiveInfinity : float.NaN;

        return (1f + (mantissa / (float)(1 << mantissaBits))) * MathF.Pow(2f, exponent - 15);
    }

    /// <summary>One bit a pixel, the most significant of each byte first.</summary>
    private static bool DecodeBilevel(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                      Span<byte> destination, int stride, bool flip)
    {
        long rowBytes = (surface.Width + 7) / 8;
        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int x = 0; x < surface.Width; x++)
            {
                byte value = (source[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)255 : (byte)0;
                int at = x * channels;

                row[at] = row[at + 1] = row[at + 2] = value;
                if (channels == 4)
                    row[at + 3] = 255;
            }
        }

        return true;
    }

    /// <summary>Copies a whole surface of packed colour into the caller's rows.</summary>
    private static void Place(ReadOnlySpan<uint> pixels, in DdsSurface surface, int channels,
                              Span<byte> destination, int stride, bool flip)
    {
        for (int y = 0; y < surface.Height; y++)
        {
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int x = 0; x < surface.Width; x++)
            {
                uint colour = pixels[(y * surface.Width) + x];
                int at = x * channels;

                row[at] = (byte)(colour >> 16);
                row[at + 1] = (byte)(colour >> 8);
                row[at + 2] = (byte)colour;

                if (channels == 4)
                    row[at + 3] = (byte)(colour >> 24);
            }
        }
    }

    private static bool DecodePacked(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                     Span<byte> destination, int stride, bool flip)
    {
        Channel red = new(surface.RedMask);
        Channel green = new(surface.GreenMask);
        Channel blue = new(surface.BlueMask);
        Channel alpha = new(surface.AlphaMask);

        int bytesPerPixel = surface.BitCount / 8;
        long rowBytes = (long)surface.Width * bytesPerPixel;

        // Checked again here, since a corrupt width can make sizing and walking disagree.
        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            int at = 0;
            for (int x = 0; x < surface.Width; x++, at += channels)
            {
                int from = x * bytesPerPixel;
                uint pixel = bytesPerPixel switch
                {
                    1 => source[from],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(source[from..]),
                    3 => source[from] | ((uint)source[from + 1] << 8) | ((uint)source[from + 2] << 16),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(source[from..]),
                };

                row[at] = red.IsPresent ? red.Extract(pixel) : (byte)0;
                row[at + 1] = green.IsPresent ? green.Extract(pixel) : (byte)0;
                row[at + 2] = blue.IsPresent ? blue.Extract(pixel) : (byte)0;
                if (channels == 4)
                    row[at + 3] = alpha.IsPresent ? alpha.Extract(pixel) : (byte)255;
            }
        }

        return true;
    }

    /// <summary>
    /// Channels stored as whole values rather than fields inside a packed word. Where a channel is
    /// missing the colour falls back to what a graphics pipeline would sample: red repeated for a
    /// single channel, and nothing for a blue that is not there.
    /// </summary>
    private static bool DecodeComponents(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                         Span<byte> destination, int stride, bool flip)
    {
        int size = surface.ComponentBits / 8;
        int count = surface.ComponentCount;
        long rowBytes = (long)surface.Width * count * size;

        if (rowBytes <= 0 || surface.DataOffset + (rowBytes * surface.Height) > data.Length)
            return false;

        Span<float> pixel = stackalloc float[4];

        for (int y = 0; y < surface.Height; y++)
        {
            ReadOnlySpan<byte> source = data.Slice(surface.DataOffset + (int)(y * rowBytes), (int)rowBytes);
            int target = flip ? surface.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, surface.Width * channels);

            for (int x = 0; x < surface.Width; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    pixel[c] = c < count
                        ? Component(source, ((x * count) + c) * size, surface)
                        : c == 3 ? 1f : Missing(surface);
                }

                // Shown the way a shader samples it, with red standing in for the rest.
                if (count == 1)
                    pixel[1] = pixel[2] = pixel[0];

                int at = x * channels;
                row[at] = ToByte(pixel[0]);
                row[at + 1] = ToByte(pixel[1]);
                row[at + 2] = ToByte(pixel[2]);
                if (channels == 4)
                    row[at + 3] = ToByte(pixel[3]);
            }
        }

        return true;
    }

    /// <summary>One channel as a value where one means full intensity.</summary>
    private static float Component(ReadOnlySpan<byte> source, int at, in DdsSurface surface)
    {
        if (surface.Float)
        {
            return surface.ComponentBits == 16
                ? (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source[at..]))
                : BinaryPrimitives.ReadSingleLittleEndian(source[at..]);
        }

        if (!surface.Signed)
        {
            return surface.ComponentBits switch
            {
                8 => source[at] / 255f,
                16 => BinaryPrimitives.ReadUInt16LittleEndian(source[at..]) / 65535f,
                _ => BinaryPrimitives.ReadUInt32LittleEndian(source[at..]) / (float)uint.MaxValue,
            };
        }

        // A signed channel runs from minus one to one with the most negative clamped so zero is
        // exact, so folding it into a byte puts a stored zero at the middle grey.
        int value = surface.ComponentBits == 8
            ? (sbyte)source[at]
            : BinaryPrimitives.ReadInt16LittleEndian(source[at..]);

        int top = surface.ComponentBits == 8 ? 127 : 32767;
        return ((Math.Max(value, -top) / (float)top) + 1f) * 0.5f;
    }

    /// <summary>What a channel the file does not store reads as, which is the format's own zero.</summary>
    private static float Missing(in DdsSurface surface) => surface.Signed ? 0.5f : 0f;

    /// <summary>Maps a value where one means full intensity onto a byte, clipping outside it.</summary>
    private static byte ToByte(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

    /// <summary>
    /// Walks the four by four grid a block layout stores. A block at the right or bottom edge is
    /// whole in the file and read short, which is why the dimensions need not be a multiple of
    /// four for a format that only knows how to store them that way.
    /// </summary>
    private static bool DecodeBlocks(ReadOnlySpan<byte> data, in DdsSurface surface, int channels,
                                     Span<byte> destination, int stride, bool flip)
    {
        int across = (surface.Width + 3) / 4;
        int down = (surface.Height + 3) / 4;
        int blockBytes = surface.BlockBytes;

        Span<uint> colours = stackalloc uint[16];
        Span<byte> alpha = stackalloc byte[16];
        Span<byte> second = stackalloc byte[16];

        for (int blockY = 0; blockY < down; blockY++)
        {
            for (int blockX = 0; blockX < across; blockX++)
            {
                int at = surface.DataOffset + (((blockY * across) + blockX) * blockBytes);
                if (at + blockBytes > data.Length)
                    return false;

                ReadOnlySpan<byte> block = data.Slice(at, blockBytes);
                Expand(surface, block, colours, alpha, second);

                for (int y = 0; y < 4; y++)
                {
                    int row = (blockY * 4) + y;
                    if (row >= surface.Height)
                        break;

                    int target = flip ? surface.Height - 1 - row : row;
                    Span<byte> line = destination.Slice(target * stride, surface.Width * channels);

                    for (int x = 0; x < 4; x++)
                    {
                        int column = (blockX * 4) + x;
                        if (column >= surface.Width)
                            break;

                        uint colour = colours[(y * 4) + x];
                        int to = column * channels;
                        line[to] = (byte)(colour >> 16);
                        line[to + 1] = (byte)(colour >> 8);
                        line[to + 2] = (byte)colour;
                        if (channels == 4)
                            line[to + 3] = (byte)(colour >> 24);
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// One block as sixteen packed colours. The four layouts differ only in what precedes the
    /// colour block and what the result stands for: an alpha channel, a single channel on its own,
    /// or two channels that together describe a direction.
    /// </summary>
    private static void Expand(in DdsSurface surface, ReadOnlySpan<byte> block, Span<uint> colours,
                               Span<byte> alpha, Span<byte> second)
    {
        switch (surface.Layout)
        {
            case DdsLayout.Bc1:
                BlockCompression.DecodeColour(block, colours, allowTransparent: true);
                return;

            case DdsLayout.Bc2:
                BlockCompression.DecodeSharpAlpha(block, alpha);
                BlockCompression.DecodeColour(block[8..], colours, allowTransparent: false);
                Combine(colours, alpha);
                if (surface.Premultiplied)
                    Separate(colours);
                return;

            case DdsLayout.Bc3:
                BlockCompression.DecodeAlpha(block, alpha, signed: false);
                BlockCompression.DecodeColour(block[8..], colours, allowTransparent: false);
                Combine(colours, alpha);
                if (surface.Premultiplied)
                    Separate(colours);
                if (surface.RedInAlpha)
                    MoveAlphaToRed(colours);
                return;

            case DdsLayout.Bc4:
                BlockCompression.DecodeAlpha(block, alpha, surface.Signed);
                for (int i = 0; i < 16; i++)
                    colours[i] = 0xFF000000u | (alpha[i] * 0x00010101u);
                return;

            case DdsLayout.Bc7:
                Bc7.Decode(block, colours);
                return;

            case DdsLayout.Bc6h:
            {
                // Clipped to what a byte holds rather than exposed for the caller.
                Span<ushort> halves = stackalloc ushort[48];
                Bc6h.Decode(block, surface.Signed, halves);

                for (int i = 0; i < 16; i++)
                {
                    colours[i] = 0xFF000000u
                        | ((uint)ToByte((float)BitConverter.UInt16BitsToHalf(halves[i * 3])) << 16)
                        | ((uint)ToByte((float)BitConverter.UInt16BitsToHalf(halves[(i * 3) + 1])) << 8)
                        | ToByte((float)BitConverter.UInt16BitsToHalf(halves[(i * 3) + 2]));
                }

                return;
            }

            case DdsLayout.Bc5:
                // A direction with its third component left to the renderer, so blue is zero.
                BlockCompression.DecodeAlpha(block, alpha, surface.Signed);
                BlockCompression.DecodeAlpha(block[8..], second, surface.Signed);
                uint blue = surface.Signed ? 128u : 0u;
                for (int i = 0; i < 16; i++)
                    colours[i] = 0xFF000000u | ((uint)alpha[i] << 16) | ((uint)second[i] << 8) | blue;
                return;

            default:
                return;
        }
    }

    /// <summary>Puts the red channel back where it belongs and makes the pixel opaque.</summary>
    private static void MoveAlphaToRed(Span<uint> colours)
    {
        for (int i = 0; i < 16; i++)
            colours[i] = 0xFF000000u | ((colours[i] >> 24) << 16) | (colours[i] & 0x0000FFFF);
    }

    /// <summary>Divides an alpha back out of the colour it was multiplied into.</summary>
    private static void Separate(Span<uint> colours)
    {
        for (int i = 0; i < 16; i++)
        {
            uint alpha = colours[i] >> 24;

            // Multiplied by nothing leaves nothing to recover, so the colour stands.
            if (alpha is 0 or 255)
                continue;

            uint result = alpha << 24;
            for (int shift = 0; shift <= 16; shift += 8)
            {
                uint value = (colours[i] >> shift) & 255;
                result |= Math.Min(255u, value * 255 / alpha) << shift;
            }

            colours[i] = result;
        }
    }

    private static void Combine(Span<uint> colours, ReadOnlySpan<byte> alpha)
    {
        for (int i = 0; i < 16; i++)
            colours[i] = (colours[i] & 0x00FFFFFF) | ((uint)alpha[i] << 24);
    }
}
