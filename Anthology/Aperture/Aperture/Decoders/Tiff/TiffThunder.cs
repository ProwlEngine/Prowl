// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// A four bit greyscale compression from a scanner that predates most of the format. Every byte is
/// a two bit code and six bits of payload: repeat the last pixel, three pixels each a small step
/// from the one before, two pixels each a larger step, or one pixel outright.
/// </summary>
internal static class TiffThunder
{
    private static ReadOnlySpan<sbyte> SmallSteps => [0, 1, 0, -1];

    private static ReadOnlySpan<sbyte> LargeSteps => [0, 1, 2, 3, 0, -3, -2, -1];

    private const int SmallSkip = 2;
    private const int LargeSkip = 4;

    public static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, int width,
                                 int rows, int rowBytes)
    {
        int at = 0;

        for (int y = 0; y < rows; y++)
        {
            Span<byte> row = destination.Slice(y * rowBytes, rowBytes);
            row.Clear();

            int written = 0;
            int last = 0;

            while (at < source.Length && written < width)
            {
                int code = source[at++];

                switch (code & 0xC0)
                {
                    case 0x00:
                    {
                        int run = code & 0x3F;
                        while (run-- > 0 && written < width)
                            Put(row, ref written, ref last, last);

                        break;
                    }

                    case 0x40:
                        for (int shift = 4; shift >= 0; shift -= 2)
                        {
                            int delta = (code >> shift) & 3;
                            if (delta != SmallSkip)
                                Put(row, ref written, ref last, last + SmallSteps[delta]);
                        }

                        break;

                    case 0x80:
                        for (int shift = 3; shift >= 0; shift -= 3)
                        {
                            int delta = (code >> shift) & 7;
                            if (delta != LargeSkip)
                                Put(row, ref written, ref last, last + LargeSteps[delta]);
                        }

                        break;

                    default:
                        Put(row, ref written, ref last, code & 0x0F);
                        break;
                }
            }

            if (written == 0 && y > 0)
                return true;
        }

        return true;
    }

    /// <summary>Writes one four bit pixel, two to a byte with the first in the high half.</summary>
    private static void Put(Span<byte> row, ref int written, ref int last, int value)
    {
        last = value & 0x0F;

        int at = written >> 1;
        if (at >= row.Length)
            return;

        if ((written & 1) != 0)
            row[at] |= (byte)last;
        else
            row[at] = (byte)(last << 4);

        written++;
    }
}
