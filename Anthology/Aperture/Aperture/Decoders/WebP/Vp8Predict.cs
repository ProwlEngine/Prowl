// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// Guessing a block from the pixels above and to the left, and the transform that puts the
/// difference back. The guess is one of a fixed set: repeat the row above, repeat the column left,
/// average them, or run a gradient at one of six angles.
/// </summary>
internal static class Vp8Predict
{
    /// <summary>Row length of the scratch block the predictors work in.</summary>
    public const int Stride = 32;

    private static byte Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

    private static byte Average3(int a, int b, int c) => (byte)((a + (2 * b) + c + 2) >> 2);

    private static byte Average2(int a, int b) => (byte)((a + b + 1) >> 1);

    /// <summary>
    /// The seven ways a whole block may be guessed. The order is the format's own: the average of
    /// the edges, the gradient, the row above, the column left, and then the three averages a
    /// block missing one or both of its edges falls back to.
    /// </summary>
    public static void Luma16(Span<byte> block, int at, int mode)
    {
        switch (mode)
        {
            case 0:
                Fill(block, at, 16, Dc(block, at, 16, true, true));
                break;

            case 1:
                TrueMotion(block, at, 16);
                break;

            case 2:
                for (int y = 0; y < 16; y++)
                    block.Slice(at - Stride, 16).CopyTo(block[(at + (y * Stride))..]);

                break;

            case 3:
                for (int y = 0; y < 16; y++)
                    block.Slice(at + (y * Stride), 16).Fill(block[at + (y * Stride) - 1]);

                break;

            case 4:
                Fill(block, at, 16, Dc(block, at, 16, false, true));
                break;

            case 5:
                Fill(block, at, 16, Dc(block, at, 16, true, false));
                break;

            default:
                Fill(block, at, 16, 0x80);
                break;
        }
    }

    /// <summary>The same seven, over the eight by eight block a chroma plane is coded in.</summary>
    public static void Chroma8(Span<byte> block, int at, int mode)
    {
        switch (mode)
        {
            case 0:
                Fill(block, at, 8, Dc(block, at, 8, true, true));
                break;

            case 1:
                TrueMotion(block, at, 8);
                break;

            case 2:
                for (int y = 0; y < 8; y++)
                    block.Slice(at - Stride, 8).CopyTo(block[(at + (y * Stride))..]);

                break;

            case 3:
                for (int y = 0; y < 8; y++)
                    block.Slice(at + (y * Stride), 8).Fill(block[at + (y * Stride) - 1]);

                break;

            case 4:
                Fill(block, at, 8, Dc(block, at, 8, false, true));
                break;

            case 5:
                Fill(block, at, 8, Dc(block, at, 8, true, false));
                break;

            default:
                Fill(block, at, 8, 0x80);
                break;
        }
    }

    /// <summary>
    /// The average of the edges, with the rounding and the shift depending on how many of them
    /// are actually there. A block at the top or left edge of the picture has fewer.
    /// </summary>
    private static byte Dc(ReadOnlySpan<byte> block, int at, int size, bool top, bool left)
    {
        int shift = size == 16 ? 4 : 3;
        int total = 0;
        int count = 0;

        if (top)
        {
            for (int x = 0; x < size; x++)
                total += block[at - Stride + x];

            count++;
        }

        if (left)
        {
            for (int y = 0; y < size; y++)
                total += block[at + (y * Stride) - 1];

            count++;
        }

        int bits = shift + count - 1;
        return (byte)((total + (1 << (bits - 1))) >> bits);
    }

    private static void Fill(Span<byte> block, int at, int size, byte value)
    {
        for (int y = 0; y < size; y++)
            block.Slice(at + (y * Stride), size).Fill(value);
    }

    /// <summary>
    /// The gradient guess, which takes the row above and shifts it by how much the pixel to the
    /// left differs from the one above it. It follows a smooth ramp in either direction.
    /// </summary>
    private static void TrueMotion(Span<byte> block, int at, int size)
    {
        int corner = block[at - Stride - 1];

        for (int y = 0; y < size; y++)
        {
            int left = block[at + (y * Stride) - 1];
            for (int x = 0; x < size; x++)
                block[at + (y * Stride) + x] = Clip(left + block[at - Stride + x] - corner);
        }
    }

    /// <summary>The ten ways a four by four block may be guessed.</summary>
    public static void Luma4(Span<byte> block, int at, int mode)
    {
        int top = at - Stride;

        int a = block[top];
        int b = block[top + 1];
        int c = block[top + 2];
        int d = block[top + 3];
        int e = block[top + 4];
        int f = block[top + 5];
        int g = block[top + 6];
        int h = block[top + 7];
        int x = block[top - 1];
        int i = block[at - 1];
        int j = block[at + Stride - 1];
        int k = block[at + (2 * Stride) - 1];
        int l = block[at + (3 * Stride) - 1];

        switch (mode)
        {
            case 0:
            {
                int total = 4;
                for (int n = 0; n < 4; n++)
                    total += block[top + n] + block[at + (n * Stride) - 1];

                Fill(block, at, 4, (byte)(total >> 3));
                break;
            }

            case 1:
                TrueMotion(block, at, 4);
                break;

            case 2:
            {
                Span<byte> row = [Average3(x, a, b), Average3(a, b, c), Average3(b, c, d), Average3(c, d, e)];
                for (int n = 0; n < 4; n++)
                    row.CopyTo(block[(at + (n * Stride))..]);

                break;
            }

            case 3:
                block.Slice(at, 4).Fill(Average3(x, i, j));
                block.Slice(at + Stride, 4).Fill(Average3(i, j, k));
                block.Slice(at + (2 * Stride), 4).Fill(Average3(j, k, l));
                block.Slice(at + (3 * Stride), 4).Fill(Average3(k, l, l));
                break;

            case 4:
                Set(block, at, 0, 3, Average3(j, k, l));
                Set(block, at, 1, 3, Average3(i, j, k));
                Set(block, at, 0, 2, Average3(i, j, k));
                Set(block, at, 2, 3, Average3(x, i, j));
                Set(block, at, 1, 2, Average3(x, i, j));
                Set(block, at, 0, 1, Average3(x, i, j));
                Set(block, at, 3, 3, Average3(a, x, i));
                Set(block, at, 2, 2, Average3(a, x, i));
                Set(block, at, 1, 1, Average3(a, x, i));
                Set(block, at, 0, 0, Average3(a, x, i));
                Set(block, at, 3, 2, Average3(b, a, x));
                Set(block, at, 2, 1, Average3(b, a, x));
                Set(block, at, 1, 0, Average3(b, a, x));
                Set(block, at, 3, 1, Average3(c, b, a));
                Set(block, at, 2, 0, Average3(c, b, a));
                Set(block, at, 3, 0, Average3(d, c, b));
                break;

            case 5:
                Set(block, at, 0, 0, Average2(x, a));
                Set(block, at, 1, 2, Average2(x, a));
                Set(block, at, 1, 0, Average2(a, b));
                Set(block, at, 2, 2, Average2(a, b));
                Set(block, at, 2, 0, Average2(b, c));
                Set(block, at, 3, 2, Average2(b, c));
                Set(block, at, 3, 0, Average2(c, d));
                Set(block, at, 0, 3, Average3(k, j, i));
                Set(block, at, 0, 2, Average3(j, i, x));
                Set(block, at, 0, 1, Average3(i, x, a));
                Set(block, at, 1, 3, Average3(i, x, a));
                Set(block, at, 1, 1, Average3(x, a, b));
                Set(block, at, 2, 3, Average3(x, a, b));
                Set(block, at, 2, 1, Average3(a, b, c));
                Set(block, at, 3, 3, Average3(a, b, c));
                Set(block, at, 3, 1, Average3(b, c, d));
                break;

            case 6:
                Set(block, at, 0, 0, Average3(a, b, c));
                Set(block, at, 1, 0, Average3(b, c, d));
                Set(block, at, 0, 1, Average3(b, c, d));
                Set(block, at, 2, 0, Average3(c, d, e));
                Set(block, at, 1, 1, Average3(c, d, e));
                Set(block, at, 0, 2, Average3(c, d, e));
                Set(block, at, 3, 0, Average3(d, e, f));
                Set(block, at, 2, 1, Average3(d, e, f));
                Set(block, at, 1, 2, Average3(d, e, f));
                Set(block, at, 0, 3, Average3(d, e, f));
                Set(block, at, 3, 1, Average3(e, f, g));
                Set(block, at, 2, 2, Average3(e, f, g));
                Set(block, at, 1, 3, Average3(e, f, g));
                Set(block, at, 3, 2, Average3(f, g, h));
                Set(block, at, 2, 3, Average3(f, g, h));
                Set(block, at, 3, 3, Average3(g, h, h));
                break;

            case 7:
                Set(block, at, 0, 0, Average2(a, b));
                Set(block, at, 1, 0, Average2(b, c));
                Set(block, at, 0, 2, Average2(b, c));
                Set(block, at, 2, 0, Average2(c, d));
                Set(block, at, 1, 2, Average2(c, d));
                Set(block, at, 3, 0, Average2(d, e));
                Set(block, at, 2, 2, Average2(d, e));
                Set(block, at, 0, 1, Average3(a, b, c));
                Set(block, at, 1, 1, Average3(b, c, d));
                Set(block, at, 0, 3, Average3(b, c, d));
                Set(block, at, 2, 1, Average3(c, d, e));
                Set(block, at, 1, 3, Average3(c, d, e));
                Set(block, at, 3, 1, Average3(d, e, f));
                Set(block, at, 2, 3, Average3(d, e, f));
                Set(block, at, 3, 2, Average3(e, f, g));
                Set(block, at, 3, 3, Average3(f, g, h));
                break;

            case 8:
                Set(block, at, 0, 0, Average2(i, x));
                Set(block, at, 2, 1, Average2(i, x));
                Set(block, at, 0, 1, Average2(j, i));
                Set(block, at, 2, 2, Average2(j, i));
                Set(block, at, 0, 2, Average2(k, j));
                Set(block, at, 2, 3, Average2(k, j));
                Set(block, at, 0, 3, Average2(l, k));
                Set(block, at, 3, 0, Average3(a, b, c));
                Set(block, at, 2, 0, Average3(x, a, b));
                Set(block, at, 1, 0, Average3(i, x, a));
                Set(block, at, 3, 1, Average3(i, x, a));
                Set(block, at, 1, 1, Average3(j, i, x));
                Set(block, at, 3, 2, Average3(j, i, x));
                Set(block, at, 1, 2, Average3(k, j, i));
                Set(block, at, 3, 3, Average3(k, j, i));
                Set(block, at, 1, 3, Average3(l, k, j));
                break;

            default:
                Set(block, at, 0, 0, Average2(i, j));
                Set(block, at, 2, 0, Average2(j, k));
                Set(block, at, 0, 1, Average2(j, k));
                Set(block, at, 2, 1, Average2(k, l));
                Set(block, at, 0, 2, Average2(k, l));
                Set(block, at, 1, 0, Average3(i, j, k));
                Set(block, at, 3, 0, Average3(j, k, l));
                Set(block, at, 1, 1, Average3(j, k, l));
                Set(block, at, 3, 1, Average3(k, l, l));
                Set(block, at, 1, 2, Average3(k, l, l));
                Set(block, at, 3, 2, (byte)l);
                Set(block, at, 2, 2, (byte)l);
                Set(block, at, 0, 3, (byte)l);
                Set(block, at, 1, 3, (byte)l);
                Set(block, at, 2, 3, (byte)l);
                Set(block, at, 3, 3, (byte)l);
                break;
        }
    }

    private static void Set(Span<byte> block, int at, int x, int y, byte value) =>
        block[at + x + (y * Stride)] = value;
}
