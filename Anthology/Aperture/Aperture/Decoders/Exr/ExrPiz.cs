// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The wavelet compression, which most files are written with. Three steps in front of the entropy
/// coder: the values a block uses are renumbered to run consecutively, a wavelet transform
/// repeatedly replaces each pair of neighbours with their average and difference, and the result
/// is Huffman coded. Undoing it runs the three backwards.
/// </summary>
internal static class ExrPiz
{
    private const int ShortRange = 1 << 16;
    private const int BitmapSize = ShortRange >> 3;

    private const int Bits = 16;
    private const int Offset = 1 << (Bits - 1);
    private const int Mask = (1 << Bits) - 1;

    public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, ExrHeader header,
                                 int firstRow, int rows, int width)
    {
        if (source.Length < 4 || (destination.Length & 1) != 0)
            return false;

        int minimum = BinaryPrimitives.ReadUInt16LittleEndian(source);
        int maximum = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        int at = 4;

        if (maximum >= BitmapSize)
            return false;

        byte[] bitmapBuffer = BufferPool.Bytes.Rent(BitmapSize);
        ushort[] lutBuffer = BufferPool.UShorts.Rent(ShortRange);
        ushort[] valueBuffer = BufferPool.UShorts.Rent(destination.Length / 2);

        try
        {
            Span<byte> bitmap = bitmapBuffer.AsSpan(0, BitmapSize);
            bitmap.Clear();

            if (minimum <= maximum)
            {
                int count = maximum - minimum + 1;
                if (at + count > source.Length)
                    return false;

                source.Slice(at, count).CopyTo(bitmap[minimum..]);
                at += count;
            }

            Span<ushort> lut = lutBuffer.AsSpan(0, ShortRange);
            int highest = BuildReverseLut(bitmap, lut);

            if (at + 4 > source.Length)
                return false;

            long packed = BinaryPrimitives.ReadUInt32LittleEndian(source[at..]);
            at += 4;

            if (packed < 0 || at + packed > source.Length)
                return false;

            Span<ushort> values = valueBuffer.AsSpan(0, destination.Length / 2);
            if (!ExrHuffman.TryDecompress(source.Slice(at, (int)packed), values))
                return false;

            int start = 0;
            foreach (ExrChannel channel in header.Channels)
            {
                int stride = channel.Bytes / 2;
                int nx = channel.SampledWidth(width);
                int ny = channel.SampledRows(firstRow, rows);
                long span = (long)nx * ny * stride;

                if (start + span > values.Length)
                    return false;

                for (int j = 0; j < stride; j++)
                    Decode(values[(start + j)..], nx, stride, ny, stride * nx, highest);

                start += (int)span;
            }

            for (int i = 0; i < values.Length; i++)
                values[i] = lut[values[i]];

            // The values come out a channel at a time, and a chunk stores them a row at a time,
            // so the last step is a transpose rather than a copy.
            int to = 0;
            for (int y = 0; y < rows; y++)
            {
                int plane = 0;
                int taken = 0;

                foreach (ExrChannel channel in header.Channels)
                {
                    int shorts = channel.SampledWidth(width) * (channel.Bytes / 2);
                    int span = channel.SampledRows(firstRow, rows) * shorts;

                    if (channel.PresentOn(firstRow + y))
                    {
                        int from = plane + (channel.SampledRows(firstRow, y) * shorts);
                        for (int i = 0; i < shorts; i++)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                destination[(to + (i * 2))..], values[from + i]);
                        }

                        to += shorts * 2;
                        taken += shorts;
                    }

                    plane += span;
                }

                _ = taken;
            }

            return to == destination.Length;
        }
        finally
        {
            BufferPool.Bytes.Return(bitmapBuffer);
            BufferPool.UShorts.Return(lutBuffer);
            BufferPool.UShorts.Return(valueBuffer);
        }
    }

    /// <summary>
    /// Lists the values the block uses, so that a renumbered value maps back to the one the file
    /// meant. Zero is always present whether the bitmap names it or not.
    /// </summary>
    private static int BuildReverseLut(ReadOnlySpan<byte> bitmap, Span<ushort> lut)
    {
        int k = 0;
        for (int i = 0; i < ShortRange; i++)
        {
            if (i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0)
                lut[k++] = (ushort)i;
        }

        int highest = k - 1;
        while (k < ShortRange)
            lut[k++] = 0;

        return highest;
    }

    /// <summary>
    /// Undoes the transform, coarsest level first. Each level takes the averages and differences
    /// the level above left and turns them back into the pairs they came from, so the picture
    /// reappears at twice the detail with every pass.
    /// </summary>
    private static void Decode(Span<ushort> data, int nx, int ox, int ny, int oy, int highest)
    {
        bool narrow = highest < (1 << 14);
        int n = Math.Min(nx, ny);
        int p = 1;

        while (p <= n)
            p <<= 1;

        p >>= 1;
        int p2 = p;
        p >>= 1;

        while (p >= 1)
        {
            int py = 0;
            int endY = oy * (ny - p2);
            int oy1 = oy * p;
            int oy2 = oy * p2;
            int ox1 = ox * p;
            int ox2 = ox * p2;

            for (; py <= endY; py += oy2)
            {
                int px = py;
                int endX = py + (ox * (nx - p2));

                for (; px <= endX; px += ox2)
                {
                    int at01 = px + ox1;
                    int at10 = px + oy1;
                    int at11 = at10 + ox1;

                    if (narrow)
                    {
                        Narrow4(data, px, at01, at10, at11);
                        continue;
                    }

                    Wide(data[px], data[at10], out ushort i00, out ushort i10);
                    Wide(data[at01], data[at11], out ushort i01, out ushort i11);
                    Wide(i00, i01, out data[px], out data[at01]);
                    Wide(i10, i11, out data[at10], out data[at11]);
                }

                if ((nx & p) != 0)
                {
                    int at10 = px + oy1;
                    ushort low;

                    if (narrow)
                        Narrow(data[px], data[at10], out low, out data[at10]);
                    else
                        Wide(data[px], data[at10], out low, out data[at10]);

                    data[px] = low;
                }
            }

            if ((ny & p) != 0)
            {
                int px = py;
                int endX = py + (ox * (nx - p2));

                for (; px <= endX; px += ox2)
                {
                    int at01 = px + ox1;
                    ushort low;

                    if (narrow)
                        Narrow(data[px], data[at01], out low, out data[at01]);
                    else
                        Wide(data[px], data[at01], out low, out data[at01]);

                    data[px] = low;
                }
            }

            p2 = p;
            p >>= 1;
        }
    }

    /// <summary>The four way step, taken when every value fits in fourteen bits and cannot wrap.</summary>
    private static void Narrow4(Span<ushort> data, int px, int at01, int at10, int at11)
    {
        int a = (short)data[px];
        int b = (short)data[at10];
        int c = (short)data[at01];
        int d = (short)data[at11];

        int i00 = a + (b & 1) + (b >> 1);
        int i10 = i00 - b;
        int i01 = c + (d & 1) + (d >> 1);
        int i11 = i01 - d;

        a = i00 + (i01 & 1) + (i01 >> 1);
        b = a - i01;
        c = i10 + (i11 & 1) + (i11 >> 1);
        d = c - i11;

        data[px] = (ushort)a;
        data[at01] = (ushort)b;
        data[at10] = (ushort)c;
        data[at11] = (ushort)d;
    }

    private static void Narrow(ushort low, ushort high, out ushort first, out ushort second)
    {
        int h = (short)high;
        int value = (short)low + (h & 1) + (h >> 1);

        first = (ushort)(short)value;
        second = (ushort)(short)(value - h);
    }

    /// <summary>
    /// The step for values that use the whole sixteen bits, where the sum of a pair can wrap. It
    /// is the same arithmetic done modulo the range rather than in signed numbers.
    /// </summary>
    private static void Wide(ushort low, ushort high, out ushort first, out ushort second)
    {
        int m = low;
        int d = high;
        int b = (m - (d >> 1)) & Mask;
        int a = (d + b - Offset) & Mask;

        second = (ushort)b;
        first = (ushort)a;
    }
}
