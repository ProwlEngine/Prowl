// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Prowl.Aperture.Decoders.Tga;

/// <summary>
/// Turns the pixel data into rows. Pixels are stored bottom up and left to right unless the
/// descriptor says otherwise, either straight or under a run length encoding whose packets are
/// free to run across the end of a row.
/// </summary>
internal static class TgaImageReader
{
    /// <summary>
    /// Whether the data present could describe the image the header declares. A run length packet
    /// costs at least two bytes and covers at most 128 pixels, which bounds what the rest reaches.
    /// </summary>
    public static bool CanDescribe(int available, in TgaHeader header)
    {
        long pixels = (long)header.Width * header.Height;
        return header.IsRunLength
            ? (long)available / 2 * 128 >= pixels
            : pixels * header.BytesPerPixel <= available;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, in TgaHeader header, int channels,
                                 Span<byte> destination, int stride, bool flip, out ApertureError error)
    {
        error = ApertureError.None;

        if (header.Kind is 32 or 33)
        {
            // The two Huffman and delta coded variants were never widely written and are not read.
            error = ApertureError.UnsupportedFeature;
            return false;
        }

        int width = header.Width;
        int height = header.Height;
        int bytesPerPixel = header.BytesPerPixel;

        Span<uint> palette = stackalloc uint[256];
        if (header.IsPaletted && !TryReadPalette(data, header, palette, out error))
            return false;

        byte[] raw = BufferPool.Bytes.Rent(width * height * bytesPerPixel);
        try
        {
            Span<byte> stored = raw.AsSpan(0, width * height * bytesPerPixel);
            if (!Gather(data, header, stored, out error))
                return false;

            bool anyAlpha = false;
            for (int y = 0; y < height; y++)
            {
                int sourceRow = header.TopDown ? y : height - 1 - y;
                ReadOnlySpan<byte> line = stored.Slice(sourceRow * width * bytesPerPixel, width * bytesPerPixel);

                int target = flip ? height - 1 - y : y;
                Span<byte> row = destination.Slice(target * stride, width * channels);

                anyAlpha |= Expand(line, header, palette, row, channels);
            }

            // An attribute channel left at zero everywhere is read as opaque rather than
            // invisible, since a file whose every pixel is clear carries no picture at all and
            // the descriptor is the only thing that would have said so.
            if (channels == 4 && header.AlphaUsed && !anyAlpha)
            {
                for (int y = 0; y < height; y++)
                {
                    Span<byte> row = destination.Slice(y * stride, width * 4);
                    for (int at = 3; at < row.Length; at += 4)
                        row[at] = 255;
                }
            }

            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(raw);
        }
    }

    /// <summary>Copies or expands the stored pixels into one flat, unencoded block.</summary>
    private static bool Gather(ReadOnlySpan<byte> data, in TgaHeader header, Span<byte> stored,
                               out ApertureError error)
    {
        error = ApertureError.None;
        int bytesPerPixel = header.BytesPerPixel;

        if (!header.IsRunLength)
        {
            if (header.PixelOffset + stored.Length > data.Length)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            data.Slice(header.PixelOffset, stored.Length).CopyTo(stored);
            return true;
        }

        int at = header.PixelOffset;
        int written = 0;

        while (written < stored.Length)
        {
            if (at >= data.Length)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            byte packet = data[at++];
            int run = (packet & 0x7F) + 1;
            int span = run * bytesPerPixel;

            // A packet is free to run past the end of a row, but not past the end of the image.
            if (written + span > stored.Length)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            if ((packet & 0x80) != 0)
            {
                if (at + bytesPerPixel > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                ReadOnlySpan<byte> value = data.Slice(at, bytesPerPixel);
                at += bytesPerPixel;

                for (int i = 0; i < run; i++, written += bytesPerPixel)
                    value.CopyTo(stored[written..]);

                continue;
            }

            if (at + span > data.Length)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            data.Slice(at, span).CopyTo(stored[written..]);
            at += span;
            written += span;
        }

        return true;
    }

    private static bool TryReadPalette(ReadOnlySpan<byte> data, in TgaHeader header,
                                       Span<uint> palette, out ApertureError error)
    {
        error = ApertureError.None;
        palette.Clear();

        int entry = (header.ColorMapDepth + 7) / 8;
        for (int i = 0; i < header.ColorMapLength; i++)
        {
            // The map need not start at index zero: its origin field says which index the first
            // stored entry stands for.
            int index = header.ColorMapFirst + i;
            if ((uint)index >= 256)
                continue;

            int at = header.ColorMapOffset + (i * entry);
            if (at + entry > data.Length)
                break;

            palette[index] = Unpack(data.Slice(at, entry), header.ColorMapDepth, header.AlphaUsed);
        }

        return true;
    }

    /// <summary>Returns whether any pixel in the row carried a non zero alpha.</summary>
    /// <summary>Where a pixel's three stored bytes land once red and blue trade places.</summary>
    private static ReadOnlySpan<byte> SwappedTriples =>
        [2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255];

    /// <summary>The same for four byte pixels, whose fourth byte is the alpha and stays put.</summary>
    private static ReadOnlySpan<byte> SwappedQuads =>
        [2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15];

    /// <summary>
    /// Expands a row of plain colour into eight bit RGBA. Returns whether any pixel carried an
    /// alpha that is not nothing, which is what decides whether the channel means anything.
    /// </summary>
    private static bool ExpandPlain(ReadOnlySpan<byte> line, int bytesPerPixel, bool alphaUsed,
                                    int width, Span<byte> destination)
    {
        bool opaque = bytesPerPixel == 3 || !alphaUsed;
        Vector128<uint> fill = Vector128.Create(0xFF000000u);
        Vector128<uint> seen = Vector128<uint>.Zero;
        int x = 0;

        if (Ssse3.IsSupported)
        {
            ref byte from = ref MemoryMarshal.GetReference(line);
            ref byte to = ref MemoryMarshal.GetReference(destination);
            Vector128<byte> order = Vector128.Create(bytesPerPixel == 3 ? SwappedTriples : SwappedQuads);

            for (; (x * bytesPerPixel) + 16 <= line.Length && x + 4 <= width; x += 4)
            {
                Vector128<byte> packed = Vector128.LoadUnsafe(ref from, (nuint)(x * bytesPerPixel));
                Vector128<uint> colour = Ssse3.Shuffle(packed, order).AsUInt32();

                if (opaque)
                    colour |= fill;
                else
                    seen |= colour;

                colour.AsByte().StoreUnsafe(ref to, (nuint)(x * 4));
            }
        }

        bool anyAlpha = opaque || (seen & fill) != Vector128<uint>.Zero;

        for (; x < width; x++)
        {
            int at = x * bytesPerPixel;
            int to = x * 4;
            destination[to] = line[at + 2];
            destination[to + 1] = line[at + 1];
            destination[to + 2] = line[at];

            byte alpha = opaque ? (byte)255 : line[at + 3];
            destination[to + 3] = alpha;
            anyAlpha |= alpha != 0;
        }

        return anyAlpha;
    }

    private static bool Expand(ReadOnlySpan<byte> line, in TgaHeader header, ReadOnlySpan<uint> palette,
                               Span<byte> destination, int channels)
    {
        bool anyAlpha = false;
        int width = header.Width;
        int bytesPerPixel = header.BytesPerPixel;

        // Three or four bytes a pixel, stored blue first, is the ordinary case, and turning that
        // into eight bit colour is a swap of two channels and nothing else.
        if (channels == 4 && !header.IsPaletted && !header.RightToLeft && bytesPerPixel is 3 or 4)
            return ExpandPlain(line, bytesPerPixel, header.AlphaUsed, width, destination);

        for (int x = 0; x < width; x++)
        {
            int from = (header.RightToLeft ? width - 1 - x : x) * bytesPerPixel;
            ReadOnlySpan<byte> pixel = line.Slice(from, bytesPerPixel);

            uint colour = header.IsPaletted
                ? palette[pixel[0]]
                : Unpack(pixel, header.Depth, header.AlphaUsed);

            int at = x * channels;
            if (channels == 1)
            {
                destination[at] = (byte)colour;
                continue;
            }

            destination[at] = (byte)(colour >> 16);
            destination[at + 1] = (byte)(colour >> 8);
            destination[at + 2] = (byte)colour;

            if (channels != 4)
                continue;

            byte alpha = (byte)(colour >> 24);
            destination[at + 3] = alpha;
            anyAlpha |= alpha != 0;
        }

        return anyAlpha;
    }

    /// <summary>
    /// One stored pixel as packed RGBA. Sixteen bits is five per colour with one attribute bit
    /// over, and the five stretch to eight by repeating rather than by shifting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Unpack(ReadOnlySpan<byte> pixel, int depth, bool alphaUsed)
    {
        switch (depth)
        {
            case 8:
                return 0xFF000000u | (pixel[0] * 0x00010101u);

            case 15:
            case 16:
            {
                uint packed = BinaryPrimitives.ReadUInt16LittleEndian(pixel);
                uint red = Stretch((packed >> 10) & 31);
                uint green = Stretch((packed >> 5) & 31);
                uint blue = Stretch(packed & 31);
                uint alpha = alphaUsed && (packed & 0x8000) == 0 ? 0u : 255u;
                return (alpha << 24) | (red << 16) | (green << 8) | blue;
            }

            case 24:
                return 0xFF000000u | ((uint)pixel[2] << 16) | ((uint)pixel[1] << 8) | pixel[0];

            default:
            {
                uint alpha = alphaUsed ? pixel[3] : 255u;
                return (alpha << 24) | ((uint)pixel[2] << 16) | ((uint)pixel[1] << 8) | pixel[0];
            }
        }
    }

    private static readonly byte[] FiveBits = UnormScale.Table(5);

    /// <summary>Stretches a five bit field to a byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Stretch(uint value) => FiveBits[value];
}
