// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The compressions a chunk may have been written with. Three share the same two closing steps:
/// the bytes are split into even and odd halves and each is stored as its difference from the one
/// before, which turns a run of similar floats into a run of small numbers.
/// </summary>
internal static class ExrCodecs
{
    public static bool TryRle(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        byte[] scratch = BufferPool.Bytes.Rent(destination.Length);
        try
        {
            Span<byte> raw = scratch.AsSpan(0, destination.Length);
            if (!TryExpandRuns(source, raw))
                return false;

            Unpredict(raw);
            Interleave(raw, destination);
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }
    }

    /// <summary>A signed count, negative for a literal run and positive for a repeated byte.</summary>
    private static bool TryExpandRuns(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int at = 0;
        int written = 0;

        while (at < source.Length)
        {
            sbyte marker = (sbyte)source[at++];

            if (marker < 0)
            {
                int count = -marker;
                if (at + count > source.Length || written + count > destination.Length)
                    return false;

                source.Slice(at, count).CopyTo(destination[written..]);
                at += count;
                written += count;
                continue;
            }

            if (at >= source.Length)
                return false;

            int run = marker + 1;
            if (written + run > destination.Length)
                return false;

            destination.Slice(written, run).Fill(source[at++]);
            written += run;
        }

        return written == destination.Length;
    }

    /// <summary>Inflates into a buffer of a size the caller already knows.</summary>
    public static byte[]? Inflate(ReadOnlySpan<byte> source, int size)
    {
        if (size < 0)
            return null;

        byte[] result = new byte[size];
        return TryInflate(source, result) ? result : null;
    }

    /// <summary>Undoes the byte differencing the zip form applies before deflating.</summary>
    public static void UndoPrediction(Span<byte> data) => Unpredict(data);

    /// <summary>Puts the two halves of the differenced bytes back into one run.</summary>
    public static void UndoInterleave(ReadOnlySpan<byte> source, Span<byte> destination) =>
        Interleave(source, destination);

    public static bool TryZip(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        byte[] scratch = BufferPool.Bytes.Rent(destination.Length);
        try
        {
            Span<byte> raw = scratch.AsSpan(0, destination.Length);
            if (!TryInflate(source, raw))
                return false;

            Unpredict(raw);
            Interleave(raw, destination);
            return true;
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }
    }

    /// <summary>
    /// The lossy twenty four bit form. A float keeps its sign, its exponent and the top fifteen
    /// bits of its mantissa, which is stored as three bytes rather than four, and every value is
    /// written as its difference from the one before it on the same row of the same channel.
    /// </summary>
    public static bool TryPxr24(ReadOnlySpan<byte> source, Span<byte> destination, ExrHeader header,
                                int firstRow, int rows, int width)
    {
        long packed = 0;

        for (int row = 0; row < rows; row++)
        {
            foreach (ExrChannel channel in header.Channels)
            {
                if (channel.PresentOn(firstRow + row))
                {
                    packed += (long)channel.SampledWidth(width) *
                              (channel.PixelType == 2 ? 3 : channel.Bytes);
                }
            }
        }

        if (packed > int.MaxValue || packed <= 0)
            return false;

        byte[] scratch = BufferPool.Bytes.Rent((int)packed);
        try
        {
            Span<byte> raw = scratch.AsSpan(0, (int)packed);
            if (!TryInflate(source, raw))
                return false;

            int from = 0;
            int to = 0;

            for (int row = 0; row < rows; row++)
            {
                foreach (ExrChannel channel in header.Channels)
                {
                    if (!channel.PresentOn(firstRow + row))
                        continue;

                    int sampled = channel.SampledWidth(width);

                    switch (channel.PixelType)
                    {
                        case 0:
                            Rebuild(raw, ref from, destination, ref to, sampled, 4, 24);
                            break;

                        case 1:
                            Rebuild(raw, ref from, destination, ref to, sampled, 2, 8);
                            break;

                        default:
                            Rebuild(raw, ref from, destination, ref to, sampled, 3, 24);
                            break;
                    }
                }
            }

            return to == destination.Length;
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }
    }

    /// <summary>
    /// Puts one channel of one row back together from its bytes, which are stored a plane at a
    /// time rather than a value at a time so that the high bytes of neighbouring values sit next
    /// to each other. A float keeps only three of its four bytes, so its low byte reads as zero.
    /// </summary>
    private static void Rebuild(ReadOnlySpan<byte> source, ref int from, Span<byte> destination,
                                ref int to, int width, int planes, int topShift)
    {
        uint running = 0;
        bool half = planes == 2;

        for (int x = 0; x < width; x++)
        {
            uint difference = 0;
            for (int plane = 0; plane < planes; plane++)
                difference |= (uint)source[from + (plane * width) + x] << (topShift - (plane * 8));

            running += difference;

            int at = to + (x * (half ? 2 : 4));
            destination[at] = (byte)running;
            destination[at + 1] = (byte)(running >> 8);

            if (half)
                continue;

            destination[at + 2] = (byte)(running >> 16);
            destination[at + 3] = (byte)(running >> 24);
        }

        from += planes * width;
        to += width * (half ? 2 : 4);
    }

    private static bool TryInflate(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        try
        {
            using MemoryStream input = new(source.ToArray(), writable: false);
            using ZLibStream inflate = new(input, CompressionMode.Decompress);

            int written = 0;
            while (written < destination.Length)
            {
                int read = inflate.Read(destination[written..]);
                if (read <= 0)
                    break;

                written += read;
            }

            return written == destination.Length;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Turns each byte back from a difference into the value it stands for.</summary>
    private static void Unpredict(Span<byte> data)
    {
        for (int at = 1; at < data.Length; at++)
            data[at] = (byte)(data[at - 1] + data[at] - 128);
    }

    /// <summary>Weaves the two halves the writer split the block into back together.</summary>
    private static void Interleave(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int first = 0;
        int second = (destination.Length + 1) / 2;

        for (int at = 0; at < destination.Length; at++)
            destination[at] = (at & 1) == 0 ? source[first++] : source[second++];
    }
}
