// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Lanes = System.Numerics.Vector;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Prowl.Runtime.MeshFeatures.Generation;

/// <summary>
/// CPU generator for the signed distance field stored in <see cref="MeshSDF"/>, negative inside.
/// <para/>
/// Exact distances are found only near the surface, where each triangle visits the voxels around it. Every other
/// voxel takes the closest triangle of a neighbour in sweeps along each axis and measures the exact distance to it,
/// so the cost grows with the voxels and the triangles rather than their product.
/// <para/>
/// A voxel is inside when rays along at least two of the three axes cross the surface an odd number of times
/// before reaching it. Counting crossings ignores which way a triangle faces, so flipped and one sided triangles
/// change nothing, and a hole or a stray plane only misleads the axis it lines up with, which the other two outvote.
/// </summary>
public static class SDFGenerator
{
    public struct Options
    {
        /// <summary>Edge length of a voxel in mesh units. Small meshes use smaller voxels to keep a usable grid.</summary>
        public float VoxelSize;

        /// <summary>Most voxels along any axis, padding included. Large meshes use larger voxels to stay within it.</summary>
        public int MaxResolution;

        public static Options Default => new() { VoxelSize = 0.1f, MaxResolution = 128 };
    }

    /// <summary>Fewest voxels along the longest axis, padding included.</summary>
    public const int MinResolution = 8;

    /// <summary>Voxels of margin on every side of the mesh bounds.</summary>
    public const int PaddingVoxels = 2;

    private const long Empty = long.MaxValue;

    // Voxels this many cells around a triangle get its exact distance before the sweeps
    private const int Band = 1;

    // The axis passes run at most this many times, enough for a closest triangle to reach round corners and for the
    // narrow wedge of space each small triangle owns on a finely tessellated mesh. Running on until nothing changes
    // takes three times as many rounds for no visible gain. A round that changes nothing ends them early
    private const int MaxSweepRounds = 4;

    /// <summary>
    /// Build a <see cref="MeshSDF"/> for the mesh. Returns null if the mesh has no usable
    /// triangles. Blocks the calling thread.
    /// </summary>
    public static MeshSDF? Generate(Mesh mesh, Options options)
    {
        var sw = Stopwatch.StartNew();

        Surface? surface = Surface.From(mesh);
        if (surface == null) return null;

        if (!Layout(surface.Bounds, options, out AABB bounds, out float cell, out Int3 resolution)) return null;

        float[] distances = ComputeDistances(surface, bounds.Min, cell, resolution);
        long compute = sw.ElapsedMilliseconds;

        // Stored as signed normalized shorts over the longest distance the volume can hold
        float maxDistance = Float3.Length(bounds.Max - bounds.Min);
        float scale = short.MaxValue / maxDistance;
        var packed = new short[distances.Length];
        Parallel.For(0, distances.Length, i => packed[i] = (short)MathF.Round(Math.Clamp(distances[i] * scale, -short.MaxValue, short.MaxValue)));

        var volume = new Texture3D((uint)resolution.X, (uint)resolution.Y, (uint)resolution.Z, false, TextureImageFormat.Short);
        volume.SetData<short>(packed);

        Debug.Log($"SDF '{mesh.Name}': {resolution.X}x{resolution.Y}x{resolution.Z}, {surface.TriangleCount:N0} tris in {compute} ms, {sw.ElapsedMilliseconds} ms with the upload");

        return new MeshSDF
        {
            Volume = volume,
            Bounds = bounds,
            Resolution = resolution,
            VoxelSize = cell,
            MaxDistance = maxDistance,
        };
    }

    /// <summary>
    /// The voxel grid for a mesh within <paramref name="meshBounds"/>: cubic voxels of the requested size where the
    /// resolution limits allow, padded on every side and centred on the mesh. False for a mesh with no extent.
    /// </summary>
    internal static bool Layout(AABB meshBounds, Options options, out AABB bounds, out float cell, out Int3 resolution)
    {
        Float3 size = meshBounds.Max - meshBounds.Min;
        float longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        bounds = default;
        cell = 0f;
        resolution = default;
        if (longest <= 0) return false;

        int maxResolution = Math.Max(MinResolution, options.MaxResolution);
        cell = MathF.Max(options.VoxelSize, 1e-6f);
        cell = MathF.Max(cell, longest / (maxResolution - 2 * PaddingVoxels));
        cell = MathF.Min(cell, longest / (MinResolution - 2 * PaddingVoxels));

        resolution = new Int3(Cells(size.X, cell), Cells(size.Y, cell), Cells(size.Z, cell));
        Float3 extent = new Float3(resolution.X, resolution.Y, resolution.Z) * cell;
        Float3 min = (meshBounds.Min + meshBounds.Max) * 0.5f - extent * 0.5f;
        bounds = new AABB(min, min + extent);
        return true;
    }

    private static int Cells(float length, float cell) => Math.Max(1, (int)MathF.Ceiling(length / cell - 1e-4f)) + 2 * PaddingVoxels;

    /// <summary>
    /// Signed distance at the centre of every voxel of a grid of <paramref name="size"/> voxels starting at
    /// <paramref name="origin"/>, x fastest then y then z.
    /// </summary>
    internal static float[] ComputeDistances(Surface surface, Float3 origin, float cell, Int3 size)
    {
        var grid = new Grid(origin, cell, size);
        int count = grid.Count;

        // Scratch grids come from a pool, since every mesh of an import needs a fresh set
        long[] best = ArrayPool<long>.Shared.Rent(count);
        int[] changedAt = ArrayPool<int>.Shared.Rent(count);
        byte[] votes = ArrayPool<byte>.Shared.Rent(count);
        Array.Fill(best, Empty, 0, count);
        Array.Clear(changedAt, 0, count);
        Array.Clear(votes, 0, count);

        SeedBand(surface, best, grid);

        // Lines worth walking again along each axis, those with a voxel that changed since they were last walked, and
        // changedAt holds the sweep each voxel last changed in, so a neighbour's triangle is only measured again once new
        var dirty = new byte[3][];
        for (int axis = 0; axis < 3; axis++)
        {
            CrossAxes(axis, out int ua, out int va);
            dirty[axis] = new byte[grid.Dim(ua) * grid.Dim(va)];
            Array.Fill(dirty[axis], (byte)1);
        }
        int sweep = 0;
        for (int round = 0; round < MaxSweepRounds; round++)
        {
            bool changed = false;
            for (int axis = 0; axis < 3; axis++)
                changed |= Sweep(surface, best, changedAt, ++sweep, grid, axis, dirty);
            if (!changed) break;
        }

        for (int axis = 0; axis < 3; axis++)
            CountCrossings(surface, votes, grid, axis);

        var distances = new float[count];
        Parallel.For(0, count, i =>
        {
            long packed = best[i];
            float distance = packed == Empty ? float.PositiveInfinity : MathF.Sqrt(DistanceSqOf(packed));
            distances[i] = votes[i] >= 2 ? -distance : distance;
        });

        ArrayPool<long>.Shared.Return(best);
        ArrayPool<int>.Shared.Return(changedAt);
        ArrayPool<byte>.Shared.Return(votes);
        return distances;
    }

    internal readonly struct Grid(Float3 origin, float cell, Int3 size)
    {
        public readonly Float3 Origin = origin;
        public readonly float Cell = cell;
        public readonly Int3 Size = size;

        public int Count => Size.X * Size.Y * Size.Z;

        public int Dim(int axis) => axis == 0 ? Size.X : axis == 1 ? Size.Y : Size.Z;

        public int Stride(int axis) => axis == 0 ? 1 : axis == 1 ? Size.X : Size.X * Size.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Float3 Centre(int x, int y, int z) => Origin + new Float3(x + 0.5f, y + 0.5f, z + 0.5f) * Cell;
    }

    // Columns sit a hair off the voxel centres, by a different amount across each axis, so a ray never runs exactly
    // along an edge or through a corner that two triangles share and gets counted twice
    private const float JitterU = 1.234e-3f, JitterV = 3.917e-3f;

    /// <summary>Adds a vote to every voxel that a ray along <paramref name="axis"/> reaches after an odd number of crossings.</summary>
    private static void CountCrossings(Surface surface, byte[] votes, Grid grid, int axis)
    {
        // The two axes across the ray, and where each column sits on them
        CrossAxes(axis, out int ua, out int va);
        int nu = grid.Dim(ua), nv = grid.Dim(va), na = grid.Dim(axis);
        float ou = Get(grid.Origin, ua), ov = Get(grid.Origin, va), oa = Get(grid.Origin, axis);
        float cell = grid.Cell;

        // Triangles binned by the columns their shadow on the cross plane covers, as one flat array
        var counts = new int[nu * nv + 1];
        var ranges = new (int U0, int U1, int V0, int V1)[surface.TriangleCount];
        Parallel.For(0, surface.TriangleCount, t =>
        {
            surface.Corners(t, out Float3 a, out Float3 b, out Float3 c);
            float minU = MathF.Min(Get(a, ua), MathF.Min(Get(b, ua), Get(c, ua))), maxU = MathF.Max(Get(a, ua), MathF.Max(Get(b, ua), Get(c, ua)));
            float minV = MathF.Min(Get(a, va), MathF.Min(Get(b, va), Get(c, va))), maxV = MathF.Max(Get(a, va), MathF.Max(Get(b, va), Get(c, va)));
            int u0 = Math.Max(0, (int)MathF.Ceiling((minU - ou) / cell - 0.5f - JitterU)), u1 = Math.Min(nu - 1, (int)MathF.Floor((maxU - ou) / cell - 0.5f - JitterU));
            int v0 = Math.Max(0, (int)MathF.Ceiling((minV - ov) / cell - 0.5f - JitterV)), v1 = Math.Min(nv - 1, (int)MathF.Floor((maxV - ov) / cell - 0.5f - JitterV));
            ranges[t] = (u0, u1, v0, v1);
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                    Interlocked.Increment(ref counts[v * nu + u + 1]);
        });
        for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        var binned = new int[counts[^1]];
        var fill = (int[])counts.Clone();
        for (int t = 0; t < surface.TriangleCount; t++)
        {
            (int u0, int u1, int v0, int v1) = ranges[t];
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                    binned[fill[v * nu + u]++] = t;
        }

        int strideA = grid.Stride(axis), strideU = grid.Stride(ua), strideV = grid.Stride(va);
        Parallel.For(0, nu * nv, column =>
        {
            int start = counts[column], end = counts[column + 1];
            if (start == end) return;
            int u = column % nu, v = column / nu;
            float pu = ou + (u + 0.5f + JitterU) * cell, pv = ov + (v + 0.5f + JitterV) * cell;
            VoteColumn(surface, votes, binned.AsSpan(start, end - start), pu, pv, ua, va, axis, oa, cell, na, u * strideU + v * strideV, strideA);
        });
    }

    private static void VoteColumn(Surface surface, byte[] votes, ReadOnlySpan<int> triangles, float pu, float pv, int ua, int va, int axis,
        float oa, float cell, int na, int baseIndex, int strideA)
    {
        Span<float> hits = triangles.Length <= 256 ? stackalloc float[triangles.Length] : new float[triangles.Length];
        int count = 0;
        foreach (int t in triangles)
        {
            surface.Corners(t, out Float3 a, out Float3 b, out Float3 c);
            if (Crossing(pu, pv, Get(a, ua), Get(a, va), Get(a, axis), Get(b, ua), Get(b, va), Get(b, axis), Get(c, ua), Get(c, va), Get(c, axis), out float at))
                hits[count++] = at;
        }
        if (count == 0) return;
        hits = hits[..count];
        hits.Sort();

        // Walk the column, flipping inside at each crossing
        int next = 0;
        bool inside = false;
        for (int k = 0; k < na; k++)
        {
            float centre = oa + (k + 0.5f) * cell;
            while (next < count && hits[next] < centre)
            {
                inside = !inside;
                next++;
            }
            if (inside) votes[baseIndex + k * strideA]++;
        }
    }

    // The lower axis varies fastest, so consecutive lines lie next to each other in memory
    private static void CrossAxes(int axis, out int ua, out int va)
    {
        ua = axis == 0 ? 1 : 0;
        va = axis == 2 ? 1 : 2;
    }

    // Where the line through (u, v) along the axis passes through the triangle, if it does
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Crossing(float pu, float pv, float au, float av, float aa, float bu, float bv, float ba, float cu, float cv, float ca, out float at)
    {
        at = 0f;
        float area = (bu - au) * (cv - av) - (bv - av) * (cu - au);
        if (MathF.Abs(area) < 1e-20f) return false;
        float w0 = ((bu - pu) * (cv - pv) - (bv - pv) * (cu - pu)) / area;
        float w1 = ((cu - pu) * (av - pv) - (cv - pv) * (au - pu)) / area;
        float w2 = 1f - w0 - w1;
        if (w0 < 0f || w1 < 0f || w2 < 0f) return false;
        at = w0 * aa + w1 * ba + w2 * ca;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Get(Float3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    // Squared distance in the high half and the triangle in the low half, so the smallest value is the closest
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(float distanceSq, int triangle)
        => ((long)BitConverter.SingleToInt32Bits(distanceSq) << 32) | (uint)triangle;

    // Each triangle measures the voxels of its bounding box near its plane, a row of voxels per vector
    private static void SeedBand(Surface surface, long[] best, Grid grid)
    {
        float reach = Band * grid.Cell * MathF.Sqrt(3f);
        Span<float> rampValues = stackalloc float[Vector<float>.Count];
        for (int j = 0; j < rampValues.Length; j++) rampValues[j] = j;
        var ramp = new Vector<float>(rampValues);

        Parallel.For(0, surface.TriangleCount, t => SeedTriangle(surface, best, grid, reach, ramp, t));
    }

    private static void SeedTriangle(Surface surface, long[] best, Grid grid, float reach, Vector<float> ramp, int t)
    {
        ref readonly Triangle tri = ref surface[t];
        Float3 min = Maths.Min(tri.A, Maths.Min(tri.B, tri.C)), max = Maths.Max(tri.A, Maths.Max(tri.B, tri.C));
        int x0 = Index(min.X, grid.Origin.X, grid.Cell, grid.Size.X, -Band), x1 = Index(max.X, grid.Origin.X, grid.Cell, grid.Size.X, Band);
        int y0 = Index(min.Y, grid.Origin.Y, grid.Cell, grid.Size.Y, -Band), y1 = Index(max.Y, grid.Origin.Y, grid.Cell, grid.Size.Y, Band);
        int z0 = Index(min.Z, grid.Origin.Z, grid.Cell, grid.Size.Z, -Band), z1 = Index(max.Z, grid.Origin.Z, grid.Cell, grid.Size.Z, Band);

        var lanes = new TriangleLanes(tri, reach);
        Span<float> distances = stackalloc float[Vector<float>.Count];
        var lastX = new Vector<float>(x1);
        var originX = new Vector<float>(grid.Origin.X + 0.5f * grid.Cell);
        var cell = new Vector<float>(grid.Cell);
        for (int z = z0; z <= z1; z++)
        {
            var pz = new Vector<float>(grid.Origin.Z + (z + 0.5f) * grid.Cell);
            for (int y = y0; y <= y1; y++)
            {
                var py = new Vector<float>(grid.Origin.Y + (y + 0.5f) * grid.Cell);
                int row = (z * grid.Size.Y + y) * grid.Size.X;
                for (int x = x0; x <= x1; x += Vector<float>.Count)
                {
                    Vector<float> xs = new Vector<float>(x) + ramp;
                    Vector<float> distanceSq = lanes.DistanceSqNearPlane(originX + xs * cell, py, pz, Lanes.LessThanOrEqual(xs, lastX));
                    if (Lanes.EqualsAll(distanceSq, new Vector<float>(float.PositiveInfinity))) continue;
                    distanceSq.CopyTo(distances);
                    for (int j = 0; j < distances.Length; j++)
                        if (distances[j] != float.PositiveInfinity)
                            AtomicMin(ref best[row + x + j], Pack(distances[j], t));
                }
            }
        }
    }

    private static int Index(float value, float origin, float cell, int count, int offset)
    {
        int i = (int)MathF.Floor((value - origin) / cell - 0.5f) + offset + (offset > 0 ? 1 : 0);
        return Math.Clamp(i, 0, count - 1);
    }

    private static void AtomicMin(ref long target, long value)
    {
        long current = Volatile.Read(ref target);
        while (value < current)
        {
            long seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current) return;
            current = seen;
        }
    }

    // Each dirty line along the axis is walked both ways, every voxel trying its neighbour's closest triangle. A voxel
    // that changes dirties the lines through it along every axis
    private static bool Sweep(Surface surface, long[] best, int[] changedAt, int sweep, Grid grid, int axis, byte[][] dirty)
    {
        // A neighbour unchanged since this axis was last swept was already tried. A change during that sweep counts as
        // new, since its forward pass sees nothing the backward pass changes
        int since = sweep - 3;
        CrossAxes(axis, out int ua, out int va);
        int nu = grid.Dim(ua), n = grid.Dim(axis);
        int stride = grid.Stride(axis), strideU = grid.Stride(ua), strideV = grid.Stride(va);
        Float3 step = axis == 0 ? new Float3(grid.Cell, 0f, 0f) : axis == 1 ? new Float3(0f, grid.Cell, 0f) : new Float3(0f, 0f, grid.Cell);
        byte[] own = dirty[axis], acrossU = dirty[ua], acrossV = dirty[va];
        int changed = 0;

        Parallel.For(0, own.Length, line =>
        {
            if (own[line] == 0) return;
            own[line] = 0;

            int u = line % nu, v = line / nu;
            int start = u * strideU + v * strideV;
            int x = start % grid.Size.X, y = start / grid.Size.X % grid.Size.Y, z = start / (grid.Size.X * grid.Size.Y);
            Float3 first = grid.Centre(x, y, z);
            var across = new Across(acrossU, LineThrough(grid, ua, axis, ua, u, va, v), acrossV, LineThrough(grid, va, axis, ua, u, va, v));
            if (SweepLine(surface, best, changedAt, since, sweep, first, step, start, stride, n, across))
            {
                own[line] = 1;
                changed = 1;
            }
        });
        return changed != 0;
    }

    // The lines across a swept line, dirtied where one of its voxels changes
    private readonly record struct Across(byte[] U, (int Start, int Step) LineU, byte[] V, (int Start, int Step) LineV)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Mark(int k)
        {
            U[LineU.Start + k * LineU.Step] = 1;
            V[LineV.Start + k * LineV.Step] = 1;
        }
    }

    // Kept apart from the parallel loop so the JIT holds everything the hot loop touches in registers
    private static bool SweepLine(Surface surface, long[] best, int[] changedAt, int since, int sweep, Float3 first, Float3 step, int start, int stride, int n, Across across)
    {
        bool any = false;
        for (int k = 1; k < n; k++)
            if (TryNeighbour(surface, best, changedAt, since, sweep, first, step, k, start + k * stride, start + (k - 1) * stride))
            {
                any = true;
                across.Mark(k);
            }
        for (int k = n - 2; k >= 0; k--)
            if (TryNeighbour(surface, best, changedAt, since, sweep, first, step, k, start + k * stride, start + (k + 1) * stride))
            {
                any = true;
                across.Mark(k);
            }
        return any;
    }

    // The line along axis b through the voxel k steps along the swept axis of line (u, v), as start + k * step
    private static (int Start, int Step) LineThrough(Grid grid, int b, int axis, int ua, int u, int va, int v)
    {
        CrossAxes(b, out int ub, out int vb);
        int Coord(int c) => c == ua ? u : c == va ? v : 0;
        return (Coord(ub) + Coord(vb) * grid.Dim(ub), ub == axis ? 1 : grid.Dim(ub));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryNeighbour(Surface surface, long[] best, int[] changedAt, int since, int sweep, in Float3 first, in Float3 step, int k, int i, int from)
    {
        long neighbour = best[from];
        if (neighbour == Empty || changedAt[from] < since) return false;
        int t = (int)(uint)neighbour;
        long current = best[i];
        if (current != Empty && (int)(uint)current == t) return false;

        Float3 p = first + step * k;
        surface.Corners(t, out Float3 a, out Float3 b, out Float3 c);
        Float3 d = p - ClosestPoint(p, a, b, c);
        long candidate = Pack(Float3.Dot(d, d), t);
        if (candidate >= current) return false;
        best[i] = candidate;
        changedAt[i] = sweep;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DistanceSqOf(long packed) => BitConverter.Int32BitsToSingle((int)(packed >> 32));

    /// <summary>Closest point on triangle (a,b,c) to p by its Voronoi regions, leaving as soon as the region is known.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Float3 ClosestPoint(Float3 p, Float3 a, Float3 b, Float3 c)
    {
        Float3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Float3.Dot(ab, ap), d2 = Float3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Float3 bp = p - b;
        float d3 = Float3.Dot(ab, bp), d4 = Float3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));

        Float3 cp = p - c;
        float d5 = Float3.Dot(ab, cp), d6 = Float3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>A triangle with what measuring distances to it needs worked out once.</summary>
    internal readonly struct Triangle
    {
        public readonly Float3 A, B, C;
        public readonly Float3 AB, BC, CA;

        // Normal, unnormalized, and the normal of each edge in the plane pointing into the triangle
        public readonly Float3 N, InAB, InBC, InCA;

        // Reciprocal squared lengths of the edges and the normal
        public readonly float InvAB, InvBC, InvCA, InvN;

        public Triangle(Float3 a, Float3 b, Float3 c)
        {
            A = a; B = b; C = c;
            AB = b - a; BC = c - b; CA = a - c;
            N = Float3.Cross(AB, c - a);
            InAB = Float3.Cross(N, AB); InBC = Float3.Cross(N, BC); InCA = Float3.Cross(N, CA);
            InvAB = 1f / Float3.Dot(AB, AB); InvBC = 1f / Float3.Dot(BC, BC); InvCA = 1f / Float3.Dot(CA, CA);
            InvN = 1f / Float3.Dot(N, N);
        }
    }

    /// <summary>A <see cref="Triangle"/> spread across vector lanes, to measure a row of voxels at once.</summary>
    private readonly struct TriangleLanes
    {
        private readonly Vector<float> _ax, _ay, _az, _bx, _by, _bz, _cx, _cy, _cz;
        private readonly Vector<float> _abx, _aby, _abz, _bcx, _bcy, _bcz, _cax, _cay, _caz;
        private readonly Vector<float> _nx, _ny, _nz, _inAbx, _inAby, _inAbz, _inBcx, _inBcy, _inBcz, _inCax, _inCay, _inCaz;
        private readonly Vector<float> _invAB, _invBC, _invCA, _invN, _reachSq;

        public TriangleLanes(in Triangle t, float reach)
        {
            (_ax, _ay, _az) = Spread(t.A); (_bx, _by, _bz) = Spread(t.B); (_cx, _cy, _cz) = Spread(t.C);
            (_abx, _aby, _abz) = Spread(t.AB); (_bcx, _bcy, _bcz) = Spread(t.BC); (_cax, _cay, _caz) = Spread(t.CA);
            (_nx, _ny, _nz) = Spread(t.N);
            (_inAbx, _inAby, _inAbz) = Spread(t.InAB); (_inBcx, _inBcy, _inBcz) = Spread(t.InBC); (_inCax, _inCay, _inCaz) = Spread(t.InCA);
            _invAB = new(t.InvAB); _invBC = new(t.InvBC); _invCA = new(t.InvCA); _invN = new(t.InvN);
            _reachSq = new(reach * reach);
        }

        private static (Vector<float>, Vector<float>, Vector<float>) Spread(Float3 v) => (new(v.X), new(v.Y), new(v.Z));

        /// <summary>
        /// Squared distance from each lane's point, to the plane over the face or else to the nearest edge. Infinity for
        /// lanes not in <paramref name="active"/> or further from the plane than the band reaches.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector<float> DistanceSqNearPlane(Vector<float> px, Vector<float> py, Vector<float> pz, Vector<int> active)
        {
            Vector<float> pax = px - _ax, pay = py - _ay, paz = pz - _az;
            Vector<float> h = pax * _nx + pay * _ny + paz * _nz;
            Vector<float> planeSq = h * h * _invN;
            active &= Lanes.LessThanOrEqual(planeSq, _reachSq);
            if (Lanes.EqualsAll(active, Vector<int>.Zero)) return new Vector<float>(float.PositiveInfinity);

            Vector<float> pbx = px - _bx, pby = py - _by, pbz = pz - _bz;
            Vector<float> pcx = px - _cx, pcy = py - _cy, pcz = pz - _cz;
            Vector<int> inside = Lanes.GreaterThanOrEqual(pax * _inAbx + pay * _inAby + paz * _inAbz, Vector<float>.Zero)
                & Lanes.GreaterThanOrEqual(pbx * _inBcx + pby * _inBcy + pbz * _inBcz, Vector<float>.Zero)
                & Lanes.GreaterThanOrEqual(pcx * _inCax + pcy * _inCay + pcz * _inCaz, Vector<float>.Zero);

            Vector<float> edges = Lanes.Min(Segment(pax, pay, paz, _abx, _aby, _abz, _invAB),
                Lanes.Min(Segment(pbx, pby, pbz, _bcx, _bcy, _bcz, _invBC), Segment(pcx, pcy, pcz, _cax, _cay, _caz, _invCA)));
            Vector<float> distanceSq = Lanes.ConditionalSelect(inside, planeSq, edges);
            return Lanes.ConditionalSelect(active, distanceSq, new Vector<float>(float.PositiveInfinity));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<float> Segment(Vector<float> dx, Vector<float> dy, Vector<float> dz, Vector<float> ex, Vector<float> ey, Vector<float> ez, Vector<float> invLengthSq)
        {
            Vector<float> s = Lanes.Min(Lanes.Max((dx * ex + dy * ey + dz * ez) * invLengthSq, Vector<float>.Zero), Vector<float>.One);
            Vector<float> rx = dx - ex * s, ry = dy - ey * s, rz = dz - ez * s;
            return rx * rx + ry * ry + rz * rz;
        }
    }

    /// <summary>A mesh's triangles that have an area, as bare corners and with their worked out values.</summary>
    internal sealed class Surface
    {
        private readonly Triangle[] _triangles;

        // Three corners per triangle, packed tight for the passes that hop between triangles
        private readonly Float3[] _corners;

        public int TriangleCount => _triangles.Length;
        public AABB Bounds { get; }

        private Surface(Triangle[] triangles)
        {
            _triangles = triangles;
            _corners = new Float3[triangles.Length * 3];
            for (int t = 0; t < triangles.Length; t++)
            {
                _corners[t * 3] = triangles[t].A;
                _corners[t * 3 + 1] = triangles[t].B;
                _corners[t * 3 + 2] = triangles[t].C;
            }

            Float3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (ref readonly Triangle t in triangles.AsSpan())
            {
                min = Maths.Min(min, Maths.Min(t.A, Maths.Min(t.B, t.C)));
                max = Maths.Max(max, Maths.Max(t.A, Maths.Max(t.B, t.C)));
            }
            Bounds = new AABB(min, max);
        }

        public static Surface? From(Mesh mesh)
        {
            Float3[]? vertices = mesh.Vertices;
            uint[]? indices = mesh.Indices;
            if (vertices == null || indices == null || vertices.Length == 0 || indices.Length < 3) return null;

            var triangles = new List<Triangle>(indices.Length / 3);
            for (int s = 0; s < mesh.SubMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (sub.Topology != Topology.Triangles) continue;
                int end = Math.Min(sub.IndexStart + sub.IndexCount, indices.Length);
                for (int i = sub.IndexStart; i + 2 < end; i += 3)
                {
                    uint ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    if (ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length) continue;
                    Float3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    if (Float3.LengthSquared(Float3.Cross(b - a, c - a)) < 1e-20f) continue;
                    triangles.Add(new Triangle(a, b, c));
                }
            }
            return triangles.Count == 0 ? null : new Surface(triangles.ToArray());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Corners(int t, out Float3 a, out Float3 b, out Float3 c)
        {
            a = _corners[t * 3];
            b = _corners[t * 3 + 1];
            c = _corners[t * 3 + 2];
        }

        public ref readonly Triangle this[int t]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _triangles[t];
        }
    }
}
