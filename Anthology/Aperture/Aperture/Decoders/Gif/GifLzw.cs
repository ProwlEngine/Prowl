// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Gif;

/// <summary>
/// The dictionary coder the format uses for its indices. The same idea as TIFF's and different in
/// every detail: codes packed least significant bit first, the width going up when the table is
/// actually full, and sub-blocks of at most 255 bytes that a code may straddle.
/// </summary>
internal static class GifLzw
{
    private const int MaxCode = 4096;

    public static bool TryDecode(ReadOnlySpan<byte> data, ref int at, int minimumCodeSize, Span<byte> destination)
    {
        int clear = 1 << minimumCodeSize;
        int end = clear + 1;

        short[] prefix = BufferPool.Shorts.Rent(MaxCode);
        byte[] suffix = BufferPool.Bytes.Rent(MaxCode);
        int[] length = BufferPool.Ints.Rent(MaxCode);

        try
        {
            int next = clear + 2;
            int width = minimumCodeSize + 1;
            int previous = -1;
            int written = 0;

            int bits = 0;
            int count = 0;
            int block = 0;
            int remaining = 0;

            while (true)
            {
                // Top up the bit buffer from however many sub-blocks it takes.
                while (count < width)
                {
                    if (remaining == 0)
                    {
                        if (at >= data.Length)
                            return Finish(data, ref at, written > 0);

                        remaining = data[at++];
                        if (remaining == 0)
                            return written > 0;

                        block = at;
                        at += remaining;
                        if (at > data.Length)
                            return written > 0;
                    }

                    bits |= data[block++] << count;
                    remaining--;
                    count += 8;
                }

                int code = bits & ((1 << width) - 1);
                bits >>= width;
                count -= width;

                if (code == end)
                    return Finish(data, ref at, written > 0);

                if (code == clear)
                {
                    next = clear + 2;
                    width = minimumCodeSize + 1;
                    previous = -1;
                    continue;
                }

                int start = written;

                if (code < next)
                {
                    if (!Emit(prefix, suffix, length, code, clear, destination, ref written))
                        return Finish(data, ref at, written > 0);
                }
                else if (code == next && previous >= 0)
                {
                    // A code the table has not reached yet can only be the previous string plus
                    // its own first byte, which is the one case the encoder may write.
                    if (!Emit(prefix, suffix, length, previous, clear, destination, ref written))
                        return Finish(data, ref at, written > 0);

                    if (written < destination.Length)
                        destination[written++] = destination[start];
                }
                else
                {
                    return Finish(data, ref at, written > 0);
                }

                if (previous >= 0 && next < MaxCode)
                {
                    prefix[next] = (short)previous;
                    suffix[next] = destination[start];
                    length[next] = (previous < clear + 2 ? 1 : length[previous]) + 1;
                    next++;

                    if (next == 1 << width && width < 12)
                        width++;
                }

                previous = code;

                if (written >= destination.Length)
                    return Finish(data, ref at, true);
            }
        }
        finally
        {
            BufferPool.Shorts.Return(prefix);
            BufferPool.Bytes.Return(suffix);
            BufferPool.Ints.Return(length);
        }
    }

    /// <summary>Walks to the end of the sub-block chain, so the caller resumes at the next block.</summary>
    private static bool Finish(ReadOnlySpan<byte> data, ref int at, bool result)
    {
        while (at < data.Length)
        {
            int length = data[at++];
            if (length == 0)
                break;

            at += length;
        }

        return result;
    }

    private static bool Emit(short[] prefix, byte[] suffix, int[] length, int code, int clear,
                             Span<byte> destination, ref int written)
    {
        if (code < clear)
        {
            if (written >= destination.Length)
                return false;

            destination[written++] = (byte)code;
            return true;
        }

        if (code < clear + 2)
            return false;

        int size = length[code];
        if (size <= 0 || written + size > destination.Length)
            return false;

        int at = written + size;
        int walk = code;
        while (walk >= clear + 2)
        {
            destination[--at] = suffix[walk];
            walk = prefix[walk];
        }

        destination[--at] = (byte)walk;
        written += size;
        return true;
    }
}
