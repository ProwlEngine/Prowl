// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.Exr;

/// <summary>
/// The eight by eight transform the lossy compressions are built on, the same one JPEG uses, and
/// the two conversions that go with it.
/// </summary>
internal static class ExrDct
{
    /// <summary>
    /// Where each coefficient of a block sits once the zig zag ordering is undone. The coder
    /// stores them along diagonals so that the ones likely to be zero end up together.
    /// </summary>
    private static ReadOnlySpan<byte> ZigZag =>
    [
        0, 1, 5, 6, 14, 15, 27, 28,
        2, 4, 7, 13, 16, 26, 29, 42,
        3, 8, 12, 17, 25, 30, 41, 43,
        9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    ];

    /// <summary>Fills the constants from the cosines the transform is defined by.</summary>
    static ExrDct()
    {
        const float Pi = 3.14159f;
        Constants[0] = 0.5f * MathF.Cos(Pi / 4.0f);
        Constants[1] = 0.5f * MathF.Cos(Pi / 16.0f);
        Constants[2] = 0.5f * MathF.Cos(Pi / 8.0f);
        Constants[3] = 0.5f * MathF.Cos(3.0f * Pi / 16.0f);
        Constants[4] = 0.5f * MathF.Cos(5.0f * Pi / 16.0f);
        Constants[5] = 0.5f * MathF.Cos(3.0f * Pi / 8.0f);
        Constants[6] = 0.5f * MathF.Cos(7.0f * Pi / 16.0f);
    }

    /// <summary>The seven cosines, in the order the transform below names them.</summary>
    private static readonly float[] Constants = new float[7];

    /// <summary>Undoes the zig zag, turning the stored halves into a block of floats.</summary>
    public static void FromZigZag(ReadOnlySpan<ushort> source, Span<float> destination)
    {
        ReadOnlySpan<byte> order = ZigZag;
        for (int i = 0; i < 64; i++)
            destination[i] = (float)BitConverter.UInt16BitsToHalf(source[order[i]]);
    }

    /// <summary>
    /// The transform, over a block whose lower rows are known to be nothing. Skipping those rows
    /// is not only faster, it is what the coder assumes: a block is stored only up to its last
    /// coefficient that is not zero.
    /// </summary>
    public static void Inverse(Span<float> data, int zeroedRows)
    {
        float a = Constants[0];
        float b = Constants[1];
        float c = Constants[2];
        float d = Constants[3];
        float e = Constants[4];
        float f = Constants[5];
        float g = Constants[6];

        Span<float> alpha = stackalloc float[4];
        Span<float> beta = stackalloc float[4];
        Span<float> theta = stackalloc float[4];
        Span<float> gamma = stackalloc float[4];

        for (int row = 0; row < 8 - zeroedRows; row++)
        {
            Span<float> line = data[(row * 8)..];

            alpha[0] = c * line[2];
            alpha[1] = f * line[2];
            alpha[2] = c * line[6];
            alpha[3] = f * line[6];

            beta[0] = (b * line[1]) + (d * line[3]) + (e * line[5]) + (g * line[7]);
            beta[1] = (d * line[1]) - (g * line[3]) - (b * line[5]) - (e * line[7]);
            beta[2] = (e * line[1]) - (b * line[3]) + (g * line[5]) + (d * line[7]);
            beta[3] = (g * line[1]) - (e * line[3]) + (d * line[5]) - (b * line[7]);

            theta[0] = a * (line[0] + line[4]);
            theta[3] = a * (line[0] - line[4]);
            theta[1] = alpha[0] + alpha[3];
            theta[2] = alpha[1] - alpha[2];

            gamma[0] = theta[0] + theta[1];
            gamma[1] = theta[3] + theta[2];
            gamma[2] = theta[3] - theta[2];
            gamma[3] = theta[0] - theta[1];

            line[0] = gamma[0] + beta[0];
            line[1] = gamma[1] + beta[1];
            line[2] = gamma[2] + beta[2];
            line[3] = gamma[3] + beta[3];
            line[4] = gamma[3] - beta[3];
            line[5] = gamma[2] - beta[2];
            line[6] = gamma[1] - beta[1];
            line[7] = gamma[0] - beta[0];
        }

        for (int column = 0; column < 8; column++)
        {
            alpha[0] = c * data[16 + column];
            alpha[1] = f * data[16 + column];
            alpha[2] = c * data[48 + column];
            alpha[3] = f * data[48 + column];

            beta[0] = (b * data[8 + column]) + (d * data[24 + column]) +
                      (e * data[40 + column]) + (g * data[56 + column]);
            beta[1] = (d * data[8 + column]) - (g * data[24 + column]) -
                      (b * data[40 + column]) - (e * data[56 + column]);
            beta[2] = (e * data[8 + column]) - (b * data[24 + column]) +
                      (g * data[40 + column]) + (d * data[56 + column]);
            beta[3] = (g * data[8 + column]) - (e * data[24 + column]) +
                      (d * data[40 + column]) - (b * data[56 + column]);

            theta[0] = a * (data[column] + data[32 + column]);
            theta[3] = a * (data[column] - data[32 + column]);
            theta[1] = alpha[0] + alpha[3];
            theta[2] = alpha[1] - alpha[2];

            gamma[0] = theta[0] + theta[1];
            gamma[1] = theta[3] + theta[2];
            gamma[2] = theta[3] - theta[2];
            gamma[3] = theta[0] - theta[1];

            data[column] = gamma[0] + beta[0];
            data[8 + column] = gamma[1] + beta[1];
            data[16 + column] = gamma[2] + beta[2];
            data[24 + column] = gamma[3] + beta[3];
            data[32 + column] = gamma[3] - beta[3];
            data[40 + column] = gamma[2] - beta[2];
            data[48 + column] = gamma[1] - beta[1];
            data[56 + column] = gamma[0] - beta[0];
        }
    }

    /// <summary>
    /// A block holding nothing but its flat value. The transform of a block whose only non zero
    /// coefficient is the first is that value spread across all of it, scaled by the constant the
    /// two passes would each have applied.
    /// </summary>
    public static void InverseFlat(Span<float> data)
    {
        data.Fill(data[0] * 3.535536e-01f * 3.535536e-01f);
    }

    /// <summary>
    /// Turns brightness and two colour differences back into red, green and blue, by the weights
    /// the 709 primaries give them.
    /// </summary>
    public static void ColourInverse(Span<float> red, Span<float> green, Span<float> blue, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float y = red[i];
            float cb = green[i];
            float cr = blue[i];

            red[i] = y + (1.5747f * cr);
            green[i] = y - (0.1873f * cb) - (0.4682f * cr);
            blue[i] = y + (1.8556f * cb);
        }
    }
}
