// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>
/// The block compression modern assets are shipped in. Eight layouts, named in the first few bits,
/// trading endpoint precision against index precision and one group of pixels against three. A
/// block may also rotate a channel into the alpha slot to spend the separate indices on it.
/// </summary>
internal static class Bc7
{
    private const int WeightMax = 64;
    private const int WeightRound = 32;
    private const int WeightShift = 6;

    public static void Decode(ReadOnlySpan<byte> block, Span<uint> pixels)
    {
        int at = 0;
        while (at < 128 && ReadBit(block, ref at) == 0)
        {
        }

        int mode = at - 1;
        if (mode is < 0 or > 7)
        {
            // The format says a block naming the reserved mode is transparent black.
            pixels.Clear();
            return;
        }

        ReadOnlySpan<byte> table = BlockTables.Bc7Modes;
        int row = mode * 15;

        int groups = table[row];
        int shapeBits = table[row + 1];
        int parityBits = table[row + 2];
        int rotationBits = table[row + 3];
        int selectorBits = table[row + 4];
        int indexBits = table[row + 5];
        int alphaIndexBits = table[row + 6];

        Span<int> precision = stackalloc int[4];
        Span<int> withParity = stackalloc int[4];
        for (int c = 0; c < 4; c++)
        {
            precision[c] = table[row + 7 + c];
            withParity[c] = table[row + 11 + c];
        }

        int endpoints = (groups + 1) << 1;

        int shape = ReadBits(block, ref at, shapeBits);
        int rotation = ReadBits(block, ref at, rotationBits);
        int selector = ReadBits(block, ref at, selectorBits);

        Span<int> colour = stackalloc int[6 * 4];

        for (int c = 0; c < 4; c++)
        {
            for (int i = 0; i < endpoints; i++)
            {
                if (at + precision[c] > 128)
                {
                    pixels.Clear();
                    return;
                }

                colour[(i * 4) + c] = precision[c] != 0 ? ReadBits(block, ref at, precision[c]) : 255;
            }
        }

        Span<int> parity = stackalloc int[6];
        for (int i = 0; i < parityBits; i++)
            parity[i] = ReadBit(block, ref at);

        // A parity bit is the bottom bit of every component of the endpoints that share it, which
        // buys one more bit of precision for the cost of one bit rather than four.
        if (parityBits != 0)
        {
            for (int i = 0; i < endpoints; i++)
            {
                int which = i * parityBits / endpoints;
                for (int c = 0; c < 4; c++)
                {
                    if (precision[c] != withParity[c])
                        colour[(i * 4) + c] = (colour[(i * 4) + c] << 1) | parity[which];
                }
            }
        }

        for (int i = 0; i < endpoints; i++)
        {
            for (int c = 0; c < 4; c++)
                colour[(i * 4) + c] = Expand(colour[(i * 4) + c], withParity[c], c == 3);
        }

        Span<int> first = stackalloc int[16];
        Span<int> second = stackalloc int[16];

        for (int i = 0; i < 16; i++)
        {
            int bits = IsAnchor(groups, shape, i) ? indexBits - 1 : indexBits;
            if (at + bits > 128)
            {
                pixels.Clear();
                return;
            }

            first[i] = ReadBits(block, ref at, bits);
        }

        if (alphaIndexBits != 0)
        {
            for (int i = 0; i < 16; i++)
            {
                int bits = i != 0 ? alphaIndexBits : alphaIndexBits - 1;
                if (at + bits > 128)
                {
                    pixels.Clear();
                    return;
                }

                second[i] = ReadBits(block, ref at, bits);
            }
        }

        for (int i = 0; i < 16; i++)
        {
            int group = BlockTables.Partitions[(((groups * 64) + shape) * 16) + i];
            int low = (group << 1) * 4;
            int high = ((group << 1) + 1) * 4;

            int colourIndex;
            int colourBits;
            int alphaIndex;
            int alphaBits;

            if (alphaIndexBits == 0)
            {
                colourIndex = alphaIndex = first[i];
                colourBits = alphaBits = indexBits;
            }
            else if (selector == 0)
            {
                colourIndex = first[i];
                colourBits = indexBits;
                alphaIndex = second[i];
                alphaBits = alphaIndexBits;
            }
            else
            {
                colourIndex = second[i];
                colourBits = alphaIndexBits;
                alphaIndex = first[i];
                alphaBits = indexBits;
            }

            int r = Mix(colour[low], colour[high], colourIndex, colourBits);
            int g = Mix(colour[low + 1], colour[high + 1], colourIndex, colourBits);
            int b = Mix(colour[low + 2], colour[high + 2], colourIndex, colourBits);
            int a = Mix(colour[low + 3], colour[high + 3], alphaIndex, alphaBits);

            // A rotation puts one colour channel where alpha was, so that the extra indices the
            // mode carries for alpha can be spent on whichever channel holds the detail.
            switch (rotation)
            {
                case 1:
                    (r, a) = (a, r);
                    break;

                case 2:
                    (g, a) = (a, g);
                    break;

                case 3:
                    (b, a) = (a, b);
                    break;
            }

            pixels[i] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        }
    }

    /// <summary>Whether this pixel's index is the one written a bit short.</summary>
    private static bool IsAnchor(int groups, int shape, int offset)
    {
        for (int p = 0; p <= groups; p++)
        {
            if (BlockTables.FixUps[(((groups * 64) + shape) * 3) + p] == offset)
                return true;
        }

        return false;
    }

    /// <summary>Stretches a value of the given width to a whole byte by repeating its top bits.</summary>
    private static int Expand(int value, int bits, bool alpha)
    {
        if (bits == 0)
            return alpha ? 255 : 0;

        int shifted = (value << (8 - bits)) & 0xFF;
        return shifted | (shifted >> bits);
    }

    private static int Mix(int low, int high, int index, int bits)
    {
        ReadOnlySpan<byte> weights = bits switch
        {
            2 => BlockTables.Weights2,
            3 => BlockTables.Weights3,
            _ => BlockTables.Weights4,
        };

        int weight = weights[index];
        return ((low * (WeightMax - weight)) + (high * weight) + WeightRound) >> WeightShift;
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
