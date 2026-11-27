// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

namespace Prowl.Aperture.Decoders.Jpeg;

/// <summary>
/// A canonical Huffman table in the form the bit reader wants: a flat lookup over the short
/// codes, and the classic per length bounds for the rest. Tables are rebuilt in place because a
/// stream may redefine any of them between scans.
/// </summary>
internal sealed class JpegHuffmanTable
{
    /// <summary>Codes no longer than this are answered by a single array read.</summary>
    public const int LookaheadBits = 8;

    /// <summary>Packed as length in the high byte, decoded value in the low byte. Zero means the code is longer.</summary>
    private readonly ushort[] _lookahead = new ushort[1 << LookaheadBits];

    /// <summary>Largest code of each length, or -1 where the length is unused.</summary>
    private readonly int[] _maxCode = new int[17];

    /// <summary>Added to a code of a given length to index <see cref="_values"/> directly.</summary>
    private readonly int[] _valueOffset = new int[17];

    private readonly byte[] _values = new byte[256];

    public bool IsDefined { get; private set; }

    /// <summary>
    /// Rebuilds from a DHT payload: sixteen code counts followed by the values in code order.
    /// Rejects a table that assigns more codes of some length than the tree has room for, which
    /// is the shape a fuzzer reaches for because it makes a naive walk read out of bounds.
    /// </summary>
    public bool TryReset(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
    {
        if (counts.Length < 16 || values.Length > 256)
            return false;

        values.CopyTo(_values);
        _lookahead.AsSpan().Clear();

        int code = 0;
        int index = 0;

        for (int length = 1; length <= 16; length++)
        {
            int count = counts[length - 1];

            // Settled before anything is written: a table claiming more codes of a length than
            // exist would run the lookahead fill off the end of its own array.
            if (index + count > values.Length || code + count > 1 << length)
                return false;

            _valueOffset[length] = index - code;

            for (int i = 0; i < count; i++, code++, index++)
            {
                if (length > LookaheadBits)
                    continue;

                // Every eight bit pattern that starts with this code resolves to it.
                int shift = LookaheadBits - length;
                int start = code << shift;
                ushort entry = (ushort)((length << 8) | _values[index]);
                _lookahead.AsSpan(start, 1 << shift).Fill(entry);
            }

            _maxCode[length] = count > 0 ? code - 1 : -1;
            code <<= 1;
        }

        IsDefined = index > 0;
        return IsDefined;
    }

    /// <summary>
    /// Resolves one code from the next sixteen bits of the stream, most significant first. The
    /// answer is the code's length in the high byte and the value it stands for in the low one,
    /// which is the shape the table already holds and saves the caller unpacking two results.
    /// Zero means the bits do not spell a code this table defines.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(int window)
    {
        int entry = _lookahead[(window >> (16 - LookaheadBits)) & 0xFF];
        return entry != 0 ? entry : DecodeLong(window);
    }

    /// <summary>The codes too long for the flat lookup, walked a length at a time.</summary>
    private int DecodeLong(int window)
    {
        for (int length = LookaheadBits + 1; length <= 16; length++)
        {
            int code = window >> (16 - length);
            if (code <= _maxCode[length])
                return (length << 8) | _values[_valueOffset[length] + code];
        }

        return 0;
    }
}
