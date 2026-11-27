// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>The compressions a strip or tile can arrive under, other than none at all.</summary>
internal static class TiffCodecs
{
    private const int ClearCode = 256;
    private const int EndOfInformation = 257;
    private const int FirstFreeCode = 258;
    private const int MaxCode = 4096;

    /// <summary>
    /// The run length form: a signed count, where a positive one introduces that many literal
    /// bytes plus one and a negative one repeats the byte after it.
    /// </summary>
    public static bool TryPackBits(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
    {
        written = 0;
        int at = 0;

        while (at < source.Length && written < destination.Length)
        {
            sbyte count = (sbyte)source[at++];

            if (count == -128)
                continue;

            if (count >= 0)
            {
                int run = count + 1;
                if (at + run > source.Length)
                    run = source.Length - at;
                if (written + run > destination.Length)
                    run = destination.Length - written;
                if (run <= 0)
                    break;

                source.Slice(at, run).CopyTo(destination[written..]);
                at += run;
                written += run;
                continue;
            }

            if (at >= source.Length)
                break;

            int repeat = 1 - count;
            if (written + repeat > destination.Length)
                repeat = destination.Length - written;

            destination.Slice(written, repeat).Fill(source[at++]);
            written += repeat;
        }

        return written > 0;
    }

    /// <summary>
    /// The dictionary form. Codes are packed most significant bit first and start nine bits wide,
    /// growing one bit early: the width goes up when the next free code reaches the last value
    /// the current width can hold, not when it passes it. Getting that off by one wrong is the
    /// classic way to decode this format into noise.
    /// </summary>
    public static bool TryLzw(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
    {
        written = 0;

        short[] prefix = BufferPool.Shorts.Rent(MaxCode);
        byte[] suffix = BufferPool.Bytes.Rent(MaxCode);
        int[] length = BufferPool.Ints.Rent(MaxCode);

        try
        {
            int next = FirstFreeCode;
            int width = 9;
            int previous = -1;

            int bitPosition = 0;
            int totalBits = source.Length * 8;

            while (bitPosition + width <= totalBits)
            {
                int code = ReadCode(source, bitPosition, width);
                bitPosition += width;

                if (code == EndOfInformation)
                    break;

                if (code == ClearCode)
                {
                    next = FirstFreeCode;
                    width = 9;
                    previous = -1;
                    continue;
                }

                int start = written;

                if (code < next && (code < ClearCode || code >= FirstFreeCode))
                {
                    if (!Emit(prefix, suffix, length, code, destination, ref written))
                        return written > 0;
                }
                else if (previous >= 0 && code == next)
                {
                    // A code the table has not seen yet can only mean the previous string plus
                    // its own first byte, which is the one case the encoder is allowed to write.
                    if (!Emit(prefix, suffix, length, previous, destination, ref written))
                        return written > 0;

                    if (written < destination.Length)
                        destination[written++] = destination[start];
                }
                else
                {
                    break;
                }

                if (previous >= 0 && next < MaxCode)
                {
                    prefix[next] = (short)previous;
                    suffix[next] = destination[start];
                    length[next] = (previous < FirstFreeCode ? 1 : length[previous]) + 1;
                    next++;
                }

                previous = code;

                if (next + 1 >= 1 << width && width < 12)
                    width++;
            }

            return written > 0;
        }
        finally
        {
            BufferPool.Shorts.Return(prefix);
            BufferPool.Bytes.Return(suffix);
            BufferPool.Ints.Return(length);
        }
    }

    /// <summary>Writes one code's string, which is stored back to front and so is reversed in place.</summary>
    private static bool Emit(short[] prefix, byte[] suffix, int[] length, int code,
                             Span<byte> destination, ref int written)
    {
        if (code < FirstFreeCode)
        {
            if (written >= destination.Length)
                return false;

            destination[written++] = (byte)code;
            return true;
        }

        int size = length[code];
        if (size <= 0 || written + size > destination.Length)
            return false;

        int at = written + size;
        int walk = code;
        while (walk >= FirstFreeCode)
        {
            destination[--at] = suffix[walk];
            walk = prefix[walk];
        }

        destination[--at] = (byte)walk;
        written += size;
        return true;
    }

    /// <summary>
    /// One code, which is nine to twelve bits and may start anywhere in a byte. Three bytes
    /// always cover it, so the whole code comes out of one window rather than a bit at a time.
    /// </summary>
    private static int ReadCode(ReadOnlySpan<byte> source, int bitPosition, int width)
    {
        int at = bitPosition >> 3;
        int offset = bitPosition & 7;

        uint window = (uint)source[at] << 16;
        if (at + 1 < source.Length)
            window |= (uint)source[at + 1] << 8;
        if (at + 2 < source.Length)
            window |= source[at + 2];

        return (int)((window >> (24 - offset - width)) & ((1u << width) - 1));
    }

    /// <summary>The deflate form, which both of its tag numbers wrap in a zlib header.</summary>
    public static bool TryDeflate(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
    {
        written = 0;
        if (source.Length < 2)
            return false;

        // A file may hold hundreds of small strips, so the bytes are copied into a rented buffer
        // rather than a fresh array each time.
        byte[] scratch = BufferPool.Bytes.Rent(source.Length);

        try
        {
            source.CopyTo(scratch);

            using MemoryStream input = new(scratch, 0, source.Length, writable: false);
            using ZLibStream inflate = new(input, CompressionMode.Decompress);

            while (written < destination.Length)
            {
                int read = inflate.Read(destination[written..]);
                if (read <= 0)
                    break;

                written += read;
            }
        }
        catch (InvalidDataException)
        {
            return written > 0;
        }
        finally
        {
            BufferPool.Bytes.Return(scratch);
        }

        return written > 0;
    }
}
