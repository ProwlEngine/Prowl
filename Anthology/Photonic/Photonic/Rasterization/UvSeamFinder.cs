// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Photonic.Rasterization;

/// <summary>
/// Finds the edges where a mesh is continuous in space but split in the bake UV layout. Those are
/// the edges whose two sides get lit into unrelated parts of the atlas and then show up as a visible
/// seam on the model, and they are what <see cref="Imaging.SeamFixer"/> stitches back together.
/// </summary>
/// <remarks>
/// An edge is a seam when the two triangles sharing it (by welded position, not by vertex index,
/// since a UV split duplicates the vertices) have matching normals but different bake UVs, and their
/// UV edges are not simply two halves of the same line.
/// </remarks>
internal static class UvSeamFinder
{
    /// <summary>One seam edge, given as the same world edge expressed in both of its UV locations.</summary>
    internal readonly struct SeamSegment
    {
        public readonly Float2 A0, A1, B0, B1;
        public SeamSegment(Float2 a0, Float2 a1, Float2 b0, Float2 b1) { A0 = a0; A1 = a1; B0 = b0; B1 = b1; }
    }

    private readonly struct Edge
    {
        public readonly int V0, V1;
        public Edge(int v0, int v1) { V0 = v0; V1 = v1; }
    }

    /// <summary>Vertex normals this far apart mark a hard edge, where the two sides really are lit differently.</summary>
    private const float NormalAgreement = 0.9f;
    private const float UvEpsilon = 1e-5f;

    public static SeamSegment[] Find(BakeMesh mesh, string bakeUVLayer)
    {
        if (!mesh.UVLayers.TryGetValue(bakeUVLayer, out var uv)) return System.Array.Empty<SeamSegment>();
        var positions = mesh.Positions;
        if (positions.Length == 0) return System.Array.Empty<SeamSegment>();

        var extent = mesh.Bounds.Max - mesh.Bounds.Min;
        float weldEpsilon = System.MathF.Max(1e-5f, Float3.Length(extent) * 1e-5f);
        var weld = BuildWeldMap(positions, weldEpsilon);

        var buckets = new Dictionary<(int, int), List<Edge>>(positions.Length);
        for (int g = 0; g < mesh.MaterialGroups.Count; g++)
        {
            var indices = mesh.MaterialGroups[g].Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                AddEdge(buckets, weld, indices[i], indices[i + 1]);
                AddEdge(buckets, weld, indices[i + 1], indices[i + 2]);
                AddEdge(buckets, weld, indices[i + 2], indices[i]);
            }
        }

        var normals = mesh.Normals;
        var seams = new List<SeamSegment>();
        foreach (var bucket in buckets.Values)
        {
            int count = System.Math.Min(bucket.Count, 4);
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                var a = bucket[i];
                var b = bucket[j];
                if (!NormalsMatch(normals, a.V0, b.V0) || !NormalsMatch(normals, a.V1, b.V1)) continue;

                var a0 = uv[a.V0]; var a1 = uv[a.V1];
                var b0 = uv[b.V0]; var b1 = uv[b.V1];
                if (Float2.Distance(a0, b0) <= UvEpsilon && Float2.Distance(a1, b1) <= UvEpsilon) continue;
                if (SharesUvSegment(a0, a1, b0, b1)) continue;

                seams.Add(new SeamSegment(a0, a1, b0, b1));
            }
        }
        return seams.ToArray();
    }

    private static void AddEdge(Dictionary<(int, int), List<Edge>> buckets, int[] weld, int v0, int v1)
    {
        int w0 = weld[v0], w1 = weld[v1];
        if (w0 == w1) return;
        // Order both the key and the stored endpoints by welded id, so the two sides of a seam are
        // bucketed together and their endpoints line up without a second matching step.
        var key = w0 < w1 ? (w0, w1) : (w1, w0);
        var edge = w0 < w1 ? new Edge(v0, v1) : new Edge(v1, v0);
        if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<Edge>(2);
        list.Add(edge);
    }

    private static bool NormalsMatch(Float3[] normals, int a, int b)
    {
        if (normals.Length == 0) return true;
        var na = normals[a];
        var nb = normals[b];
        if (Float3.LengthSquared(na) < 1e-8f || Float3.LengthSquared(nb) < 1e-8f) return true;
        return Float3.Dot(Float3.Normalize(na), Float3.Normalize(nb)) >= NormalAgreement;
    }

    /// <summary>
    /// True when both UV edges lie on one line and overlap: the layout is continuous there even
    /// though the vertices do not coincide, so there is nothing to stitch.
    /// </summary>
    private static bool SharesUvSegment(Float2 a0, Float2 a1, Float2 b0, Float2 b1)
    {
        var dir = a1 - a0;
        float lengthSq = Float2.LengthSquared(dir);
        if (lengthSq < 1e-12f) return false;

        // Perpendicular distance of each B endpoint from the A line, as a fraction of the edge length.
        float length = System.MathF.Sqrt(lengthSq);
        const float Collinear = 1e-3f;
        if (System.MathF.Abs(Float2.Cross(dir, b0 - a0)) / length > length * Collinear) return false;
        if (System.MathF.Abs(Float2.Cross(dir, b1 - a0)) / length > length * Collinear) return false;

        float t0 = Float2.Dot(b0 - a0, dir) / lengthSq;
        float t1 = Float2.Dot(b1 - a0, dir) / lengthSq;
        float lo = System.MathF.Min(t0, t1), hi = System.MathF.Max(t0, t1);
        return System.MathF.Min(hi, 1f) - System.MathF.Max(lo, 0f) > 1e-3f;
    }

    /// <summary>Maps every vertex to the lowest index sharing its position, via a spatial hash with a one-cell probe.</summary>
    private static int[] BuildWeldMap(Float3[] positions, float epsilon)
    {
        var weld = new int[positions.Length];
        var cells = new Dictionary<(int, int, int), List<int>>(positions.Length);
        float inv = 1f / epsilon;
        float epsilonSq = epsilon * epsilon;

        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            int cx = (int)System.MathF.Floor(p.X * inv);
            int cy = (int)System.MathF.Floor(p.Y * inv);
            int cz = (int)System.MathF.Floor(p.Z * inv);

            int representative = -1;
            for (int dz = -1; dz <= 1 && representative < 0; dz++)
            for (int dy = -1; dy <= 1 && representative < 0; dy++)
            for (int dx = -1; dx <= 1 && representative < 0; dx++)
            {
                if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    if (Float3.DistanceSquared(positions[list[k]], p) > epsilonSq) continue;
                    representative = weld[list[k]];
                    break;
                }
            }

            weld[i] = representative >= 0 ? representative : i;
            if (!cells.TryGetValue((cx, cy, cz), out var own)) cells[(cx, cy, cz)] = own = new List<int>(4);
            own.Add(i);
        }
        return weld;
    }
}
