// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Aperture.Decoders.Dds;

namespace Prowl.Aperture.Encoders.Blocks;

/// <summary>
/// BC7: eight modes trading endpoint precision, index precision and the number of pixel groups.
/// Partition shapes are ranked by how far each group's pixels fall from a line, and the best few
/// are fitted for real. Each group starts on its principal axis and is refined by least squares
/// against its own index choice, trying every parity bit combination the mode allows. The block
/// that wins is then nudged an endpoint step at a time while that lowers the error.
/// </summary>
internal sealed class Bc7BlockEncoder : IBlockEncoder
{
    private enum Parity
    {
        None,
        PerEndpoint,
        Shared,
    }

    private static readonly ModeInfo[] Modes = BuildModes();

    /// <summary>The closest stored value to each byte, by stored width then by any, even or odd bottom bit.</summary>
    private static readonly byte[] Nearest = BuildNearestTable();

    /// <summary>For each index width, the index whose weight is nearest each weight from 0 to 64.</summary>
    private static readonly byte[] LevelOfWeight = BuildLevelTable();

    /// <summary>A count, four channel sums and ten pairwise products per pixel.</summary>
    private const int MomentStride = 15;

    // Single region modes come first so that blocks they already fit well can skip the rest.
    // Mode 7 is left out for opaque blocks, where mode 3 has the same layout at more precision.
    private static readonly int[] OpaqueModes = [6, 5, 4, 1, 3, 0, 2];
    private static readonly int[] AlphaModes = [6, 5, 4, 7];
    private static readonly int[] FastOpaqueModes = [6, 5, 1];
    private static readonly int[] FastAlphaModes = [6, 5, 7];

    private readonly bool _fast;
    private readonly int _shapes;
    private readonly int _refinements;
    private readonly int _searchRounds;

    /// <summary>Squared error per pixel and channel, in quarters, below which partitioned modes are skipped.</summary>
    private readonly int _partitionThreshold;

    /// <summary>
    /// Creates an encoder that searches as hard as <paramref name="quality"/> asks.
    /// </summary>
    /// <param name="quality">How many modes, shapes and refinement passes to try per block.</param>
    public Bc7BlockEncoder(TextureEncodeQuality quality)
    {
        switch (quality)
        {
            case TextureEncodeQuality.Fast:
                _fast = true;
                _shapes = 1;
                _refinements = 1;
                _searchRounds = 0;
                _partitionThreshold = 8;
                break;

            case TextureEncodeQuality.Balanced:
                _shapes = 4;
                _refinements = 2;
                _searchRounds = 1;
                _partitionThreshold = 2;
                break;

            default:
                _shapes = 16;
                _refinements = 4;
                _searchRounds = 8;
                break;
        }
    }

    /// <summary>
    /// Creates the working buffers one thread needs to encode blocks.
    /// </summary>
    /// <returns>A new scratch object to pass to <see cref="EncodeBlock"/> on the same thread.</returns>
    public object CreateScratch() => new Scratch();

    /// <summary>
    /// Encodes a single 4x4 block of RGBA8 pixels into a 16 byte BC7 block.
    /// </summary>
    /// <param name="pixels">The 16 source pixels, in row-major order, four bytes each.</param>
    /// <param name="output">The buffer that receives the 16 byte encoded block.</param>
    /// <param name="scratch">Working buffers from <see cref="CreateScratch"/>, owned by the calling thread.</param>
    public void EncodeBlock(ReadOnlySpan<byte> pixels, Span<byte> output, object scratch)
    {
        Scratch s = (Scratch)scratch;
        bool opaque = true;

        for (int i = 0; i < 64; i++)
            s.Source[i] = pixels[i];

        for (int i = 0; i < 16; i++)
            opaque &= pixels[(i * 4) + 3] == 255;

        int channels = opaque ? 3 : 4;
        PrepareMoments(s, channels);
        bool ranked2 = false, ranked3 = false;

        // Below this error a block is near enough lossless that splitting it is not worth trying.
        long partitionThreshold = _partitionThreshold * 4L * channels;

        s.Best.Error = long.MaxValue;
        int[] modes = _fast ? (opaque ? FastOpaqueModes : FastAlphaModes) : (opaque ? OpaqueModes : AlphaModes);
        int rotations = _fast ? 1 : 4;

        foreach (int mode in modes)
        {
            ModeInfo m = Modes[mode];

            if (mode == 4)
            {
                for (int rotation = 0; rotation < rotations; rotation++)
                {
                    Try(s, mode, 0, rotation, 0);
                    Try(s, mode, 0, rotation, 1);
                }
            }
            else if (mode == 5)
            {
                for (int rotation = 0; rotation < rotations; rotation++)
                    Try(s, mode, 0, rotation, 0);
            }
            else if (m.Groups == 0)
            {
                Try(s, mode, 0, 0, 0);
            }
            else
            {
                if (s.Best.Error <= partitionThreshold)
                    break;

                int[] ranking = m.Groups == 1 ? s.Ranking2 : s.Ranking3;
                ref bool ranked = ref (m.Groups == 1 ? ref ranked2 : ref ranked3);
                if (!ranked)
                {
                    RankShapes(s, m.Groups, channels, ranking);
                    ranked = true;
                }

                int limit = 1 << m.ShapeBits;
                int tried = 0;

                for (int r = 0; r < 64 && tried < _shapes; r++)
                {
                    if (ranking[r] >= limit)
                        continue;

                    Try(s, mode, ranking[r], 0, 0);
                    tried++;
                }
            }

            if (s.Best.Error == 0)
                break;
        }

        if (_searchRounds > 0 && s.Best.Error > 0)
        {
            Candidate trial = s.Trial;
            trial.Mode = s.Best.Mode;
            trial.Shape = s.Best.Shape;
            trial.Rotation = s.Best.Rotation;
            trial.Selector = s.Best.Selector;
            Fit(s, trial, _searchRounds, s.Best.Error);

            if (trial.Error < s.Best.Error)
                (s.Best, s.Trial) = (s.Trial, s.Best);
        }

        Write(s.Best, output);
    }

    /// <summary>Fits one mode, shape, rotation and index selector, keeping it if it beats the best so far.</summary>
    private void Try(Scratch s, int mode, int shape, int rotation, int selector)
    {
        Candidate trial = s.Trial;
        trial.Mode = mode;
        trial.Shape = shape;
        trial.Rotation = rotation;
        trial.Selector = selector;
        Fit(s, trial, 0, s.Best.Error);

        if (trial.Error < s.Best.Error)
            (s.Best, s.Trial) = (s.Trial, s.Best);
    }

    /// <summary>
    /// Fits every group of the candidate's mode, shape and rotation, filling in its endpoints and
    /// indices. Groups add their errors, so once the finished ones reach <paramref name="limit"/>
    /// the candidate cannot win and is abandoned.
    /// </summary>
    private void Fit(Scratch s, Candidate c, int searchRounds, long limit)
    {
        ModeInfo m = Modes[c.Mode];

        // A rotation swaps a colour channel with alpha before encoding; the decoder swaps it back.
        for (int i = 0; i < 64; i++)
            s.Pixels[i] = s.Source[i];

        if (c.Rotation != 0)
        {
            for (int i = 0; i < 16; i++)
            {
                int channel = (i * 4) + c.Rotation - 1;
                (s.Pixels[channel], s.Pixels[(i * 4) + 3]) = (s.Pixels[(i * 4) + 3], s.Pixels[channel]);
            }
        }

        long error = 0;

        if (m.AlphaIndexBits != 0)
        {
            // Colour and alpha each get their own endpoints and their own indices.
            for (int i = 0; i < 16; i++)
                s.Members[i] = i;

            bool swapped = c.Selector != 0;
            error += FitGroup(s, 16, 0, 3, m.ColourFull, Parity.None, swapped ? m.AlphaIndexBits : m.IndexBits,
                searchRounds, c.Endpoints, 0, 4, swapped ? c.Secondary : c.Primary);
            if (error >= limit)
            {
                c.Error = long.MaxValue;
                return;
            }

            error += FitGroup(s, 16, 3, 1, m.AlphaFull, Parity.None, swapped ? m.IndexBits : m.AlphaIndexBits,
                searchRounds, c.Endpoints, 0, 4, swapped ? c.Primary : c.Secondary);
        }
        else
        {
            int channels = m.AlphaBits != 0 ? 4 : 3;
            int endpoints = (m.Groups + 1) * 2;
            Parity parity = m.ParityBits == 0 ? Parity.None : m.ParityBits == endpoints ? Parity.PerEndpoint : Parity.Shared;
            ReadOnlySpan<byte> partitions = BlockTables.Partitions.Slice(((m.Groups * 64) + c.Shape) * 16, 16);

            for (int group = 0; group <= m.Groups; group++)
            {
                int count = 0;
                for (int i = 0; i < 16; i++)
                {
                    if (partitions[i] == group)
                        s.Members[count++] = i;
                }

                error += FitGroup(s, count, 0, channels, m.ColourFull, parity, m.IndexBits, searchRounds,
                    c.Endpoints, group * 8, (group * 8) + 4, c.Primary);
                if (error >= limit)
                {
                    c.Error = long.MaxValue;
                    return;
                }
            }

            // Modes without alpha decode it as opaque.
            if (channels == 3)
            {
                for (int i = 0; i < 16; i++)
                {
                    int d = 255 - s.Pixels[(i * 4) + 3];
                    error += d * d;
                }
            }
        }

        c.Error = error;
    }

    /// <summary>
    /// Fits one pair of endpoints to the member pixels over the given channels, writing the stored
    /// endpoint values and each member's index. Returns the squared error.
    /// </summary>
    private long FitGroup(Scratch s, int count, int first, int channels, int bits, Parity parity, int indexBits,
                          int searchRounds, int[] endpoints, int at0, int at1, int[] indices)
    {
        Span<float> mean = stackalloc float[4];
        Span<float> axis = stackalloc float[4];
        PrincipalAxis(s.Pixels, s.Members, count, first, channels, mean, axis);

        float low = float.MaxValue;
        float high = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            int p = s.Members[k] * 4;
            float t = 0;
            for (int c = 0; c < channels; c++)
                t += (s.Pixels[p + first + c] - mean[c]) * axis[c];

            low = Math.Min(low, t);
            high = Math.Max(high, t);
        }

        Span<float> a = stackalloc float[4];
        Span<float> b = stackalloc float[4];
        for (int c = 0; c < channels; c++)
        {
            a[c] = mean[c] + (axis[c] * low);
            b[c] = mean[c] + (axis[c] * high);
        }

        Span<int> best0 = stackalloc int[4];
        Span<int> best1 = stackalloc int[4];
        Span<int> try0 = stackalloc int[4];
        Span<int> try1 = stackalloc int[4];

        long best = QuantiseAndEvaluate(s, count, first, channels, bits, parity, indexBits, a, b, best0, best1, indices, long.MaxValue);

        for (int pass = 0; pass < _refinements && best > 0; pass++)
        {
            if (!LeastSquares(s, count, first, channels, indexBits, indices, a, b))
                break;

            long error = QuantiseAndEvaluate(s, count, first, channels, bits, parity, indexBits, a, b, try0, try1, s.TrialIndices, best);
            if (error >= best)
                break;

            best = error;
            try0.CopyTo(best0);
            try1.CopyTo(best1);
            CopyMembers(s, s.TrialIndices, indices, count);
        }

        // Step one stored value at a time, by two where a parity bit must stay put.
        int step = parity == Parity.None ? 1 : 2;
        int max = (1 << bits) - 1;

        for (int round = 0; round < searchRounds && best > 0; round++)
        {
            bool improved = false;

            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                for (int c = 0; c < channels; c++)
                {
                    for (int direction = -step; direction <= step; direction += 2 * step)
                    {
                        best0.CopyTo(try0);
                        best1.CopyTo(try1);
                        Span<int> target = endpoint == 0 ? try0 : try1;
                        int value = target[c] + direction;
                        if (value < 0 || value > max)
                            continue;

                        target[c] = value;
                        long error = Evaluate(s, count, first, channels, bits, indexBits, try0, try1, s.EvalIndices, best);
                        if (error < best)
                        {
                            best = error;
                            try0.CopyTo(best0);
                            try1.CopyTo(best1);
                            CopyMembers(s, s.EvalIndices, indices, count);
                            improved = true;
                        }
                    }
                }
            }

            if (!improved)
                break;
        }

        for (int c = 0; c < channels; c++)
        {
            endpoints[at0 + first + c] = best0[c];
            endpoints[at1 + first + c] = best1[c];
        }

        return best;
    }

    /// <summary>
    /// Rounds both endpoints to stored values under each parity choice the mode allows, keeping the
    /// best. Returns <see cref="long.MaxValue"/> and writes nothing if no choice beats the limit.
    /// </summary>
    private static long QuantiseAndEvaluate(Scratch s, int count, int first, int channels, int bits, Parity parity, int indexBits,
                                            ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<int> q0, Span<int> q1,
                                            int[] indices, long limit)
    {
        Span<int> t0 = stackalloc int[4];
        Span<int> t1 = stackalloc int[4];
        long best = long.MaxValue;

        int combinations = parity switch
        {
            Parity.None => 1,
            Parity.Shared => 2,
            _ => 4,
        };

        for (int combination = 0; combination < combinations; combination++)
        {
            // Zero is unconstrained, one even and two odd.
            int first0, first1;
            if (parity == Parity.None)
                (first0, first1) = (0, 0);
            else if (parity == Parity.Shared)
                (first0, first1) = (1 + combination, 1 + combination);
            else
                (first0, first1) = (1 + (combination & 1), 1 + (combination >> 1));

            int row0 = ((bits * 3) + first0) * 256;
            int row1 = ((bits * 3) + first1) * 256;
            for (int c = 0; c < channels; c++)
            {
                t0[c] = Nearest[row0 + ToByte(a[c])];
                t1[c] = Nearest[row1 + ToByte(b[c])];
            }

            long error = Evaluate(s, count, first, channels, bits, indexBits, t0, t1, s.EvalIndices, Math.Min(best, limit));
            if (error < best && error < limit)
            {
                best = error;
                t0.CopyTo(q0);
                t1.CopyTo(q1);
                CopyMembers(s, s.EvalIndices, indices, count);
            }
        }

        return best;
    }

    /// <summary>
    /// Picks the nearest palette entry for each member, stopping early once past <paramref name="limit"/>.
    /// The palette lies along the line between the endpoints, so each pixel's projection onto that
    /// line names the likely entry and only it and its neighbours are measured.
    /// </summary>
    private static long Evaluate(Scratch s, int count, int first, int channels, int bits, int indexBits,
                                 ReadOnlySpan<int> q0, ReadOnlySpan<int> q1, int[] indices, long limit)
    {
        ReadOnlySpan<byte> weights = Weights(indexBits);
        ReadOnlySpan<byte> levelOf = LevelOfWeight.AsSpan(indexBits * 65, 65);
        int top = (1 << indexBits) - 1;
        Span<int> palette = stackalloc int[16 * 4];
        Span<int> e0 = stackalloc int[4];
        Span<int> d = stackalloc int[4];
        int dd = 0;

        for (int c = 0; c < channels; c++)
        {
            int low = Expand(q0[c], bits);
            int high = Expand(q1[c], bits);
            e0[c] = low;
            d[c] = high - low;
            dd += d[c] * d[c];

            for (int l = 0; l <= top; l++)
                palette[(l * 4) + c] = ((low * (64 - weights[l])) + (high * weights[l]) + 32) >> 6;
        }

        int[] pixels = s.Pixels;
        long total = 0;

        for (int k = 0; k < count; k++)
        {
            int member = s.Members[k];
            int p = (member * 4) + first;

            int guess = 0;
            if (dd != 0)
            {
                int dot = 0;
                for (int c = 0; c < channels; c++)
                    dot += (pixels[p + c] - e0[c]) * d[c];

                guess = levelOf[Math.Clamp(((dot * 64) + (dd >> 1)) / dd, 0, 64)];
            }

            int bestError = int.MaxValue;
            int bestLevel = guess;
            int from = Math.Max(0, guess - 1);
            int to = dd != 0 ? Math.Min(top, guess + 1) : 0;

            for (int l = from; l <= to; l++)
            {
                int error = 0;
                for (int c = 0; c < channels; c++)
                {
                    int diff = palette[(l * 4) + c] - pixels[p + c];
                    error += diff * diff;
                }

                if (error < bestError)
                {
                    bestError = error;
                    bestLevel = l;
                }
            }

            indices[member] = bestLevel;
            total += bestError;
            if (total >= limit)
                return total;
        }

        return total;
    }

    /// <summary>
    /// Solves for the endpoints that best reproduce the members given their current indices, or
    /// returns false when every member has the same weight and the endpoints are undetermined.
    /// </summary>
    private static bool LeastSquares(Scratch s, int count, int first, int channels, int indexBits, int[] indices,
                                     Span<float> a, Span<float> b)
    {
        ReadOnlySpan<byte> weights = Weights(indexBits);
        Span<float> ax = stackalloc float[4];
        Span<float> bx = stackalloc float[4];
        ax.Clear();
        bx.Clear();
        float aa = 0, ab = 0, bb = 0;

        for (int k = 0; k < count; k++)
        {
            int member = s.Members[k];
            float w = weights[indices[member]] / 64f;
            float v = 1 - w;
            aa += v * v;
            ab += v * w;
            bb += w * w;

            for (int c = 0; c < channels; c++)
            {
                float x = s.Pixels[(member * 4) + first + c];
                ax[c] += v * x;
                bx[c] += w * x;
            }
        }

        float det = (aa * bb) - (ab * ab);
        if (Math.Abs(det) < 1e-6f)
            return false;

        for (int c = 0; c < channels; c++)
        {
            a[c] = Math.Clamp(((bb * ax[c]) - (ab * bx[c])) / det, 0, 255);
            b[c] = Math.Clamp(((aa * bx[c]) - (ab * ax[c])) / det, 0, 255);
        }

        return true;
    }

    /// <summary>
    /// Finds the mean and dominant direction of the members by power iteration on their covariance,
    /// returning how far the members sit from the line that makes.
    /// </summary>
    private static float PrincipalAxis(int[] pixels, int[] members, int count, int first, int channels,
                                       Span<float> mean, Span<float> axis)
    {
        mean.Clear();
        axis.Clear();

        for (int k = 0; k < count; k++)
        {
            for (int c = 0; c < channels; c++)
                mean[c] += pixels[(members[k] * 4) + first + c];
        }

        for (int c = 0; c < channels; c++)
            mean[c] /= count;

        Span<float> cov = stackalloc float[16];
        cov.Clear();
        for (int k = 0; k < count; k++)
        {
            int p = (members[k] * 4) + first;
            for (int i = 0; i < channels; i++)
            {
                float di = pixels[p + i] - mean[i];
                for (int j = i; j < channels; j++)
                    cov[(i * 4) + j] += di * (pixels[p + j] - mean[j]);
            }
        }

        float trace = 0;
        int widest = 0;
        for (int i = 0; i < channels; i++)
        {
            for (int j = 0; j < i; j++)
                cov[(i * 4) + j] = cov[(j * 4) + i];

            trace += cov[(i * 4) + i];
            if (cov[(i * 4) + i] > cov[(widest * 4) + widest])
                widest = i;
        }

        if (cov[(widest * 4) + widest] <= 0)
            return 0;

        // Starting from the widest channel's column keeps the start off any axis orthogonal to
        // the answer, since that column is the covariance applied to that channel's direction.
        Span<float> next = stackalloc float[4];
        for (int i = 0; i < channels; i++)
            axis[i] = cov[(i * 4) + widest];

        float eigenvalue = 0;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            float length = 0;
            for (int i = 0; i < channels; i++)
                length += axis[i] * axis[i];

            length = MathF.Sqrt(length);
            if (length < 1e-12f)
                break;

            for (int i = 0; i < channels; i++)
                axis[i] /= length;

            next.Clear();
            for (int i = 0; i < channels; i++)
            {
                for (int j = 0; j < channels; j++)
                    next[i] += cov[(i * 4) + j] * axis[j];
            }

            eigenvalue = 0;
            for (int i = 0; i < channels; i++)
                eigenvalue += next[i] * axis[i];

            next[..channels].CopyTo(axis);
        }

        float norm = 0;
        for (int i = 0; i < channels; i++)
            norm += axis[i] * axis[i];

        norm = MathF.Sqrt(norm);
        if (norm > 0)
        {
            for (int i = 0; i < channels; i++)
                axis[i] /= norm;
        }

        // What is left after the principal axis: how far the members sit from a line.
        return Math.Max(0, trace - eigenvalue);
    }

    /// <summary>Each pixel's count, channels and pairwise products, so a group's covariance is a sum of rows.</summary>
    private static void PrepareMoments(Scratch s, int channels)
    {
        int[] m = s.Moments;
        int[] totals = s.Totals;
        Array.Clear(totals);

        for (int i = 0; i < 16; i++)
        {
            int at = i * MomentStride;
            m[at] = 1;
            int k = at + 1 + channels;

            for (int a = 0; a < channels; a++)
            {
                int x = s.Source[(i * 4) + a];
                m[at + 1 + a] = x;

                for (int b = a; b < channels; b++)
                    m[k++] = x * s.Source[(i * 4) + b];
            }

            for (int t = 0; t < MomentStride; t++)
                totals[t] += m[at + t];
        }
    }

    /// <summary>
    /// Orders the shapes of a two or three group split by how far each group's pixels fall from
    /// a line through them: the covariance's trace less its largest eigenvalue.
    /// </summary>
    private static void RankShapes(Scratch s, int groups, int channels, int[] ranking)
    {
        ReadOnlySpan<byte> partitions = BlockTables.Partitions;
        int terms = 1 + channels + ((channels * (channels + 1)) / 2);
        Span<int> sums = stackalloc int[3 * MomentStride];
        Span<float> cov = stackalloc float[16];
        Span<float> v = stackalloc float[4];
        Span<float> next = stackalloc float[4];
        int[] moments = s.Moments;

        for (int shape = 0; shape < 64; shape++)
        {
            ReadOnlySpan<byte> row = partitions.Slice(((groups * 64) + shape) * 16, 16);
            sums.Clear();

            for (int i = 0; i < 16; i++)
            {
                if (row[i] == 0)
                    continue;

                int to = row[i] * MomentStride;
                int from = i * MomentStride;
                for (int t = 0; t < terms; t++)
                    sums[to + t] += moments[from + t];
            }

            for (int t = 0; t < terms; t++)
            {
                int rest = sums[MomentStride + t] + (groups == 2 ? sums[(2 * MomentStride) + t] : 0);
                sums[t] = s.Totals[t] - rest;
            }

            float estimate = 0;
            for (int group = 0; group <= groups; group++)
            {
                int at = group * MomentStride;
                float n = sums[at];
                float trace = 0;
                int k = at + 1 + channels;
                int widest = 0;

                for (int a = 0; a < channels; a++)
                {
                    for (int b = a; b < channels; b++)
                    {
                        float value = sums[k++] - (sums[at + 1 + a] * (float)sums[at + 1 + b] / n);
                        cov[(a * 4) + b] = value;
                        cov[(b * 4) + a] = value;
                    }

                    trace += cov[(a * 4) + a];
                    if (cov[(a * 4) + a] > cov[(widest * 4) + widest])
                        widest = a;
                }

                if (cov[(widest * 4) + widest] <= 0)
                    continue;

                // A few power steps from the widest channel's column. The Rayleigh quotient never
                // exceeds the largest eigenvalue, so an unconverged estimate only errs high.
                for (int a = 0; a < channels; a++)
                    v[a] = cov[(a * 4) + widest];

                for (int iteration = 0; iteration < 3; iteration++)
                {
                    float length = 0;
                    for (int a = 0; a < channels; a++)
                    {
                        float sum = 0;
                        for (int b = 0; b < channels; b++)
                            sum += cov[(a * 4) + b] * v[b];

                        next[a] = sum;
                        length = Math.Max(length, Math.Abs(sum));
                    }

                    if (length <= 0)
                        break;

                    for (int a = 0; a < channels; a++)
                        v[a] = next[a] / length;
                }

                float vv = 0, vcv = 0;
                for (int a = 0; a < channels; a++)
                {
                    float sum = 0;
                    for (int b = 0; b < channels; b++)
                        sum += cov[(a * 4) + b] * v[b];

                    vv += v[a] * v[a];
                    vcv += v[a] * sum;
                }

                estimate += Math.Max(0, trace - (vv > 0 ? vcv / vv : 0));
            }

            s.Estimates[shape] = estimate;
            ranking[shape] = shape;
        }

        s.Estimates.AsSpan().Sort(ranking.AsSpan());
    }

    /// <summary>Packs the candidate's mode, shape, endpoints and indices into the 128 bit block.</summary>
    private static void Write(Candidate c, Span<byte> output)
    {
        ModeInfo m = Modes[c.Mode];
        FixAnchors(c, m);

        output[..16].Clear();
        int at = 0;
        WriteBits(output, ref at, 1 << c.Mode, c.Mode + 1);
        WriteBits(output, ref at, c.Shape, m.ShapeBits);
        WriteBits(output, ref at, c.Rotation, m.RotationBits);
        WriteBits(output, ref at, c.Selector, m.SelectorBits);

        int endpoints = (m.Groups + 1) * 2;
        for (int channel = 0; channel < 4; channel++)
        {
            int precision = channel < 3 ? m.ColourBits : m.AlphaBits;
            int full = channel < 3 ? m.ColourFull : m.AlphaFull;
            if (precision == 0)
                continue;

            for (int i = 0; i < endpoints; i++)
                WriteBits(output, ref at, c.Endpoints[(i * 4) + channel] >> (full - precision), precision);
        }

        // Every channel of an endpoint carries the same bottom bit, so the red one stands for all.
        for (int j = 0; j < m.ParityBits; j++)
            WriteBits(output, ref at, c.Endpoints[(j * endpoints / m.ParityBits) * 4] & 1, 1);

        for (int i = 0; i < 16; i++)
            WriteBits(output, ref at, c.Primary[i], IsAnchor(m.Groups, c.Shape, i) ? m.IndexBits - 1 : m.IndexBits);

        if (m.AlphaIndexBits != 0)
        {
            for (int i = 0; i < 16; i++)
                WriteBits(output, ref at, c.Secondary[i], i == 0 ? m.AlphaIndexBits - 1 : m.AlphaIndexBits);
        }
    }

    /// <summary>
    /// Each group's anchor index is stored without its top bit, so where that bit is set the
    /// group's endpoints are swapped and its indices mirrored. The weights are symmetric, so the
    /// block decodes the same.
    /// </summary>
    private static void FixAnchors(Candidate c, ModeInfo m)
    {
        if (m.AlphaIndexBits != 0)
        {
            bool swapped = c.Selector != 0;
            FixSeparate(c, c.Primary, m.IndexBits, swapped ? 3 : 0, swapped ? 1 : 3);
            FixSeparate(c, c.Secondary, m.AlphaIndexBits, swapped ? 0 : 3, swapped ? 3 : 1);
            return;
        }

        ReadOnlySpan<byte> partitions = BlockTables.Partitions.Slice(((m.Groups * 64) + c.Shape) * 16, 16);
        int top = (1 << m.IndexBits) - 1;

        for (int group = 0; group <= m.Groups; group++)
        {
            int anchor = BlockTables.FixUps[(((m.Groups * 64) + c.Shape) * 3) + group];
            if ((c.Primary[anchor] >> (m.IndexBits - 1)) == 0)
                continue;

            for (int i = 0; i < 16; i++)
            {
                if (partitions[i] == group)
                    c.Primary[i] = top - c.Primary[i];
            }

            for (int channel = 0; channel < 4; channel++)
            {
                int low = (group * 8) + channel;
                (c.Endpoints[low], c.Endpoints[low + 4]) = (c.Endpoints[low + 4], c.Endpoints[low]);
            }
        }
    }

    /// <summary>
    /// Applies the anchor rule to one of the two index sets of mode 4 or 5, whose anchor is always pixel 0.
    /// </summary>
    private static void FixSeparate(Candidate c, int[] indices, int bits, int firstChannel, int channels)
    {
        if ((indices[0] >> (bits - 1)) == 0)
            return;

        int top = (1 << bits) - 1;
        for (int i = 0; i < 16; i++)
            indices[i] = top - indices[i];

        for (int channel = firstChannel; channel < firstChannel + channels; channel++)
            (c.Endpoints[channel], c.Endpoints[channel + 4]) = (c.Endpoints[channel + 4], c.Endpoints[channel]);
    }

    /// <summary>Whether the pixel at <paramref name="offset"/> holds the index written a bit short in its group.</summary>
    private static bool IsAnchor(int groups, int shape, int offset)
    {
        for (int p = 0; p <= groups; p++)
        {
            if (BlockTables.FixUps[(((groups * 64) + shape) * 3) + p] == offset)
                return true;
        }

        return false;
    }

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/> at bit <paramref name="at"/>, lowest first, and advances it.</summary>
    private static void WriteBits(Span<byte> output, ref int at, int value, int count)
    {
        for (int i = 0; i < count; i++, at++)
        {
            if (((value >> i) & 1) != 0)
                output[at >> 3] |= (byte)(1 << (at & 7));
        }
    }

    private static void CopyMembers(Scratch s, int[] from, int[] to, int count)
    {
        for (int k = 0; k < count; k++)
            to[s.Members[k]] = from[s.Members[k]];
    }

    private static ReadOnlySpan<byte> Weights(int bits) => bits switch
    {
        2 => BlockTables.Weights2,
        3 => BlockTables.Weights3,
        _ => BlockTables.Weights4,
    };

    private static int ToByte(float value) => (int)(Math.Clamp(value, 0, 255) + 0.5f);

    /// <summary>Stretches a stored value to a byte the way the decoder does.</summary>
    private static int Expand(int value, int bits)
    {
        int shifted = (value << (8 - bits)) & 0xFF;
        return shifted | (shifted >> bits);
    }

    /// <summary>Builds <see cref="Nearest"/> by trying every stored value against every byte.</summary>
    private static byte[] BuildNearestTable()
    {
        byte[] table = new byte[9 * 3 * 256];

        for (int bits = 1; bits <= 8; bits++)
        {
            for (int constraint = 0; constraint < 3; constraint++)
            {
                for (int target = 0; target < 256; target++)
                {
                    int best = 0;
                    int bestError = int.MaxValue;

                    for (int value = 0; value < 1 << bits; value++)
                    {
                        if (constraint != 0 && (value & 1) != constraint - 1)
                            continue;

                        int error = Math.Abs(Expand(value, bits) - target);
                        if (error < bestError)
                        {
                            bestError = error;
                            best = value;
                        }
                    }

                    table[(((bits * 3) + constraint) * 256) + target] = (byte)best;
                }
            }
        }

        return table;
    }

    /// <summary>Builds <see cref="LevelOfWeight"/> for two, three and four bit indices.</summary>
    private static byte[] BuildLevelTable()
    {
        byte[] table = new byte[5 * 65];

        for (int bits = 2; bits <= 4; bits++)
        {
            ReadOnlySpan<byte> weights = Weights(bits);
            for (int w = 0; w <= 64; w++)
            {
                int best = 0;
                for (int l = 1; l < weights.Length; l++)
                {
                    if (Math.Abs(weights[l] - w) < Math.Abs(weights[best] - w))
                        best = l;
                }

                table[(bits * 65) + w] = (byte)best;
            }
        }

        return table;
    }

    /// <summary>Reads the decoder's packed mode table into one <see cref="ModeInfo"/> per mode.</summary>
    private static ModeInfo[] BuildModes()
    {
        ReadOnlySpan<byte> table = BlockTables.Bc7Modes;
        ModeInfo[] modes = new ModeInfo[8];

        for (int mode = 0; mode < 8; mode++)
        {
            int row = mode * 15;
            modes[mode] = new ModeInfo(
                Groups: table[row],
                ShapeBits: table[row + 1],
                ParityBits: table[row + 2],
                RotationBits: table[row + 3],
                SelectorBits: table[row + 4],
                IndexBits: table[row + 5],
                AlphaIndexBits: table[row + 6],
                ColourBits: table[row + 7],
                AlphaBits: table[row + 10],
                ColourFull: table[row + 11],
                AlphaFull: table[row + 14]);
        }

        return modes;
    }

    /// <summary>One row of the mode table; the full widths include the parity bit where there is one.</summary>
    private readonly record struct ModeInfo(int Groups, int ShapeBits, int ParityBits, int RotationBits, int SelectorBits,
                                            int IndexBits, int AlphaIndexBits, int ColourBits, int AlphaBits,
                                            int ColourFull, int AlphaFull);

    /// <summary>One way of encoding the block: endpoints by endpoint then channel, at their full stored width.</summary>
    private sealed class Candidate
    {
        public int Mode;
        public int Shape;
        public int Rotation;
        public int Selector;
        public long Error;
        public readonly int[] Endpoints = new int[6 * 4];
        public readonly int[] Primary = new int[16];
        public readonly int[] Secondary = new int[16];
    }

    private sealed class Scratch
    {
        public readonly int[] Source = new int[64];
        public readonly int[] Pixels = new int[64];
        public readonly int[] Members = new int[16];
        public readonly int[] EvalIndices = new int[16];
        public readonly int[] TrialIndices = new int[16];
        public readonly float[] Estimates = new float[64];
        public readonly int[] Moments = new int[16 * MomentStride];
        public readonly int[] Totals = new int[MomentStride];
        public readonly int[] Ranking2 = new int[64];
        public readonly int[] Ranking3 = new int[64];
        public Candidate Best = new();
        public Candidate Trial = new();
    }
}
