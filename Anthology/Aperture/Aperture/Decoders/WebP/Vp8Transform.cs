// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Decoders.WebP;

/// <summary>
/// The inverse transforms, which turn coefficients back into the correction added to a guess. An
/// integer approximation of a cosine transform chosen so every decoder gets the same answer, with
/// a shortcut for the flat block that most blocks are.
/// </summary>
internal static class Vp8Transform
{
    private const int Stride = Vp8Predict.Stride;

    private const int C1 = 20091;
    private const int C2 = 35468;

    private static int Mul1(int a) => ((a * C1) >> 16) + a;

    private static int Mul2(int a) => (a * C2) >> 16;

    private static byte Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

    private static void Store(Span<byte> block, int at, int x, int y, int value)
    {
        int to = at + x + (y * Stride);
        block[to] = Clip(block[to] + (value >> 3));
    }

    public static void One(ReadOnlySpan<short> input, int from, Span<byte> block, int at)
    {
        Span<int> scratch = stackalloc int[16];

        for (int i = 0; i < 4; i++)
        {
            int a = input[from + i] + input[from + i + 8];
            int b = input[from + i] - input[from + i + 8];
            int c = Mul2(input[from + i + 4]) - Mul1(input[from + i + 12]);
            int d = Mul1(input[from + i + 4]) + Mul2(input[from + i + 12]);

            scratch[i * 4] = a + d;
            scratch[(i * 4) + 1] = b + c;
            scratch[(i * 4) + 2] = b - c;
            scratch[(i * 4) + 3] = a - d;
        }

        for (int i = 0; i < 4; i++)
        {
            int dc = scratch[i] + 4;
            int a = dc + scratch[i + 8];
            int b = dc - scratch[i + 8];
            int c = Mul2(scratch[i + 4]) - Mul1(scratch[i + 12]);
            int d = Mul1(scratch[i + 4]) + Mul2(scratch[i + 12]);

            Store(block, at, 0, i, a + d);
            Store(block, at, 1, i, b + c);
            Store(block, at, 2, i, b - c);
            Store(block, at, 3, i, a - d);
        }
    }

    /// <summary>A block with nothing but a flat value, which needs no transform at all.</summary>
    public static void Dc(ReadOnlySpan<short> input, int from, Span<byte> block, int at)
    {
        int value = input[from] + 4;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
                Store(block, at, x, y, value);
        }
    }

    /// <summary>A block with only the flat value and the two lowest frequencies.</summary>
    public static void Ac3(ReadOnlySpan<short> input, int from, Span<byte> block, int at)
    {
        int a = input[from] + 4;
        int c4 = Mul2(input[from + 4]);
        int d4 = Mul1(input[from + 4]);
        int c1 = Mul2(input[from + 1]);
        int d1 = Mul1(input[from + 1]);

        Store2(block, at, 0, a + d4, d1, c1);
        Store2(block, at, 1, a + c4, d1, c1);
        Store2(block, at, 2, a - c4, d1, c1);
        Store2(block, at, 3, a - d4, d1, c1);
    }

    private static void Store2(Span<byte> block, int at, int y, int dc, int d, int c)
    {
        Store(block, at, 0, y, dc + d);
        Store(block, at, 1, y, dc + c);
        Store(block, at, 2, y, dc - c);
        Store(block, at, 3, y, dc - d);
    }

    /// <summary>
    /// The second transform a whole block carries, which codes the sixteen flat values of its
    /// four by four blocks together because they are far more alike than the rest.
    /// </summary>
    public static void Walsh(ReadOnlySpan<short> input, Span<short> output, int to)
    {
        Span<int> scratch = stackalloc int[16];

        for (int i = 0; i < 4; i++)
        {
            int a0 = input[i] + input[12 + i];
            int a1 = input[4 + i] + input[8 + i];
            int a2 = input[4 + i] - input[8 + i];
            int a3 = input[i] - input[12 + i];

            scratch[i] = a0 + a1;
            scratch[8 + i] = a0 - a1;
            scratch[4 + i] = a3 + a2;
            scratch[12 + i] = a3 - a2;
        }

        for (int i = 0; i < 4; i++)
        {
            int dc = scratch[i * 4] + 3;
            int a0 = dc + scratch[(i * 4) + 3];
            int a1 = scratch[(i * 4) + 1] + scratch[(i * 4) + 2];
            int a2 = scratch[(i * 4) + 1] - scratch[(i * 4) + 2];
            int a3 = dc - scratch[(i * 4) + 3];

            output[to] = (short)((a0 + a1) >> 3);
            output[to + 16] = (short)((a3 + a2) >> 3);
            output[to + 32] = (short)((a0 - a1) >> 3);
            output[to + 48] = (short)((a3 - a2) >> 3);
            to += 64;
        }
    }
}
