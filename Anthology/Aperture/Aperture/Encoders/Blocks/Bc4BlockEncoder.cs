// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>
/// BC4 for one channel, or BC5 for two side by side: two bytes and a three bit index per pixel.
/// Both the eight step and the six step with exact black and white modes are searched around the
/// block's range.
/// </summary>
internal sealed class Bc4BlockEncoder(TextureEncodeQuality quality, int channels, int firstChannel = 0) : IBlockEncoder
{
    private readonly int _radius = quality switch
    {
        TextureEncodeQuality.Fast => 0,
        TextureEncodeQuality.Balanced => 2,
        _ => 5,
    };

    public object CreateScratch() => new byte[16];

    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        byte[] values = (byte[])scratch;

        for (int channel = 0; channel < channels; channel++)
        {
            for (int i = 0; i < 16; i++)
                values[i] = pixels[(i * 4) + firstChannel + channel];

            EncodeChannel(values, output.Slice(channel * 8, 8));
        }
    }

    private void EncodeChannel(byte[] values, Span<byte> output)
    {
        int min = 255, max = 0, innerMin = 255, innerMax = 0;
        for (int i = 0; i < 16; i++)
        {
            int v = values[i];
            min = Math.Min(min, v);
            max = Math.Max(max, v);

            if (v != 0 && v != 255)
            {
                innerMin = Math.Min(innerMin, v);
                innerMax = Math.Max(innerMax, v);
            }
        }

        int bestError = int.MaxValue;
        int bestFirst = min, bestSecond = min;

        if (min == max)
        {
            Write(output, min, min, 0);
            return;
        }

        Span<int> palette = stackalloc int[8];

        // Eight interpolated steps, which need the first endpoint above the second.
        for (int a = max - _radius; a <= max + _radius; a++)
        {
            for (int b = min - _radius; b <= min + _radius; b++)
            {
                if (a < 0 || a > 255 || b < 0 || b > 255 || a <= b)
                    continue;

                Try(values, palette, a, b, ref bestError, ref bestFirst, ref bestSecond);
            }
        }

        // Six steps plus exact black and white, for blocks reaching either extreme.
        if (innerMin <= innerMax && (min == 0 || max == 255))
        {
            for (int a = innerMin - _radius; a <= innerMin + _radius; a++)
            {
                for (int b = innerMax - _radius; b <= innerMax + _radius; b++)
                {
                    if (a < 0 || a > 255 || b < 0 || b > 255 || a > b)
                        continue;

                    Try(values, palette, a, b, ref bestError, ref bestFirst, ref bestSecond);
                }
            }
        }

        Fill(palette, bestFirst, bestSecond);
        ulong indices = 0;
        for (int i = 0; i < 16; i++)
            indices |= (ulong)Nearest(palette, values[i], out _) << (i * 3);

        Write(output, bestFirst, bestSecond, indices);
    }

    private static void Try(byte[] values, Span<int> palette, int a, int b, ref int bestError, ref int bestFirst, ref int bestSecond)
    {
        Fill(palette, a, b);
        int error = 0;
        for (int i = 0; i < 16 && error < bestError; i++)
        {
            Nearest(palette, values[i], out int d);
            error += d;
        }

        if (error < bestError)
        {
            bestError = error;
            bestFirst = a;
            bestSecond = b;
        }
    }

    private static int Nearest(ReadOnlySpan<int> palette, int value, out int error)
    {
        int best = 0;
        error = int.MaxValue;
        for (int e = 0; e < 8; e++)
        {
            int d = palette[e] - value;
            d *= d;
            if (d < error)
            {
                error = d;
                best = e;
            }
        }

        return best;
    }

    /// <summary>The eight levels as the decoder rounds them.</summary>
    private static void Fill(Span<int> palette, int a, int b)
    {
        palette[0] = a;
        palette[1] = b;

        if (a > b)
        {
            for (int i = 1; i < 7; i++)
                palette[i + 1] = ((2 * (((7 - i) * a) + (i * b))) + 7) / 14;
        }
        else
        {
            for (int i = 1; i < 5; i++)
                palette[i + 1] = ((2 * (((5 - i) * a) + (i * b))) + 5) / 10;

            palette[6] = 0;
            palette[7] = 255;
        }
    }

    private static void Write(Span<byte> output, int first, int second, ulong indices)
    {
        output[0] = (byte)first;
        output[1] = (byte)second;
        for (int i = 0; i < 6; i++)
            output[2 + i] = (byte)(indices >> (i * 8));
    }
}
