// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Prowl.Aperture.Decoders.Bmp;

/// <summary>
/// Turns the pixel data into rows. Rows are stored bottom up unless the header says otherwise,
/// padded to a four byte boundary, and in one of three shapes: indices into a palette, channels
/// picked out by bit masks, or a run length encoding that can also skip regions entirely.
/// </summary>
internal static class BmpImageReader
{
    /// <summary>Pulls one channel out of a packed pixel and scales it to a byte.</summary>
    private readonly struct Channel
    {
        private readonly int _shift;
        private readonly uint _mask;
        private readonly byte[] _table;
        private readonly bool _present;

        public Channel(uint mask)
        {
            if (mask == 0)
            {
                _shift = 0;
                _mask = 0;
                _table = UnormScale.Absent;
                _present = false;
                return;
            }

            int shift = BitOperations.TrailingZeroCount(mask);
            int bits = BitOperations.PopCount(mask);

            // A field wider than a byte loses its low bits rather than its high ones.
            if (bits > 8)
            {
                shift += bits - 8;
                bits = 8;
            }

            _shift = shift;
            _mask = (1u << bits) - 1;
            _table = UnormScale.Table(bits);
            _present = true;
        }

        public readonly bool IsPresent => _present;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly byte Extract(uint pixel) => _table[(pixel >> _shift) & _mask];
    }

    /// <summary>
    /// Whether the data present could describe the image the header declares, checked before
    /// anything is allocated for it. Uncompressed rows have a known length; two bytes of run
    /// length encoding cover at most 255 pixels, so that rate bounds what the rest can reach.
    /// </summary>
    public static bool CanDescribe(int available, in BmpHeader header)
    {
        long pixels = (long)header.Width * header.Height;
        return header.Compression is 1 or 2
            ? (long)available / 2 * 255 >= pixels
            : (long)header.Stride * header.Height <= available;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, in BmpHeader header, int channels,
                                 Span<byte> destination, int stride, bool flip, out ApertureError error)
    {
        error = ApertureError.None;

        Span<uint> palette = stackalloc uint[256];
        if (header.IsIndexed && !TryReadPalette(data, header, palette, out error))
            return false;

        if (header.Compression is 1 or 2)
            return TryDecodeRuns(data, header, palette, channels, destination, stride, flip, out error);

        return TryDecodeRows(data, header, palette, channels, destination, stride, flip, out error);
    }

    private static bool TryReadPalette(ReadOnlySpan<byte> data, in BmpHeader header,
                                       Span<uint> palette, out ApertureError error)
    {
        error = ApertureError.None;
        palette.Clear();

        int size = header.PaletteEntrySize;
        for (int i = 0; i < header.PaletteEntries; i++)
        {
            int at = header.PaletteOffset + (i * size);
            if (at + 3 > data.Length)
                break;

            // Entries are blue, green, red, and a fourth byte the format reserves and every
            // writer leaves at zero, so it is not read as alpha.
            palette[i] = 0xFF000000u | ((uint)data[at + 2] << 16) | ((uint)data[at + 1] << 8) | data[at];
        }

        return true;
    }

    private static bool TryDecodeRows(ReadOnlySpan<byte> data, in BmpHeader header, ReadOnlySpan<uint> palette,
                                      int channels, Span<byte> destination, int stride, bool flip,
                                      out ApertureError error)
    {
        error = ApertureError.None;

        int rowBytes = header.Stride;
        long needed = (long)rowBytes * header.Height;
        if (header.PixelOffset < 0 || header.PixelOffset + needed > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        Channel red = new(header.RedMask);
        Channel green = new(header.GreenMask);
        Channel blue = new(header.BlueMask);
        Channel alpha = new(header.AlphaMask);
        int bytesPerPixel = header.BitsPerPixel / 8;
        bool anyAlpha = false;

        // Most files store a byte a channel in the usual order, which makes the whole conversion
        // a swap of red and blue rather than three field extractions a pixel.
        bool plain = !header.IsIndexed && channels == 4 && !alpha.IsPresent &&
                     bytesPerPixel is 3 or 4 &&
                     header.RedMask == 0x00FF0000 && header.GreenMask == 0x0000FF00 &&
                     header.BlueMask == 0x000000FF;

        for (int y = 0; y < header.Height; y++)
        {
            int fileRow = header.TopDown ? y : header.Height - 1 - y;
            ReadOnlySpan<byte> source = data.Slice(header.PixelOffset + (fileRow * rowBytes), rowBytes);

            int target = flip ? header.Height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, header.Width * channels);

            if (header.IsIndexed)
            {
                if (!ExpandIndices(source, palette, header, row, channels))
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
            }
            else if (plain)
                ExpandPlain(source, bytesPerPixel, header.Width, row);
            else
                anyAlpha |= ExpandChannels(source, bytesPerPixel, red, green, blue, alpha,
                                           header.Width, row, channels);
        }

        // An alpha channel left at zero everywhere is read as opaque rather than invisible.
        // Writers do this constantly, and the alternative cannot be seen.
        if (channels == 4 && alpha.IsPresent && !anyAlpha)
            MakeOpaque(destination, stride, header.Width, header.Height);

        return true;
    }

    private static void MakeOpaque(Span<byte> destination, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = destination.Slice(y * stride, width * 4);
            for (int at = 3; at < row.Length; at += 4)
                row[at] = 255;
        }
    }

    /// <summary>Returns false if the row names a colour the palette does not have.</summary>
    private static bool ExpandIndices(ReadOnlySpan<byte> source, ReadOnlySpan<uint> palette,
                                      in BmpHeader header, Span<byte> destination, int channels)
    {
        int bits = header.BitsPerPixel;
        int width = header.Width;
        int entries = header.PaletteEntries;
        int perByte = 8 / bits;
        int mask = (1 << bits) - 1;
        int at = 0;

        for (int x = 0; x < width; x++, at += channels)
        {
            int index;
            if (bits == 8)
            {
                index = source[x];
            }
            else
            {
                int shift = 8 - bits - ((x % perByte) * bits);
                index = (source[x / perByte] >> shift) & mask;
            }

            if (index >= entries)
                return false;

            Write(destination, at, palette[index], channels);
        }

        return true;
    }

    /// <summary>Returns whether any pixel in the row carried a non zero alpha.</summary>
    private static bool ExpandChannels(ReadOnlySpan<byte> source, int bytesPerPixel,
                                       in Channel red, in Channel green, in Channel blue, in Channel alpha,
                                       int width, Span<byte> destination, int channels)
    {
        bool anyAlpha = false;
        int at = 0;
        for (int x = 0; x < width; x++, at += channels)
        {
            int from = x * bytesPerPixel;
            uint pixel = bytesPerPixel switch
            {
                2 => BinaryPrimitives.ReadUInt16LittleEndian(source[from..]),
                3 => source[from] | ((uint)source[from + 1] << 8) | ((uint)source[from + 2] << 16),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(source[from..]),
            };

            destination[at] = red.Extract(pixel);
            destination[at + 1] = green.Extract(pixel);
            destination[at + 2] = blue.Extract(pixel);

            if (channels != 4)
                continue;

            byte value = alpha.IsPresent ? alpha.Extract(pixel) : (byte)255;
            destination[at + 3] = value;
            anyAlpha |= value != 0;
        }

        return anyAlpha;
    }

    /// <summary>Where a pixel's three stored bytes land once red and blue trade places.</summary>
    private static ReadOnlySpan<byte> SwappedTriples =>
        [2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255];

    /// <summary>The same for four byte pixels, whose fourth byte is not colour and is dropped.</summary>
    private static ReadOnlySpan<byte> SwappedQuads =>
        [2, 1, 0, 255, 6, 5, 4, 255, 10, 9, 8, 255, 14, 13, 12, 255];

    /// <summary>
    /// Expands a row stored as plain colour into opaque eight bit RGBA. Four pixels at a time is
    /// one shuffle and one or, since nothing has to be scaled or masked.
    /// </summary>
    private static void ExpandPlain(ReadOnlySpan<byte> source, int bytesPerPixel, int width,
                                    Span<byte> destination)
    {
        int x = 0;

        if (Ssse3.IsSupported)
        {
            ref byte from = ref MemoryMarshal.GetReference(source);
            ref byte to = ref MemoryMarshal.GetReference(destination);
            Vector128<byte> order = Vector128.Create(bytesPerPixel == 3 ? SwappedTriples : SwappedQuads);
            Vector128<uint> opaque = Vector128.Create(0xFF000000u);

            for (; (x * bytesPerPixel) + 16 <= source.Length && x + 4 <= width; x += 4)
            {
                Vector128<byte> packed = Vector128.LoadUnsafe(ref from, (nuint)(x * bytesPerPixel));
                (Ssse3.Shuffle(packed, order).AsUInt32() | opaque).AsByte().StoreUnsafe(ref to, (nuint)(x * 4));
            }
        }

        for (; x < width; x++)
        {
            int at = x * bytesPerPixel;
            int to = x * 4;
            destination[to] = source[at + 2];
            destination[to + 1] = source[at + 1];
            destination[to + 2] = source[at];
            destination[to + 3] = 255;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write(Span<byte> destination, int at, uint colour, int channels)
    {
        destination[at] = (byte)(colour >> 16);
        destination[at + 1] = (byte)(colour >> 8);
        destination[at + 2] = (byte)colour;
        if (channels == 4)
            destination[at + 3] = (byte)(colour >> 24);
    }

    /// <summary>
    /// Expands the run length forms. Both can end a row early or jump the cursor, leaving pixels
    /// the encoding never mentions; the format says nothing about those, so they keep the first
    /// palette entry rather than whatever the buffer happened to hold.
    /// </summary>
    private static bool TryDecodeRuns(ReadOnlySpan<byte> data, in BmpHeader header, ReadOnlySpan<uint> palette,
                                      int channels, Span<byte> destination, int stride, bool flip,
                                      out ApertureError error)
    {
        error = ApertureError.None;

        if (header.BitsPerPixel != (header.Compression == 1 ? 8 : 4))
        {
            error = ApertureError.InvalidData;
            return false;
        }

        bool four = header.Compression == 2;
        int width = header.Width;
        int height = header.Height;

        // A run past the right edge is clipped, since four bit runs come in whole bytes and an
        // odd width always ends over. Running past the last row is where a corrupt length shows.

        byte[] indices = BufferPool.Bytes.Rent(width * height);
        try
        {
            Span<byte> canvas = indices.AsSpan(0, width * height);
            canvas.Clear();

            int at = header.PixelOffset;
            int x = 0;
            int row = 0;

            while (at + 1 < data.Length && row < height)
            {
                byte count = data[at];
                byte value = data[at + 1];
                at += 2;

                if (count != 0)
                {
                    for (int i = 0; i < count && x < width; i++, x++)
                    {
                        int index = four ? (i & 1) == 0 ? value >> 4 : value & 15 : value;
                        canvas[(row * width) + x] = (byte)index;
                    }
                    continue;
                }

                switch (value)
                {
                    case 0:
                        x = 0;
                        row++;
                        break;

                    case 1:
                        row = height;
                        break;

                    case 2:
                        if (at + 1 >= data.Length)
                        {
                            row = height;
                            break;
                        }

                        x += data[at];
                        row += data[at + 1];
                        at += 2;
                        break;

                    default:
                        int literal = four ? (value + 1) / 2 : value;
                        if (at + literal > data.Length)
                        {
                            row = height;
                            break;
                        }

                        for (int i = 0; i < value && x < width; i++, x++)
                        {
                            byte packed = data[at + (four ? i / 2 : i)];
                            int index = four ? (i & 1) == 0 ? packed >> 4 : packed & 15 : packed;
                            canvas[(row * width) + x] = (byte)index;
                        }

                        // Runs of literals are padded out to an even number of bytes.
                        at += literal + (literal & 1);
                        break;
                }

                if (row < 0 || x < 0 || row > height)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }
            }

            for (int y = 0; y < height; y++)
            {
                int fileRow = header.TopDown ? y : height - 1 - y;
                int target = flip ? height - 1 - y : y;
                Span<byte> output = destination.Slice(target * stride, width * channels);

                ReadOnlySpan<byte> line = canvas.Slice(fileRow * width, width);
                int to = 0;
                for (int column = 0; column < width; column++, to += channels)
                    Write(output, to, palette[line[column]], channels);
            }

            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(indices);
        }
    }
}
