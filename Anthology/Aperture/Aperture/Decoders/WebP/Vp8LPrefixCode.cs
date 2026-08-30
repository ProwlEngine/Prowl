// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// One canonical prefix code, built from the lengths a stream states rather than from the codes:
/// shorter codes take the lower numbers and each length starts where the one below left off,
/// doubled. As in Deflate, a code goes into the stream most significant end first even though the
/// stream is filled from the other end.
/// </summary>
internal sealed class Vp8LPrefixCode
{
    private const int MaxLength = 15;

    /// <summary>Codes no longer than this are answered by a single array read.</summary>
    private const int LookaheadBits = 8;

    private readonly int[] _counts = new int[MaxLength + 1];
    private readonly int[] _symbols;

    /// <summary>Length in the high half and symbol in the low. Zero means the code is longer.</summary>
    private readonly int[] _lookahead = new int[1 << LookaheadBits];

    /// <summary>The one symbol this code stands for when it stands for only one.</summary>
    public int Single { get; private set; } = -1;

    private Vp8LPrefixCode(int symbols) => _symbols = new int[symbols];

    /// <summary>Builds a code, failing when the lengths do not describe a complete one.</summary>
    public static Vp8LPrefixCode? Build(ReadOnlySpan<int> lengths)
    {
        Vp8LPrefixCode code = new(lengths.Length);

        int used = 0;
        int last = -1;

        for (int i = 0; i < lengths.Length; i++)
        {
            int length = lengths[i];
            if (length == 0)
                continue;

            if (length > MaxLength)
                return null;

            code._counts[length]++;
            used++;
            last = i;
        }

        if (used == 0)
            return null;

        // A code over a single symbol costs no bits at all, which the general construction cannot
        // express, so it is carried separately.
        if (used == 1)
        {
            code.Single = last;
            return code;
        }

        Span<int> offsets = stackalloc int[MaxLength + 2];
        int running = 0;
        int available = 1;

        for (int length = 1; length <= MaxLength; length++)
        {
            available = (available << 1) - code._counts[length];
            if (available < 0)
                return null;

            offsets[length] = running;
            running += code._counts[length];
        }

        if (available != 0)
            return null;

        Span<int> cursor = stackalloc int[MaxLength + 2];
        offsets[..(MaxLength + 2)].CopyTo(cursor);

        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] != 0)
                code._symbols[cursor[lengths[i]]++] = i;
        }

        code.BuildLookahead();
        return code;
    }

    /// <summary>
    /// Fills the flat table the short codes are answered from. The stream hands over its bits
    /// least significant first while a code is written most significant first, so a code indexes
    /// the table by its own bits reversed, and every longer pattern that begins with it lands on
    /// the same entry.
    /// </summary>
    private void BuildLookahead()
    {
        int value = 0;
        int index = 0;

        for (int length = 1; length <= MaxLength; length++)
        {
            for (int i = 0; i < _counts[length]; i++, index++, value++)
            {
                if (length > LookaheadBits)
                    continue;

                int entry = (length << 16) | _symbols[index];
                int step = 1 << length;

                for (int slot = Reverse(value, length); slot < 1 << LookaheadBits; slot += step)
                    _lookahead[slot] = entry;
            }

            value <<= 1;
        }
    }

    /// <summary>Turns a code back to front, which is the order its bits arrive in.</summary>
    private static int Reverse(int value, int length)
    {
        int reversed = 0;
        for (int i = 0; i < length; i++)
            reversed |= ((value >> i) & 1) << (length - 1 - i);

        return reversed;
    }

    /// <summary>Resolves one symbol, by a single table read where the code is short enough.</summary>
    public int Read(ref Vp8LBitReader reader)
    {
        if (Single >= 0)
            return Single;

        int entry = _lookahead[reader.Peek(LookaheadBits)];
        if (entry != 0)
        {
            reader.Skip(entry >> 16);
            return entry & 0xFFFF;
        }

        return ReadLong(ref reader);
    }

    /// <summary>Walks the stream one bit at a time until the bits so far name a symbol.</summary>
    private int ReadLong(ref Vp8LBitReader reader)
    {
        int code = 0;
        int first = 0;
        int index = 0;

        for (int length = 1; length <= MaxLength; length++)
        {
            code |= (int)reader.ReadBit();
            int count = _counts[length];

            if (code - first < count)
                return _symbols[index + (code - first)];

            index += count;
            first = (first + count) << 1;
            code <<= 1;
        }

        return -1;
    }
}
