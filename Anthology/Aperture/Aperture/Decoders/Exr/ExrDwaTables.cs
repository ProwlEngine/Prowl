// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The curve the lossy compressions quantise along, and the one that undoes it. Below one it is a
/// power of about a fifth, and above one a logarithm joined smoothly at one, since a power
/// function runs away. A half takes 65,536 values, so it is a table, built on first use.
/// </summary>
internal static class ExrDwaTables
{
    private static ushort[]? _toLinear;

    /// <summary>Turns a value back from the curve it was quantised along.</summary>
    public static ushort[] ToLinear => _toLinear ??= Build();

    private static ushort[] Build()
    {
        ushort[] table = new ushort[65536];

        for (int i = 0; i < table.Length; i++)
        {
            ushort bits = (ushort)i;

            // Nothing, and anything that is not a number, stay as they are.
            if (bits == 0 || (bits & 0x7C00) == 0x7C00)
            {
                table[i] = 0;
                continue;
            }

            double value = (double)BitConverter.UInt16BitsToHalf(bits);
            double sign = value < 0.0 ? -1.0 : 1.0;
            value = Math.Abs(value);

            double baseValue;
            double exponent;

            if (value <= 1.0)
            {
                baseValue = value;
                exponent = 2.2;
            }
            else
            {
                // The number whose logarithm the curve above one is taken against.
                baseValue = 9.02501329156;
                exponent = value - 1.0;
            }

            table[i] = BitConverter.HalfToUInt16Bits((Half)(float)(sign * Math.Pow(baseValue, exponent)));
        }

        return table;
    }
}
