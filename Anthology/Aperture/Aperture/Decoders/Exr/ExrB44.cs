// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The fixed rate lossy compression. Sixteen halves, thirty two bytes as stored, packed into
/// fourteen by keeping the first value whole and the rest as six bit differences scaled by a shift
/// the block chooses. A block whose values are all the same costs three bytes instead.
/// </summary>
internal static class ExrB44
{
    private static ushort[]? _linear;

    public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, ExrHeader header,
                                 int firstRow, int rows, int width)
    {
        long planes = 0;
        foreach (ExrChannel channel in header.Channels)
        {
            planes += (long)channel.SampledWidth(width) *
                      channel.SampledRows(firstRow, rows) * channel.Bytes;
        }

        if (planes <= 0 || planes > int.MaxValue)
            return false;

        byte[] scratch = BufferPool.Bytes.Rent((int)planes);
        try
        {
            Span<byte> flat = scratch.AsSpan(0, (int)planes);
            int at = 0;
            int to = 0;

            foreach (ExrChannel channel in header.Channels)
            {
                int nx = channel.SampledWidth(width);
                int ny = channel.SampledRows(firstRow, rows);
                int size = nx * ny * channel.Bytes;

                if (size == 0)
                    continue;

                // Only half channels are packed; anything wider is stored as it stands.
                if (channel.PixelType != 1)
                {
                    if (at + size > source.Length)
                        return false;

                    source.Slice(at, size).CopyTo(flat[to..]);
                    at += size;
                    to += size;
                    continue;
                }

                if (!TryExpand(source, ref at, flat.Slice(to, size), nx, ny, channel.Linear))
                    return false;

                to += size;
            }

            return Transpose(flat, destination, header, firstRow, rows, width);
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }
    }

    private static bool TryExpand(ReadOnlySpan<byte> source, ref int at, Span<byte> plane,
                                  int nx, int ny, bool linear)
    {
        Span<ushort> block = stackalloc ushort[16];

        for (int y = 0; y < ny; y += 4)
        {
            for (int x = 0; x < nx; x += 4)
            {
                if (at + 3 > source.Length)
                    return false;

                // A block whose third byte is past the largest real shift is the flat form.
                if (source[at + 2] >= 13 << 2)
                {
                    Flat(source.Slice(at, 3), block);
                    at += 3;
                }
                else
                {
                    if (at + 14 > source.Length)
                        return false;

                    Unpack(source.Slice(at, 14), block);
                    at += 14;
                }

                if (linear)
                {
                    ushort[] table = LinearTable();
                    for (int i = 0; i < 16; i++)
                        block[i] = table[block[i]];
                }

                int columns = Math.Min(4, nx - x);
                for (int row = 0; row < 4 && y + row < ny; row++)
                {
                    int start = (((y + row) * nx) + x) * 2;
                    for (int i = 0; i < columns; i++)
                        BinaryPrimitives.WriteUInt16LittleEndian(plane[(start + (i * 2))..], block[(row * 4) + i]);
                }
            }
        }

        return true;
    }

    /// <summary>Puts the channel planes back into the row at a time order a chunk stores.</summary>
    private static bool Transpose(ReadOnlySpan<byte> flat, Span<byte> destination, ExrHeader header,
                                 int firstRow, int rows, int width)
    {
        int to = 0;

        for (int y = 0; y < rows; y++)
        {
            int plane = 0;

            foreach (ExrChannel channel in header.Channels)
            {
                int nx = channel.SampledWidth(width);
                int ny = channel.SampledRows(firstRow, rows);
                int rowBytes = nx * channel.Bytes;
                int size = ny * rowBytes;

                if (size == 0)
                    continue;

                if (channel.PresentOn(firstRow + y))
                {
                    int from = plane + (channel.SampledRows(firstRow, y) * rowBytes);
                    if (from + rowBytes > flat.Length || to + rowBytes > destination.Length)
                        return false;

                    flat.Slice(from, rowBytes).CopyTo(destination[to..]);
                    to += rowBytes;
                }

                plane += size;
            }
        }

        return to == destination.Length;
    }

    /// <summary>The three byte form, where every value in the block is the same.</summary>
    private static void Flat(ReadOnlySpan<byte> source, Span<ushort> block)
    {
        ushort value = (ushort)((source[0] << 8) | source[1]);
        block[0] = (ushort)((value & 0x8000) != 0 ? value & 0x7FFF : ~value);

        for (int i = 1; i < 16; i++)
            block[i] = block[0];
    }

    /// <summary>
    /// The fourteen byte form. The first value is stored whole and the rest as six bit
    /// differences walking down each column and then across, all scaled by one shared shift.
    /// </summary>
    private static void Unpack(ReadOnlySpan<byte> b, Span<ushort> s)
    {
        s[0] = (ushort)((b[0] << 8) | b[1]);

        int shift = b[2] >> 2;
        int bias = 0x20 << shift;

        s[4] = Step(s[0], ((b[2] << 4) | (b[3] >> 4)) & 0x3F, shift, bias);
        s[8] = Step(s[4], ((b[3] << 2) | (b[4] >> 6)) & 0x3F, shift, bias);
        s[12] = Step(s[8], b[4] & 0x3F, shift, bias);

        s[1] = Step(s[0], b[5] >> 2, shift, bias);
        s[5] = Step(s[4], ((b[5] << 4) | (b[6] >> 4)) & 0x3F, shift, bias);
        s[9] = Step(s[8], ((b[6] << 2) | (b[7] >> 6)) & 0x3F, shift, bias);
        s[13] = Step(s[12], b[7] & 0x3F, shift, bias);

        s[2] = Step(s[1], b[8] >> 2, shift, bias);
        s[6] = Step(s[5], ((b[8] << 4) | (b[9] >> 4)) & 0x3F, shift, bias);
        s[10] = Step(s[9], ((b[9] << 2) | (b[10] >> 6)) & 0x3F, shift, bias);
        s[14] = Step(s[13], b[10] & 0x3F, shift, bias);

        s[3] = Step(s[2], b[11] >> 2, shift, bias);
        s[7] = Step(s[6], ((b[11] << 4) | (b[12] >> 4)) & 0x3F, shift, bias);
        s[11] = Step(s[10], ((b[12] << 2) | (b[13] >> 6)) & 0x3F, shift, bias);
        s[15] = Step(s[14], b[13] & 0x3F, shift, bias);

        // The values were flipped on the way in so that they sort the same as the floats they
        // stand for, which is what lets a plain difference mean anything.
        for (int i = 0; i < 16; i++)
            s[i] = (ushort)((s[i] & 0x8000) != 0 ? s[i] & 0x7FFF : ~s[i]);
    }

    private static ushort Step(ushort previous, int difference, int shift, int bias) =>
        (ushort)(previous + (uint)(difference << shift) - (uint)bias);

    /// <summary>
    /// The table a channel marked as holding light rather than a perceptual value is read
    /// through, which is the logarithm the writer took before packing.
    /// </summary>
    private static ushort[] LinearTable()
    {
        if (_linear is not null)
            return _linear;

        ushort[] table = new ushort[1 << 16];
        for (int i = 0; i < table.Length; i++)
        {
            float value = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            Half result = !float.IsFinite(value) || value < 0
                ? (Half)0f
                : (Half)(8f * MathF.Log(value));

            table[i] = BitConverter.HalfToUInt16Bits(result);
        }

        _linear = table;
        return table;
    }
}
