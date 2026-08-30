// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The entropy coder the wavelet compression finishes with. It codes sixteen bit values, so its
/// alphabet is 65,536 symbols plus one standing for a run. A table that size would cost more than
/// the data, so the code lengths are themselves run length coded.
/// </summary>
internal static class ExrHuffman
{
    private const int EncodeBits = 16;
    private const int DecodeBits = 14;
    private const int EncodeSize = (1 << EncodeBits) + 1;
    private const int DecodeSize = 1 << DecodeBits;
    private const int DecodeMask = DecodeSize - 1;

    private const int ShortZeroRun = 59;
    private const int LongZeroRun = 63;
    private const int ShortestLongRun = 2 + LongZeroRun - ShortZeroRun;

    /// <summary>A code's length lives in the low six bits and the code itself above them.</summary>
    private static int LengthOf(long code) => (int)(code & 63);

    private static long CodeOf(long code) => (long)((ulong)code >> 6);

    public static bool TryDecompress(ReadOnlySpan<byte> source, Span<ushort> destination)
    {
        if (source.Length < 20)
            return destination.Length == 0;

        int minimum = ReadInt32(source);
        int maximum = ReadInt32(source[4..]);
        int bits = ReadInt32(source[12..]);

        if ((uint)minimum >= EncodeSize || (uint)maximum >= EncodeSize || minimum > maximum || bits < 0)
            return false;

        long bytes = ((long)bits + 7) / 8;
        if (20 + bytes > source.Length)
            return false;

        long[] codes = BufferPool.Longs.Rent(EncodeSize);
        try
        {
            Span<long> hcode = codes.AsSpan(0, EncodeSize);
            hcode.Clear();

            ReadOnlySpan<byte> table = source[20..];
            if (!TryUnpackTable(table, minimum, maximum, hcode, out int consumed))
                return false;

            BuildCanonical(hcode);

            DecodeTable decode = DecodeTable.Rent();
            if (!decode.TryBuild(hcode, minimum, maximum))
                return false;

            return TryDecode(hcode, decode, table[consumed..], bits, maximum, destination);
        }
        finally
        {
            BufferPool.Longs.Return(codes);
        }
    }

    /// <summary>
    /// Reads the code lengths. Six bits each, except that a length of fifty nine or more stands
    /// for a run of symbols with no code at all, which is most of them in a typical block.
    /// </summary>
    private static bool TryUnpackTable(ReadOnlySpan<byte> source, int minimum, int maximum,
                                       Span<long> hcode, out int consumed)
    {
        consumed = 0;
        ulong window = 0;
        int held = 0;
        int at = 0;

        for (int i = minimum; i <= maximum; i++)
        {
            if (held < 6 && at >= source.Length)
                return false;

            long length = ReadBits(6, source, ref at, ref window, ref held);
            hcode[i] = length;

            if (length == LongZeroRun)
            {
                if (held < 8 && at >= source.Length)
                    return false;

                long run = ReadBits(8, source, ref at, ref window, ref held) + ShortestLongRun;
                if (i + run > maximum + 1)
                    return false;

                while (run-- > 0)
                    hcode[i++] = 0;

                i--;
            }
            else if (length >= ShortZeroRun)
            {
                long run = length - ShortZeroRun + 2;
                if (i + run > maximum + 1)
                    return false;

                while (run-- > 0)
                    hcode[i++] = 0;

                i--;
            }
        }

        consumed = at;
        return true;
    }

    /// <summary>
    /// Turns a table of lengths into the one code of each length that a decoder can follow: the
    /// shortest codes take the lowest numbers, and each length starts where the one below it left
    /// off, doubled.
    /// </summary>
    private static void BuildCanonical(Span<long> hcode)
    {
        Span<long> counts = stackalloc long[59];
        counts.Clear();

        for (int i = 0; i < EncodeSize; i++)
            counts[(int)hcode[i]]++;

        long next = 0;
        for (int i = 58; i > 0; i--)
        {
            long following = (next + counts[i]) >> 1;
            counts[i] = next;
            next = following;
        }

        for (int i = 0; i < EncodeSize; i++)
        {
            long length = hcode[i];
            if (length > 0)
                hcode[i] = length | (counts[(int)length]++ << 6);
        }
    }

    /// <summary>
    /// The lookup a decoder walks. Every code of fourteen bits or fewer fills a run of entries so
    /// that one read of fourteen bits finds it outright; the rare longer ones share an entry and
    /// are told apart by comparing the code itself.
    /// </summary>
    private sealed class DecodeTable
    {
        /// <summary>
        /// One table a thread, reused. It is a quarter of a megabyte, and a file is decoded a
        /// chunk at a time, so building a fresh one for every chunk of a picture was allocating
        /// several times what the picture itself takes.
        /// </summary>
        [ThreadStatic]
        private static DecodeTable? _spare;

        public readonly int[] Length = new int[DecodeSize];
        public readonly int[] Symbol = new int[DecodeSize];
        public readonly List<int>?[] Long = new List<int>?[DecodeSize];

        /// <summary>Whether any code was long enough to need the overflow lists.</summary>
        private bool _overflowed;

        public static DecodeTable Rent()
        {
            DecodeTable table = _spare ??= new DecodeTable();

            Array.Clear(table.Length);
            Array.Clear(table.Symbol);

            if (table._overflowed)
            {
                Array.Clear(table.Long);
                table._overflowed = false;
            }

            return table;
        }

        public bool TryBuild(ReadOnlySpan<long> hcode, int minimum, int maximum)
        {
            for (int i = minimum; i <= maximum; i++)
            {
                long code = CodeOf(hcode[i]);
                int length = LengthOf(hcode[i]);

                if (length > 0 && (ulong)code >> length != 0)
                    return false;

                if (length > DecodeBits)
                {
                    int index = (int)(code >> (length - DecodeBits));
                    if ((uint)index >= DecodeSize || Length[index] != 0)
                        return false;

                    _overflowed = true;
                    (Long[index] ??= []).Add(i);
                    continue;
                }

                if (length == 0)
                    continue;

                int start = (int)(code << (DecodeBits - length));
                int span = 1 << (DecodeBits - length);
                if (start < 0 || start + span > DecodeSize)
                    return false;

                for (int j = 0; j < span; j++)
                {
                    if (Length[start + j] != 0 || Long[start + j] is not null)
                        return false;

                    Length[start + j] = length;
                    Symbol[start + j] = i;
                }
            }

            return true;
        }
    }

    private static bool TryDecode(ReadOnlySpan<long> hcode, DecodeTable table, ReadOnlySpan<byte> source,
                                  int bits, int runSymbol, Span<ushort> destination)
    {
        long available = ((long)bits + 7) / 8;
        if (available > source.Length)
            return false;

        ulong window = 0;
        int held = 0;
        int at = 0;
        int written = 0;
        int end = (int)available;

        while (at < end)
        {
            window = (window << 8) | source[at++];
            held += 8;

            while (held >= DecodeBits)
            {
                int index = (int)((window >> (held - DecodeBits)) & DecodeMask);

                if (table.Length[index] != 0)
                {
                    if (table.Length[index] > held)
                        return false;

                    held -= table.Length[index];
                    if (!Emit(table.Symbol[index], runSymbol, source, ref at, end, ref window,
                              ref held, destination, ref written))
                        return false;

                    continue;
                }

                List<int>? candidates = table.Long[index];
                if (candidates is null)
                    return false;

                bool found = false;
                foreach (int symbol in candidates)
                {
                    int length = LengthOf(hcode[symbol]);
                    while (held < length && at < end)
                    {
                        window = (window << 8) | source[at++];
                        held += 8;
                    }

                    if (held < length)
                        continue;

                    if (CodeOf(hcode[symbol]) != (long)((window >> (held - length)) & ((1UL << length) - 1)))
                        continue;

                    held -= length;
                    if (!Emit(symbol, runSymbol, source, ref at, end, ref window, ref held,
                              destination, ref written))
                        return false;

                    found = true;
                    break;
                }

                if (!found)
                    return false;
            }
        }

        // The last byte read carries more bits than the stream declared, so the tail is shifted
        // off before the codes that remain are taken.
        int spare = (8 - bits) & 7;
        window >>= spare;
        held -= spare;

        while (held > 0)
        {
            int index = (int)((window << (DecodeBits - held)) & DecodeMask);
            if (table.Length[index] == 0 || table.Length[index] > held)
                return false;

            held -= table.Length[index];
            if (!Emit(table.Symbol[index], runSymbol, source, ref at, end, ref window, ref held,
                      destination, ref written))
                return false;
        }

        return written == destination.Length;
    }

    /// <summary>
    /// Writes one decoded symbol, which is either a value or the run marker. The marker is
    /// followed by a byte saying how many more times to repeat whatever was written last.
    /// </summary>
    private static bool Emit(int symbol, int runSymbol, ReadOnlySpan<byte> source, ref int at, int end,
                             ref ulong window, ref int held, Span<ushort> destination, ref int written)
    {
        if (symbol != runSymbol)
        {
            if (written >= destination.Length)
                return false;

            destination[written++] = (ushort)symbol;
            return true;
        }

        if (held < 8)
        {
            if (at >= end)
                return false;

            window = (window << 8) | source[at++];
            held += 8;
        }

        held -= 8;
        int count = (byte)(window >> held);

        if (written == 0 || written + count > destination.Length)
            return false;

        ushort repeated = destination[written - 1];
        for (int i = 0; i < count; i++)
            destination[written++] = repeated;

        return true;
    }

    private static long ReadBits(int count, ReadOnlySpan<byte> source, ref int at, ref ulong window,
                                 ref int held)
    {
        while (held < count)
        {
            window = (window << 8) | (at < source.Length ? source[at++] : 0u);
            held += 8;
        }

        held -= count;
        return (long)((window >> held) & ((1UL << count) - 1));
    }

    private static int ReadInt32(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
}
