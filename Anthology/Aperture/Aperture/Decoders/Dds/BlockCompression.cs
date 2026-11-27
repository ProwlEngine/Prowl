// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Dds;

/// <summary>
/// The block compressed layouts, which store a four by four square in eight or sixteen bytes: two
/// endpoints and a small index per pixel into the line between them. Comparing the endpoints picks
/// the spacing, which is how a block buys a transparent value at the cost of one step of precision.
/// </summary>
internal static class BlockCompression
{
    /// <summary>Expands one colour block into sixteen opaque pixels, packed as RGBA words.</summary>
    public static void DecodeColour(ReadOnlySpan<byte> block, Span<uint> pixels, bool allowTransparent)
    {
        ushort first = BinaryPrimitives.ReadUInt16LittleEndian(block);
        ushort second = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        Span<uint> palette = stackalloc uint[4];
        palette[0] = Blend(first, second, 1, 0);
        palette[1] = Blend(first, second, 0, 1);

        // The order of the two endpoints is itself a bit of information: the smaller one first
        // asks for three colours and a hole rather than four colours.
        if (first > second || !allowTransparent)
        {
            palette[2] = Blend(first, second, 2, 1);
            palette[3] = Blend(first, second, 1, 2);
        }
        else
        {
            palette[2] = Blend(first, second, 1, 1);
            palette[3] = 0;
        }

        for (int i = 0; i < 16; i++)
            pixels[i] = palette[(int)((indices >> (i * 2)) & 3)];
    }

    /// <summary>Expands one interpolated alpha block into sixteen levels.</summary>
    public static void DecodeAlpha(ReadOnlySpan<byte> block, Span<byte> levels, bool signed)
    {
        Span<float> palette = stackalloc float[8];
        int first = signed ? (sbyte)block[0] : block[0];
        int second = signed ? (sbyte)block[1] : block[1];

        palette[0] = first;
        palette[1] = second;

        if (first > second)
        {
            for (int i = 1; i < 7; i++)
                palette[i + 1] = ((((7 - i) * first) + (i * second)) / 7f);
        }
        else
        {
            for (int i = 1; i < 5; i++)
                palette[i + 1] = ((((5 - i) * first) + (i * second)) / 5f);

            palette[6] = signed ? -127 : 0;
            palette[7] = signed ? 127 : 255;
        }

        // Sixteen three bit indices are packed into six bytes, which straddle byte boundaries.
        ulong bits = 0;
        for (int i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (i * 8);

        for (int i = 0; i < 16; i++)
        {
            float value = palette[(int)((bits >> (i * 3)) & 7)];

            // Both of the two most negative encodings stand for minus one, so the range is 127
            // steps either side of zero and a stored zero shows as the middle grey.
            levels[i] = signed
                ? (byte)(((((Math.Max(value, -127f) / 127f) + 1f) * 0.5f) * 255f) + 0.5f)
                : (byte)(value + 0.5f);
        }
    }

    /// <summary>Expands one four bit alpha block, which names every level outright.</summary>
    public static void DecodeSharpAlpha(ReadOnlySpan<byte> block, Span<byte> levels)
    {
        for (int i = 0; i < 16; i++)
        {
            int nibble = (block[i / 2] >> ((i & 1) * 4)) & 15;
            levels[i] = (byte)((nibble << 4) | nibble);
        }
    }

    /// <summary>
    /// A weighted blend of two endpoints, done on the stored five and six bit fields and rounded
    /// to a byte once. Expanding first and blending after rounds twice and reads a step out.
    /// </summary>
    private static uint Blend(ushort first, ushort second, int firstWeight, int secondWeight)
    {
        int total = firstWeight + secondWeight;

        uint r = Resolve((((first >> 11) & 31) * firstWeight) + (((second >> 11) & 31) * secondWeight), 31, total);
        uint g = Resolve((((first >> 5) & 63) * firstWeight) + (((second >> 5) & 63) * secondWeight), 63, total);
        uint b = Resolve(((first & 31) * firstWeight) + ((second & 31) * secondWeight), 31, total);

        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    /// <summary>Turns a weighted sum of field values into the byte the intensity stands for.</summary>
    private static uint Resolve(int weighted, int max, int total)
    {
        int denominator = max * total;
        return (uint)(((weighted * 255) + (denominator / 2)) / denominator);
    }
}
