// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders;

/// <summary>
/// Stretches a channel narrower than a byte to a full one, by the division the format is defined
/// by rather than by repeating the field's bits. The shortcut is exact only where the width
/// divides eight, and at five bits reads a step low on nearly half the values.
/// </summary>
internal static class UnormScale
{
    private static readonly byte[][] Tables = Build();

    /// <summary>Stands in for a channel the file does not have, which reads as nothing.</summary>
    public static byte[] Absent { get; } = [0];

    /// <summary>The table for a field of the given width, from one bit through eight.</summary>
    public static byte[] Table(int bits) => Tables[Math.Clamp(bits, 1, 8)];

    private static byte[][] Build()
    {
        byte[][] tables = new byte[9][];
        for (int bits = 1; bits <= 8; bits++)
        {
            int max = (1 << bits) - 1;
            byte[] table = new byte[max + 1];
            for (int value = 0; value <= max; value++)
                table[value] = (byte)(((value * 255) + (max / 2)) / max);

            tables[bits] = table;
        }

        tables[0] = tables[1];
        return tables;
    }
}
