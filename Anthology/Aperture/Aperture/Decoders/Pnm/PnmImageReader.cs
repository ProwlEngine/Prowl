// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Prowl.Aperture.Decoders.Pnm;

/// <summary>
/// Reads the samples that follow the header, in whichever of the format's three shapes the magic
/// asked for: one bit a pixel, bytes, or big endian pairs.
/// </summary>
internal static class PnmImageReader
{
    /// <summary>Whether the data present could hold the samples the header declares.</summary>
    public static bool CanDescribe(int available, in PnmHeader header)
    {
        if (header.IsAscii)
        {
            // One digit and one separator is the shortest a sample can be written.
            long samples = (long)header.Width * header.Height * header.Channels;
            return header.IsBitmap ? samples <= (long)available * 2 : samples * 2 <= available + 1;
        }

        long bytes = header.IsBitmap
            ? (long)((header.Width + 7) / 8) * header.Height
            : (long)header.Width * header.Height * header.Channels * (header.BitsPerChannel / 8);

        return bytes <= available;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, in PnmHeader header, Span<byte> destination,
                                 int stride, bool flip, out ApertureError error)
    {
        error = ApertureError.None;

        int width = header.Width;
        int height = header.Height;
        int channels = header.Channels;
        bool wide = header.BitsPerChannel == 16;
        int at = header.DataOffset;

        for (int y = 0; y < height; y++)
        {
            int target = flip ? height - 1 - y : y;
            Span<byte> row = destination.Slice(target * stride, width * channels * (wide ? 2 : 1));

            if (header.IsBitmap)
            {
                if (!ReadBitmapRow(data, header, ref at, row, width, out error))
                    return false;

                continue;
            }

            if (!ReadSampleRow(data, header, ref at, row, width * channels, wide, out error))
                return false;
        }

        return true;
    }

    /// <summary>
    /// One bit a pixel, and a set bit means black. Binary rows restart on a byte boundary; the
    /// text form has no such padding, because it has no bytes to pad.
    /// </summary>
    private static bool ReadBitmapRow(ReadOnlySpan<byte> data, in PnmHeader header, ref int at,
                                      Span<byte> row, int width, out ApertureError error)
    {
        error = ApertureError.None;

        if (header.IsAscii)
        {
            for (int x = 0; x < width; x++)
            {
                if (!TryReadDigit(data, ref at, out int bit))
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                row[x] = bit != 0 ? (byte)0 : (byte)255;
            }

            return true;
        }

        int bytes = (width + 7) / 8;
        if (at + bytes > data.Length)
        {
            error = ApertureError.UnexpectedEndOfData;
            return false;
        }

        for (int x = 0; x < width; x++)
            row[x] = (data[at + (x >> 3)] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;

        at += bytes;
        return true;
    }

    private static bool ReadSampleRow(ReadOnlySpan<byte> data, in PnmHeader header, ref int at,
                                      Span<byte> row, int count, bool wide, out ApertureError error)
    {
        error = ApertureError.None;

        int maxValue = header.MaxValue;
        int ceiling = wide ? 65535 : 255;
        bool scale = maxValue != ceiling;

        // A binary file whose header calls the widest value full intensity needs no arithmetic
        // at all: the samples are already what the output holds, so a row is a copy.
        if (!header.IsAscii && !wide && !scale)
        {
            if (at + count > data.Length)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            data.Slice(at, count).CopyTo(row);
            at += count;
            return true;
        }

        // The same holds of a sixteen bit file, except that the samples are stored the other way
        // round from the way the output holds them.
        if (!header.IsAscii && wide && !scale)
        {
            if (at + (count * 2) > data.Length)
            {
                error = ApertureError.UnexpectedEndOfData;
                return false;
            }

            BinaryPrimitives.ReverseEndianness(
                MemoryMarshal.Cast<byte, ushort>(data.Slice(at, count * 2)),
                MemoryMarshal.Cast<byte, ushort>(row));

            at += count * 2;
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            int sample;
            if (header.IsAscii)
            {
                if (!TryReadNumber(data, ref at, out sample))
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }
            }
            else if (wide)
            {
                if (at + 2 > data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                sample = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
                at += 2;
            }
            else
            {
                if (at >= data.Length)
                {
                    error = ApertureError.UnexpectedEndOfData;
                    return false;
                }

                sample = data[at++];
            }

            // A sample above the value the header calls full intensity is not a bright pixel,
            // it is a file that disagrees with its own header.
            if (sample > maxValue)
            {
                error = ApertureError.InvalidData;
                return false;
            }

            // The header names the value that stands for full intensity, which is rarely the one
            // the output layout uses, so the sample is stretched across the wider range.
            if (scale)
                sample = (int)(((long)sample * ceiling) / maxValue);

            if (wide)
                BinaryPrimitives.WriteUInt16LittleEndian(row[(i * 2)..], (ushort)sample);
            else
                row[i] = (byte)sample;
        }

        return true;
    }

    private static bool TryReadDigit(ReadOnlySpan<byte> data, ref int at, out int value)
    {
        value = 0;
        while (at < data.Length)
        {
            byte c = data[at];
            if (c is (byte)'0' or (byte)'1')
            {
                value = c - '0';
                at++;
                return true;
            }

            if (c == (byte)'#')
            {
                while (at < data.Length && data[at] != (byte)'\n')
                    at++;
                continue;
            }

            at++;
        }

        return false;
    }

    private static bool TryReadNumber(ReadOnlySpan<byte> data, ref int at, out int value)
    {
        value = 0;
        bool any = false;

        while (at < data.Length)
        {
            byte c = data[at];

            if (c == (byte)'#')
            {
                while (at < data.Length && data[at] != (byte)'\n')
                    at++;
                continue;
            }

            if (c is >= (byte)'0' and <= (byte)'9')
                break;

            at++;
        }

        while (at < data.Length && data[at] is >= (byte)'0' and <= (byte)'9')
        {
            value = Math.Min((value * 10) + (data[at] - '0'), 65535);
            at++;
            any = true;
        }

        return any;
    }
}
