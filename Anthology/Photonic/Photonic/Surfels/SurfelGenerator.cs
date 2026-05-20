using Prowl.Vector;
using Prowl.Photonic.Sampling;

namespace Prowl.Photonic.Surfels;

/// <summary>Configuration for <see cref="SurfelGenerator.Generate"/>.</summary>
public struct SurfelGenerationOptions
{
    /// <summary>Target surfels per square metre of world surface (stochastic-rounded per triangle).</summary>
    public float SurfelsPerSquareMeter;

    /// <summary>Deterministic seed for the Halton offsets and stochastic rounding.</summary>
    public ulong Seed;

    /// <summary>If true, reject candidate surfels whose nearest aligned-normal neighbour is closer than the Poisson radius.</summary>
    public bool NormalRejection;

    /// <summary>Poisson radius (aligned-only) as a fraction of the per-surfel kernel radius.</summary>
    public float PoissonRadiusFactor;

    /// <summary>Dot-product threshold above which two surfels count as "aligned" for Poisson rejection.</summary>
    public float PoissonAlignThreshold;

    /// <summary>Sensible defaults: density 2/m^2, no normal rejection, fixed seed.</summary>
    public static SurfelGenerationOptions Default => new SurfelGenerationOptions
    {
        SurfelsPerSquareMeter = 2.0f,
        Seed = 0x9E3779B97F4A7C15UL,
        NormalRejection = false,
        PoissonRadiusFactor = 0.6f,
        PoissonAlignThreshold = 0.85f,
    };
}

/// <summary>
/// Scatters surfels across every <see cref="BakeInstance"/>'s triangles at a target world-space
/// density (surfels per square metre). Per-triangle count is area x density, stochastically
/// rounded so the overall density matches the target on average regardless of triangulation.
/// </summary>
public static class SurfelGenerator
{
    public static SurfelCloud Generate(System.Collections.Generic.IReadOnlyList<BakeInstance> instances,
                                       SurfelGenerationOptions options)
    {
        float surfelsPerWorldUnitSquared = options.SurfelsPerSquareMeter;
        ulong seed = options.Seed;
        bool normalRejection = options.NormalRejection;
        float poissonRadiusFactor = options.PoissonRadiusFactor;
        float poissonAlignThreshold = options.PoissonAlignThreshold;
        var rng = new Sampler(seed);
        var list = new System.Collections.Generic.List<Surfel>(8192);
        Float3 boundsMin = new Float3(float.PositiveInfinity);
        Float3 boundsMax = new Float3(float.NegativeInfinity);

        // Per-surfel influence radius derived from density alone, NOT per-triangle area: a 1 m^2
        // patch of mesh gets the same count of surfels regardless of how many triangles it's
        // split into, so the radius (= local density) should also be uniform across the whole
        // cloud. Each surfel "owns" ~1/density m^2; the sqrt(2) factor sizes the kernel so
        // adjacent surfels' kernels overlap, giving smooth probe-like interpolation.
        float surfelRadius = (float)System.Math.Sqrt(2.0 / System.Math.Max(1e-6, surfelsPerWorldUnitSquared));

        // Normal-aware Poisson disk: aligned-normal surfels must be at least poissonR apart;
        // misaligned surfels are invisible to each other at interpolation time so we don't care
        // if they cluster. We build the rejection grid incrementally as we add surfels.
        float poissonR = surfelRadius * poissonRadiusFactor;
        float poissonR2 = poissonR * poissonR;
        float gridCellSize = System.Math.Max(0.01f, poissonR);
        float invGridCell = 1f / gridCellSize;
        var grid = normalRejection
            ? new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>()
            : null;

        for (int instIdx = 0; instIdx < instances.Count; instIdx++)
        {
            var inst = instances[instIdx];
            var mesh = inst.Mesh;
            var w = inst.WorldTransform;
            var positions = mesh.Positions;
            var normals = mesh.Normals;
            mesh.UVLayers.TryGetValue("UV0", out var uv0);

            for (int g = 0; g < mesh.MaterialGroups.Count; g++)
            {
                var grp = mesh.MaterialGroups[g];
                var idx = grp.Indices;
                for (int t = 0; t < idx.Length; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    var p0 = Float4x4.TransformPoint(positions[i0], w);
                    var p1 = Float4x4.TransformPoint(positions[i1], w);
                    var p2 = Float4x4.TransformPoint(positions[i2], w);
                    var e1 = p1 - p0;
                    var e2 = p2 - p0;
                    var nm = Float3.Cross(e1, e2);
                    float twoArea = Float3.Length(nm);
                    if (twoArea < 1e-9f) continue;
                    float area = twoArea * 0.5f;

                    // Expected surfel count for this triangle = area x density. Stochastic
                    // rounding produces the exact density on average across the whole mesh: a
                    // 0.01 m^2 triangle at density 2/m^2 has p=0.02 of getting a surfel, p=0.98
                    // of getting none. Forcing every tiny triangle to >=1 (as we used to)
                    // massively over-samples sliver-heavy meshes like Sponza's curtains and
                    // breaks the "same surfels per square metre regardless of triangulation"
                    // invariant.
                    float expected = area * surfelsPerWorldUnitSquared;
                    int n = (int)expected;
                    float frac = expected - n;
                    if (rng.NextFloat() < frac) n++;
                    if (n == 0) continue;

                    var faceN = nm * (1f / twoArea);
                    var n0 = normals[i0];
                    var n1 = normals[i1];
                    var n2 = normals[i2];

                    Float2 uvA = uv0 is null ? Float2.Zero : uv0[i0];
                    Float2 uvB = uv0 is null ? Float2.Zero : uv0[i1];
                    Float2 uvC = uv0 is null ? Float2.Zero : uv0[i2];

                    // Per-triangle Halton (2, 3) sequence with a random start offset. The Halton
                    // points are low-discrepancy: instead of two random floats clustering or
                    // leaving gaps, consecutive Halton indices sweep the unit square in a
                    // self-avoiding pattern, which after the triangle-warp gives noticeably more
                    // even surfel coverage than pure uniform sampling. The random start (0..1024)
                    // decorrelates triangles from each other so seams at triangle edges don't all
                    // sample the same parametric point.
                    int haltonStart = 1 + (int)(rng.NextFloat() * 1024);

                    for (int k = 0; k < n; k++)
                    {
                        int hidx = haltonStart + k;
                        float u = Halton(hidx, 2);
                        float v = Halton(hidx, 3);
                        // (1 - sqrt(u))*A + sqrt(u)*(1-v)*B + sqrt(u)*v*C uniformly maps the unit
                        // square to barycentric coordinates of the triangle.
                        float sru = (float)System.Math.Sqrt(u);
                        float a = 1f - sru, b = sru * (1f - v), c = sru * v;

                        var pos = p0 * a + p1 * b + p2 * c;
                        var nrm = n0 * a + n1 * b + n2 * c;
                        if (Float3.LengthSquared(nrm) < 1e-10f) nrm = faceN;
                        else nrm = Float3.Normalize(nrm);
                        var uv = uvA * a + uvB * b + uvC * c;

                        // Normal-aware Poisson rejection: only reject this candidate if an
                        // ALIGNED-normal surfel already lives within poissonR of it. Misaligned
                        // surfels (a wall meeting a floor at a corner) coexist freely.
                        if (grid is not null)
                        {
                            int cx = (int)System.Math.Floor(pos.X * invGridCell);
                            int cy = (int)System.Math.Floor(pos.Y * invGridCell);
                            int cz = (int)System.Math.Floor(pos.Z * invGridCell);
                            bool rejected = false;
                            for (int dz = -1; dz <= 1 && !rejected; dz++)
                            for (int dy = -1; dy <= 1 && !rejected; dy++)
                            for (int dx = -1; dx <= 1 && !rejected; dx++)
                            {
                                long key = EncodeCellKey(cx + dx, cy + dy, cz + dz);
                                if (!grid.TryGetValue(key, out var bucket)) continue;
                                for (int b2 = 0; b2 < bucket.Count; b2++)
                                {
                                    var existing = list[bucket[b2]];
                                    var diff = existing.Position - pos;
                                    float d2 = diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z;
                                    if (d2 < poissonR2 &&
                                        Float3.Dot(existing.Normal, nrm) > poissonAlignThreshold)
                                    {
                                        rejected = true;
                                        break;
                                    }
                                }
                            }
                            if (rejected) continue;

                            long ownKey = EncodeCellKey(cx, cy, cz);
                            if (!grid.TryGetValue(ownKey, out var ownBucket))
                            {
                                ownBucket = new System.Collections.Generic.List<int>(4);
                                grid[ownKey] = ownBucket;
                            }
                            ownBucket.Add(list.Count);
                        }

                        list.Add(new Surfel
                        {
                            Position = pos,
                            Normal = nrm,
                            UV0 = uv,
                            InstanceIndex = instIdx,
                            MaterialGroupIndex = g,
                            Radius = surfelRadius,
                        });

                        if (pos.X < boundsMin.X) boundsMin.X = pos.X;
                        if (pos.Y < boundsMin.Y) boundsMin.Y = pos.Y;
                        if (pos.Z < boundsMin.Z) boundsMin.Z = pos.Z;
                        if (pos.X > boundsMax.X) boundsMax.X = pos.X;
                        if (pos.Y > boundsMax.Y) boundsMax.Y = pos.Y;
                        if (pos.Z > boundsMax.Z) boundsMax.Z = pos.Z;
                    }
                }
            }
        }

        if (list.Count == 0)
        {
            // Avoid empty AABB if no surfels emitted.
            return new SurfelCloud(System.Array.Empty<Surfel>(), new AABB(Float3.Zero, Float3.One));
        }
        var bounds = new AABB(boundsMin, boundsMax);
        // Expand the AABB a touch so surfels exactly on the boundary still land in valid cells.
        bounds.Expand(0.001f);

        return new SurfelCloud(list.ToArray(), bounds);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static float Halton(int index, int b)
    {
        float f = 1f, r = 0f;
        int i = index;
        while (i > 0)
        {
            f /= b;
            r += f * (i % b);
            i /= b;
        }
        return r;
    }

    /// <summary>Pack 21-bit signed cell coords into a 63-bit key for the rejection grid.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static long EncodeCellKey(int x, int y, int z)
    {
        const long mask = (1L << 21) - 1;
        // Bias each axis by +2^20 so the modest negative range of world coords folds into [0, 2^21).
        long xb = (x + (1L << 20)) & mask;
        long yb = (y + (1L << 20)) & mask;
        long zb = (z + (1L << 20)) & mask;
        return xb | (yb << 21) | (zb << 42);
    }
}
