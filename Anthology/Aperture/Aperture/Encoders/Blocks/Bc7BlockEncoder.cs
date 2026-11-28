// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.Intrinsics;

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

    // The closest stored value to each byte, by stored width then by any, even or odd bottom bit.
    private static readonly byte[] Nearest = BuildNearestTable();

    // For each group count less one, shape and group, a bit per pixel in that group.
    private static readonly ushort[] GroupMasks = BuildGroupMasks();

    // For each index width and level k from 1, the least weight nearer level k than level k - 1:
    // one more than half the sum of their two weights, rounded down.
    private static readonly byte[] LevelSteps = BuildStepTable();

    // Per pixel: a count, then four channel values from offset 4, then the full four by four
    // table of pairwise products from offset 8, a row of four per channel.
    private const int MomentStride = 24;

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

        // The laid out copies belong to the previous block until the first fit lays this one out.
        s.Rotation = -1;

        int channels = opaque ? 3 : 4;
        bool prepared = false;
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
                    if (!prepared)
                    {
                        PrepareMoments(s, channels);
                        prepared = true;
                    }

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
        Rotate(s, c.Rotation);

        long error = 0;

        if (m.AlphaIndexBits != 0)
        {
            // Colour and alpha each get their own endpoints and their own indices.
            for (int i = 0; i < 16; i++)
            {
                s.Members[i] = i;
                s.MemberMask[i] = -1;
            }

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
                    bool member = partitions[i] == group;
                    s.MemberMask[i] = member ? -1 : 0;
                    if (member)
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
                Vector<int> sum = Vector<int>.Zero;
                for (int at = 0; at < 16; at += Vector<int>.Count)
                {
                    Vector<int> d = new Vector<int>(255) - new Vector<int>(s.Planes, (3 * 16) + at);
                    sum += d * d;
                }

                error += Vector.Sum(sum);
            }
        }

        c.Error = error;
    }

    /// <summary>
    /// Lays the block out for the given rotation, which swaps a colour channel with alpha before
    /// encoding; the decoder swaps it back. Fills both the per-pixel and per-channel copies.
    /// </summary>
    private static void Rotate(Scratch s, int rotation)
    {
        if (s.Rotation == rotation)
            return;

        s.Rotation = rotation;
        for (int c = 0; c < 4; c++)
        {
            int from = rotation == 0 ? c : c == 3 ? rotation - 1 : c == rotation - 1 ? 3 : c;
            for (int i = 0; i < 16; i++)
            {
                int value = s.Source[(i * 4) + from];
                s.Pixels[(i * 4) + c] = value;
                s.Planes[(c * 16) + i] = value;
            }
        }
    }

    /// <summary>
    /// Fits one pair of endpoints to the member pixels over the given channels, writing the stored
    /// endpoint values and each member's index. Returns the squared error.
    /// </summary>
    private long FitGroup(Scratch s, int count, int first, int channels, int bits, Parity parity, int indexBits,
                          int searchRounds, int[] endpoints, int at0, int at1, int[] indices)
    {
        Vector128<float> mask = ChannelMask(channels);
        PrincipalAxis(s, count, first, channels, out Vector128<float> mean, out Vector128<float> axis);

        float low = float.MaxValue;
        float high = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            float t = SumChannels((LoadPixel(s.Pixels, s.Members[k], first, mask) - mean) * axis, channels);
            low = Math.Min(low, t);
            high = Math.Max(high, t);
        }

        Span<float> a = stackalloc float[4];
        Span<float> b = stackalloc float[4];
        (mean + (axis * low)).CopyTo(a);
        (mean + (axis * high)).CopyTo(b);

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
            CopyMembers(s, s.TrialIndices, indices);
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
                        long error = Evaluate(s, first, channels, bits, indexBits, try0, try1, s.EvalIndices, best);
                        if (error < best)
                        {
                            best = error;
                            try0.CopyTo(best0);
                            try1.CopyTo(best1);
                            CopyMembers(s, s.EvalIndices, indices);
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

            long error = Evaluate(s, first, channels, bits, indexBits, t0, t1, s.EvalIndices, Math.Min(best, limit));
            if (error < best && error < limit)
            {
                best = error;
                t0.CopyTo(q0);
                t1.CopyTo(q1);
                CopyMembers(s, s.EvalIndices, indices);
            }
        }

        return best;
    }

    /// <summary>
    /// Picks the nearest palette entry for each member, writing every pixel's choice to
    /// <paramref name="indices"/> (only members' entries mean anything) and returning the members'
    /// squared error, or some error of at least <paramref name="limit"/> once it is clear the
    /// endpoints cannot beat it. The palette lies along the line between the endpoints, so each
    /// pixel's projection onto that line names the likely entry and only it and its neighbours are
    /// measured, lowest first so that ties keep the lower entry. A vector of pixels is handled at
    /// a time, with each pixel's candidate entries worked out in its own lane rather than looked up.
    /// </summary>
    private static long Evaluate(Scratch s, int first, int channels, int bits, int indexBits,
                                 ReadOnlySpan<int> q0, ReadOnlySpan<int> q1, int[] indices, long limit) => channels switch
    {
        1 => Evaluate<OneChannel>(s, first, bits, indexBits, q0, q1, indices, limit),
        3 => Evaluate<ThreeChannels>(s, first, bits, indexBits, q0, q1, indices, limit),
        _ => Evaluate<FourChannels>(s, first, bits, indexBits, q0, q1, indices, limit),
    };

    /// <summary><see cref="Evaluate(Scratch, int, int, int, int, ReadOnlySpan{int}, ReadOnlySpan{int}, int[], long)"/> compiled for a fixed number of channels.</summary>
    private static long Evaluate<TChannels>(Scratch s, int first, int bits, int indexBits,
                                            ReadOnlySpan<int> q0, ReadOnlySpan<int> q1, int[] indices, long limit)
        where TChannels : struct, IChannelCount
    {
        int channels = TChannels.Count;
        int top = (1 << indexBits) - 1;
        Span<int> low = stackalloc int[4];
        Span<int> d = stackalloc int[4];
        low.Clear();
        d.Clear();
        int dd = 0;

        for (int c = 0; c < channels; c++)
        {
            low[c] = Expand(q0[c], bits);
            d[c] = Expand(q1[c], bits) - low[c];
            dd += d[c] * d[c];
        }

        // Channels past the group's own stay zero and are never read.
        Vector<int> low0 = new(low[0]), low1 = new(low[1]), low2 = new(low[2]), low3 = new(low[3]);
        Vector<int> d0 = new(d[0]), d1 = new(d[1]), d2 = new(d[2]), d3 = new(d[3]);

        // An entry is low + (high - low) * weight / 64, rounded: the same sum the decoder makes.
        Vector<int> base0 = new((low[0] * 64) + 32), base1 = new((low[1] * 64) + 32);
        Vector<int> base2 = new((low[2] * 64) + 32), base3 = new((low[3] * 64) + 32);

        // Twice a pixel's weight, round(64 * dot / dd), passes the midpoint between levels k - 1
        // and k exactly when 64 * dot reaches this; the nearest level counts the midpoints passed.
        Span<int> thresholds = stackalloc int[16];
        ReadOnlySpan<byte> steps = LevelSteps.AsSpan(indexBits * 16, 16);
        for (int k = 1; k <= top; k++)
            thresholds[k] = (steps[k] * dd) - (dd >> 1);

        Vector<int> topv = new(top);
        Vector<float> weightScale = new(64f / top);
        Vector<float> rounding = new(0.5f);
        Vector<int> total = Vector<int>.Zero;
        int[] planes = s.Planes;

        for (int at = 0; at < 16; at += Vector<int>.Count)
        {
            Vector<int> member = new(s.MemberMask, at);
            if (member == Vector<int>.Zero)
                continue;

            Vector<int> p0 = new(planes, (first * 16) + at);
            Vector<int> p1 = channels > 1 ? new(planes, ((first + 1) * 16) + at) : default;
            Vector<int> p2 = channels > 2 ? new(planes, ((first + 2) * 16) + at) : default;
            Vector<int> p3 = channels > 3 ? new(planes, ((first + 3) * 16) + at) : default;

            // The entry whose weight is nearest, then it and its neighbours in turn. With both
            // endpoints the same every entry is too, and the first is the one kept.
            Vector<int> nearest = Vector<int>.Zero;
            if (dd != 0)
            {
                Vector<int> dot = (p0 - low0) * d0;
                if (channels > 1)
                    dot += ((p1 - low1) * d1) + ((p2 - low2) * d2);
                if (channels > 3)
                    dot += (p3 - low3) * d3;

                Vector<int> scaled = dot << 6;
                for (int k = 1; k <= top; k++)
                    nearest -= Vector.GreaterThanOrEqual(scaled, new Vector<int>(thresholds[k]));
            }

            Vector<int> bestError = new(int.MaxValue);
            Vector<int> bestLevel = Vector<int>.Zero;

            for (int offset = -1; offset <= 1; offset++)
            {
                Vector<int> level = nearest + new Vector<int>(offset);
                Vector<int> valid = Vector.GreaterThanOrEqual(level, Vector<int>.Zero) & Vector.LessThanOrEqual(level, topv);
                Vector<int> w = Truncate((Vector.ConvertToSingle(level) * weightScale) + rounding);

                Vector<int> e0 = ((base0 + (d0 * w)) >> 6) - p0;
                Vector<int> error = e0 * e0;
                if (channels > 1)
                {
                    Vector<int> e1 = ((base1 + (d1 * w)) >> 6) - p1;
                    Vector<int> e2 = ((base2 + (d2 * w)) >> 6) - p2;
                    error += (e1 * e1) + (e2 * e2);
                }

                if (channels > 3)
                {
                    Vector<int> e3 = ((base3 + (d3 * w)) >> 6) - p3;
                    error += e3 * e3;
                }

                Vector<int> better = valid & Vector.LessThan(error, bestError);
                bestError = Vector.ConditionalSelect(better, error, bestError);
                bestLevel = Vector.ConditionalSelect(better, level, bestLevel);
            }

            total += bestError & member;
            bestLevel.CopyTo(indices, at);

            if (at + Vector<int>.Count < 16 && Vector.Sum(total) >= limit)
                break;
        }

        return Vector.Sum(total);
    }

    /// <summary>
    /// Solves for the endpoints that best reproduce the members given their current indices, or
    /// returns false when every member has the same weight and the endpoints are undetermined.
    /// </summary>
    private static bool LeastSquares(Scratch s, int count, int first, int channels, int indexBits, int[] indices,
                                     Span<float> a, Span<float> b)
    {
        ReadOnlySpan<byte> weights = Weights(indexBits);
        Vector128<float> mask = ChannelMask(channels);
        Vector128<float> ax = Vector128<float>.Zero;
        Vector128<float> bx = Vector128<float>.Zero;
        float aa = 0, ab = 0, bb = 0;

        for (int k = 0; k < count; k++)
        {
            int member = s.Members[k];
            float w = weights[indices[member]] / 64f;
            float v = 1 - w;
            aa += v * v;
            ab += v * w;
            bb += w * w;

            Vector128<float> x = LoadPixel(s.Pixels, member, first, mask);
            ax += Vector128.Create(v) * x;
            bx += Vector128.Create(w) * x;
        }

        float det = (aa * bb) - (ab * ab);
        if (Math.Abs(det) < 1e-6f)
            return false;

        for (int c = 0; c < channels; c++)
        {
            a[c] = Math.Clamp(((bb * ax.GetElement(c)) - (ab * bx.GetElement(c))) / det, 0, 255);
            b[c] = Math.Clamp(((aa * bx.GetElement(c)) - (ab * ax.GetElement(c))) / det, 0, 255);
        }

        return true;
    }

    /// <summary>
    /// Finds the mean and dominant direction of the members by power iteration on their covariance.
    /// Lane <c>c</c> of each result is channel <c>first + c</c>; lanes past the group's channels are zero.
    /// Sums run over the members in order, one lane per channel, so they round exactly as a
    /// channel-at-a-time loop would.
    /// </summary>
    private static void PrincipalAxis(Scratch s, int count, int first, int channels,
                                      out Vector128<float> mean, out Vector128<float> axis)
    {
        Vector128<float> mask = ChannelMask(channels);
        float[] pixels = s.Pixels;
        int[] members = s.Members;

        Vector128<float> sum = Vector128<float>.Zero;
        for (int k = 0; k < count; k++)
            sum += LoadPixel(pixels, members[k], first, mask);

        mean = sum / Vector128.Create((float)count);
        axis = Vector128<float>.Zero;

        // The covariance comes out exactly symmetric, so its rows double as columns.
        Covariance cov = default;
        for (int k = 0; k < count; k++)
            cov.Add(LoadPixel(pixels, members[k], first, mask) - mean, channels);

        int widest = cov.Widest(channels, out _);
        if (cov.Row(widest).GetElement(widest) <= 0)
            return;

        // Starting from the widest channel's column keeps the start off any axis orthogonal to
        // the answer, since that column is the covariance applied to that channel's direction.
        axis = cov.Row(widest);

        for (int iteration = 0; iteration < 8; iteration++)
        {
            float length = MathF.Sqrt(SumChannels(axis * axis, channels));
            if (length < 1e-12f)
                break;

            axis /= Vector128.Create(length);
            axis = cov.Apply(axis, channels);
        }

        float norm = MathF.Sqrt(SumChannels(axis * axis, channels));
        if (norm > 0)
            axis /= Vector128.Create(norm);
    }

    /// <summary>Rounds each lane toward zero; every caller's values are well inside the range of an int.</summary>
    private static Vector<int> Truncate(Vector<float> value)
#if NET9_0_OR_GREATER
        => Vector.ConvertToInt32Native(value);
#else
        => Vector.ConvertToInt32(value);
#endif

    /// <summary>Loads one pixel's channels <paramref name="first"/> onward into the low lanes, zeroing the rest.</summary>
    private static Vector128<float> LoadPixel(float[] pixels, int pixel, int first, Vector128<float> mask)
        => first == 0
            ? Vector128.LoadUnsafe(ref pixels[pixel * 4]) & mask
            : Vector128.CreateScalar(pixels[(pixel * 4) + first]);

    /// <summary>All ones in the lanes below <paramref name="channels"/>, zero above.</summary>
    private static Vector128<float> ChannelMask(int channels)
        => Vector128.LessThan(Vector128.Create(0, 1, 2, 3), Vector128.Create(channels)).AsSingle();

    /// <summary>Adds the low <paramref name="channels"/> lanes in order, rounding as a scalar loop would.</summary>
    private static float SumChannels(Vector128<float> v, int channels)
    {
        float sum = 0;
        sum += v.ToScalar();
        if (channels > 1)
        {
            sum += v.GetElement(1);
            sum += v.GetElement(2);
            if (channels > 3)
                sum += v.GetElement(3);
        }

        return sum;
    }

    /// <summary>Each pixel's count, channels and pairwise products, so a group's covariance is a sum of rows.</summary>
    private static void PrepareMoments(Scratch s, int channels)
    {
        int[] m = s.Moments;
        int[] totals = s.Totals;
        Array.Clear(m);
        Array.Clear(totals);

        for (int i = 0; i < 16; i++)
        {
            int at = i * MomentStride;
            m[at] = 1;

            for (int a = 0; a < channels; a++)
            {
                int x = s.Source[(i * 4) + a];
                m[at + 4 + a] = x;

                for (int b = 0; b < channels; b++)
                    m[at + 8 + (a * 4) + b] = x * s.Source[(i * 4) + b];
            }

            for (int t = 0; t < MomentStride; t += 4)
                (Vector128.Create(totals.AsSpan(t)) + Vector128.Create(m.AsSpan(at + t))).CopyTo(totals.AsSpan(t));
        }
    }

    /// <summary>
    /// Orders the shapes of a two or three group split by how far each group's pixels fall from
    /// a line through them: the covariance's trace less its largest eigenvalue. Shapes are
    /// estimated a vector at a time, one per lane; every lane rounds exactly as it would alone.
    /// </summary>
    private static void RankShapes(Scratch s, int groups, int channels, int[] ranking)
    {
        int[] sums = s.ShapeSums;
        int[] moments = s.Moments;
        int[] totals = s.Totals;
        ReadOnlySpan<ushort> masks = GroupMasks.AsSpan(groups * 64 * 3, 64 * 3);

        for (int shape = 0; shape < 64; shape++)
        {
            Span<int> group0 = sums.AsSpan(shape * 3 * MomentStride, MomentStride);
            Span<int> group1 = sums.AsSpan(((shape * 3) + 1) * MomentStride, MomentStride);
            Span<int> group2 = sums.AsSpan(((shape * 3) + 2) * MomentStride, MomentStride);
            SumMoments(moments, masks[(shape * 3) + 1], group1);
            if (groups == 2)
                SumMoments(moments, masks[(shape * 3) + 2], group2);
            else
                group2.Clear();

            // Group 0 is whatever the others leave.
            for (int t = 0; t < MomentStride; t += 4)
            {
                Vector128<int> rest = Vector128.Create(group1[t..]) + Vector128.Create(group2[t..]);
                (Vector128.Create(totals.AsSpan(t)) - rest).CopyTo(group0[t..]);
            }
        }

        if (channels == 3)
            EstimateShapes<ThreeChannels>(s, groups);
        else
            EstimateShapes<FourChannels>(s, groups);

        for (int shape = 0; shape < 64; shape++)
            ranking[shape] = shape;

        s.Estimates.AsSpan().Sort(ranking.AsSpan());
    }

    /// <summary>Adds up the moments of the pixels whose bits are set in <paramref name="members"/>.</summary>
    private static void SumMoments(int[] moments, int members, Span<int> to)
    {
        Vector128<int> a0 = default, a1 = default, a2 = default, a3 = default, a4 = default, a5 = default;

        while (members != 0)
        {
            ReadOnlySpan<int> m = moments.AsSpan(BitOperations.TrailingZeroCount(members) * MomentStride, MomentStride);
            members &= members - 1;
            a0 += Vector128.Create(m);
            a1 += Vector128.Create(m[4..]);
            a2 += Vector128.Create(m[8..]);
            a3 += Vector128.Create(m[12..]);
            a4 += Vector128.Create(m[16..]);
            a5 += Vector128.Create(m[20..]);
        }

        a0.CopyTo(to);
        a1.CopyTo(to[4..]);
        a2.CopyTo(to[8..]);
        a3.CopyTo(to[12..]);
        a4.CopyTo(to[16..]);
        a5.CopyTo(to[20..]);
    }

    /// <summary>
    /// Fills <see cref="Scratch.Estimates"/> from the summed moments of every shape's groups. Within
    /// a lane this is a few power steps from the widest channel's column, then the trace less the
    /// Rayleigh quotient. The quotient never exceeds the largest eigenvalue, so an unconverged
    /// estimate only errs high. Where one shape would stop early its lane is simply left alone.
    /// </summary>
    private static void EstimateShapes<TChannels>(Scratch s, int groups)
        where TChannels : struct, IChannelCount
    {
        int channels = TChannels.Count;
        int width = Vector<float>.Count;
        Span<int> lanes = stackalloc int[Vector<float>.Count];
        Span<Vector<float>> sum = stackalloc Vector<float>[4];
        Span<Vector<float>> cov = stackalloc Vector<float>[16];
        Span<Vector<float>> v = stackalloc Vector<float>[4];
        Span<Vector<float>> next = stackalloc Vector<float>[4];
        Vector<float> zero = Vector<float>.Zero;

        for (int first = 0; first < 64; first += width)
        {
            Vector<float> estimate = zero;

            for (int group = 0; group <= groups; group++)
            {
                int at = group * MomentStride;
                Vector<float> n = GatherSums(s.ShapeSums, first, at, lanes);
                for (int a = 0; a < channels; a++)
                    sum[a] = GatherSums(s.ShapeSums, first, at + 4 + a, lanes);

                for (int a = 0; a < channels; a++)
                {
                    for (int b = a; b < channels; b++)
                    {
                        Vector<float> products = GatherSums(s.ShapeSums, first, at + 8 + (a * 4) + b, lanes);
                        cov[(b * 4) + a] = cov[(a * 4) + b] = products - ((sum[a] * sum[b]) / n);
                    }
                }

                // The widest channel, first on ties, and the sum of the variances.
                Vector<float> trace = zero;
                trace += cov[0];
                Vector<float> most = cov[0];
                Vector<int> widest = Vector<int>.Zero;
                for (int a = 1; a < channels; a++)
                {
                    Vector<float> variance = cov[(a * 4) + a];
                    trace += variance;
                    Vector<int> wider = Vector.GreaterThan(variance, most);
                    widest = Vector.ConditionalSelect(wider, new Vector<int>(a), widest);
                    most = Vector.ConditionalSelect(wider, variance, most);
                }

                // Groups with no spread add nothing.
                Vector<int> active = ~Vector.LessThanOrEqual(most, zero);
                if (active == Vector<int>.Zero)
                    continue;

                for (int a = 0; a < channels; a++)
                {
                    v[a] = cov[a * 4];
                    for (int b = 1; b < channels; b++)
                        v[a] = Vector.ConditionalSelect(Vector.Equals(widest, new Vector<int>(b)), cov[(a * 4) + b], v[a]);
                }

                Vector<int> spread = active;
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    Multiply<TChannels>(cov, v, next);

                    Vector<float> length = Vector.Max(zero, Vector.Abs(next[0]));
                    for (int a = 1; a < channels; a++)
                        length = Vector.Max(length, Vector.Abs(next[a]));

                    active &= ~Vector.LessThanOrEqual(length, zero);
                    for (int a = 0; a < channels; a++)
                        v[a] = Vector.ConditionalSelect(active, next[a] / length, v[a]);
                }

                Multiply<TChannels>(cov, v, next);
                Vector<float> vv = zero, vcv = zero;
                for (int a = 0; a < channels; a++)
                {
                    vv += v[a] * v[a];
                    vcv += v[a] * next[a];
                }

                Vector<float> quotient = Vector.ConditionalSelect(Vector.GreaterThan(vv, zero), vcv / vv, zero);
                estimate += Vector.ConditionalSelect(spread, Vector.Max(zero, trace - quotient), zero);
            }

            estimate.CopyTo(s.Estimates, first);
        }
    }

    /// <summary>Lane by lane, the covariance times <paramref name="v"/>, each entry summed over channels in order.</summary>
    private static void Multiply<TChannels>(ReadOnlySpan<Vector<float>> cov, ReadOnlySpan<Vector<float>> v, Span<Vector<float>> result)
        where TChannels : struct, IChannelCount
    {
        for (int a = 0; a < TChannels.Count; a++)
        {
            Vector<float> total = Vector<float>.Zero;
            for (int b = 0; b < TChannels.Count; b++)
                total += cov[(b * 4) + a] * v[b];

            result[a] = total;
        }
    }

    /// <summary>One summed moment of one group, for the shapes starting at <paramref name="first"/>, one per lane.</summary>
    private static Vector<float> GatherSums(int[] sums, int first, int offset, Span<int> lanes)
    {
        for (int j = 0; j < lanes.Length; j++)
            lanes[j] = sums[((first + j) * 3 * MomentStride) + offset];

        return Vector.ConvertToSingle(new Vector<int>(lanes));
    }

    /// <summary>
    /// A symmetric covariance of up to four channels, one row per vector. Unused rows and lanes
    /// stay zero. Lane sums run in channel order so that every result rounds as a loop over
    /// channels would.
    /// </summary>
    private struct Covariance
    {
        /// <summary>The rows for each channel, in order.</summary>
        public Vector128<float> R0, R1, R2, R3;

        /// <summary>Adds the outer product of one pixel's offset from the mean.</summary>
        public void Add(Vector128<float> d, int channels)
        {
            R0 += Vector128.Create(d.GetElement(0)) * d;
            if (channels > 1)
            {
                R1 += Vector128.Create(d.GetElement(1)) * d;
                R2 += Vector128.Create(d.GetElement(2)) * d;
                if (channels > 3)
                    R3 += Vector128.Create(d.GetElement(3)) * d;
            }
        }

        /// <summary>Row (and so column) <paramref name="i"/>.</summary>
        public readonly Vector128<float> Row(int i) => i switch
        {
            0 => R0,
            1 => R1,
            2 => R2,
            _ => R3,
        };

        /// <summary>The channel with the largest variance, first on ties, and the sum of the variances.</summary>
        public readonly int Widest(int channels, out float trace)
        {
            float d0 = R0.ToScalar();
            trace = 0;
            trace += d0;
            if (channels == 1)
                return 0;

            float d1 = R1.GetElement(1), d2 = R2.GetElement(2);
            trace += d1;
            trace += d2;
            int widest = 0;
            float most = d0;
            if (d1 > most)
                (widest, most) = (1, d1);
            if (d2 > most)
                (widest, most) = (2, d2);
            if (channels > 3)
            {
                float d3 = R3.GetElement(3);
                trace += d3;
                if (d3 > most)
                    widest = 3;
            }

            return widest;
        }

        /// <summary>The covariance times <paramref name="v"/>, each lane summed over channels in order.</summary>
        public readonly Vector128<float> Apply(Vector128<float> v, int channels)
        {
            Vector128<float> result = Vector128<float>.Zero;
            result += R0 * Vector128.Create(v.GetElement(0));
            if (channels > 1)
            {
                result += R1 * Vector128.Create(v.GetElement(1));
                result += R2 * Vector128.Create(v.GetElement(2));
                if (channels > 3)
                    result += R3 * Vector128.Create(v.GetElement(3));
            }

            return result;
        }
    }

    /// <summary>Packs the candidate's mode, shape, endpoints and indices into the 128 bit block.</summary>
    private static void Write(Candidate c, Span<byte> output)
    {
        ModeInfo m = Modes[c.Mode];
        FixAnchors(c, m);

        UInt128 block = 0;
        int at = 0;
        WriteBits(ref block, ref at, 1 << c.Mode, c.Mode + 1);
        WriteBits(ref block, ref at, c.Shape, m.ShapeBits);
        WriteBits(ref block, ref at, c.Rotation, m.RotationBits);
        WriteBits(ref block, ref at, c.Selector, m.SelectorBits);

        int endpoints = (m.Groups + 1) * 2;
        for (int channel = 0; channel < 4; channel++)
        {
            int precision = channel < 3 ? m.ColourBits : m.AlphaBits;
            int full = channel < 3 ? m.ColourFull : m.AlphaFull;
            if (precision == 0)
                continue;

            for (int i = 0; i < endpoints; i++)
                WriteBits(ref block, ref at, c.Endpoints[(i * 4) + channel] >> (full - precision), precision);
        }

        // Every channel of an endpoint carries the same bottom bit, so the red one stands for all.
        for (int j = 0; j < m.ParityBits; j++)
            WriteBits(ref block, ref at, c.Endpoints[(j * endpoints / m.ParityBits) * 4] & 1, 1);

        for (int i = 0; i < 16; i++)
            WriteBits(ref block, ref at, c.Primary[i], IsAnchor(m.Groups, c.Shape, i) ? m.IndexBits - 1 : m.IndexBits);

        if (m.AlphaIndexBits != 0)
        {
            for (int i = 0; i < 16; i++)
                WriteBits(ref block, ref at, c.Secondary[i], i == 0 ? m.AlphaIndexBits - 1 : m.AlphaIndexBits);
        }

        BinaryPrimitives.WriteUInt64LittleEndian(output, (ulong)block);
        BinaryPrimitives.WriteUInt64LittleEndian(output[8..], (ulong)(block >> 64));
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
    private static void WriteBits(ref UInt128 block, ref int at, int value, int count)
    {
        block |= (UInt128)(uint)(value & ((1 << count) - 1)) << at;
        at += count;
    }

    /// <summary>Copies the current members' entries of <paramref name="from"/> into <paramref name="to"/>.</summary>
    private static void CopyMembers(Scratch s, int[] from, int[] to)
    {
        for (int at = 0; at < 16; at += Vector<int>.Count)
        {
            Vector<int> member = new(s.MemberMask, at);
            Vector.ConditionalSelect(member, new Vector<int>(from, at), new Vector<int>(to, at)).CopyTo(to, at);
        }
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

    /// <summary>Builds <see cref="GroupMasks"/> from the partition table.</summary>
    private static ushort[] BuildGroupMasks()
    {
        ushort[] table = new ushort[3 * 64 * 3];

        for (int groups = 0; groups < 3; groups++)
        {
            for (int shape = 0; shape < 64; shape++)
            {
                ReadOnlySpan<byte> row = BlockTables.Partitions.Slice(((groups * 64) + shape) * 16, 16);
                for (int i = 0; i < 16; i++)
                    table[(((groups * 64) + shape) * 3) + row[i]] |= (ushort)(1 << i);
            }
        }

        return table;
    }

    /// <summary>Builds <see cref="LevelSteps"/> for two, three and four bit indices.</summary>
    private static byte[] BuildStepTable()
    {
        byte[] table = new byte[5 * 16];

        for (int bits = 2; bits <= 4; bits++)
        {
            ReadOnlySpan<byte> weights = Weights(bits);
            for (int k = 1; k < weights.Length; k++)
                table[(bits * 16) + k] = (byte)(((weights[k - 1] + weights[k]) / 2) + 1);
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

    /// <summary>A number of channels fixed at compile time, so that loops over them unroll.</summary>
    private interface IChannelCount
    {
        static abstract int Count { get; }
    }

    /// <summary>Alpha on its own.</summary>
    private readonly struct OneChannel : IChannelCount
    {
        public static int Count => 1;
    }

    /// <summary>Red, green and blue.</summary>
    private readonly struct ThreeChannels : IChannelCount
    {
        public static int Count => 3;
    }

    /// <summary>All four channels.</summary>
    private readonly struct FourChannels : IChannelCount
    {
        public static int Count => 4;
    }

    /// <summary>One row of the mode table; the full widths include the parity bit where there is one.</summary>
    private readonly record struct ModeInfo(int Groups, int ShapeBits, int ParityBits, int RotationBits, int SelectorBits,
                                            int IndexBits, int AlphaIndexBits, int ColourBits, int AlphaBits,
                                            int ColourFull, int AlphaFull);

    /// <summary>One way of encoding the block: endpoints by endpoint then channel, at their full stored width.</summary>
    private sealed class Candidate
    {
        public int Mode; // Which of the eight modes, 0 to 7.
        public int Shape; // The partition shape, for modes that split the block into groups.
        public int Rotation; // Which colour channel swaps with alpha, 1 to 3, or 0 for none.
        public int Selector; // For mode 4, whether colour takes the wider index set.
        public long Error; // The squared error over the block, or long.MaxValue if abandoned.
        public readonly int[] Endpoints = new int[6 * 4]; // Up to six endpoints of four channels each.
        public readonly int[] Primary = new int[16]; // Each pixel's index, or its colour index in modes 4 and 5.
        public readonly int[] Secondary = new int[16]; // Each pixel's alpha index in modes 4 and 5.
    }

    /// <summary>One thread's working buffers, reused for every block it encodes.</summary>
    private sealed class Scratch
    {
        public readonly int[] Source = new int[64]; // The block as given, pixel by pixel.
        public readonly float[] Pixels = new float[64]; // The block as rotated for the current fit, pixel by pixel.
        public readonly int[] Planes = new int[64]; // The same values channel by channel: all sixteen reds, then greens, blues and alphas.
        public readonly int[] Members = new int[16]; // The pixels in the group being fitted, in order.
        public readonly int[] MemberMask = new int[16]; // All ones for each pixel in the group being fitted, zero for the rest.
        public int Rotation = -1; // The rotation Pixels and Planes are laid out for, or -1 for none yet.
        public readonly int[] EvalIndices = new int[16]; // Indices from the latest evaluation, kept only if it wins.
        public readonly int[] TrialIndices = new int[16]; // Indices from the latest least squares pass, kept only if it wins.
        public readonly float[] Estimates = new float[64]; // Each shape's estimated spread from a line, sorted alongside its ranking.
        public readonly int[] Moments = new int[16 * MomentStride]; // Each source pixel's moments, laid out as MomentStride describes.
        public readonly int[] Totals = new int[MomentStride]; // The moments summed over the whole block.
        public readonly int[] ShapeSums = new int[64 * 3 * MomentStride]; // For each shape and group, the summed moments of the group's pixels.
        public readonly int[] Ranking2 = new int[64]; // Two group shapes, most promising first.
        public readonly int[] Ranking3 = new int[64]; // Three group shapes, most promising first.
        public Candidate Best = new(); // The best encoding found so far.
        public Candidate Trial = new(); // The encoding being fitted; swapped with Best when it wins.
    }
}
