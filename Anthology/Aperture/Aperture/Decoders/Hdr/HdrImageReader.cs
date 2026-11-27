// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace Prowl.Aperture.Decoders.Hdr;

/// <summary>
/// Reads Radiance scanlines. A pixel is three mantissas and one exponent they share, which is
/// what lets four bytes cover a range no fixed point encoding reaches, and the file stores those
/// four bytes in one of three layouts: flat, an old run length encoding that repeats whole
/// pixels, and a newer one that splits a scanline into four channels and encodes each on its own.
/// </summary>
internal static class HdrImageReader
{
    /// <summary>Scanline width the newer encoding can address, beyond which a file must be flat.</summary>
    private const int MaxRunLengthWidth = 0x7FFF;

    /// <summary>Whether the data present could hold the scanlines the header declares.</summary>
    public static bool CanDescribe(int available, int width, int height)
    {
        // A run of pixels costs two bytes at least, and covers at most 127 of them per channel.
        long pixels = (long)width * height;
        return (long)available / 2 * 127 >= pixels;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, int offset, int width, int height,
                                 bool bottomUp, Span<byte> destination, int stride, bool flip,
                                 out ApertureError error)
    {
        error = ApertureError.None;

        byte[] scanline = BufferPool.Bytes.Rent(width * 4);
        try
        {
            Span<byte> rgbe = scanline.AsSpan(0, width * 4);
            int at = offset;

            for (int y = 0; y < height; y++)
            {
                if (!ReadScanline(data, ref at, rgbe, width, out error))
                    return false;

                // The resolution line names the axis order, so a file stored bottom up is turned
                // the right way here rather than being handed back upside down.
                int row = bottomUp ? height - 1 - y : y;
                int target = flip ? height - 1 - row : row;
                Convert(rgbe, MemoryMarshal.Cast<byte, float>(destination.Slice(target * stride, width * 12)));
            }

            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(scanline);
        }
    }

    private static bool ReadScanline(ReadOnlySpan<byte> data, ref int at, Span<byte> rgbe, int width,
                                     out ApertureError error)
    {
        error = ApertureError.None;

        // The newer encoding announces itself with a pixel that could not be a colour, carrying
        // the scanline width where the mantissas would be.
        if (width is >= 8 and <= MaxRunLengthWidth && at + 4 <= data.Length &&
            data[at] == 2 && data[at + 1] == 2 && ((data[at + 2] << 8) | data[at + 3]) == width)
        {
            at += 4;
            return ReadSplitScanline(data, ref at, rgbe, width, out error);
        }

        return ReadFlatScanline(data, ref at, rgbe, width, out error);
    }

    /// <summary>Four independently encoded channels, each a run of literals or a repeated byte.</summary>
    private static bool ReadSplitScanline(ReadOnlySpan<byte> data, ref int at, Span<byte> rgbe, int width,
                                          out ApertureError error)
    {
        error = ApertureError.None;

        for (int channel = 0; channel < 4; channel++)
        {
            int x = 0;
            while (x < width)
            {
                if (at >= data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                int count = data[at++];
                if (count > 128)
                {
                    count -= 128;
                    if (x + count > width || at >= data.Length)
                    {
                        error = ApertureError.InvalidData;
                        return false;
                    }

                    byte value = data[at++];
                    for (int i = 0; i < count; i++, x++)
                        rgbe[(x * 4) + channel] = value;

                    continue;
                }

                if (count == 0 || x + count > width || at + count > data.Length)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                for (int i = 0; i < count; i++, x++)
                    rgbe[(x * 4) + channel] = data[at + i];

                at += count;
            }
        }

        return true;
    }

    /// <summary>
    /// Whole pixels, with the older encoding's repeat marker mixed in: a pixel whose three
    /// mantissas are all one is not a colour but a count of how many times to repeat the last one,
    /// and consecutive markers shift that count further up.
    /// </summary>
    private static bool ReadFlatScanline(ReadOnlySpan<byte> data, ref int at, Span<byte> rgbe, int width,
                                         out ApertureError error)
    {
        error = ApertureError.None;
        int x = 0;
        int shift = 0;

        while (x < width)
        {
            if (at + 4 > data.Length)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            if (data[at] == 1 && data[at + 1] == 1 && data[at + 2] == 1)
            {
                if (x == 0)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                int count = data[at + 3] << shift;
                at += 4;
                shift += 8;

                if (x + count > width)
                {
                    error = ApertureError.InvalidData;
                    return false;
                }

                Span<byte> previous = rgbe.Slice((x - 1) * 4, 4);
                for (int i = 0; i < count; i++, x++)
                    previous.CopyTo(rgbe[(x * 4)..]);

                continue;
            }

            shift = 0;
            data.Slice(at, 4).CopyTo(rgbe[(x * 4)..]);
            at += 4;
            x++;
        }

        return true;
    }

    /// <summary>
    /// Turns the shared exponent form back into three floats, the way the format's own
    /// implementation does: the half added to each mantissa is the middle of the step it stands
    /// for, and an exponent of zero means the pixel is black however the mantissas read.
    /// </summary>
    private static void Convert(ReadOnlySpan<byte> rgbe, Span<float> destination)
    {
        for (int x = 0; x < destination.Length / 3; x++)
        {
            int at = x * 4;
            int exponent = rgbe[at + 3];
            int to = x * 3;

            if (exponent == 0)
            {
                destination[to] = 0f;
                destination[to + 1] = 0f;
                destination[to + 2] = 0f;
                continue;
            }

            float scale = MathF.ScaleB(1f, exponent - (128 + 8));
            destination[to] = (rgbe[at] + 0.5f) * scale;
            destination[to + 1] = (rgbe[at + 1] + 0.5f) * scale;
            destination[to + 2] = (rgbe[at + 2] + 0.5f) * scale;
        }
    }
}
