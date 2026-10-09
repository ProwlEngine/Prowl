// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;
using Prowl.Vector;

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
        /// <summary>Grid resolution along each axis. Default 64.</summary>
        public int Resolution;

        /// <summary>Margin added around the source mesh AABB, as fraction of the longest bounds axis. Default 0.1.</summary>
        public float PaddingFraction;

        /// <summary>Distance values are clamped to <c>MaxDistanceFraction × longest_axis</c>. Default 0.25.</summary>
        public float MaxDistanceFraction;

        public static Options Default => new()
        {
            Resolution = 64,
            PaddingFraction = 0.1f,
            MaxDistanceFraction = 0.25f,
        };
    }

    private const long Empty = long.MaxValue;

    // Voxels this many cells around a triangle get its exact distance before the sweeps
    private const int Band = 1;

    // The axis passes run this many times, enough for a closest triangle to reach round corners and for the narrow
    // wedge of space each small triangle owns on a finely tessellated mesh. A round that changes nothing costs little
    private const int SweepRounds = 4;

    /// <summary>
    /// Build a <see cref="MeshSDF"/> for the mesh. Returns null if the mesh has no usable
    /// triangles. Blocks the calling thread.
    /// </summary>
    public static MeshSDF? Generate(Mesh mesh, Options options)
    {
        var sw = Stopwatch.StartNew();

        Surface? surface = Surface.From(mesh);
        if (surface == null) return null;

        int res = Math.Max(4, options.Resolution);
        AABB bounds = surface.Bounds;
        Float3 size = bounds.Max - bounds.Min;
        float longestAxis = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (longestAxis <= 0) return null;

        float padding = longestAxis * MathF.Max(0, options.PaddingFraction);
        bounds.Expand(padding);
        size = bounds.Max - bounds.Min;
        float maxDistance = longestAxis * MathF.Max(0.01f, options.MaxDistanceFraction);

        float[] distances = ComputeDistances(surface, bounds.Min, size / res, res, maxDistance);
        long compute = sw.ElapsedMilliseconds;

        var volume = new Texture3D((uint)res, (uint)res, (uint)res, false, TextureImageFormat.Float);
        volume.SetData<float>(distances);

        Debug.Log($"SDF '{mesh.Name}': {res}^3, {surface.TriangleCount:N0} tris in {compute} ms, {sw.ElapsedMilliseconds} ms with the upload");

        return new MeshSDF
        {
            Volume = volume,
            Bounds = bounds,
            Resolution = new Int3(res, res, res),
            Padding = padding,
            MaxDistance = maxDistance,
        };
    }

    /// <summary>
    /// Signed distance at the centre of every voxel of a <paramref name="res"/> cubed grid starting at
    /// <paramref name="origin"/>, x fastest then y then z, clamped to <paramref name="maxDistance"/>.
    /// </summary>
    internal static float[] ComputeDistances(Surface surface, Float3 origin, Float3 cell, int res, float maxDistance)
    {
        var best = new long[res * res * res];
        Array.Fill(best, Empty);

        SeedBand(surface, best, origin, cell, res);
        for (int round = 0; round < SweepRounds; round++)
            for (int axis = 0; axis < 3; axis++)
                Sweep(surface, best, origin, cell, res, axis);

        var votes = new byte[best.Length];
        for (int axis = 0; axis < 3; axis++)
            CountCrossings(surface, votes, origin, cell, res, axis);

        var distances = new float[best.Length];
        Parallel.For(0, best.Length, i =>
        {
            long packed = best[i];
            float distance = packed == Empty ? maxDistance : MathF.Sqrt(BitConverter.Int32BitsToSingle((int)(packed >> 32)));
            if (votes[i] >= 2) distance = -distance;
            distances[i] = Math.Clamp(distance, -maxDistance, maxDistance);
        });
        return distances;
    }

    // Columns sit a hair off the voxel centres, by a different amount across each axis, so a ray never runs exactly
    // along an edge or through a corner that two triangles share and gets counted twice
    private const float JitterU = 1.234e-3f, JitterV = 3.917e-3f;

    /// <summary>Adds a vote to every voxel that a ray along <paramref name="axis"/> reaches after an odd number of crossings.</summary>
    private static void CountCrossings(Surface surface, byte[] votes, Float3 origin, Float3 cell, int res, int axis)
    {
        // The two axes across the ray, and where each column sits on them
        int ua = (axis + 1) % 3, va = (axis + 2) % 3;
        float ou = Get(origin, ua), ov = Get(origin, va), oa = Get(origin, axis);
        float cu = Get(cell, ua), cv = Get(cell, va), ca = Get(cell, axis);

        // Triangles binned by the columns their shadow on the cross plane covers, as one flat array
        var counts = new int[res * res + 1];
        var ranges = new (int U0, int U1, int V0, int V1)[surface.TriangleCount];
        Parallel.For(0, surface.TriangleCount, t =>
        {
            surface.Corners(t, out Float3 a, out Float3 b, out Float3 c);
            float minU = MathF.Min(Get(a, ua), MathF.Min(Get(b, ua), Get(c, ua))), maxU = MathF.Max(Get(a, ua), MathF.Max(Get(b, ua), Get(c, ua)));
            float minV = MathF.Min(Get(a, va), MathF.Min(Get(b, va), Get(c, va))), maxV = MathF.Max(Get(a, va), MathF.Max(Get(b, va), Get(c, va)));
            int u0 = Math.Max(0, (int)MathF.Ceiling((minU - ou) / cu - 0.5f - JitterU)), u1 = Math.Min(res - 1, (int)MathF.Floor((maxU - ou) / cu - 0.5f - JitterU));
            int v0 = Math.Max(0, (int)MathF.Ceiling((minV - ov) / cv - 0.5f - JitterV)), v1 = Math.Min(res - 1, (int)MathF.Floor((maxV - ov) / cv - 0.5f - JitterV));
            ranges[t] = (u0, u1, v0, v1);
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                    Interlocked.Increment(ref counts[v * res + u + 1]);
        });
        for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        var binned = new int[counts[^1]];
        var fill = (int[])counts.Clone();
        for (int t = 0; t < surface.TriangleCount; t++)
        {
            (int u0, int u1, int v0, int v1) = ranges[t];
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                    binned[fill[v * res + u]++] = t;
        }

        int strideA = Stride(axis, res), strideU = Stride(ua, res), strideV = Stride(va, res);
        Parallel.For(0, res * res, column =>
        {
            int u = column % res, v = column / res;
            int start = counts[column], end = counts[column + 1];
            if (start == end) return;

            float pu = ou + (u + 0.5f + JitterU) * cu, pv = ov + (v + 0.5f + JitterV) * cv;
            Span<float> hits = end - start <= 256 ? stackalloc float[end - start] : new float[end - start];
            int count = 0;
            for (int k = start; k < end; k++)
            {
                surface.Corners(binned[k], out Float3 a, out Float3 b, out Float3 c);
                if (Crossing(pu, pv, Get(a, ua), Get(a, va), Get(a, axis), Get(b, ua), Get(b, va), Get(b, axis), Get(c, ua), Get(c, va), Get(c, axis), out float at))
                    hits[count++] = at;
            }
            if (count == 0) return;
            hits = hits[..count];
            hits.Sort();

            // Walk the column, flipping inside at each crossing
            int next = 0;
            bool inside = false;
            int baseIndex = u * strideU + v * strideV;
            for (int k = 0; k < res; k++)
            {
                float centre = oa + (k + 0.5f) * ca;
                while (next < count && hits[next] < centre)
                {
                    inside = !inside;
                    next++;
                }
                if (inside) votes[baseIndex + k * strideA]++;
            }
        });
    }

    private static int Stride(int axis, int res) => axis == 0 ? 1 : axis == 1 ? res : res * res;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Float3 Centre(Float3 origin, Float3 cell, int x, int y, int z)
        => new(origin.X + (x + 0.5f) * cell.X, origin.Y + (y + 0.5f) * cell.Y, origin.Z + (z + 0.5f) * cell.Z);

    // Squared distance in the high half and the triangle in the low half, so the smallest value is the closest
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(float distanceSq, int triangle)
        => ((long)BitConverter.SingleToInt32Bits(distanceSq) << 32) | (uint)triangle;

    private static void SeedBand(Surface surface, long[] best, Float3 origin, Float3 cell, int res)
    {
        float reach = Band * MathF.Sqrt(cell.X * cell.X + cell.Y * cell.Y + cell.Z * cell.Z);
        Parallel.For(0, surface.TriangleCount, t =>
        {
            surface.Corners(t, out Float3 a, out Float3 b, out Float3 c);
            Float3 min = Maths.Min(a, Maths.Min(b, c)), max = Maths.Max(a, Maths.Max(b, c));
            int x0 = Index(min.X, origin.X, cell.X, res, -Band), x1 = Index(max.X, origin.X, cell.X, res, Band);
            int y0 = Index(min.Y, origin.Y, cell.Y, res, -Band), y1 = Index(max.Y, origin.Y, cell.Y, res, Band);
            int z0 = Index(min.Z, origin.Z, cell.Z, res, -Band), z1 = Index(max.Z, origin.Z, cell.Z, res, Band);

            // Large flat triangles cover boxes of voxels mostly far off their plane, which are skipped cheaply
            Float3 n = surface.FaceNormal(t);
            float plane = Float3.Dot(n, a);

            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Float3 p = Centre(origin, cell, x, y, z);
                        if (MathF.Abs(Float3.Dot(n, p) - plane) > reach) continue;
                        Float3 d = p - ClosestPoint(p, a, b, c);
                        AtomicMin(ref best[(z * res + y) * res + x], Pack(Float3.Dot(d, d), t));
                    }
        });
    }

    private static int Index(float value, float origin, float cell, int res, int offset)
    {
        int i = (int)MathF.Floor((value - origin) / cell - 0.5f) + offset + (offset > 0 ? 1 : 0);
        return Math.Clamp(i, 0, res - 1);
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

    // Each line along the axis is walked both ways, every voxel trying its neighbour's closest triangle
    private static void Sweep(Surface surface, long[] best, Float3 origin, Float3 cell, int res, int axis)
    {
        int stride = axis == 0 ? 1 : axis == 1 ? res : res * res;
        Parallel.For(0, res * res, line =>
        {
            int u = line % res, v = line / res;
            int start = axis switch
            {
                0 => (v * res + u) * res,
                1 => v * res * res + u,
                _ => v * res + u,
            };

            for (int k = 1; k < res; k++) TryNeighbour(surface, best, origin, cell, res, start + k * stride, start + (k - 1) * stride);
            for (int k = res - 2; k >= 0; k--) TryNeighbour(surface, best, origin, cell, res, start + k * stride, start + (k + 1) * stride);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TryNeighbour(Surface surface, long[] best, Float3 origin, Float3 cell, int res, int i, int from)
    {
        long neighbour = best[from];
        if (neighbour == Empty) return;
        int t = (int)(uint)neighbour;
        long current = best[i];
        if (current != Empty && (int)(uint)current == t) return;

        int x = i % res, y = i / res % res, z = i / (res * res);
        Float3 p = Centre(origin, cell, x, y, z);
        surface.Corners(t, out Float3 a, out Float3 b, out Float3 c);
        Float3 d = p - ClosestPoint(p, a, b, c);
        long candidate = Pack(Float3.Dot(d, d), t);
        if (candidate < current) best[i] = candidate;
    }

    /// <summary>Closest point on triangle (a,b,c) to p, Ericson's Voronoi region method.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Float3 ClosestPoint(Float3 p, Float3 a, Float3 b, Float3 c)
    {
        Float3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Float3.Dot(ab, ap);
        float d2 = Float3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Float3 bp = p - b;
        float d3 = Float3.Dot(ab, bp);
        float d4 = Float3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));

        Float3 cp = p - c;
        float d5 = Float3.Dot(ab, cp);
        float d6 = Float3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1.0f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>A mesh's triangles over welded corners, with each one's face normal.</summary>
    internal sealed class Surface
    {
        private readonly Float3[] _positions;
        private readonly int[] _corners;
        private readonly Float3[] _faceNormals;

        public int TriangleCount => _faceNormals.Length;
        public AABB Bounds { get; }

        private Surface(Float3[] positions, int[] corners)
        {
            _positions = positions;
            _corners = corners;
            _faceNormals = new Float3[corners.Length / 3];
            for (int t = 0; t < _faceNormals.Length; t++)
            {
                Corners(t, out Float3 a, out Float3 b, out Float3 c);
                Float3 n = Float3.Cross(b - a, c - a);
                float length = Float3.Length(n);
                _faceNormals[t] = length > 0f ? n / length : Float3.Zero;
            }

            Float3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (Float3 p in positions)
            {
                min = Maths.Min(min, p);
                max = Maths.Max(max, p);
            }
            Bounds = new AABB(min, max);
        }

        public static Surface? From(Mesh mesh)
        {
            Float3[]? vertices = mesh.Vertices;
            uint[]? indices = mesh.Indices;
            if (vertices == null || indices == null || vertices.Length == 0 || indices.Length < 3) return null;

            // Corners at the same position are one, so triangles collapsed by a seam are dropped
            var welded = new Dictionary<Float3, int>();
            var remap = new int[vertices.Length];
            var positions = new List<Float3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!welded.TryGetValue(vertices[i], out int w))
                {
                    w = positions.Count;
                    welded[vertices[i]] = w;
                    positions.Add(vertices[i]);
                }
                remap[i] = w;
            }

            var corners = new List<int>(indices.Length);
            for (int s = 0; s < mesh.SubMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (sub.Topology != Topology.Triangles) continue;
                int end = Math.Min(sub.IndexStart + sub.IndexCount, indices.Length);
                for (int i = sub.IndexStart; i + 2 < end; i += 3)
                {
                    uint ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    if (ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length) continue;
                    int a = remap[ia], b = remap[ib], c = remap[ic];
                    if (a == b || b == c || a == c) continue;
                    corners.Add(a);
                    corners.Add(b);
                    corners.Add(c);
                }
            }
            return corners.Count == 0 ? null : new Surface(positions.ToArray(), corners.ToArray());
        }

        /// <summary>For tests and generators that already hold triangles: corners index into <paramref name="positions"/>.</summary>
        internal static Surface FromTriangles(Float3[] positions, int[] corners) => new(positions, corners);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Corners(int t, out Float3 a, out Float3 b, out Float3 c)
        {
            a = _positions[_corners[t * 3]];
            b = _positions[_corners[t * 3 + 1]];
            c = _positions[_corners[t * 3 + 2]];
        }

        public Float3 FaceNormal(int t) => _faceNormals[t];
    }
}
