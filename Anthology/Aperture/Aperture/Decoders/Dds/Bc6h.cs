// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>
/// The block compression for pictures whose values run past one. Sixteen half floats a block, no
/// alpha, in fourteen modes that trade endpoint precision against endpoint range, most of them
/// storing the second endpoint as a difference from the first.
/// </summary>
internal static class Bc6h
{
    private const int WeightMax = 64;
    private const int WeightRound = 32;
    private const int WeightShift = 6;

    private const int FieldShape = 2;
    private const int FieldFirst = 3;

    public static void Decode(ReadOnlySpan<byte> block, bool signedValue, Span<ushort> halves)
    {
        int at = 0;
        int mode = ReadBits(block, ref at, 2);

        if (mode is not (0 or 1))
            mode = (ReadBits(block, ref at, 3) << 2) | mode;

        sbyte row = BlockTables.Bc6ModeToInfo[mode];
        if (row < 0)
        {
            halves.Clear();
            return;
        }

        ReadOnlySpan<byte> info = BlockTables.Bc6Modes;
        int start = row * 16;

        int groups = info[start + 1];
        bool transformed = info[start + 2] != 0;
        int indexBits = info[start + 3];

        // Four triples: the widths of each endpoint in turn. The first is also the width
        // everything is unquantised against, whatever the endpoint was stored in.
        Span<int> precision = stackalloc int[12];
        for (int i = 0; i < 12; i++)
            precision[i] = info[start + 4 + i];

        // Six values: the two endpoints of each of up to two groups, three components each.
        Span<int> endpoints = stackalloc int[4 * 3];
        endpoints.Clear();

        int shape = 0;
        int header = groups > 0 ? 82 : 65;
        ReadOnlySpan<byte> layout = BlockTables.Bc6Layout;

        while (at < header)
        {
            int here = at;
            if (ReadBit(block, ref at) == 0)
                continue;

            int field = layout[((row * 82) + here) * 2];
            int bit = layout[(((row * 82) + here) * 2) + 1];

            if (field == FieldShape)
            {
                shape |= 1 << bit;
                continue;
            }

            if (field < FieldFirst)
            {
                halves.Clear();
                return;
            }

            // The fields run red, green then blue, each with its four endpoints in turn.
            int index = field - FieldFirst;
            endpoints[((index & 3) * 3) + (index >> 2)] |= 1 << bit;
        }

        if (signedValue)
            SignExtend(endpoints, 0, precision);

        if (signedValue || transformed)
        {
            for (int p = 0; p <= groups; p++)
            {
                if (p != 0)
                    SignExtend(endpoints, p * 2, precision);

                SignExtend(endpoints, (p * 2) + 1, precision);
            }
        }


        if (transformed)
            Untransform(endpoints, groups, precision, signedValue);

        for (int i = 0; i < 16; i++)
        {
            int bits = IsAnchor(groups, shape, i) ? indexBits - 1 : indexBits;
            if (at + bits > 128)
            {
                halves.Clear();
                return;
            }

            int index = ReadBits(block, ref at, bits);
            if (index >= (groups > 0 ? 8 : 16))
            {
                halves.Clear();
                return;
            }

            int group = BlockTables.Partitions[(((groups * 64) + shape) * 16) + i];
            ReadOnlySpan<byte> weights = groups > 0 ? BlockTables.Weights3 : BlockTables.Weights4;
            int weight = weights[index];

            for (int c = 0; c < 3; c++)
            {
                int low = Unquantise(endpoints[(group * 2 * 3) + c], precision[c], signedValue);
                int high = Unquantise(endpoints[(((group * 2) + 1) * 3) + c], precision[c], signedValue);
                int mixed = ((low * (WeightMax - weight)) + (high * weight) + WeightRound) >> WeightShift;

                halves[(i * 3) + c] = ToHalf(Finish(mixed, signedValue), signedValue);
            }
        }
    }

    /// <summary>Puts back the sign bit an endpoint stored in fewer bits than an integer holds.</summary>
    private static void SignExtend(Span<int> endpoints, int which, ReadOnlySpan<int> precision)
    {
        for (int c = 0; c < 3; c++)
        {
            int bits = precision[(which * 3) + c];
            if (bits <= 0)
                continue;

            int value = endpoints[(which * 3) + c];
            endpoints[(which * 3) + c] = (value & (1 << (bits - 1))) != 0
                ? value | ~((1 << bits) - 1)
                : value;
        }
    }

    /// <summary>
    /// Turns the differences the later endpoints were stored as back into values. They wrap
    /// inside the width of the first endpoint, which is what lets a small difference cross zero.
    /// </summary>
    private static void Untransform(Span<int> endpoints, int groups, ReadOnlySpan<int> precision,
                                    bool signedValue)
    {
        int last = groups > 0 ? 3 : 1;

        for (int c = 0; c < 3; c++)
        {
            int mask = (1 << precision[c]) - 1;
            int anchor = endpoints[c];

            for (int which = 1; which <= last; which++)
                endpoints[(which * 3) + c] = (endpoints[(which * 3) + c] + anchor) & mask;
        }

        if (!signedValue)
            return;

        // The wrap left the sign off again, and it is the first endpoint's width that governs
        // here rather than the width the later ones were stored in.
        for (int which = 1; which <= last; which++)
        {
            for (int c = 0; c < 3; c++)
            {
                int bits = precision[c];
                int value = endpoints[(which * 3) + c];
                endpoints[(which * 3) + c] = (value & (1 << (bits - 1))) != 0
                    ? value | ~((1 << bits) - 1)
                    : value;
            }
        }
    }

    private static bool IsAnchor(int groups, int shape, int offset)
    {
        for (int p = 0; p <= groups; p++)
        {
            if (BlockTables.FixUps[(((groups * 64) + shape) * 3) + p] == offset)
                return true;
        }

        return false;
    }

    /// <summary>Stretches an endpoint of the stored width out to the sixteen bits a half holds.</summary>
    private static int Unquantise(int value, int bits, bool signedValue)
    {
        if (!signedValue)
        {
            if (bits >= 15)
                return value;

            if (value == 0)
                return 0;

            return value == (1 << bits) - 1 ? 0xFFFF : ((value << 16) + 0x8000) >> bits;
        }

        if (bits >= 16)
            return value;

        int sign = 0;
        if (value < 0)
        {
            sign = 1;
            value = -value;
        }

        int result = value == 0 ? 0
            : value >= (1 << (bits - 1)) - 1 ? 0x7FFF
            : ((value << 15) + 0x4000) >> (bits - 1);

        return sign != 0 ? -result : result;
    }

    /// <summary>The last scaling, which brings the value into the range a half can hold.</summary>
    private static int Finish(int value, bool signedValue)
    {
        if (!signedValue)
            return (value * 31) >> 6;

        return value < 0 ? -(((-value) * 31) >> 5) : (value * 31) >> 5;
    }

    private static ushort ToHalf(int value, bool signedValue)
    {
        if (!signedValue)
            return (ushort)value;

        int sign = 0;
        if (value < 0)
        {
            sign = 0x8000;
            value = -value;
        }

        return (ushort)(sign | value);
    }

    private static int ReadBit(ReadOnlySpan<byte> block, ref int at)
    {
        int value = (block[at >> 3] >> (at & 7)) & 1;
        at++;
        return value;
    }

    private static int ReadBits(ReadOnlySpan<byte> block, ref int at, int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
            value |= ReadBit(block, ref at) << i;

        return value;
    }
}
