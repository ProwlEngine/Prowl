// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Numerics;
using Prowl.Aperture.Decoders.Dds;

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>
/// ASTC LDR encoder. Each block is fitted with one to four partitions, optionally a second weight
/// plane, and every weight grid the block size allows. Every grid and weight range is scored by a
/// closed form estimate, a shortlist is encoded once for real, and the best few get further passes
/// of least squares refinement. Endpoints are decoded with the decoder's own routines, so every
/// block written decodes to exactly the error that was measured.
/// </summary>
internal sealed class AstcEncoder : IBlockEncoder
{
    private const int MaxTexels = 144;
    private const int MaxFits = 96;

    private static readonly ConcurrentDictionary<int, AstcLayout> Layouts = new();

    private readonly AstcLayout _layout;
    private readonly int _partitionLimit;
    private readonly int[] _patterns;
    private readonly int _shortlist;
    private readonly int _finalists;
    private readonly int _iterations;
    private readonly int _dualPlanes;
    private readonly bool _dualPartitioned;

    private AstcEncoder(AstcLayout layout, TextureEncodeQuality quality)
    {
        _layout = layout;

        switch (quality)
        {
            case TextureEncodeQuality.Fast:
                _partitionLimit = 2;
                _patterns = [0, 1, 2, 0, 0];
                _shortlist = 4;
                _finalists = 1;
                _iterations = 1;
                _dualPlanes = 1;
                break;

            case TextureEncodeQuality.Balanced:
                _partitionLimit = 2;
                _patterns = [0, 1, 4, 0, 0];
                _shortlist = 12;
                _finalists = 2;
                _iterations = 2;
                _dualPlanes = 1;
                break;

            default:
                _partitionLimit = 4;
                _patterns = [0, 1, 6, 4, 3];
                _shortlist = 24;
                _finalists = 4;
                _iterations = 3;
                _dualPlanes = 2;
                _dualPartitioned = true;
                break;
        }
    }

    public static AstcEncoder For(int width, int height, TextureEncodeQuality quality) =>
        new(Layouts.GetOrAdd((width << 8) | height, key => new AstcLayout(key >> 8, key & 0xFF)), quality);

    public object CreateScratch() => new Scratch();

    private enum Family
    {
        Luminance,
        LuminanceAlpha,
        Rgb,
        Rgba,
    }

    private sealed class Fit
    {
        public int Partitions;
        public int Pattern;
        public int Plane;
        public byte[] Assign = [];
        public int AssignOffset;
        public float Residual;
        public readonly float[] Endpoints = new float[32];
        public readonly float[] Ideal0 = new float[MaxTexels];
        public readonly float[] Ideal1 = new float[MaxTexels];
        public readonly float[] TexelScale0 = new float[MaxTexels];
        public readonly float[] TexelScale1 = new float[MaxTexels];
    }

    private struct Candidate
    {
        public float Estimate;
        public int Fit;
        public int Grid;
        public AstcMode Mode;
    }

    private sealed class Scratch
    {
        public readonly float[] Texels = new float[MaxTexels * 4];
        public readonly int[] Values = new int[MaxTexels * 4];
        public readonly Fit[] Fits = new Fit[MaxFits];
        public int FitCount;
        public readonly Candidate[] Candidates = new Candidate[32];
        public int CandidateCount;

        public readonly float[] Grid0 = new float[64];
        public readonly float[] Grid1 = new float[64];
        public readonly float[] Correlation0 = new float[13 * 144];
        public readonly float[] Correlation1 = new float[13 * 144];
        public readonly bool[] Stamp0 = new bool[13];
        public readonly bool[] Stamp1 = new bool[13];
        public readonly float[] HalfX0 = new float[13 * 144];
        public readonly float[] HalfX1 = new float[13 * 144];
        public readonly bool[] HalfStamp0 = new bool[13];
        public readonly bool[] HalfStamp1 = new bool[13];
        public readonly float[] HalfWork = new float[13 * 144];
        public readonly bool[] HalfStampWork = new bool[13];
        public readonly float[] Work0 = new float[MaxTexels];
        public readonly float[] Work1 = new float[MaxTexels];
        public readonly byte[] Raw0 = new byte[64];
        public readonly byte[] Raw1 = new byte[64];
        public readonly int[] Weights0 = new int[MaxTexels];
        public readonly int[] Weights1 = new int[MaxTexels];
        public readonly float[] Endpoints = new float[32];
        public readonly byte[] TrialRaw = new byte[32];
        public readonly int[] TrialDecoded = new int[32];
        public readonly byte[] ChosenRaw = new byte[32];
        public readonly int[] ChosenDecoded = new int[32];
        public readonly byte[] Sequence = new byte[64];

        public readonly byte[] Cluster = new byte[MaxTexels];
        public readonly ulong[] ClusterMasks = new ulong[12];
        public readonly int[] PatternIds = new int[16];
        public readonly int[] PatternScores = new int[16];

        public ulong BestLow;
        public ulong BestHigh;
        public long BestError;

        public Scratch()
        {
            for (int i = 0; i < Fits.Length; i++)
                Fits[i] = new Fit();
        }
    }

    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        Scratch s = (Scratch)scratch;
        int texels = _layout.Texels;

        bool constant = true, opaque = true, grey = true;
        uint first = BitConverter.ToUInt32(pixels);

        for (int t = 0; t < texels; t++)
        {
            int at = t * 4;
            byte r = pixels[at], g = pixels[at + 1], b = pixels[at + 2], a = pixels[at + 3];

            s.Values[at] = r;
            s.Values[at + 1] = g;
            s.Values[at + 2] = b;
            s.Values[at + 3] = a;
            s.Texels[at] = r;
            s.Texels[at + 1] = g;
            s.Texels[at + 2] = b;
            s.Texels[at + 3] = a;

            constant &= BitConverter.ToUInt32(pixels[at..]) == first;
            opaque &= a == 255;
            grey &= r == g && g == b;
        }

        if (constant)
        {
            WriteSolid(output, pixels[0], pixels[1], pixels[2], pixels[3]);
            return;
        }

        Family family = grey ? (opaque ? Family.Luminance : Family.LuminanceAlpha) : (opaque ? Family.Rgb : Family.Rgba);
        int directValues = family switch { Family.Luminance => 2, Family.LuminanceAlpha => 4, Family.Rgb => 6, _ => 8 };
        int fewestValues = family switch { Family.Luminance => 2, Family.LuminanceAlpha => 4, Family.Rgb => 4, _ => 6 };
        int channels = family is Family.Luminance or Family.Rgb ? 3 : 4;

        s.FitCount = 0;
        s.CandidateCount = 0;
        s.BestError = long.MaxValue;

        Span<int> planes = stackalloc int[5];
        int planeCount = ChoosePlanes(family, planes);

        for (int partitions = 1; partitions <= _partitionLimit; partitions++)
        {
            if (partitions * fewestValues > 18)
                break;

            int patterns = partitions == 1 ? 1 : SelectPatterns(s, partitions, _patterns[partitions]);

            for (int i = 0; i < patterns; i++)
            {
                int pattern = partitions == 1 ? 0 : s.PatternIds[i];
                int kept = -1;

                for (int p = 0; p < planeCount && s.FitCount < MaxFits; p++)
                {
                    int plane = planes[p];
                    if (plane >= 0 && (partitions == 4 || (partitions > 1 && !_dualPartitioned && plane != 3)))
                        continue;

                    int index = s.FitCount;
                    Fit fit = s.Fits[index];
                    ComputeFit(s, fit, partitions, pattern, plane);

                    // A single second plane goes to the channel whose removal leaves the straightest line.
                    if (plane >= 0 && _dualPlanes == 1)
                    {
                        if (kept < 0)
                        {
                            kept = s.FitCount++;
                        }
                        else if (fit.Residual < s.Fits[kept].Residual)
                        {
                            s.Fits[index] = s.Fits[kept];
                            s.Fits[kept] = fit;
                        }

                        continue;
                    }

                    s.FitCount++;
                    Estimate(s, index, directValues * partitions, fewestValues * partitions, channels);
                }

                if (kept >= 0)
                    Estimate(s, kept, directValues * partitions, fewestValues * partitions, channels);
            }
        }

        int count = s.CandidateCount;

        if (count <= _finalists)
        {
            for (int i = 0; i < count; i++)
                Refine(s, s.Candidates[i], family, _iterations);
        }
        else
        {
            // One real pass ranks the shortlist far better than the estimate, then the best few get every pass.
            Span<long> quick = stackalloc long[count];
            for (int i = 0; i < count; i++)
                quick[i] = Refine(s, s.Candidates[i], family, 1);

            for (int round = 0; round < _finalists && _iterations > 1; round++)
            {
                int pick = 0;
                for (int i = 1; i < count; i++)
                {
                    if (quick[i] < quick[pick])
                        pick = i;
                }

                if (quick[pick] == long.MaxValue)
                    break;

                quick[pick] = long.MaxValue;
                Refine(s, s.Candidates[pick], family, _iterations);
            }
        }

        if (s.BestError == long.MaxValue)
        {
            WriteAverage(s, output);
            return;
        }

        BitConverter.TryWriteBytes(output, s.BestLow);
        BitConverter.TryWriteBytes(output[8..], s.BestHigh);
    }

    /// <summary>The channels that may get a weight plane of their own, after none.</summary>
    private int ChoosePlanes(Family family, Span<int> planes)
    {
        planes[0] = -1;
        int count = 1;

        if (_dualPlanes == 0)
            return count;

        if (family is Family.LuminanceAlpha or Family.Rgba)
            planes[count++] = 3;

        if (family == Family.Rgb || (family == Family.Rgba && _dualPlanes > 1))
        {
            planes[count++] = 0;
            planes[count++] = 1;
            planes[count++] = 2;
        }

        return count;
    }

    /// <summary>
    /// Clusters the block with k means and ranks every partition pattern by how many pixels it
    /// places in the same groups. Fills the best <paramref name="wanted"/> ids, best first.
    /// </summary>
    private int SelectPatterns(Scratch s, int partitions, int wanted)
    {
        if (wanted == 0)
            return 0;

        int texels = _layout.Texels;
        int words = _layout.Words;
        float[] x = s.Texels;

        Span<float> centroids = stackalloc float[16];
        Span<float> sums = stackalloc float[16];
        Span<int> counts = stackalloc int[4];

        Span<float> mean = stackalloc float[4];
        for (int t = 0; t < texels; t++)
            for (int c = 0; c < 4; c++)
                mean[c] += x[(t * 4) + c];

        for (int c = 0; c < 4; c++)
            mean[c] /= texels;

        int seed = 0;
        float far = -1;
        for (int t = 0; t < texels; t++)
        {
            float d = Distance(x, t, mean);
            if (d > far)
            {
                far = d;
                seed = t;
            }
        }

        for (int c = 0; c < 4; c++)
            centroids[c] = x[(seed * 4) + c];

        for (int k = 1; k < partitions; k++)
        {
            far = -1;
            for (int t = 0; t < texels; t++)
            {
                float nearest = float.MaxValue;
                for (int j = 0; j < k; j++)
                    nearest = MathF.Min(nearest, Distance(x, t, centroids.Slice(j * 4, 4)));

                if (nearest > far)
                {
                    far = nearest;
                    seed = t;
                }
            }

            for (int c = 0; c < 4; c++)
                centroids[(k * 4) + c] = x[(seed * 4) + c];
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            sums.Clear();
            counts.Clear();

            for (int t = 0; t < texels; t++)
            {
                int nearest = 0;
                float best = float.MaxValue;
                for (int k = 0; k < partitions; k++)
                {
                    float d = Distance(x, t, centroids.Slice(k * 4, 4));
                    if (d < best)
                    {
                        best = d;
                        nearest = k;
                    }
                }

                s.Cluster[t] = (byte)nearest;
                counts[nearest]++;
                for (int c = 0; c < 4; c++)
                    sums[(nearest * 4) + c] += x[(t * 4) + c];
            }

            for (int k = 0; k < partitions; k++)
            {
                if (counts[k] == 0)
                    continue;

                for (int c = 0; c < 4; c++)
                    centroids[(k * 4) + c] = sums[(k * 4) + c] / counts[k];
            }
        }

        Array.Clear(s.ClusterMasks);
        for (int t = 0; t < texels; t++)
            s.ClusterMasks[(s.Cluster[t] * words) + (t >> 6)] |= 1UL << (t & 63);

        AstcPartitions table = _layout.Partitions(partitions);
        ReadOnlySpan<byte> permutations = Permutations(partitions);
        int found = 0;
        Span<int> agreement = stackalloc int[16];

        foreach (int id in table.Patterns)
        {
            int maskAt = id * partitions * words;
            int score;

            if (partitions == 2)
            {
                // With two groups one overlap fixes the other three, given the group sizes.
                int shared = 0;
                for (int w = 0; w < words; w++)
                    shared += BitOperations.PopCount(table.Masks[maskAt + w] & s.ClusterMasks[w]);

                int same = shared + (texels - table.Sizes[id * 2] - counts[0] + shared);
                score = Math.Max(same, texels - same);
            }
            else
            {
                score = Score(table, maskAt, partitions, words, s.ClusterMasks, permutations, agreement);
            }

            if (found == wanted && score <= s.PatternScores[found - 1])
                continue;

            int slot = found < wanted ? found++ : found - 1;
            while (slot > 0 && s.PatternScores[slot - 1] < score)
            {
                s.PatternScores[slot] = s.PatternScores[slot - 1];
                s.PatternIds[slot] = s.PatternIds[slot - 1];
                slot--;
            }

            s.PatternScores[slot] = score;
            s.PatternIds[slot] = id;
        }

        return found;
    }

    /// <summary>Pixels a pattern and the clusters agree on under the best matching of their groups.</summary>
    private static int Score(AstcPartitions table, int maskAt, int partitions, int words, ulong[] clusters,
                             ReadOnlySpan<byte> permutations, Span<int> agreement)
    {
        for (int p = 0; p < partitions; p++)
        {
            for (int q = 0; q < partitions; q++)
            {
                int shared = 0;
                for (int w = 0; w < words; w++)
                    shared += BitOperations.PopCount(table.Masks[maskAt + (p * words) + w] & clusters[(q * words) + w]);

                agreement[(p * 4) + q] = shared;
            }
        }

        int score = 0;
        for (int at = 0; at < permutations.Length; at += partitions)
        {
            int total = 0;
            for (int p = 0; p < partitions; p++)
                total += agreement[(p * 4) + permutations[at + p]];

            score = Math.Max(score, total);
        }

        return score;
    }

    private static float Distance(float[] x, int t, ReadOnlySpan<float> to)
    {
        float dr = x[t * 4] - to[0];
        float dg = x[(t * 4) + 1] - to[1];
        float db = x[(t * 4) + 2] - to[2];
        float da = x[(t * 4) + 3] - to[3];
        return (dr * dr) + (dg * dg) + (db * db) + (da * da);
    }

    /// <summary>
    /// The ideal unquantised fit of one partitioning: per partition a principal line through the
    /// channels sharing the first plane, a plain range for the channel on the second, and the
    /// weight each pixel would want on both.
    /// </summary>
    private void ComputeFit(Scratch s, Fit fit, int partitions, int pattern, int plane)
    {
        int texels = _layout.Texels;
        float[] x = s.Texels;

        fit.Partitions = partitions;
        fit.Pattern = pattern;
        fit.Plane = plane;
        fit.Assign = partitions == 1 ? _layout.Single : _layout.Partitions(partitions).Assign;
        fit.AssignOffset = partitions == 1 ? 0 : pattern * texels;
        fit.Residual = 0;

        Span<float> mean = stackalloc float[4];
        Span<float> cov = stackalloc float[16];
        Span<float> axis = stackalloc float[4];

        for (int p = 0; p < partitions; p++)
        {
            mean.Clear();
            cov.Clear();
            int count = 0;
            float low = float.MaxValue, high = float.MinValue;

            for (int t = 0; t < texels; t++)
            {
                if (fit.Assign[fit.AssignOffset + t] != p)
                    continue;

                count++;
                for (int c = 0; c < 4; c++)
                    mean[c] += x[(t * 4) + c];

                if (plane >= 0)
                {
                    low = MathF.Min(low, x[(t * 4) + plane]);
                    high = MathF.Max(high, x[(t * 4) + plane]);
                }
            }

            for (int c = 0; c < 4; c++)
                mean[c] /= count;

            for (int t = 0; t < texels; t++)
            {
                if (fit.Assign[fit.AssignOffset + t] != p)
                    continue;

                for (int i = 0; i < 4; i++)
                {
                    if (i == plane)
                        continue;

                    float di = x[(t * 4) + i] - mean[i];
                    for (int j = i; j < 4; j++)
                    {
                        if (j != plane)
                            cov[(i * 4) + j] += di * (x[(t * 4) + j] - mean[j]);
                    }
                }
            }

            PrincipalAxis(cov, axis);

            float tMin = float.MaxValue, tMax = float.MinValue;
            for (int t = 0; t < texels; t++)
            {
                if (fit.Assign[fit.AssignOffset + t] != p)
                    continue;

                float along = 0, length = 0;
                for (int c = 0; c < 4; c++)
                {
                    if (c == plane)
                        continue;

                    float d = x[(t * 4) + c] - mean[c];
                    along += d * axis[c];
                    length += d * d;
                }

                fit.Ideal0[t] = along;
                fit.Residual += MathF.Max(0, length - (along * along));
                tMin = MathF.Min(tMin, along);
                tMax = MathF.Max(tMax, along);
            }

            float[] e = fit.Endpoints;
            int o = p * 8;
            for (int c = 0; c < 4; c++)
            {
                e[o + (c * 2)] = Math.Clamp(mean[c] + (axis[c] * tMin), 0, 255);
                e[o + (c * 2) + 1] = Math.Clamp(mean[c] + (axis[c] * tMax), 0, 255);
            }

            if (plane >= 0)
            {
                e[o + (plane * 2)] = low;
                e[o + (plane * 2) + 1] = high;
            }

            // Endpoint modes that compare brightness read the brighter endpoint as the second.
            float before = 0, after = 0;
            for (int c = 0; c < 3; c++)
            {
                if (c == plane)
                    continue;

                before += e[o + (c * 2)];
                after += e[o + (c * 2) + 1];
            }

            bool flip = after < before;
            if (flip)
            {
                for (int c = 0; c < 4; c++)
                {
                    if (c != plane)
                        (e[o + (c * 2)], e[o + (c * 2) + 1]) = (e[o + (c * 2) + 1], e[o + (c * 2)]);
                }
            }

            float range = tMax - tMin;
            float planeRange = high - low;
            float scale = 0;
            for (int c = 0; c < 4; c++)
            {
                if (c == plane)
                    continue;

                float d = e[o + (c * 2) + 1] - e[o + (c * 2)];
                scale += d * d;
            }

            for (int t = 0; t < texels; t++)
            {
                if (fit.Assign[fit.AssignOffset + t] != p)
                    continue;

                fit.TexelScale0[t] = scale;
                fit.TexelScale1[t] = planeRange * planeRange;

                float w = range > 1e-4f ? (fit.Ideal0[t] - tMin) / range : 0;
                fit.Ideal0[t] = flip ? 1 - w : w;

                if (plane >= 0)
                    fit.Ideal1[t] = planeRange > 1e-4f ? (x[(t * 4) + plane] - low) / planeRange : 0;
            }
        }
    }

    /// <summary>Dominant eigenvector of an upper triangular covariance by power iteration.</summary>
    private static void PrincipalAxis(Span<float> cov, Span<float> axis)
    {
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < i; j++)
                cov[(i * 4) + j] = cov[(j * 4) + i];

        int start = 0;
        for (int i = 1; i < 4; i++)
        {
            if (cov[(i * 4) + i] > cov[(start * 4) + start])
                start = i;
        }

        Span<float> next = stackalloc float[4];
        for (int c = 0; c < 4; c++)
            axis[c] = cov[(start * 4) + c];

        for (int iteration = 0; iteration < 8; iteration++)
        {
            float largest = 0;
            for (int i = 0; i < 4; i++)
            {
                float sum = 0;
                for (int j = 0; j < 4; j++)
                    sum += cov[(i * 4) + j] * axis[j];

                next[i] = sum;
                largest = MathF.Max(largest, MathF.Abs(sum));
            }

            if (largest < 1e-12f)
                break;

            for (int i = 0; i < 4; i++)
                axis[i] = next[i] / largest;
        }

        float norm = MathF.Sqrt((axis[0] * axis[0]) + (axis[1] * axis[1]) + (axis[2] * axis[2]) + (axis[3] * axis[3]));
        if (norm < 1e-12f)
        {
            axis.Clear();
            return;
        }

        for (int i = 0; i < 4; i++)
            axis[i] /= norm;
    }

    /// <summary>Scores every grid and weight range for one fit and keeps the most promising.</summary>
    private void Estimate(Scratch s, int fitIndex, int values, int fewestValues, int channels)
    {
        Fit fit = s.Fits[fitIndex];
        if (s.CandidateCount == _shortlist && fit.Residual >= s.Candidates[_shortlist - 1].Estimate)
            return;

        bool dual = fit.Plane >= 0;
        int configBits = 13 + (fit.Partitions == 1 ? 4 : 16) + (dual ? 2 : 0);
        int texels = _layout.Texels;

        float norm0 = 0, norm1 = 0, meanScale0 = 0, meanScale1 = 0;
        for (int t = 0; t < texels; t++)
        {
            norm0 += fit.Ideal0[t] * fit.Ideal0[t];
            meanScale0 += fit.TexelScale0[t];
            if (dual)
            {
                norm1 += fit.Ideal1[t] * fit.Ideal1[t];
                meanScale1 += fit.TexelScale1[t];
            }
        }

        meanScale0 /= texels;
        meanScale1 /= texels;
        s.Stamp0.AsSpan().Clear();
        s.Stamp1.AsSpan().Clear();
        s.HalfStamp0.AsSpan().Clear();
        s.HalfStamp1.AsSpan().Clear();

        for (int g = 0; g < _layout.Grids.Length; g++)
        {
            AstcMode[] modes = _layout.Modes[(g * 2) + (dual ? 1 : 0)];
            if (modes.Length == 0)
                continue;

            float limit = s.CandidateCount == _shortlist ? s.Candidates[_shortlist - 1].Estimate : float.MaxValue;
            AstcGrid grid = _layout.Grids[g];

            // The lowest weight range leaves the most bits for endpoints, so it bounds the endpoint error from below.
            int widest = AstcQuant.EndpointRange(values, 128 - configBits - modes[0].WeightBits);
            if (widest < 4)
                widest = AstcQuant.EndpointRange(fewestValues, 128 - configBits - modes[0].WeightBits);

            if (widest < 4 || fit.Residual + (AstcQuant.EndpointNoise[widest] * texels * channels) >= limit)
                continue;

            float baseline = fit.Residual + (ProjectionError(grid, s.Correlation0, s.Stamp0, fit.Ideal0, norm0) * meanScale0);
            if (dual)
                baseline += ProjectionError(grid, s.Correlation1, s.Stamp1, fit.Ideal1, norm1) * meanScale1;

            if (baseline >= limit)
                continue;

            bool valuesReady = false;

            foreach (AstcMode mode in modes)
            {
                int remaining = 128 - configBits - mode.WeightBits;
                int range = AstcQuant.EndpointRange(values, remaining);
                if (range < 4)
                    range = AstcQuant.EndpointRange(fewestValues, remaining);

                if (range < 4)
                    continue;

                float estimate = baseline + (AstcQuant.EndpointNoise[range] * texels * channels);
                if (estimate >= limit)
                    continue;

                if (!valuesReady)
                {
                    valuesReady = true;
                    GridValues(grid, s.HalfX0, s.HalfStamp0, fit.Ideal0, s.Grid0);
                    if (dual)
                        GridValues(grid, s.HalfX1, s.HalfStamp1, fit.Ideal1, s.Grid1);
                }

                estimate += QuantisationError(grid, s.Grid0, mode.Range) * meanScale0;
                if (dual)
                    estimate += QuantisationError(grid, s.Grid1, mode.Range) * meanScale1;

                if (estimate >= limit)
                    continue;

                Insert(s, new Candidate { Estimate = estimate, Fit = fitIndex, Grid = g, Mode = mode });
                limit = s.CandidateCount == _shortlist ? s.Candidates[_shortlist - 1].Estimate : float.MaxValue;
            }
        }
    }

    /// <summary>
    /// Least squares error of fitting a grid to the weights, treating the interpolation as separable:
    /// the squared length of the weights less the part the grid's two axes can represent. The
    /// horizontal half depends only on the grid width, so it is shared by every grid of that width.
    /// </summary>
    private float ProjectionError(AstcGrid grid, float[] correlation, bool[] stamp, float[] ideal, float norm)
    {
        if (grid.Identity)
            return 0;

        int width = _layout.Width;
        int height = _layout.Height;
        int at = grid.Width * height * height;

        if (!stamp[grid.Width])
        {
            stamp[grid.Width] = true;
            float[] basis = _layout.BasisX[grid.Width];
            int rank = _layout.RankX[grid.Width];
            Span<float> projected = stackalloc float[12 * 12];

            for (int y = 0; y < height; y++)
            {
                for (int i = 0; i < rank; i++)
                {
                    float sum = 0;
                    for (int x = 0; x < width; x++)
                        sum += ideal[(y * width) + x] * basis[(x * rank) + i];

                    projected[(y * rank) + i] = sum;
                }
            }

            for (int a = 0; a < height; a++)
            {
                for (int b = a; b < height; b++)
                {
                    float sum = 0;
                    for (int i = 0; i < rank; i++)
                        sum += projected[(a * rank) + i] * projected[(b * rank) + i];

                    correlation[at + (a * height) + b] = sum;
                    correlation[at + (b * height) + a] = sum;
                }
            }
        }

        float[] projection = _layout.ProjectionY[grid.Height];
        float kept = 0;
        for (int k = 0; k < height * height; k++)
            kept += projection[k] * correlation[at + k];

        return MathF.Max(0, norm - kept);
    }

    /// <summary>
    /// The least squares grid for the weights under the separable reading of the interpolation,
    /// one axis at a time. The horizontal pass depends only on the grid width and is shared.
    /// </summary>
    private void GridValues(AstcGrid grid, float[] half, bool[] stamp, float[] ideal, float[] values)
    {
        int width = _layout.Width;
        int height = _layout.Height;

        if (grid.Identity)
        {
            Array.Copy(ideal, values, width * height);
            return;
        }

        int gw = grid.Width, gh = grid.Height;
        int at = gw * 144;

        if (!stamp[gw])
        {
            stamp[gw] = true;
            float[] inverse = _layout.InverseX[gw];
            for (int y = 0; y < height; y++)
            {
                for (int i = 0; i < gw; i++)
                {
                    float sum = 0;
                    for (int x = 0; x < width; x++)
                        sum += inverse[(i * width) + x] * ideal[(y * width) + x];

                    half[at + (y * gw) + i] = sum;
                }
            }
        }

        float[] inverseY = _layout.InverseY[gh];
        for (int j = 0; j < gh; j++)
        {
            for (int i = 0; i < gw; i++)
            {
                float sum = 0;
                for (int y = 0; y < height; y++)
                    sum += inverseY[(j * height) + y] * half[at + (y * gw) + i];

                values[(j * gw) + i] = Math.Clamp(sum, 0, 1);
            }
        }
    }

    private static float QuantisationError(AstcGrid grid, float[] values, int range)
    {
        float[] rounded = AstcQuant.WeightRounded[range];
        float error = 0;
        for (int j = 0; j < grid.Count; j++)
        {
            float d = values[j] - rounded[(int)((values[j] * 1024) + 0.5f)];
            error += grid.GridSquareSum[j] * d * d;
        }

        return error;
    }

    private void Insert(Scratch s, Candidate candidate)
    {
        int slot = s.CandidateCount < _shortlist ? s.CandidateCount++ : _shortlist - 1;
        while (slot > 0 && s.Candidates[slot - 1].Estimate > candidate.Estimate)
        {
            s.Candidates[slot] = s.Candidates[slot - 1];
            slot--;
        }

        s.Candidates[slot] = candidate;
    }

    /// <summary>
    /// Encodes one candidate for real: quantised weights, least squares endpoints, the best
    /// endpoint mode, the exact error, and then again with weights refitted to what was decoded.
    /// </summary>
    private long Refine(Scratch s, Candidate candidate, Family family, int iterations)
    {
        Fit fit = s.Fits[candidate.Fit];
        AstcGrid grid = _layout.Grids[candidate.Grid];
        AstcMode mode = candidate.Mode;
        int texels = _layout.Texels;
        bool dual = fit.Plane >= 0;
        int configBits = 13 + (fit.Partitions == 1 ? 4 : 16) + (dual ? 2 : 0);
        int remaining = 128 - configBits - mode.WeightBits;

        long own = long.MaxValue;
        Array.Copy(fit.Ideal0, s.Work0, texels);
        if (dual)
            Array.Copy(fit.Ideal1, s.Work1, texels);

        ReadOnlySpan<int> modes = family switch
        {
            Family.Luminance => [0],
            Family.LuminanceAlpha => [4],
            Family.Rgb => [8, 9, 6],
            _ => [12, 13, 10],
        };

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            QuantiseWeights(s, grid, s.Work0, s.Grid0, s.Raw0, s.Weights0, mode.Range);
            if (dual)
                QuantiseWeights(s, grid, s.Work1, s.Grid1, s.Raw1, s.Weights1, mode.Range);

            SolveEndpoints(s, fit);

            float bestEndpointError = float.MaxValue;
            int chosenMode = -1;
            int chosenRange = 0;

            foreach (int endpointMode in modes)
            {
                int range = AstcQuant.EndpointRange(ValueCount(endpointMode) * fit.Partitions, remaining);
                if (range < 4)
                    continue;

                float error = 0;
                for (int p = 0; p < fit.Partitions && error < bestEndpointError; p++)
                {
                    error += EncodeEndpoints(endpointMode, range, s.Endpoints.AsSpan(p * 8, 8),
                                             s.TrialRaw.AsSpan(p * 8, 8), s.TrialDecoded.AsSpan(p * 8, 8));
                }

                if (error < bestEndpointError)
                {
                    bestEndpointError = error;
                    chosenMode = endpointMode;
                    chosenRange = range;
                    Array.Copy(s.TrialRaw, s.ChosenRaw, 32);
                    Array.Copy(s.TrialDecoded, s.ChosenDecoded, 32);
                }
            }

            if (chosenMode < 0)
                return own;

            long texelError = TexelError(s, fit, s.ChosenDecoded, long.MaxValue);
            own = Math.Min(own, texelError);
            if (texelError < s.BestError)
            {
                s.BestError = texelError;
                Pack(s, fit, grid, mode, chosenMode, chosenRange);
            }

            if (s.BestError == 0)
                return own;

            if (iteration + 1 < iterations)
                RefitWeights(s, fit, s.ChosenDecoded);
        }

        return own;
    }

    /// <summary>Fits the grid to the weights, quantises it, and spreads it back over the pixels exactly as the decoder does.</summary>
    private void QuantiseWeights(Scratch s, AstcGrid grid, float[] ideal, float[] weights, byte[] raw, int[] texelWeights, int range)
    {
        int texels = _layout.Texels;
        s.HalfStampWork[grid.Width] = false;
        GridValues(grid, s.HalfWork, s.HalfStampWork, ideal, weights);

        byte[] quantise = AstcQuant.WeightQuantise[range];
        byte[] value = AstcQuant.WeightValue[range];
        Span<int> grid64 = stackalloc int[64];

        for (int j = 0; j < grid.Count; j++)
        {
            raw[j] = quantise[(int)((Math.Clamp(weights[j], 0, 1) * 1024) + 0.5f)];
            grid64[j] = value[raw[j]];
        }

        if (grid.Identity)
        {
            for (int t = 0; t < texels; t++)
                texelWeights[t] = grid64[t];

            return;
        }

        for (int t = 0; t < texels; t++)
        {
            int at = t * 4;
            texelWeights[t] = ((grid.TexelFactor[at] * grid64[grid.TexelGrid[at]])
                + (grid.TexelFactor[at + 1] * grid64[grid.TexelGrid[at + 1]])
                + (grid.TexelFactor[at + 2] * grid64[grid.TexelGrid[at + 2]])
                + (grid.TexelFactor[at + 3] * grid64[grid.TexelGrid[at + 3]]) + 8) >> 4;
        }
    }

    /// <summary>Least squares endpoints for the quantised weights, per partition and channel.</summary>
    private void SolveEndpoints(Scratch s, Fit fit)
    {
        int texels = _layout.Texels;
        float[] x = s.Texels;
        Span<float> sumLow = stackalloc float[4];
        Span<float> sumHigh = stackalloc float[4];
        Span<float> sum = stackalloc float[4];

        for (int p = 0; p < fit.Partitions; p++)
        {
            float aa = 0, ab = 0, bb = 0, aa1 = 0, ab1 = 0, bb1 = 0;
            int count = 0;
            sumLow.Clear();
            sumHigh.Clear();
            sum.Clear();

            for (int t = 0; t < texels; t++)
            {
                if (fit.Assign[fit.AssignOffset + t] != p)
                    continue;

                count++;
                float u = s.Weights0[t] * (1f / 64f);
                float v = 1 - u;
                aa += v * v;
                ab += v * u;
                bb += u * u;

                float u1 = 0, v1 = 0;
                if (fit.Plane >= 0)
                {
                    u1 = s.Weights1[t] * (1f / 64f);
                    v1 = 1 - u1;
                    aa1 += v1 * v1;
                    ab1 += v1 * u1;
                    bb1 += u1 * u1;
                }

                for (int c = 0; c < 4; c++)
                {
                    float value = x[(t * 4) + c];
                    sum[c] += value;
                    if (c == fit.Plane)
                    {
                        sumLow[c] += v1 * value;
                        sumHigh[c] += u1 * value;
                    }
                    else
                    {
                        sumLow[c] += v * value;
                        sumHigh[c] += u * value;
                    }
                }
            }

            for (int c = 0; c < 4; c++)
            {
                bool second = c == fit.Plane;
                float a = second ? aa1 : aa;
                float b = second ? ab1 : ab;
                float d = second ? bb1 : bb;
                float determinant = (a * d) - (b * b);
                int o = (p * 8) + (c * 2);

                if (MathF.Abs(determinant) < 1e-4f * count * count)
                {
                    s.Endpoints[o] = s.Endpoints[o + 1] = sum[c] / count;
                    continue;
                }

                s.Endpoints[o] = Math.Clamp(((sumLow[c] * d) - (sumHigh[c] * b)) / determinant, 0, 255);
                s.Endpoints[o + 1] = Math.Clamp(((sumHigh[c] * a) - (sumLow[c] * b)) / determinant, 0, 255);
            }
        }
    }

    /// <summary>Moves each pixel's ideal weight onto the line between the endpoints actually decoded.</summary>
    private void RefitWeights(Scratch s, Fit fit, int[] decoded)
    {
        int texels = _layout.Texels;
        float[] x = s.Texels;

        for (int t = 0; t < texels; t++)
        {
            int o = fit.Assign[fit.AssignOffset + t] * 8;
            float along = 0, length = 0;

            for (int c = 0; c < 4; c++)
            {
                float low = decoded[o + (c * 2)];
                float d = decoded[o + (c * 2) + 1] - low;

                if (c == fit.Plane)
                {
                    if (d != 0)
                        s.Work1[t] = Math.Clamp((x[(t * 4) + c] - low) / d, 0, 1);

                    continue;
                }

                along += (x[(t * 4) + c] - low) * d;
                length += d * d;
            }

            if (length > 0)
                s.Work0[t] = Math.Clamp(along / length, 0, 1);
        }
    }

    /// <summary>Exact squared error of the block as the decoder would produce it.</summary>
    private long TexelError(Scratch s, Fit fit, int[] decoded, long limit)
    {
        int texels = _layout.Texels;
        long error = 0;

        for (int t = 0; t < texels; t++)
        {
            int o = fit.Assign[fit.AssignOffset + t] * 8;
            int w0 = s.Weights0[t];
            int w1 = fit.Plane >= 0 ? s.Weights1[t] : w0;

            for (int c = 0; c < 4; c++)
            {
                int w = c == fit.Plane ? w1 : w0;
                int low = decoded[o + (c * 2)] * 0x101;
                int high = decoded[o + (c * 2) + 1] * 0x101;
                int wide = ((low * (64 - w)) + (high * w) + 32) >> 6;
                int d = (((wide * 255) + 32767) / 65535) - s.Values[(t * 4) + c];
                error += d * d;
            }

            if (error >= limit)
                return error;
        }

        return error;
    }

    private static int ValueCount(int mode) => 2 + (2 * (mode >> 2));

    /// <summary>
    /// Quantises one partition's endpoints in one endpoint mode. Each way of writing them is tried
    /// and decoded, and the one landing nearest the target endpoints is kept.
    /// </summary>
    private static float EncodeEndpoints(int mode, int range, ReadOnlySpan<float> target, Span<byte> raw, Span<int> decoded)
    {
        float best = float.MaxValue;
        Span<byte> trial = stackalloc byte[8];

        float r0 = target[0], r1 = target[1], g0 = target[2], g1 = target[3];
        float b0 = target[4], b1 = target[5], a0 = target[6], a1 = target[7];

        switch (mode)
        {
            case 0:
                trial[0] = Q(range, (r0 + g0 + b0) / 3);
                trial[1] = Q(range, (r1 + g1 + b1) / 3);
                Consider(mode, range, trial, target, ref best, raw, decoded);
                break;

            case 4:
                trial[0] = Q(range, (r0 + g0 + b0) / 3);
                trial[1] = Q(range, (r1 + g1 + b1) / 3);
                trial[2] = Q(range, a0);
                trial[3] = Q(range, a1);
                Consider(mode, range, trial, target, ref best, raw, decoded);
                break;

            case 8:
            case 12:
                for (int i = 0; i < 8; i++)
                    trial[i] = Q(range, target[i]);

                OrderDirect(range, trial, target);
                Consider(mode, range, trial, target, ref best, raw, decoded);

                // Blue contraction stores red and green relative to blue, buying a bit of precision.
                trial[0] = Q(range, (2 * r1) - b1);
                trial[1] = Q(range, (2 * r0) - b0);
                trial[2] = Q(range, (2 * g1) - b1);
                trial[3] = Q(range, (2 * g0) - b0);
                trial[4] = Q(range, b1);
                trial[5] = Q(range, b0);
                trial[6] = Q(range, a1);
                trial[7] = Q(range, a0);
                Consider(mode, range, trial, target, ref best, raw, decoded);
                break;

            case 9:
            case 13:
                if (OffsetPairs(range, target, false, mode == 13, trial))
                    Consider(mode, range, trial, target, ref best, raw, decoded);

                if (OffsetPairs(range, target, true, mode == 13, trial))
                    Consider(mode, range, trial, target, ref best, raw, decoded);
                break;

            case 6:
            case 10:
            {
                trial[0] = Q(range, r1);
                trial[1] = Q(range, g1);
                trial[2] = Q(range, b1);
                trial[4] = Q(range, a0);
                trial[5] = Q(range, a1);

                byte[] value = AstcQuant.EndpointValue[range];
                float ur = value[trial[0]], ug = value[trial[1]], ub = value[trial[2]];
                float length = (ur * ur) + (ug * ug) + (ub * ub);
                float scale = length > 0 ? 256 * ((r0 * ur) + (g0 * ug) + (b0 * ub)) / length : 0;
                byte nearest = Q(range, scale);

                trial[3] = nearest;
                Consider(mode, range, trial, target, ref best, raw, decoded);
                trial[3] = AstcQuant.EndpointUp[range][nearest];
                Consider(mode, range, trial, target, ref best, raw, decoded);
                trial[3] = AstcQuant.EndpointDown[range][nearest];
                Consider(mode, range, trial, target, ref best, raw, decoded);
                break;
            }
        }

        return best;
    }

    private static byte Q(int range, float value) =>
        AstcQuant.EndpointQuantise[range][Math.Clamp((int)(value + 0.5f), 0, 255)];

    private static void Consider(int mode, int range, ReadOnlySpan<byte> trial, ReadOnlySpan<float> target,
                                 ref float best, Span<byte> raw, Span<int> decoded)
    {
        byte[] value = AstcQuant.EndpointValue[range];
        Span<byte> values = stackalloc byte[8];
        Span<int> endpoints = stackalloc int[8];
        int count = ValueCount(mode);

        for (int i = 0; i < count; i++)
            values[i] = value[trial[i]];

        if (!Astc.DecodeEndpoints(mode, values, endpoints))
            return;

        float error = 0;
        for (int i = 0; i < 8; i++)
        {
            float d = endpoints[i] - target[i];
            error += d * d;
        }

        if (error >= best)
            return;

        best = error;
        trial[..count].CopyTo(raw);
        endpoints.CopyTo(decoded);
    }

    /// <summary>
    /// Direct RGB endpoints swap and blue contract when the second sums darker than the first,
    /// so quantisation that tips them is undone a step at a time at the least cost.
    /// </summary>
    private static void OrderDirect(int range, Span<byte> trial, ReadOnlySpan<float> target)
    {
        byte[] value = AstcQuant.EndpointValue[range];

        for (int step = 0; step < 16; step++)
        {
            int low = value[trial[0]] + value[trial[2]] + value[trial[4]];
            int high = value[trial[1]] + value[trial[3]] + value[trial[5]];
            if (high >= low)
                return;

            int bestIndex = -1;
            byte bestRaw = 0;
            float bestCost = float.MaxValue;

            for (int i = 0; i < 6; i++)
            {
                byte moved = (i & 1) != 0 ? AstcQuant.EndpointUp[range][trial[i]] : AstcQuant.EndpointDown[range][trial[i]];
                if (moved == trial[i])
                    continue;

                float before = value[trial[i]] - target[i];
                float after = value[moved] - target[i];
                float cost = (after * after) - (before * before);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIndex = i;
                    bestRaw = moved;
                }
            }

            if (bestIndex < 0)
                return;

            trial[bestIndex] = bestRaw;
        }
    }

    /// <summary>
    /// Base and offset endpoint pairs: the base in eight bits and a six bit signed offset, packed
    /// across two values the way the decoder unpacks them. False when the offset does not fit.
    /// </summary>
    private static bool OffsetPairs(int range, ReadOnlySpan<float> target, bool contracted, bool alpha, Span<byte> trial)
    {
        Span<int> bases = stackalloc int[4];
        Span<int> offsets = stackalloc int[4];
        int sum = 0;

        for (int c = 0; c < 3; c++)
        {
            float low = target[c * 2], high = target[(c * 2) + 1];
            float baseValue = contracted ? high : low;
            float other = contracted ? low : high;

            if (contracted && c < 2)
            {
                baseValue = (2 * high) - target[5];
                other = (2 * low) - target[4];
            }

            int b = (int)MathF.Round(baseValue);
            int offset = (int)MathF.Round(other) - b;
            if (b < 0 || b > 255 || offset < -32 || offset > 31)
                return false;

            bases[c] = b;
            offsets[c] = offset;
            sum += offset;
        }

        if (contracted ? sum >= 0 : sum < 0)
            return false;

        bases[3] = (int)MathF.Round(contracted ? target[7] : target[6]);
        offsets[3] = (int)MathF.Round(contracted ? target[6] : target[7]) - bases[3];
        if (alpha && (offsets[3] < -32 || offsets[3] > 31))
            return false;

        int channels = alpha ? 4 : 3;
        for (int c = 0; c < channels; c++)
        {
            int first = (bases[c] & 0x7F) << 1;
            int second = (bases[c] & 0x80) | ((offsets[c] & 0x3F) << 1);

            // The spare low bit copies the top one, which is how seven bit values widen exactly.
            first |= (first >> 7) & 1;
            second |= (second >> 7) & 1;

            trial[c * 2] = AstcQuant.EndpointQuantise[range][first];
            trial[(c * 2) + 1] = AstcQuant.EndpointQuantise[range][second];
        }

        return true;
    }

    private void Pack(Scratch s, Fit fit, AstcGrid grid, AstcMode mode, int endpointMode, int endpointRange)
    {
        AstcBits bits = default;
        bits.Write(0, 11, mode.Bits);
        bits.Write(11, 2, (uint)(fit.Partitions - 1));

        int at;
        if (fit.Partitions == 1)
        {
            bits.Write(13, 4, (uint)endpointMode);
            at = 17;
        }
        else
        {
            bits.Write(13, 10, (uint)fit.Pattern);
            bits.Write(23, 6, (uint)(endpointMode << 2));
            at = 29;
        }

        int count = ValueCount(endpointMode);
        Span<byte> sequence = s.Sequence;
        for (int p = 0; p < fit.Partitions; p++)
            for (int i = 0; i < count; i++)
                sequence[(p * count) + i] = s.ChosenRaw[(p * 8) + i];

        bits.WriteSequence(endpointRange, sequence[..(count * fit.Partitions)], at);

        bool dual = fit.Plane >= 0;
        int weights = grid.Count * (dual ? 2 : 1);
        for (int j = 0; j < grid.Count; j++)
        {
            if (dual)
            {
                sequence[j * 2] = s.Raw0[j];
                sequence[(j * 2) + 1] = s.Raw1[j];
            }
            else
            {
                sequence[j] = s.Raw0[j];
            }
        }

        AstcBits weightBits = default;
        weightBits.WriteSequence(mode.Range, sequence[..weights], 0);
        bits.Low |= AstcBits.Reverse(weightBits.High);
        bits.High |= AstcBits.Reverse(weightBits.Low);

        if (dual)
            bits.Write(128 - mode.WeightBits - 2, 2, (uint)fit.Plane);

        s.BestLow = bits.Low;
        s.BestHigh = bits.High;
    }

    private static void WriteSolid(Span<byte> output, int r, int g, int b, int a)
    {
        BitConverter.TryWriteBytes(output, 0xFFFFFFFFFFFFFDFCUL);
        ulong colour = ((ulong)r * 0x101) | ((ulong)g * 0x101 << 16) | ((ulong)b * 0x101 << 32) | ((ulong)a * 0x101 << 48);
        BitConverter.TryWriteBytes(output[8..], colour);
    }

    private void WriteAverage(Scratch s, Span<byte> output)
    {
        Span<int> sum = stackalloc int[4];
        int texels = _layout.Texels;
        for (int t = 0; t < texels; t++)
            for (int c = 0; c < 4; c++)
                sum[c] += s.Values[(t * 4) + c];

        WriteSolid(output, sum[0] / texels, sum[1] / texels, sum[2] / texels, sum[3] / texels);
    }

    private static ReadOnlySpan<byte> Permutations(int count) => count switch
    {
        2 => [0, 1, 1, 0],
        3 => [0, 1, 2, 0, 2, 1, 1, 0, 2, 1, 2, 0, 2, 0, 1, 2, 1, 0],
        _ =>
        [
            0, 1, 2, 3, 0, 1, 3, 2, 0, 2, 1, 3, 0, 2, 3, 1, 0, 3, 1, 2, 0, 3, 2, 1,
            1, 0, 2, 3, 1, 0, 3, 2, 1, 2, 0, 3, 1, 2, 3, 0, 1, 3, 0, 2, 1, 3, 2, 0,
            2, 0, 1, 3, 2, 0, 3, 1, 2, 1, 0, 3, 2, 1, 3, 0, 2, 3, 0, 1, 2, 3, 1, 0,
            3, 0, 1, 2, 3, 0, 2, 1, 3, 1, 0, 2, 3, 1, 2, 0, 3, 2, 0, 1, 3, 2, 1, 0,
        ],
    };
}

/// <summary>One legal block mode: its eleven bits, weight range and the bits its weights occupy.</summary>
internal readonly record struct AstcMode(ushort Bits, byte Range, byte WeightBits);

/// <summary>How a weight grid spreads over the pixels of a block, in the decoder's sixteenths.</summary>
internal sealed class AstcGrid
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool Identity { get; init; }
    public int Count => Width * Height;

    public required int[] TexelGrid { get; init; }
    public required int[] TexelFactor { get; init; }
    public required float[] GridSquareSum { get; init; }
}

/// <summary>Every partition pattern of one count that splits the block into that many non empty groups.</summary>
internal sealed class AstcPartitions
{
    public required int[] Patterns { get; init; }
    public required byte[] Assign { get; init; }
    public required ulong[] Masks { get; init; }
    public required int[] Sizes { get; init; }
}

/// <summary>Everything about a block size the encoder looks up rather than recomputes.</summary>
internal sealed class AstcLayout
{
    private readonly AstcPartitions?[] _partitions = new AstcPartitions?[5];

    public int Width { get; }
    public int Height { get; }
    public int Texels { get; }
    public int Words { get; }
    public byte[] Single { get; }
    public AstcGrid[] Grids { get; }
    public AstcMode[][] Modes { get; }
    public float[][] BasisX { get; } = new float[13][];
    public int[] RankX { get; } = new int[13];
    public float[][] ProjectionY { get; } = new float[13][];
    public float[][] InverseX { get; } = new float[13][];
    public float[][] InverseY { get; } = new float[13][];

    public AstcLayout(int width, int height)
    {
        Width = width;
        Height = height;
        Texels = width * height;
        Words = (Texels + 63) / 64;
        Single = new byte[Texels];

        List<AstcGrid> grids = [];
        List<List<AstcMode>> modes = [];
        Dictionary<(int, int), int> gridIndex = [];
        HashSet<(int, int, int, bool)> seen = [];

        for (int bits = 0; bits < 2048; bits++)
        {
            Astc.Block block = new((ulong)bits, 0);
            if (!Astc.TryConfig(block, out Astc.Config config) || config.Solid)
                continue;

            int gw = config.GridWidth, gh = config.GridHeight;
            if (gw > width || gh > height)
                continue;

            int values = (config.DualPlane ? 2 : 1) * gw * gh;
            int weightBits = Astc.SequenceBits(values, config.WeightRange);
            if (values > 64 || weightBits < 24 || weightBits > 96)
                continue;

            if (!seen.Add((gw, gh, config.WeightRange, config.DualPlane)))
                continue;

            if (!gridIndex.TryGetValue((gw, gh), out int index))
            {
                index = grids.Count;
                gridIndex[(gw, gh)] = index;
                grids.Add(BuildGrid(gw, gh));
                modes.Add([]);
                modes.Add([]);
            }

            modes[(index * 2) + (config.DualPlane ? 1 : 0)].Add(new AstcMode((ushort)bits, (byte)config.WeightRange, (byte)weightBits));
        }

        Grids = [.. grids];
        Modes = modes.Select(list => list.OrderBy(m => m.Range).ToArray()).ToArray();

        for (int size = 2; size <= 12; size++)
        {
            if (size <= width)
            {
                (BasisX[size], RankX[size]) = AxisBasis(width, size);
                InverseX[size] = AxisInverse(width, size);
            }

            if (size <= height)
            {
                (float[] basis, int rank) = AxisBasis(height, size);
                float[] projection = new float[height * height];
                for (int a = 0; a < height; a++)
                    for (int b = 0; b < height; b++)
                        for (int i = 0; i < rank; i++)
                            projection[(a * height) + b] += basis[(a * rank) + i] * basis[(b * rank) + i];

                ProjectionY[size] = projection;
                InverseY[size] = AxisInverse(height, size);
            }
        }
    }

    /// <summary>The one dimensional interpolation from <paramref name="size"/> grid points to <paramref name="length"/> pixels, as the decoder spaces them.</summary>
    private static double[] AxisInterpolation(int length, int size)
    {
        double[] columns = new double[length * size];
        int scale = (1024 + (length / 2)) / (length - 1);

        for (int x = 0; x < length; x++)
        {
            if (size == length)
            {
                columns[(x * size) + x] = 1;
                continue;
            }

            int g = ((scale * x * (size - 1)) + 32) >> 6;
            int j = g >> 4, f = g & 0xF;
            columns[(x * size) + j] += (16 - f) / 16.0;
            columns[(x * size) + Math.Min(j + 1, size - 1)] += f / 16.0;
        }

        return columns;
    }

    /// <summary>The least squares inverse of <see cref="AxisInterpolation"/>, mapping pixels back to grid points.</summary>
    private static float[] AxisInverse(int length, int size)
    {
        double[] u = AxisInterpolation(length, size);
        double[] normal = new double[size * size * 2];

        for (int a = 0; a < size; a++)
        {
            for (int b = 0; b < size; b++)
            {
                double sum = 0;
                for (int x = 0; x < length; x++)
                    sum += u[(x * size) + a] * u[(x * size) + b];

                normal[(a * size * 2) + b] = sum + (a == b ? 1e-9 : 0);
            }

            normal[(a * size * 2) + size + a] = 1;
        }

        for (int c = 0; c < size; c++)
        {
            double pivot = normal[(c * size * 2) + c];
            for (int k = 0; k < size * 2; k++)
                normal[(c * size * 2) + k] /= pivot;

            for (int r = 0; r < size; r++)
            {
                if (r == c)
                    continue;

                double factor = normal[(r * size * 2) + c];
                for (int k = 0; k < size * 2; k++)
                    normal[(r * size * 2) + k] -= factor * normal[(c * size * 2) + k];
            }
        }

        float[] inverse = new float[size * length];
        for (int i = 0; i < size; i++)
        {
            for (int x = 0; x < length; x++)
            {
                double sum = 0;
                for (int k = 0; k < size; k++)
                    sum += normal[(i * size * 2) + size + k] * u[(x * size) + k];

                inverse[(i * length) + x] = (float)sum;
            }
        }

        return inverse;
    }

    /// <summary>An orthonormal basis for what a one dimensional grid of <paramref name="size"/> points can represent across <paramref name="length"/> pixels.</summary>
    private static (float[] Basis, int Rank) AxisBasis(int length, int size)
    {
        double[] columns = AxisInterpolation(length, size);

        List<double[]> basis = [];
        for (int c = 0; c < size; c++)
        {
            double[] v = new double[length];
            for (int x = 0; x < length; x++)
                v[x] = columns[(x * size) + c];

            foreach (double[] q in basis)
            {
                double dot = 0;
                for (int x = 0; x < length; x++)
                    dot += v[x] * q[x];

                for (int x = 0; x < length; x++)
                    v[x] -= dot * q[x];
            }

            double norm = Math.Sqrt(v.Sum(e => e * e));
            if (norm < 1e-9)
                continue;

            for (int x = 0; x < length; x++)
                v[x] /= norm;

            basis.Add(v);
        }

        float[] result = new float[length * basis.Count];
        for (int x = 0; x < length; x++)
            for (int i = 0; i < basis.Count; i++)
                result[(x * basis.Count) + i] = (float)basis[i][x];

        return (result, basis.Count);
    }

    public AstcPartitions Partitions(int count)
    {
        AstcPartitions? table = Volatile.Read(ref _partitions[count]);
        if (table != null)
            return table;

        table = BuildPartitions(count);
        Interlocked.CompareExchange(ref _partitions[count], table, null);
        return _partitions[count]!;
    }

    private AstcGrid BuildGrid(int gw, int gh)
    {
        int[] texelGrid = new int[Texels * 4];
        int[] texelFactor = new int[Texels * 4];
        bool identity = gw == Width && gh == Height;

        int scaleX = (1024 + (Width / 2)) / (Width - 1);
        int scaleY = (1024 + (Height / 2)) / (Height - 1);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int at = ((y * Width) + x) * 4;

                if (identity)
                {
                    texelGrid[at] = (y * Width) + x;
                    texelFactor[at] = 16;
                    continue;
                }

                int gx = ((scaleX * x * (gw - 1)) + 32) >> 6;
                int gy = ((scaleY * y * (gh - 1)) + 32) >> 6;
                int jx = gx >> 4, jy = gy >> 4;
                int fx = gx & 0xF, fy = gy & 0xF;

                int w11 = ((fx * fy) + 8) >> 4;
                int nextX = Math.Min(jx + 1, gw - 1);
                int nextY = Math.Min(jy + 1, gh - 1);

                texelGrid[at] = jx + (jy * gw);
                texelFactor[at] = 16 - fx - fy + w11;
                texelGrid[at + 1] = nextX + (jy * gw);
                texelFactor[at + 1] = fx - w11;
                texelGrid[at + 2] = jx + (nextY * gw);
                texelFactor[at + 2] = fy - w11;
                texelGrid[at + 3] = nextX + (nextY * gw);
                texelFactor[at + 3] = w11;
            }
        }

        int count = gw * gh;
        float[] squareSum = new float[count];
        for (int k = 0; k < Texels * 4; k++)
            squareSum[texelGrid[k]] += texelFactor[k] * texelFactor[k] / 256f;

        return new AstcGrid
        {
            Width = gw,
            Height = gh,
            Identity = identity,
            TexelGrid = texelGrid,
            TexelFactor = texelFactor,
            GridSquareSum = squareSum,
        };
    }

    private AstcPartitions BuildPartitions(int count)
    {
        byte[] assign = new byte[1024 * Texels];
        int[] groupSizes = new int[1024 * count];
        ulong[] masks = new ulong[1024 * count * Words];
        List<int> patterns = [];
        HashSet<(ulong, ulong)> seen = [];
        bool small = Texels < 31;
        Span<int> sizes = stackalloc int[4];
        Span<int> relabel = stackalloc int[4];

        for (int id = 0; id < 1024; id++)
        {
            sizes.Clear();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int p = Astc.Partition(id, x, y, count, small);
                    assign[(id * Texels) + (y * Width) + x] = (byte)p;
                    sizes[p]++;
                }
            }

            bool empty = false;
            for (int p = 0; p < count; p++)
                empty |= sizes[p] == 0;

            if (empty)
                continue;

            // Patterns that only rename the groups of an earlier one add nothing.
            relabel.Fill(-1);
            int next = 0;
            ulong hashA = 14695981039346656037UL, hashB = 1099511628211UL;
            for (int t = 0; t < Texels; t++)
            {
                int p = assign[(id * Texels) + t];
                if (relabel[p] < 0)
                    relabel[p] = next++;

                hashA = (hashA ^ (ulong)relabel[p]) * 1099511628211UL;
                hashB = (hashB * 31) + (ulong)relabel[p] + 1;
            }

            if (!seen.Add((hashA, hashB)))
                continue;

            for (int t = 0; t < Texels; t++)
            {
                int p = assign[(id * Texels) + t];
                masks[(((id * count) + p) * Words) + (t >> 6)] |= 1UL << (t & 63);
            }

            for (int p = 0; p < count; p++)
                groupSizes[(id * count) + p] = sizes[p];

            patterns.Add(id);
        }

        return new AstcPartitions { Patterns = [.. patterns], Assign = assign, Masks = masks, Sizes = groupSizes };
    }
}

/// <summary>A 128 bit block under construction.</summary>
internal struct AstcBits
{
    public ulong Low;
    public ulong High;

    public void Write(int at, int count, uint value)
    {
        if (count == 0)
            return;

        ulong v = value & ((1UL << count) - 1);
        if (at < 64)
        {
            Low |= v << at;
            if (at + count > 64)
                High |= v >> (64 - at);
        }
        else
        {
            High |= v << (at - 64);
        }
    }

    /// <summary>Writes values of one range the way the decoder reads them, trits and quints packed in groups.</summary>
    public void WriteSequence(int range, ReadOnlySpan<byte> values, int at)
    {
        ReadOnlySpan<sbyte> table = AstcTables.Ranges;
        int bits = table[range * 3];
        uint mask = (1u << bits) - 1;

        if (table[(range * 3) + 1] != 0)
        {
            ReadOnlySpan<byte> widths = [2, 2, 1, 2, 1];
            for (int start = 0; start < values.Length; start += 5)
            {
                int count = Math.Min(values.Length - start, 5);
                int key = 0;
                for (int i = count - 1; i >= 0; i--)
                    key = (key * 3) + (values[start + i] >> bits);

                uint packed = AstcQuant.TritPack[count - 1][key];
                for (int i = 0, shift = 0; i < count; i++)
                {
                    Write(at, bits, values[start + i] & mask);
                    at += bits;
                    Write(at, widths[i], packed >> shift);
                    at += widths[i];
                    shift += widths[i];
                }
            }

            return;
        }

        if (table[(range * 3) + 2] != 0)
        {
            ReadOnlySpan<byte> widths = [3, 2, 2];
            for (int start = 0; start < values.Length; start += 3)
            {
                int count = Math.Min(values.Length - start, 3);
                int key = 0;
                for (int i = count - 1; i >= 0; i--)
                    key = (key * 5) + (values[start + i] >> bits);

                uint packed = AstcQuant.QuintPack[count - 1][key];
                for (int i = 0, shift = 0; i < count; i++)
                {
                    Write(at, bits, values[start + i] & mask);
                    at += bits;
                    Write(at, widths[i], packed >> shift);
                    at += widths[i];
                    shift += widths[i];
                }
            }

            return;
        }

        foreach (byte value in values)
        {
            Write(at, bits, value);
            at += bits;
        }
    }

    public static ulong Reverse(ulong value)
    {
        value = ((value >> 1) & 0x5555555555555555UL) | ((value & 0x5555555555555555UL) << 1);
        value = ((value >> 2) & 0x3333333333333333UL) | ((value & 0x3333333333333333UL) << 2);
        value = ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL) | ((value & 0x0F0F0F0F0F0F0F0FUL) << 4);
        return System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);
    }
}

/// <summary>Quantisation tables for endpoints and weights, built from the decoder's own unquantisation.</summary>
internal static class AstcQuant
{
    public static readonly byte[][] EndpointQuantise = new byte[21][];
    public static readonly byte[][] EndpointValue = new byte[21][];
    public static readonly byte[][] EndpointUp = new byte[21][];
    public static readonly byte[][] EndpointDown = new byte[21][];
    public static readonly float[] EndpointNoise = new float[21];
    public static readonly float[] WeightNoise = new float[12];

    public static readonly byte[][] WeightQuantise = new byte[12][];
    public static readonly byte[][] WeightValue = new byte[12][];
    public static readonly float[][] WeightRounded = new float[12][];

    public static readonly byte[][] TritPack = new byte[5][];
    public static readonly byte[][] QuintPack = new byte[3][];

    private static readonly sbyte[] Ranges = new sbyte[19 * 129];

    static AstcQuant()
    {
        for (int range = 4; range <= 20; range++)
        {
            int levels = AstcTables.Levels[range];
            byte[] value = new byte[levels];
            for (int raw = 0; raw < levels; raw++)
                value[raw] = (byte)Astc.DequantiseEndpoint((uint)raw, range);

            int[] order = Enumerable.Range(0, levels).OrderBy(raw => value[raw]).ToArray();
            byte[] up = new byte[levels];
            byte[] down = new byte[levels];
            for (int i = 0; i < levels; i++)
            {
                up[order[i]] = (byte)order[Math.Min(i + 1, levels - 1)];
                down[order[i]] = (byte)order[Math.Max(i - 1, 0)];
            }

            byte[] quantise = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                int best = 0;
                for (int raw = 1; raw < levels; raw++)
                {
                    if (Math.Abs(value[raw] - v) < Math.Abs(value[best] - v))
                        best = raw;
                }

                quantise[v] = (byte)best;
            }

            float step = 255f / (levels - 1);
            EndpointValue[range] = value;
            EndpointUp[range] = up;
            EndpointDown[range] = down;
            EndpointQuantise[range] = quantise;
            EndpointNoise[range] = (2f / 3f) * step * step / 12f;
        }

        for (int range = 0; range < 12; range++)
        {
            int levels = AstcTables.Levels[range];
            byte[] value = new byte[levels];
            for (int raw = 0; raw < levels; raw++)
                value[raw] = (byte)Astc.DequantiseWeight((uint)raw, range);

            byte[] quantise = new byte[1025];
            float[] rounded = new float[1025];
            for (int i = 0; i <= 1024; i++)
            {
                float target = i / 16f;
                int best = 0;
                for (int raw = 1; raw < levels; raw++)
                {
                    if (MathF.Abs(value[raw] - target) < MathF.Abs(value[best] - target))
                        best = raw;
                }

                quantise[i] = (byte)best;
                rounded[i] = value[best] / 64f;
            }

            WeightNoise[range] = 1f / ((levels - 1) * (levels - 1) * 12f);
            WeightValue[range] = value;
            WeightQuantise[range] = quantise;
            WeightRounded[range] = rounded;
        }

        ReadOnlySpan<int> tritBits = [2, 4, 5, 7, 8];
        for (int count = 1; count <= 5; count++)
            TritPack[count - 1] = BuildPack(AstcTables.Trits, 5, 3, count, tritBits[count - 1]);

        ReadOnlySpan<int> quintBits = [3, 5, 7];
        for (int count = 1; count <= 3; count++)
            QuintPack[count - 1] = BuildPack(AstcTables.Quints, 3, 5, count, quintBits[count - 1]);

        for (int values = 1; values <= 18; values++)
        {
            for (int remaining = 0; remaining <= 128; remaining++)
            {
                sbyte found = -1;
                for (int k = 20; k >= 4; k--)
                {
                    if (Astc.SequenceBits(values, k) <= remaining)
                    {
                        found = (sbyte)k;
                        break;
                    }
                }

                Ranges[(values * 129) + remaining] = found;
            }
        }
    }

    /// <summary>The largest endpoint range that fits <paramref name="values"/> in the bits left, or minus one.</summary>
    public static int EndpointRange(int values, int remaining) =>
        values > 18 || remaining < 0 ? -1 : Ranges[(values * 129) + Math.Min(remaining, 128)];

    /// <summary>
    /// For a group of <paramref name="count"/> digits, the packing whose first written bits decode
    /// to them, since the decoder never reads the bits of digits past the end of a sequence.
    /// </summary>
    private static byte[] BuildPack(ReadOnlySpan<byte> table, int group, int radix, int count, int written)
    {
        int keys = 1;
        for (int i = 0; i < count; i++)
            keys *= radix;

        byte[] pack = new byte[keys];
        bool[] found = new bool[keys];

        for (int packed = 0; packed < (1 << written); packed++)
        {
            int key = 0;
            for (int i = count - 1; i >= 0; i--)
                key = (key * radix) + table[(packed * group) + i];

            if (!found[key])
            {
                found[key] = true;
                pack[key] = (byte)packed;
            }
        }

        return pack;
    }
}
