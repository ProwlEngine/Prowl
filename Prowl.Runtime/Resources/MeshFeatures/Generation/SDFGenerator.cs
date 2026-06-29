// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Prowl.Graphite;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Stopwatch = System.Diagnostics.Stopwatch;

namespace Prowl.Runtime.MeshFeatures.Generation;

/// <summary>
/// CPU-side generator for an unsigned distance field stored in <see cref="MeshSDF"/>.
/// Brute-force iterates every triangle per voxel, with a cheap per-triangle bounding-sphere
/// reject so far-away triangles never reach the exact closest-point computation.
/// </summary>
/// <remarks>
/// The field is unsigned values are always &gt;= 0. Sufficient for sphere tracing from
/// outside the surface (the use case for raymarched previews and proximity queries).
/// </remarks>
public static class SDFGenerator
{
    public struct Options
    {
        /// <summary>Grid resolution along each axis. Default 64.</summary>
        public int Resolution;

        /// <summary>Margin added around the source mesh AABB, as fraction of the longest bounds axis. Default 0.1.</summary>
        public float PaddingFraction;

        /// <summary>
        /// Distance values are clamped to <c>MaxDistanceFraction × longest_axis</c>. Smaller
        /// values dramatically speed up generation because more triangles get sphere-rejected.
        /// Default 0.25.
        /// </summary>
        public float MaxDistanceFraction;

        public static Options Default => new()
        {
            Resolution = 64,
            PaddingFraction = 0.1f,
            MaxDistanceFraction = 0.25f,
        };
    }

    /// <summary>
    /// Build a <see cref="MeshSDF"/> for the mesh. Returns null if the mesh has no usable
    /// triangles. Blocks the calling thread.
    /// </summary>
    public static MeshSDF? Generate(Mesh mesh, Options options)
    {
        var totalSw = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();

        var tris = ExtractTriangles(mesh);
        if (tris.Length == 0) return null;
        long tExtract = sw.ElapsedMilliseconds; sw.Restart();

        int res = Math.Max(4, options.Resolution);

        AABB bounds = ComputeBounds(tris);
        Float3 size = bounds.Max - bounds.Min;
        float longestAxis = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (longestAxis <= 0) return null;

        float padding = longestAxis * MathF.Max(0, options.PaddingFraction);
        bounds.Expand(padding);
        size = bounds.Max - bounds.Min;

        float maxDistance = longestAxis * MathF.Max(0.01f, options.MaxDistanceFraction);

        Float3 cellSize = new(size.X / res, size.Y / res, size.Z / res);

        // Per-tri pre-pass: center + (radius + maxDistance)² for the sphere reject.
        ComputeTriBounds(tris, maxDistance);
        long tPrep = sw.ElapsedMilliseconds; sw.Restart();

        float maxDistSq = maxDistance * maxDistance;
        float[] distances = new float[res * res * res];

        Parallel.For(0, res, z =>
        {
            float pz = bounds.Min.Z + (z + 0.5f) * cellSize.Z;
            int sliceBase = z * res * res;
            for (int y = 0; y < res; y++)
            {
                float py = bounds.Min.Y + (y + 0.5f) * cellSize.Y;
                int rowBase = sliceBase + y * res;
                for (int x = 0; x < res; x++)
                {
                    float px = bounds.Min.X + (x + 0.5f) * cellSize.X;

                    float minSq = maxDistSq;
                    for (int t = 0; t < tris.Length; t++)
                    {
                        ref readonly var tri = ref tris[t];

                        // Sphere reject: if voxel is further than (triRadius + maxDistance)
                        // from the triangle's centre, this triangle can't beat maxDistance.
                        float dx = px - tri.Center.X;
                        float dy = py - tri.Center.Y;
                        float dz = pz - tri.Center.Z;
                        float centreDistSq = dx * dx + dy * dy + dz * dz;
                        if (centreDistSq > tri.RejectDistSq) continue;

                        Float3 p = new(px, py, pz);
                        Float3 closest = ClosestPointOnTriangle(p, tri.A, tri.B, tri.C);
                        float ex = px - closest.X;
                        float ey = py - closest.Y;
                        float ez = pz - closest.Z;
                        float exactSq = ex * ex + ey * ey + ez * ez;
                        if (exactSq < minSq) minSq = exactSq;
                    }

                    distances[rowBase + x] = MathF.Sqrt(minSq);
                }
            }
        });
        long tVoxels = sw.ElapsedMilliseconds; sw.Restart();

        var volume = new Texture3D((uint)res, (uint)res, (uint)res, false, Graphite.PixelFormat.R32_Float);
        volume.SetData<float>(distances);
        long tUpload = sw.ElapsedMilliseconds;
        long tTotal = totalSw.ElapsedMilliseconds;

        Debug.Log(
            $"SDF '{mesh.Name}': {res}^3, {tris.Length:N0} tris. " +
            $"Total {tTotal} ms (extract {tExtract}, prep {tPrep}, voxels {tVoxels}, upload {tUpload})");

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
    /// Triangle plus its bounding sphere (centre + radius²) and a precomputed reject
    /// threshold = (radius + maxDistance)². Voxels further from the centre than this
    /// can't possibly bring the SDF below maxDistance via this triangle.
    /// </summary>
    internal struct Tri
    {
        public Float3 A, B, C;
        public Float3 Center;
        public float RejectDistSq;
    }

    private static Tri[] ExtractTriangles(Mesh mesh)
    {
        var verts = mesh.Vertices;
        var indices = mesh.Indices;
        if (verts == null || indices == null || verts.Length == 0 || indices.Length < 3)
            return Array.Empty<Tri>();

        var list = new List<Tri>(indices.Length / 3);
        int subCount = mesh.SubMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            var sub = mesh.GetSubMesh(s);
            if (sub.Topology != PrimitiveTopology.TriangleList) continue;

            int end = Math.Min(sub.IndexStart + sub.IndexCount, indices.Length);
            for (int i = sub.IndexStart; i + 2 < end; i += 3)
            {
                uint ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                if (ia >= verts.Length || ib >= verts.Length || ic >= verts.Length) continue;
                list.Add(new Tri { A = verts[ia], B = verts[ib], C = verts[ic] });
            }
        }
        return list.ToArray();
    }

    private static void ComputeTriBounds(Tri[] tris, float maxDistance)
    {
        for (int i = 0; i < tris.Length; i++)
        {
            ref var t = ref tris[i];
            Float3 c = (t.A + t.B + t.C) * (1f / 3f);
            float ra = SqDist(c, t.A);
            float rb = SqDist(c, t.B);
            float rc = SqDist(c, t.C);
            float radius = MathF.Sqrt(MathF.Max(ra, MathF.Max(rb, rc)));
            float reject = radius + maxDistance;
            t.Center = c;
            t.RejectDistSq = reject * reject;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SqDist(Float3 a, Float3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static AABB ComputeBounds(Tri[] tris)
    {
        Float3 min = new(float.MaxValue);
        Float3 max = new(float.MinValue);
        for (int i = 0; i < tris.Length; i++)
        {
            var t = tris[i];
            Encapsulate(ref min, ref max, t.A);
            Encapsulate(ref min, ref max, t.B);
            Encapsulate(ref min, ref max, t.C);
        }
        return new AABB(min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Encapsulate(ref Float3 min, ref Float3 max, Float3 p)
    {
        if (p.X < min.X) min.X = p.X;
        if (p.Y < min.Y) min.Y = p.Y;
        if (p.Z < min.Z) min.Z = p.Z;
        if (p.X > max.X) max.X = p.X;
        if (p.Y > max.Y) max.Y = p.Y;
        if (p.Z > max.Z) max.Z = p.Z;
    }

    /// <summary>Closest point on triangle (a,b,c) to p Ericson's Voronoi-region method.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Float3 ClosestPointOnTriangle(Float3 p, Float3 a, Float3 b, Float3 c)
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
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return a + ab * (d1 / (d1 - d3));

        Float3 cp = p - c;
        float d5 = Float3.Dot(ab, cp);
        float d6 = Float3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1.0f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
