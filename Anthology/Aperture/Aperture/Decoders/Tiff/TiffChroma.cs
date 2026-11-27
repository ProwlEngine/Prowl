// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// Spreading a chroma pair that several pixels share back out to one a pixel. A file that
/// subsamples stores units rather than rows: the brightness samples of a small rectangle, then the
/// colour pair they share, so the data is walked as a grid and written back out as rows.
/// </summary>
internal static class TiffChroma
{
    /// <summary>Bytes a band of rows occupies while still in unit order.</summary>
    public static long PackedBytes(TiffImage image, int width, int rows)
    {
        long across = (width + image.ChromaAcross - 1) / image.ChromaAcross;
        long down = (rows + image.ChromaDown - 1) / image.ChromaDown;
        long unit = ((long)image.ChromaAcross * image.ChromaDown) + 2;

        return across * down * unit;
    }

    /// <summary>Turns unit ordered data into the row ordered samples the rest of the reader wants.</summary>
    public static bool TryExpand(ReadOnlySpan<byte> packed, Span<byte> destination, TiffImage image,
                                 int width, int rows, int rowBytes)
    {
        int across = image.ChromaAcross;
        int down = image.ChromaDown;
        int perUnit = (across * down) + 2;

        int units = (width + across - 1) / across;
        int bands = (rows + down - 1) / down;

        if ((long)units * bands * perUnit > packed.Length)
            return false;

        destination.Clear();

        for (int band = 0; band < bands; band++)
        {
            for (int unit = 0; unit < units; unit++)
            {
                int from = ((band * units) + unit) * perUnit;
                byte cb = packed[from + (across * down)];
                byte cr = packed[from + (across * down) + 1];

                for (int y = 0; y < down; y++)
                {
                    int row = (band * down) + y;
                    if (row >= rows)
                        break;

                    for (int x = 0; x < across; x++)
                    {
                        int column = (unit * across) + x;
                        if (column >= width)
                            break;

                        int at = (row * rowBytes) + (column * 3);
                        destination[at] = packed[from + (y * across) + x];
                        destination[at + 1] = cb;
                        destination[at + 2] = cr;
                    }
                }
            }
        }

        return true;
    }
}
