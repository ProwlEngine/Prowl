// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// Turns a file storing one full size Y and two half size RY and BY back into red, green and blue.
/// The chroma channels hold the difference of red and of blue from the brightness, divided by it,
/// so both come straight back and green follows from the three averaging to that brightness. What
/// each weighs in the average follows from the chromaticities attribute, or the 709 defaults.
/// </summary>
internal static class ExrLuminanceChroma
{
    /// <summary>Whether the channels are a brightness and two colour differences rather than colour.</summary>
    public static bool Describes(ExrHeader header)
    {
        bool luminance = false;
        bool red = false;
        bool blue = false;

        foreach (ExrChannel channel in header.Channels)
        {
            switch (channel.Name)
            {
                case "Y": luminance = true; break;
                case "RY": red = true; break;
                case "BY": blue = true; break;
                case "R" or "G" or "B": return false;
            }
        }

        return luminance && red && blue;
    }

    /// <summary>
    /// Rewrites a surface of brightness and colour differences as one of red, green and blue.
    /// The three arrive interleaved, one float each, in the order the channels were resolved.
    /// </summary>
    public static void ToColour(ExrHeader header, Span<byte> destination, int stride,
                                int width, int height, int channels)
    {
        Weights(header, out float red, out float green, out float blue);

        for (int y = 0; y < height; y++)
        {
            Span<byte> row = destination.Slice(y * stride, width * channels * 4);

            for (int x = 0; x < width; x++)
            {
                int at = x * channels * 4;

                float luminance = BinaryPrimitives.ReadSingleLittleEndian(row[at..]);
                float fromRed = BinaryPrimitives.ReadSingleLittleEndian(row[(at + 4)..]);
                float fromBlue = BinaryPrimitives.ReadSingleLittleEndian(row[(at + 8)..]);

                float r = (fromRed + 1f) * luminance;
                float b = (fromBlue + 1f) * luminance;
                float g = (luminance - (r * red) - (b * blue)) / green;

                BinaryPrimitives.WriteSingleLittleEndian(row[at..], r);
                BinaryPrimitives.WriteSingleLittleEndian(row[(at + 4)..], g);
                BinaryPrimitives.WriteSingleLittleEndian(row[(at + 8)..], b);
            }
        }
    }

    /// <summary>
    /// How much each primary contributes to brightness: a three by three solve scaling the three
    /// so that together they make the white point. The primaries are left as the numbers the file
    /// gives rather than divided through, since one may carry no brightness at all.
    /// </summary>
    private static void Weights(ExrHeader header, out float red, out float green, out float blue)
    {
        float[] c = header.Chromaticities;

        double xr = c[0], yr = c[1], zr = 1.0 - c[0] - c[1];
        double xg = c[2], yg = c[3], zg = 1.0 - c[2] - c[3];
        double xb = c[4], yb = c[5], zb = 1.0 - c[4] - c[5];

        double determinant = (xr * ((yg * zb) - (yb * zg)))
                           - (xg * ((yr * zb) - (yb * zr)))
                           + (xb * ((yr * zg) - (yg * zr)));

        if (c[7] != 0 && Math.Abs(determinant) > 1e-12)
        {
            // White at unit brightness, which is what the three primaries have to add up to.
            double wx = c[6] / c[7];
            double wz = (1.0 - c[6] - c[7]) / c[7];

            double sr = ((wx * ((yg * zb) - (yb * zg))) - (xg * (zb - (yb * wz))) + (xb * (zg - (yg * wz))))
                        / determinant;
            double sg = ((xr * (zb - (yb * wz))) - (wx * ((yr * zb) - (yb * zr))) + (xb * ((yr * wz) - zr)))
                        / determinant;
            double sb = ((xr * ((yg * wz) - zg)) - (xg * ((yr * wz) - zr)) + (wx * ((yr * zg) - (yg * zr))))
                        / determinant;

            double total = (sr * yr) + (sg * yg) + (sb * yb);
            if (total > 0)
            {
                red = (float)(sr * yr / total);
                green = (float)(sg * yg / total);
                blue = (float)(sb * yb / total);
                return;
            }
        }

        red = 0.2126f;
        green = 0.7152f;
        blue = 0.0722f;
    }
}
