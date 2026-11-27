// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>Compresses one block of RGBA8 pixels, using scratch state owned by the calling thread.</summary>
internal interface IBlockEncoder
{
    object CreateScratch();

    void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch);
}

/// <summary>
/// BC1: two RGB565 endpoints and a two bit index per pixel. Endpoints start on the principal axis
/// and are refined by least squares against their own index choice, trying both endpoint orders
/// so the three colour mode is considered whenever it fits better. The colour half of BC3 always
/// decodes four colours and ignores alpha, which <paramref name="fourColour"/> asks for.
/// </summary>
internal sealed class Bc1BlockEncoder(TextureEncodeQuality quality, bool fourColour = false) : IBlockEncoder
{
    private static readonly byte[] Match5 = BuildSingleColourTable(31);
    private static readonly byte[] Match6 = BuildSingleColourTable(63);

    private readonly int _refinements = quality switch
    {
        TextureEncodeQuality.Fast => 1,
        TextureEncodeQuality.Balanced => 3,
        _ => 6,
    };

    private readonly int _searchRounds = quality switch
    {
        TextureEncodeQuality.Fast => 0,
        TextureEncodeQuality.Balanced => 1,
        _ => 4,
    };

    public object CreateScratch() => new int[16 * 4];

    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        int[] texels = (int[])scratch;
        int transparent = 0;
        int opaque = 0;

        for (int i = 0; i < 16; i++)
        {
            texels[i * 4] = pixels[i * 4];
            texels[(i * 4) + 1] = pixels[(i * 4) + 1];
            texels[(i * 4) + 2] = pixels[(i * 4) + 2];

            if (!fourColour && pixels[(i * 4) + 3] < 128)
                transparent |= 1 << i;
            else
                opaque++;
        }

        if (opaque == 0)
        {
            Write(output, 0, 0, 0xFFFFFFFFu);
            return;
        }

        if (transparent == 0 && TrySingleColour(texels, output))
            return;

        Span<float> mean = stackalloc float[3];
        Span<float> axis = stackalloc float[3];
        PrincipalAxis(texels, transparent, mean, axis);

        float low = float.MaxValue;
        float high = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
                continue;

            float t = ((texels[i * 4] - mean[0]) * axis[0]) + ((texels[(i * 4) + 1] - mean[1]) * axis[1])
                + ((texels[(i * 4) + 2] - mean[2]) * axis[2]);
            low = Math.Min(low, t);
            high = Math.Max(high, t);
        }

        Span<float> start = stackalloc float[3];
        Span<float> end = stackalloc float[3];
        for (int c = 0; c < 3; c++)
        {
            start[c] = mean[c] + (axis[c] * low);
            end[c] = mean[c] + (axis[c] * high);
        }

        int bestError = int.MaxValue;
        ushort bestFirst = 0;
        ushort bestSecond = 0;
        uint bestIndices = 0;

        ushort first = Pack(start);
        ushort second = Pack(end);

        for (int pass = 0; pass <= _refinements; pass++)
        {
            bool improved = TryPair(texels, transparent, first, second, ref bestError, ref bestFirst, ref bestSecond, ref bestIndices)
                | TryPair(texels, transparent, second, first, ref bestError, ref bestFirst, ref bestSecond, ref bestIndices);

            if (pass == _refinements || bestError == 0 || (!improved && pass > 0))
                break;

            if (!Refit(texels, transparent, fourColour || bestFirst > bestSecond, bestIndices, start, end))
                break;

            first = Pack(start);
            second = Pack(end);
        }

        if (_searchRounds > 0)
            LocalSearch(texels, transparent, _searchRounds, ref bestError, ref bestFirst, ref bestSecond, ref bestIndices);

        Write(output, bestFirst, bestSecond, bestIndices);
    }

    /// <summary>Nudges each endpoint channel a step either way while doing so lowers the error.</summary>
    private void LocalSearch(int[] texels, int transparent, int rounds, ref int bestError, ref ushort bestFirst,
                                    ref ushort bestSecond, ref uint bestIndices)
    {
        ReadOnlySpan<int> shifts = [11, 5, 0];
        ReadOnlySpan<int> limits = [31, 63, 31];

        for (int round = 0; round < rounds; round++)
        {
            bool improved = false;

            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                for (int c = 0; c < 3; c++)
                {
                    for (int step = -1; step <= 1; step += 2)
                    {
                        ushort source = endpoint == 0 ? bestFirst : bestSecond;
                        int field = ((source >> shifts[c]) & limits[c]) + step;
                        if (field < 0 || field > limits[c])
                            continue;

                        ushort moved = (ushort)((source & ~(limits[c] << shifts[c])) | (field << shifts[c]));
                        ushort first = endpoint == 0 ? moved : bestFirst;
                        ushort second = endpoint == 0 ? bestSecond : moved;

                        improved |= TryPair(texels, transparent, first, second, ref bestError, ref bestFirst, ref bestSecond, ref bestIndices);
                    }
                }
            }

            if (!improved || bestError == 0)
                return;
        }
    }

    private bool TryPair(int[] texels, int transparent, ushort first, ushort second, ref int bestError,
                         ref ushort bestFirst, ref ushort bestSecond, ref uint bestIndices)
    {
        int error = Evaluate(texels, transparent, first, second, fourColour, bestError, out uint indices);
        if (error >= bestError)
            return false;

        bestError = error;
        bestFirst = first;
        bestSecond = second;
        bestIndices = indices;
        return true;
    }

    /// <summary>Squared error of the best index per pixel for a pair, stopping early once past <paramref name="limit"/>.</summary>
    private static int Evaluate(int[] texels, int transparent, ushort first, ushort second, bool fourColour, int limit, out uint indices)
    {
        indices = 0;
        bool four = fourColour || first > second;
        if (four && transparent != 0)
            return int.MaxValue;

        Span<int> palette = stackalloc int[12];
        Expand(first, palette, 0);
        Expand(second, palette, 3);

        if (four)
        {
            Blend(first, second, 2, 1, palette, 6);
            Blend(first, second, 1, 2, palette, 9);
        }
        else
        {
            Blend(first, second, 1, 1, palette, 6);
        }

        int entries = four ? 4 : 3;
        int total = 0;

        for (int i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
            {
                indices |= 3u << (i * 2);
                continue;
            }

            int r = texels[i * 4];
            int g = texels[(i * 4) + 1];
            int b = texels[(i * 4) + 2];
            int best = int.MaxValue;
            int chosen = 0;

            for (int e = 0; e < entries; e++)
            {
                int dr = palette[e * 3] - r;
                int dg = palette[(e * 3) + 1] - g;
                int db = palette[(e * 3) + 2] - b;
                int d = (dr * dr) + (dg * dg) + (db * db);
                if (d < best)
                {
                    best = d;
                    chosen = e;
                }
            }

            total += best;
            if (total >= limit)
                return int.MaxValue;

            indices |= (uint)chosen << (i * 2);
        }

        return total;
    }

    /// <summary>Least squares endpoints for the index choice of a pair, false when the indices pin nothing.</summary>
    private static bool Refit(int[] texels, int transparent, bool four, uint indices, Span<float> start, Span<float> end)
    {
        float aa = 0, ab = 0, bb = 0;
        Span<float> sa = stackalloc float[3];
        Span<float> sb = stackalloc float[3];

        for (int i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
                continue;

            int index = (int)((indices >> (i * 2)) & 3);
            float t = index switch
            {
                0 => 0f,
                1 => 1f,
                2 => four ? 1f / 3f : 0.5f,
                _ => 2f / 3f,
            };

            float s = 1f - t;
            aa += s * s;
            ab += s * t;
            bb += t * t;

            for (int c = 0; c < 3; c++)
            {
                sa[c] += s * texels[(i * 4) + c];
                sb[c] += t * texels[(i * 4) + c];
            }
        }

        float determinant = (aa * bb) - (ab * ab);
        if (Math.Abs(determinant) < 1e-6f)
            return false;

        float inverse = 1f / determinant;
        for (int c = 0; c < 3; c++)
        {
            start[c] = ((sa[c] * bb) - (sb[c] * ab)) * inverse;
            end[c] = ((sb[c] * aa) - (sa[c] * ab)) * inverse;
        }

        return true;
    }

    private static bool TrySingleColour(int[] texels, Span<byte> output)
    {
        for (int i = 1; i < 16; i++)
        {
            if (texels[i * 4] != texels[0] || texels[(i * 4) + 1] != texels[1] || texels[(i * 4) + 2] != texels[2])
                return false;
        }

        int r = texels[0], g = texels[1], b = texels[2];
        ushort first = (ushort)((Match5[r * 2] << 11) | (Match6[g * 2] << 5) | Match5[b * 2]);
        ushort second = (ushort)((Match5[(r * 2) + 1] << 11) | (Match6[(g * 2) + 1] << 5) | Match5[(b * 2) + 1]);

        // Every pixel sits a third of the way along, so the order only decides which index that is.
        uint indices = first > second ? 0xAAAAAAAAu : first < second ? 0xFFFFFFFFu : 0u;
        if (first < second)
            (first, second) = (second, first);

        if (first == second)
            indices = 0;

        Write(output, first, second, indices);
        return true;
    }

    /// <summary>For each byte, the endpoint fields whose two to one blend decodes nearest to it.</summary>
    private static byte[] BuildSingleColourTable(int max)
    {
        byte[] table = new byte[512];

        for (int value = 0; value < 256; value++)
        {
            int bestError = int.MaxValue;
            for (int a = 0; a <= max; a++)
            {
                for (int b = 0; b <= max; b++)
                {
                    int decoded = Resolve((2 * a) + b, max, 3);
                    int error = (Math.Abs(decoded - value) * 256) + Math.Abs(a - b);
                    if (error < bestError)
                    {
                        bestError = error;
                        table[value * 2] = (byte)a;
                        table[(value * 2) + 1] = (byte)b;
                    }
                }
            }
        }

        return table;
    }

    private static void PrincipalAxis(int[] texels, int transparent, Span<float> mean, Span<float> axis)
    {
        int count = 0;
        for (int i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
                continue;

            mean[0] += texels[i * 4];
            mean[1] += texels[(i * 4) + 1];
            mean[2] += texels[(i * 4) + 2];
            count++;
        }

        mean[0] /= count;
        mean[1] /= count;
        mean[2] /= count;

        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < 16; i++)
        {
            if ((transparent & (1 << i)) != 0)
                continue;

            float x = texels[i * 4] - mean[0];
            float y = texels[(i * 4) + 1] - mean[1];
            float z = texels[(i * 4) + 2] - mean[2];
            xx += x * x;
            xy += x * y;
            xz += x * z;
            yy += y * y;
            yz += y * z;
            zz += z * z;
        }

        PowerIteration(xx, xy, xz, yy, yz, zz, axis);
    }

    internal static void PowerIteration(float xx, float xy, float xz, float yy, float yz, float zz, Span<float> axis)
    {
        float vx = xx >= yy && xx >= zz ? 1 : 0;
        float vy = vx == 0 && yy >= zz ? 1 : 0;
        float vz = vx == 0 && vy == 0 ? 1 : 0;

        // Starting from the row of the largest diagonal entry avoids a start orthogonal to the answer.
        float rx = vx * xx + vy * xy + vz * xz;
        float ry = vx * xy + vy * yy + vz * yz;
        float rz = vx * xz + vy * yz + vz * zz;

        for (int i = 0; i < 8; i++)
        {
            float nx = (rx * xx) + (ry * xy) + (rz * xz);
            float ny = (rx * xy) + (ry * yy) + (rz * yz);
            float nz = (rx * xz) + (ry * yz) + (rz * zz);
            float length = MathF.Max(MathF.Max(MathF.Abs(nx), MathF.Abs(ny)), MathF.Abs(nz));
            if (length < 1e-12f)
                break;

            rx = nx / length;
            ry = ny / length;
            rz = nz / length;
        }

        float norm = MathF.Sqrt((rx * rx) + (ry * ry) + (rz * rz));
        if (norm < 1e-12f)
        {
            axis[0] = axis[1] = axis[2] = 0.57735f;
            return;
        }

        axis[0] = rx / norm;
        axis[1] = ry / norm;
        axis[2] = rz / norm;
    }

    private static ushort Pack(ReadOnlySpan<float> colour)
    {
        int r = Math.Clamp((int)((colour[0] * 31f / 255f) + 0.5f), 0, 31);
        int g = Math.Clamp((int)((colour[1] * 63f / 255f) + 0.5f), 0, 63);
        int b = Math.Clamp((int)((colour[2] * 31f / 255f) + 0.5f), 0, 31);
        return (ushort)((r << 11) | (g << 5) | b);
    }

    private static void Expand(ushort colour, Span<int> palette, int at)
    {
        palette[at] = Resolve((colour >> 11) & 31, 31, 1);
        palette[at + 1] = Resolve((colour >> 5) & 63, 63, 1);
        palette[at + 2] = Resolve(colour & 31, 31, 1);
    }

    private static void Blend(ushort first, ushort second, int firstWeight, int secondWeight, Span<int> palette, int at)
    {
        int total = firstWeight + secondWeight;
        palette[at] = Resolve((((first >> 11) & 31) * firstWeight) + (((second >> 11) & 31) * secondWeight), 31, total);
        palette[at + 1] = Resolve((((first >> 5) & 63) * firstWeight) + (((second >> 5) & 63) * secondWeight), 63, total);
        palette[at + 2] = Resolve(((first & 31) * firstWeight) + ((second & 31) * secondWeight), 31, total);
    }

    private static int Resolve(int weighted, int max, int total)
    {
        int denominator = max * total;
        return ((weighted * 255) + (denominator / 2)) / denominator;
    }

    private static void Write(Span<byte> output, ushort first, ushort second, uint indices)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(output, first);
        BinaryPrimitives.WriteUInt16LittleEndian(output[2..], second);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], indices);
    }
}

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

    private static int Nearest(Span<int> palette, int value, out int error)
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

/// <summary>BC3: a BC4 block for alpha followed by a four colour BC1 block for colour.</summary>
internal sealed class Bc3BlockEncoder(TextureEncodeQuality quality) : IBlockEncoder
{
    private readonly Bc4BlockEncoder _alpha = new(quality, 1, 3);
    private readonly Bc1BlockEncoder _colour = new(quality, fourColour: true);

    public object CreateScratch() => new[] { _alpha.CreateScratch(), _colour.CreateScratch() };

    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        object[] scratches = (object[])scratch;
        _alpha.EncodeBlock(pixels, output[..8], scratches[0]);
        _colour.EncodeBlock(pixels, output[8..], scratches[1]);
    }
}
