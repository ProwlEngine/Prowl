// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Tiff;

/// <summary>
/// Turning one measurement a pixel into three, by averaging the neighbours of each missing colour.
/// That is the plainest of the many ways to do it, and what a viewer does rather than what a raw
/// converter does.
/// </summary>
internal static class TiffCfa
{
    /// <summary>Fills the three channels of one row from the single measurement each site holds.</summary>
    public static void Interpolate(TiffImage image, ReadOnlySpan<int> plane, int width, int height,
                                   int y, Span<int> row)
    {
        ReadOnlySpan<byte> pattern = image.CfaPattern;
        int across = image.CfaAcross;
        int down = image.CfaDown;

        for (int x = 0; x < width; x++)
        {
            int own = pattern[((y % down) * across) + (x % across)];

            for (int c = 0; c < 3; c++)
            {
                // What the site measured itself stands as it is. Only the two colours it was
                // blind to are worked out from around it.
                if (c == own)
                {
                    row[(x * 3) + c] = plane[(y * width) + x];
                    continue;
                }

                int total = 0;
                int count = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if ((uint)ny >= (uint)height)
                        continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        if ((uint)nx >= (uint)width)
                            continue;

                        if (pattern[((ny % down) * across) + (nx % across)] != c)
                            continue;

                        total += plane[(ny * width) + nx];
                        count++;
                    }
                }

                row[(x * 3) + c] = count > 0 ? total / count : 0;
            }
        }
    }
}
