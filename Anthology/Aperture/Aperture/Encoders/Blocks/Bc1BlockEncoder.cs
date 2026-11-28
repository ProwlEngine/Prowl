// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Prowl.Aperture.Encoders.Blocks;

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

    private static readonly Vector128<int> LaneBits = Vector128.Create(1, 2, 4, 8);
    private static readonly Vector128<int> IndexWeights = Vector128.Create(1, 4, 16, 64);

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

    /// <summary>Sixteen reds, then sixteen greens, then sixteen blues, so four pixels load at once.</summary>
    public object CreateScratch() => new int[16 * 3];

    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        int[] texels = (int[])scratch;
        int transparent = 0;
        int opaque = 0;

        for (int i = 0; i < 16; i++)
        {
            texels[i] = pixels[i * 4];
            texels[16 + i] = pixels[(i * 4) + 1];
            texels[32 + i] = pixels[(i * 4) + 2];

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

            float t = ((texels[i] - mean[0]) * axis[0]) + ((texels[16 + i] - mean[1]) * axis[1])
                + ((texels[32 + i] - mean[2]) * axis[2]);
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

        // Room for every pair the refinement passes and local search can ask for, 62 at most.
        Search search = new(stackalloc uint[64]);

        ushort first = Pack(start);
        ushort second = Pack(end);

        for (int pass = 0; pass <= _refinements; pass++)
        {
            bool improved = TryPair(texels, transparent, first, second, ref search);

            // Four colour palettes hold the same colours in either order, so a swap can only tie.
            if (!fourColour && first != second)
                improved |= TryPair(texels, transparent, second, first, ref search);

            if (pass == _refinements || search.Error == 0 || (!improved && pass > 0))
                break;

            if (!Refit(texels, transparent, fourColour || search.First > search.Second, search.Indices, start, end))
                break;

            first = Pack(start);
            second = Pack(end);
        }

        if (_searchRounds > 0 && search.Error > 0)
            LocalSearch(texels, transparent, _searchRounds, ref search);

        Write(output, search.First, search.Second, search.Indices);
    }

    /// <summary>Nudges each endpoint channel a step either way while doing so lowers the error.</summary>
    private void LocalSearch(int[] texels, int transparent, int rounds, ref Search search)
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
                        ushort source = endpoint == 0 ? search.First : search.Second;
                        int field = ((source >> shifts[c]) & limits[c]) + step;
                        if (field < 0 || field > limits[c])
                            continue;

                        ushort moved = (ushort)((source & ~(limits[c] << shifts[c])) | (field << shifts[c]));
                        ushort first = endpoint == 0 ? moved : search.First;
                        ushort second = endpoint == 0 ? search.Second : moved;

                        improved |= TryPair(texels, transparent, first, second, ref search);
                    }
                }
            }

            if (!improved || search.Error == 0)
                return;
        }
    }

    private bool TryPair(int[] texels, int transparent, ushort first, ushort second, ref Search search)
    {
        // A pair seen before cannot beat the best now, which has only fallen since.
        if (!search.MarkTried(first, second))
            return false;

        int error = Evaluate(texels, transparent, first, second, fourColour, search.Error, out uint indices);
        if (error >= search.Error)
            return false;

        search.Error = error;
        search.First = first;
        search.Second = second;
        search.Indices = indices;
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

            // A copy of entry two never wins a tie, so index three stays reserved for transparency.
            palette.Slice(6, 3).CopyTo(palette[9..]);
        }

        Span<Vector128<int>> colours = stackalloc Vector128<int>[12];
        for (int i = 0; i < 12; i++)
            colours[i] = Vector128.Create(palette[i]);

        int total = 0;

        // Four pixels at a time.
        for (int at = 0; at < 16; at += 4)
        {
            Vector128<int> r = Vector128.Create<int>(texels.AsSpan(at, 4));
            Vector128<int> g = Vector128.Create<int>(texels.AsSpan(16 + at, 4));
            Vector128<int> b = Vector128.Create<int>(texels.AsSpan(32 + at, 4));

            Vector128<int> best = Distance(r, g, b, colours, 0);
            Vector128<int> chosen = Vector128<int>.Zero;

            for (int e = 1; e < 4; e++)
            {
                Vector128<int> d = Distance(r, g, b, colours, e);
                Vector128<int> closer = Vector128.LessThan(d, best);
                best = Vector128.Min(best, d);
                chosen = Vector128.ConditionalSelect(closer, Vector128.Create(e), chosen);
            }

            if (transparent != 0)
            {
                // Transparent pixels cost nothing and always take index three.
                Vector128<int> hidden = Vector128.Equals(Vector128.Create(transparent >> at) & LaneBits, LaneBits);
                best = Vector128.AndNot(best, hidden);
                chosen = Vector128.ConditionalSelect(hidden, Vector128.Create(3), chosen);
            }

            total += Vector128.Sum(best);
            if (total >= limit)
                return int.MaxValue;

            // The weighted lanes occupy separate bits, so their sum packs four indices into a byte.
            indices |= (uint)Vector128.Sum(chosen * IndexWeights) << (at * 2);
        }

        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Distance(Vector128<int> r, Vector128<int> g, Vector128<int> b,
                                           ReadOnlySpan<Vector128<int>> colours, int entry)
    {
        Vector128<int> dr = colours[entry * 3] - r;
        Vector128<int> dg = colours[(entry * 3) + 1] - g;
        Vector128<int> db = colours[(entry * 3) + 2] - b;
        return (dr * dr) + (dg * dg) + (db * db);
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
                sa[c] += s * texels[(c * 16) + i];
                sb[c] += t * texels[(c * 16) + i];
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
            if (texels[i] != texels[0] || texels[16 + i] != texels[16] || texels[32 + i] != texels[32])
                return false;
        }

        int r = texels[0], g = texels[16], b = texels[32];
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

            mean[0] += texels[i];
            mean[1] += texels[16 + i];
            mean[2] += texels[32 + i];
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

            float x = texels[i] - mean[0];
            float y = texels[16 + i] - mean[1];
            float z = texels[32 + i] - mean[2];
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
        float best = 0f;
        axis[0] = axis[1] = axis[2] = 0.57735f;

        // Any one start can be orthogonal to the answer, so try every column and keep the most variance.
        Span<float> columns = [xx, xy, xz, xy, yy, yz, xz, yz, zz];
        for (int start = 0; start < 3; start++)
        {
            float rx = columns[start * 3];
            float ry = columns[(start * 3) + 1];
            float rz = columns[(start * 3) + 2];

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
                continue;

            rx /= norm;
            ry /= norm;
            rz /= norm;

            float variance = (rx * rx * xx) + (ry * ry * yy) + (rz * rz * zz)
                + (2f * ((rx * ry * xy) + (rx * rz * xz) + (ry * rz * yz)));
            if (variance > best)
            {
                best = variance;
                axis[0] = rx;
                axis[1] = ry;
                axis[2] = rz;
            }
        }
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

    /// <summary>The best pair found for one block, and the pairs already tried.</summary>
    private ref struct Search(Span<uint> tried)
    {
        private readonly Span<uint> _tried = tried;
        private int _count;

        public int Error = int.MaxValue;
        public ushort First;
        public ushort Second;
        public uint Indices;

        /// <summary>Records a pair, false when it was already recorded.</summary>
        public bool MarkTried(ushort first, ushort second)
        {
            uint key = ((uint)first << 16) | second;
            if (_tried[.._count].Contains(key))
                return false;

            _tried[_count++] = key;
            return true;
        }
    }
}
