// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Photonic.Rasterization;
using Prowl.Photonic.Raytracing;
using Prowl.Vector;

namespace Prowl.Photonic.Integration;

/// <summary>
/// The interpolation half of sparse sampling: for every texel that does not trace, the handful of
/// traced points it should read from, and how much of each.
/// </summary>
/// <remarks>
/// Neighbours are chosen in world space, not atlas space. Two texels next to each other in the atlas
/// can be metres apart on the model, and two texels metres apart in the atlas can be touching in the
/// world, so the atlas is simply the wrong space to measure "near" in.
/// <para>Weights fall off with world distance and with how far the point's normal has turned away,
/// reaching zero at a right angle: past that the two texels face different ways and belong to
/// different lighting. On top of that a point is dimmed by any nearer point lying in roughly the same
/// direction from the texel, which drops to nothing once they line up. A point directly behind
/// another has nothing to add that the closer one does not already say, and it is usually behind it
/// because something solid is in the way.</para>
/// </remarks>
internal sealed class SparseNeighbours
{
    /// <summary>Points blended per texel.</summary>
    public const int MaxNeighbours = 8;

    /// <summary>Directional occlusion starts here and reaches full strength head-on.</summary>
    private const float OcclusionCosine = 0.5f;

    /// <summary>Texel indices of each texel's points, <see cref="MaxNeighbours"/> slots each, -1 for unused.</summary>
    public required int[] Index { get; init; }

    /// <summary>Matching weights, normalised to sum to 255 across a texel's slots.</summary>
    public required byte[] Weight { get; init; }

    /// <summary>Texels that found nothing to interpolate from and were made to trace instead.</summary>
    public int Isolated { get; private set; }

    public static SparseNeighbours Build(TargetWorkspace ws, SparseTexelSet points, Blas? blas, BakeOptions options,
                                         System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        int w = ws.Width, h = ws.Height;
        var neighbours = new SparseNeighbours
        {
            Index = new int[w * h * MaxNeighbours],
            Weight = new byte[w * h * MaxNeighbours],
        };
        System.Array.Fill(neighbours.Index, -1);

        var grid = PointGrid.Build(ws, points);
        var isolated = new bool[w * h];

        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            System.Span<int> found = stackalloc int[MaxNeighbours];
            System.Span<float> distance = stackalloc float[MaxNeighbours];
            System.Span<float> weight = stackalloc float[MaxNeighbours];

            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;

                var texel = ws.Samples[idx];
                int count = grid.FindNearest(ws, points, idx, texel, found, distance);
                if (count == 0)
                {
                    if (!points.IsPoint[idx]) isolated[idx] = true;
                    continue;
                }

                float total = Weigh(ws, points, blas, options, texel, found, distance, weight, count);
                if (total <= 0f)
                {
                    if (!points.IsPoint[idx]) isolated[idx] = true;
                    continue;
                }

                // Quantise to a byte each, correcting the last slot so the set still sums to 255 and
                // the blend keeps its total energy.
                int at = idx * MaxNeighbours;
                int spent = 0, last = -1;
                for (int i = 0; i < count; i++)
                {
                    if (weight[i] <= 0f) continue;
                    int q = (int)System.MathF.Round(weight[i] / total * 255f);
                    if (q <= 0) continue;
                    neighbours.Index[at + i] = found[i];
                    neighbours.Weight[at + i] = (byte)System.Math.Min(255, q);
                    spent += neighbours.Weight[at + i];
                    last = at + i;
                }
                if (last >= 0 && spent != 255)
                    neighbours.Weight[last] = (byte)System.Math.Clamp(neighbours.Weight[last] + (255 - spent), 1, 255);
                else if (last < 0 && !points.IsPoint[idx]) isolated[idx] = true;
            }
        });

        int extra = 0;
        for (int i = 0; i < isolated.Length; i++) if (isolated[i]) extra++;
        neighbours.Isolated = extra;
        if (extra > 0) points.AddPoints(isolated, extra);
        return neighbours;
    }

    /// <summary>
    /// Score each candidate: distance, how well the normals agree, whether anything solid is in the
    /// way, and how much a nearer point in the same direction already covers it.
    /// </summary>
    private static float Weigh(TargetWorkspace ws, SparseTexelSet points, Blas? blas, BakeOptions options,
                               in TexelSample texel, System.Span<int> found, System.Span<float> distance,
                               System.Span<float> weight, int count)
    {
        float floor = System.MathF.Max(texel.WorldRadius, 1e-5f);
        float reach = System.MathF.Max(distance[count - 1], floor) * 1.25f;

        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            var point = ws.Samples[found[i]];

            float facing = Float3.Dot(texel.Normal, point.Normal);
            if (facing <= 0f) { weight[i] = 0f; continue; }   // a right angle or worse: different surface

            float d = distance[i];
            float falloff = 1f - d / reach;
            if (falloff <= 0f) { weight[i] = 0f; continue; }

            // Dim by any nearer point sitting in the same direction. Candidates arrive sorted, so
            // everything before this one is closer.
            float shadow = 0f;
            var toPoint = Float3.NormalizeSafe(point.Position - texel.Position, texel.Normal);
            for (int j = 0; j < i && shadow < 1f; j++)
            {
                if (weight[j] <= 0f) continue;
                var toNearer = Float3.NormalizeSafe(ws.Samples[found[j]].Position - texel.Position, texel.Normal);
                float align = Float3.Dot(toNearer, toPoint);
                if (align <= OcclusionCosine) continue;
                float t = (align - OcclusionCosine) / (1f - OcclusionCosine);
                shadow = System.MathF.Max(shadow, t * t);
            }
            if (shadow >= 1f) { weight[i] = 0f; continue; }

            float value = falloff * falloff * falloff * facing * facing * (1f - shadow);

            if (value > 0f && blas is not null && distance[i] > floor * 3f)
            {
                // Both ends lift off their own surface first: a straight line between two points on
                // anything convex passes through the solid, and would report every sphere and tube as
                // blocking its own interpolation.
                float lift = System.MathF.Max(floor * 2f, distance[i] * 0.15f);
                var from = texel.Position + texel.Normal * lift;
                var to = point.Position + point.Normal * lift;
                var delta = to - from;
                float span = Float3.Length(delta);
                if (span > options.EffectiveRayBias * 8f
                    && blas.AnyHit(from, delta / span, options.EffectiveRayBias, span - options.EffectiveRayBias * 4f))
                    value = 0f;
            }

            weight[i] = value;
            total += value;
        }
        return total;
    }

    /// <summary>Blend every interpolated texel from its stored points.</summary>
    public void Apply(TargetWorkspace ws, SparseTexelSet points, System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        int w = ws.Width, h = ws.Height;
        var pixels = ws.Working();
        var scratch = ws.ScratchRGB();

        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;

                int at = idx * MaxNeighbours;
                float r = 0f, g = 0f, b = 0f, total = 0f;
                for (int i = 0; i < MaxNeighbours; i++)
                {
                    int point = Index[at + i];
                    if (point < 0) continue;
                    float weight = Weight[at + i];
                    r += pixels[point * 3] * weight;
                    g += pixels[point * 3 + 1] * weight;
                    b += pixels[point * 3 + 2] * weight;
                    total += weight;
                }

                if (total <= 0f)
                {
                    scratch[idx * 3] = pixels[idx * 3];
                    scratch[idx * 3 + 1] = pixels[idx * 3 + 1];
                    scratch[idx * 3 + 2] = pixels[idx * 3 + 2];
                    continue;
                }

                float inv = 1f / total;
                scratch[idx * 3] = r * inv;
                scratch[idx * 3 + 1] = g * inv;
                scratch[idx * 3 + 2] = b * inv;
            }
        });

        System.Threading.Tasks.Parallel.For(0, h, parallelOpts, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (!ws.Covered[idx]) continue;
                pixels[idx * 3] = scratch[idx * 3];
                pixels[idx * 3 + 1] = scratch[idx * 3 + 1];
                pixels[idx * 3 + 2] = scratch[idx * 3 + 2];
            }
        });
    }

    /// <summary>
    /// Hashed uniform grid over the traced points, sized so a cell holds a couple of them. Hash
    /// collisions only ever add candidates that lose on distance, so they cost a little time and
    /// nothing else.
    /// </summary>
    private sealed class PointGrid
    {
        private readonly float _inverseCell;
        private readonly int[] _start;
        private readonly int[] _entries;
        private readonly int _buckets;

        private PointGrid(float inverseCell, int[] start, int[] entries, int buckets)
        {
            _inverseCell = inverseCell;
            _start = start;
            _entries = entries;
            _buckets = buckets;
        }

        public static PointGrid Build(TargetWorkspace ws, SparseTexelSet points)
        {
            var list = points.Points;
            int buckets = System.Math.Max(64, Primes.Above(list.Length * 2));

            // Cell size follows the spacing the points were elected at, so a cell holds roughly one.
            double radius = 0;
            int sampled = 0;
            for (int i = 0; i < list.Length; i += System.Math.Max(1, list.Length / 4096))
            {
                radius += ws.Samples[list[i]].WorldRadius;
                sampled++;
            }
            float cell = sampled > 0 ? (float)(radius / sampled) * 2f * points.Stride : 1f;
            if (!(cell > 1e-6f)) cell = 1f;
            float inverseCell = 1f / cell;

            var counts = new int[buckets + 1];
            foreach (int p in list) counts[Bucket(ws.Samples[p].Position, inverseCell, buckets)]++;

            var start = new int[buckets + 1];
            for (int i = 0; i < buckets; i++) start[i + 1] = start[i] + counts[i];
            var entries = new int[list.Length];
            var cursor = (int[])start.Clone();
            foreach (int p in list) entries[cursor[Bucket(ws.Samples[p].Position, inverseCell, buckets)]++] = p;

            return new PointGrid(inverseCell, start, entries, buckets);
        }

        /// <summary>
        /// Nearest points to one texel, sorted by distance. Only points that share the texel's role
        /// are eligible: a contact answers for contacts and an open point for open surface.
        /// </summary>
        public int FindNearest(TargetWorkspace ws, SparseTexelSet points, int texelIndex, in TexelSample texel,
                               System.Span<int> found, System.Span<float> distance)
        {
            int count = 0;
            bool contact = points.IsContact[texelIndex];

            for (int pass = 0; pass < 2 && count < MaxNeighbours; pass++)
            {
                // First pass keeps to the texel's own population; if that leaves it with nothing, the
                // second lets the other one in rather than leaving the texel unlit.
                bool strict = pass == 0;
                int ring = 1 + pass;
                count = 0;

                int cx = (int)System.MathF.Floor(texel.Position.X * _inverseCell);
                int cy = (int)System.MathF.Floor(texel.Position.Y * _inverseCell);
                int cz = (int)System.MathF.Floor(texel.Position.Z * _inverseCell);

                for (int dz = -ring; dz <= ring; dz++)
                for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int bucket = Bucket(cx + dx, cy + dy, cz + dz, _buckets);
                    for (int e = _start[bucket]; e < _start[bucket + 1]; e++)
                    {
                        int point = _entries[e];
                        if (strict && points.IsContact[point] != contact) continue;

                        var candidate = ws.Samples[point];
                        if (Float3.Dot(texel.Normal, candidate.Normal) <= 0f) continue;

                        float d = Float3.Distance(candidate.Position, texel.Position);

                        // Insertion sort into the running best-of list.
                        if (count == MaxNeighbours && d >= distance[count - 1]) continue;
                        int at = count < MaxNeighbours ? count++ : MaxNeighbours - 1;
                        while (at > 0 && distance[at - 1] > d)
                        {
                            distance[at] = distance[at - 1];
                            found[at] = found[at - 1];
                            at--;
                        }
                        distance[at] = d;
                        found[at] = point;
                    }
                }

                if (count > 0) break;
            }
            return count;
        }

        private static int Bucket(Float3 p, float inverseCell, int buckets) => Bucket(
            (int)System.MathF.Floor(p.X * inverseCell),
            (int)System.MathF.Floor(p.Y * inverseCell),
            (int)System.MathF.Floor(p.Z * inverseCell), buckets);

        private static int Bucket(int x, int y, int z, int buckets)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
                return (h & 0x7FFFFFFF) % buckets;
            }
        }
    }

    private static class Primes
    {
        public static int Above(int n)
        {
            for (int candidate = System.Math.Max(3, n | 1); ; candidate += 2)
                if (IsPrime(candidate)) return candidate;
        }

        private static bool IsPrime(int n)
        {
            if (n % 2 == 0) return n == 2;
            for (int d = 3; (long)d * d <= n; d += 2)
                if (n % d == 0) return false;
            return true;
        }
    }
}
