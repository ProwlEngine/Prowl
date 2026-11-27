// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// The separate alpha plane a lossy picture carries, since the lossy codec has none of its own. It
/// is coded either as plain bytes or as a lossless picture whose green channel holds the alpha,
/// and either way usually stored as differences from its neighbours first.
/// </summary>
internal static class WebPAlpha
{
    public static bool TryDecode(ReadOnlySpan<byte> data, int width, int height, out byte[]? alpha)
    {
        alpha = null;

        if (data.Length < 1 || width <= 0 || height <= 0)
            return false;

        int flags = data[0];
        int method = flags & 3;
        int filter = (flags >> 2) & 3;
        int processing = (flags >> 4) & 3;

        if (method > 1 || processing > 1 || (flags >> 6) != 0)
            return false;

        long total = (long)width * height;
        if (total > int.MaxValue)
            return false;

        byte[] plane = new byte[(int)total];
        ReadOnlySpan<byte> body = data[1..];

        if (method == 0)
        {
            if (body.Length < plane.Length)
                return false;

            body[..plane.Length].CopyTo(plane);
        }
        else
        {
            if (!Vp8LDecoder.TryDecodeAlpha(body, width, height, out uint[]? pixels))
                return false;

            // The alpha travels in the green channel, which is the one the lossless coder gives
            // its own prediction to rather than deriving from another.
            for (int i = 0; i < plane.Length; i++)
                plane[i] = (byte)(pixels![i] >> 8);
        }

        Unfilter(plane, width, height, filter);

        alpha = plane;
        return true;
    }

    /// <summary>Turns each byte back from a difference into the value it stands for.</summary>
    private static void Unfilter(byte[] plane, int width, int height, int filter)
    {
        if (filter == 0)
            return;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int above = row - width;

            switch (filter)
            {
                case 1:
                    Horizontal(plane, row, y == 0 ? -1 : above, width);
                    break;

                case 2:
                    if (y == 0)
                        Horizontal(plane, row, -1, width);
                    else
                    {
                        for (int x = 0; x < width; x++)
                            plane[row + x] = (byte)(plane[above + x] + plane[row + x]);
                    }

                    break;

                default:
                    if (y == 0)
                    {
                        Horizontal(plane, row, -1, width);
                        break;
                    }

                    int left = plane[above];
                    int topLeft = left;

                    for (int x = 0; x < width; x++)
                    {
                        int top = plane[above + x];
                        left = (byte)(plane[row + x] + Gradient(left, top, topLeft));
                        topLeft = top;
                        plane[row + x] = (byte)left;
                    }

                    break;
            }
        }
    }

    private static void Horizontal(byte[] plane, int row, int above, int width)
    {
        int running = above < 0 ? 0 : plane[above];
        for (int x = 0; x < width; x++)
        {
            plane[row + x] = (byte)(running + plane[row + x]);
            running = plane[row + x];
        }
    }

    /// <summary>The guess a gradient makes, which is the two neighbours less the corner.</summary>
    private static int Gradient(int left, int top, int topLeft)
    {
        int value = left + top - topLeft;
        return (value & ~0xFF) == 0 ? value : value < 0 ? 0 : 255;
    }
}
