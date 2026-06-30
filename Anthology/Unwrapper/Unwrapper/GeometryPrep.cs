using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>
/// Triangle/vertex array view of the input mesh — what the unwrapper actually consumes.
/// </summary>
internal sealed class CleanedGeometry
{
    public double[] Positions = System.Array.Empty<double>();
    public int[] Triangles = System.Array.Empty<int>();
    public int[] TriangleRemap = System.Array.Empty<int>();
    public int[]? DegenerateTriangleIndices;
    public double[]? TriangleUVs;

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Triangles.Length / 3;
}

/// <summary>
/// Cleans up the input mesh before feeding it to the half-edge builder:
/// fits it inside a unit cube, welds duplicate vertices, drops degenerate triangles,
/// removes the now-unused vertices, and surfaces a remap from the cleaned triangles
/// back to the original triangle indices.
/// </summary>
internal static class GeometryPrep
{
    private const double WeldDistance = 1e-6;
    private const double AreaEpsilon = 1e-10;
    private const double SideEpsilon = 1e-6;
    private const double AngleEpsilon = 1e-3;

    public static bool TryPrepare(
        Double3[] vertices,
        Double3[]? normals,
        int[] triangles,
        Double2[]? perCornerUVs,
        out CleanedGeometry result,
        out string? error,
        System.Action<string>? progress = null)
    {
        result = new CleanedGeometry();
        error = null;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int vertexCount = vertices.Length;
        int triangleCount = triangles.Length / 3;
        if (vertexCount == 0 || triangleCount == 0)
        {
            error = "Mesh has no vertices or triangles.";
            return false;
        }

        double[] positions = new double[3 * vertexCount];
        for (int i = 0; i < vertexCount; ++i)
        {
            positions[3 * i + 0] = vertices[i].X;
            positions[3 * i + 1] = vertices[i].Y;
            positions[3 * i + 2] = vertices[i].Z;
        }

        FitIntoCube(positions, new Double3(0, 0, 0), side: 1.0);
        progress?.Invoke($"[prep] flatten + fit in {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        // Weld coincident corners that share a "compatible" normal — keeps creases as creases.
        // Rewrites triCleaned in place to point at the welded positions.
        var triCleaned = (int[])triangles.Clone();
        WeldVertices(positions, vertexCount, triCleaned, triangleCount, normals, out int newPosCount);
        positions = ResizeArray(positions, 3 * newPosCount);
        progress?.Invoke($"[prep] weld in {sw.ElapsedMilliseconds} ms ({vertexCount} -> {newPosCount} verts)");
        sw.Restart();

        // Make the topology manifold before the half-edge builder sees it: edges with >2
        // faces, mismatched winding, or double-sided dupes get cut by duplicating vertices.
        var fixResult = NonManifoldFixer.Fix(newPosCount, triCleaned, progress);
        if (fixResult.ExtraVertexSourceIndices.Count > 0)
        {
            int extras = fixResult.ExtraVertexSourceIndices.Count;
            int grown = newPosCount + extras;
            double[] expanded = new double[3 * grown];
            System.Array.Copy(positions, expanded, 3 * newPosCount);
            for (int i = 0; i < extras; ++i)
            {
                int src = fixResult.ExtraVertexSourceIndices[i];
                expanded[3 * (newPosCount + i) + 0] = positions[3 * src + 0];
                expanded[3 * (newPosCount + i) + 1] = positions[3 * src + 1];
                expanded[3 * (newPosCount + i) + 2] = positions[3 * src + 2];
            }
            positions = expanded;
            newPosCount = grown;
        }
        progress?.Invoke($"[prep] non-manifold fix in {sw.ElapsedMilliseconds} ms ({fixResult.ExtraVertexSourceIndices.Count} cuts)");
        sw.Restart();

        // Per-triangle UVs come in flat as 6 doubles per triangle (3 corners × 2 components).
        double[]? flatUVs = null;
        if (perCornerUVs is not null)
        {
            if (perCornerUVs.Length != 3 * triangleCount)
            {
                error = "perCornerUVs length must equal 3 * triangleCount.";
                return false;
            }
            flatUVs = new double[6 * triangleCount];
            for (int t = 0; t < triangleCount; ++t)
            {
                for (int c = 0; c < 3; ++c)
                {
                    flatUVs[6 * t + 2 * c + 0] = perCornerUVs[3 * t + c].X;
                    flatUVs[6 * t + 2 * c + 1] = perCornerUVs[3 * t + c].Y;
                }
            }
        }

        var triRemap = new int[triangleCount];
        for (int t = 0; t < triangleCount; ++t) triRemap[t] = t;

        RemoveDegenerate(ref positions, ref newPosCount, triCleaned, ref triangleCount, triRemap, out int[]? degenerate);
        positions = ResizeArray(positions, 3 * newPosCount);
        triCleaned = ResizeArray(triCleaned, 3 * triangleCount);

        if (triangleCount == 0)
        {
            error = "No triangles left after cleanup — input geometry may be fully degenerate or welded.";
            return false;
        }

        result.Positions = positions;
        result.Triangles = triCleaned;
        result.TriangleRemap = ResizeArray(triRemap, triangleCount);
        result.DegenerateTriangleIndices = degenerate;

        if (flatUVs is not null)
        {
            // Remap original-triangle-indexed UVs to surviving triangles.
            var newUVs = new double[6 * triangleCount];
            for (int t = 0; t < triangleCount; ++t)
            {
                int src = result.TriangleRemap[t];
                System.Array.Copy(flatUVs, 6 * src, newUVs, 6 * t, 6);
            }
            result.TriangleUVs = newUVs;
        }

        return true;
    }

    /// <summary>Scale and recentre so the longest axis fits inside [-side/2, side/2].</summary>
    private static void FitIntoCube(double[] pos, Double3 center, double side)
    {
        int n = pos.Length / 3;
        double invN = 1.0 / n;

        Double3 centroid = new(-center.X, -center.Y, -center.Z);
        Double3 lo = new(1e32, 1e32, 1e32), hi = new(-1e32, -1e32, -1e32);

        for (int i = 0; i < n; ++i)
        {
            double x = pos[3 * i + 0], y = pos[3 * i + 1], z = pos[3 * i + 2];
            lo.X = System.Math.Min(lo.X, x); hi.X = System.Math.Max(hi.X, x);
            lo.Y = System.Math.Min(lo.Y, y); hi.Y = System.Math.Max(hi.Y, y);
            lo.Z = System.Math.Min(lo.Z, z); hi.Z = System.Math.Max(hi.Z, z);
            centroid.X += x * invN;
            centroid.Y += y * invN;
            centroid.Z += z * invN;
        }

        Double3 ext = hi - lo;
        const double tooSmall = 1e-6;
        const double tooBig = 1e6;
        double maxAxis = System.Math.Max(System.Math.Max(ext.X, ext.Y), ext.Z);
        if (maxAxis < tooSmall || maxAxis > tooBig) return;

        double scale = 0.5 * side / maxAxis;
        for (int i = 0; i < n; ++i)
        {
            pos[3 * i + 0] = (pos[3 * i + 0] - centroid.X) * scale;
            pos[3 * i + 1] = (pos[3 * i + 1] - centroid.Y) * scale;
            pos[3 * i + 2] = (pos[3 * i + 2] - centroid.Z) * scale;
        }
    }

    /// <summary>
    /// Per-corner vertex weld, guarded by normal compatibility so creases survive.
    /// Walks triangle-by-triangle: each corner gets a normal (per-vertex if the caller provided one,
    /// otherwise the auto-computed face normal of the current triangle) and is find-or-added into the
    /// position hash. Triangles are rewritten in place to reference the welded slots.
    /// </summary>
    /// <remarks>
    /// When no per-vertex normals are supplied, we fall back to the triangle's face normal so
    /// two corners at the same position from triangles that face nearly-opposite directions
    /// (dot &lt; -0.9) stay separate — that keeps thin shells and back-to-back walls from collapsing.
    /// </remarks>
    private static void WeldVertices(double[] pos, int vertexCount, int[] triangles, int triangleCount, Double3[]? perVertexNormals, out int newVertexCount)
    {
        var welded = new List<Double3>();
        var weldedNormals = new List<Double3>();
        var bucket = new Dictionary<long, List<int>>();

        const double cell = WeldDistance * 5.0;

        long Bucket(Double3 p)
        {
            long bx = (long)System.Math.Floor(p.X / cell);
            long by = (long)System.Math.Floor(p.Y / cell);
            long bz = (long)System.Math.Floor(p.Z / cell);
            unchecked
            {
                return (bx * 73856093L) ^ (by * 19349663L) ^ (bz * 83492791L);
            }
        }

        int FindOrAdd(Double3 p, Double3 normal)
        {
            // Probe all 27 neighbouring cells so positions on a cell boundary still find each other.
            for (int dx = -1; dx <= 1; ++dx)
            for (int dy = -1; dy <= 1; ++dy)
            for (int dz = -1; dz <= 1; ++dz)
            {
                Double3 probe = p + new Double3(dx * cell, dy * cell, dz * cell);
                long key = Bucket(probe);
                if (!bucket.TryGetValue(key, out var candidates)) continue;
                foreach (int candidate in candidates)
                {
                    if (Double3.Distance(p, welded[candidate]) > WeldDistance) continue;
                    // -0.9 dot = "facing within ~155° of each other"; tighter than that and the
                    // crease gets respected (vertices stay separate).
                    if (Double3.Dot(normal, weldedNormals[candidate]) < -0.9) continue;
                    return candidate;
                }
            }

            int newIndex = welded.Count;
            welded.Add(p);
            weldedNormals.Add(normal);
            long bk = Bucket(p);
            if (!bucket.TryGetValue(bk, out var list)) bucket[bk] = list = new List<int>();
            list.Add(newIndex);
            return newIndex;
        }

        for (int triI = 0; triI < triangleCount; ++triI)
        {
            int i0 = triangles[3 * triI + 0];
            int i1 = triangles[3 * triI + 1];
            int i2 = triangles[3 * triI + 2];
            Double3 p0 = new(pos[3 * i0 + 0], pos[3 * i0 + 1], pos[3 * i0 + 2]);
            Double3 p1 = new(pos[3 * i1 + 0], pos[3 * i1 + 1], pos[3 * i1 + 2]);
            Double3 p2 = new(pos[3 * i2 + 0], pos[3 * i2 + 1], pos[3 * i2 + 2]);

            Double3 n0, n1, n2;
            if (perVertexNormals is not null)
            {
                // Caller-supplied per-vertex normals.
                n0 = perVertexNormals[i0];
                n1 = perVertexNormals[i1];
                n2 = perVertexNormals[i2];
            }
            else
            {
                // Auto: triangle face normal applied to all three corners (matches CalculateNormals).
                Double3 cross = Double3.Cross(p1 - p0, p2 - p0);
                double len = Double3.Length(cross);
                Double3 faceNormal = len > NumericHelpers.Tiny ? cross / len : default;
                n0 = n1 = n2 = faceNormal;
            }

            triangles[3 * triI + 0] = FindOrAdd(p0, n0);
            triangles[3 * triI + 1] = FindOrAdd(p1, n1);
            triangles[3 * triI + 2] = FindOrAdd(p2, n2);
        }

        newVertexCount = welded.Count;
        for (int i = 0; i < newVertexCount; ++i)
        {
            pos[3 * i + 0] = welded[i].X;
            pos[3 * i + 1] = welded[i].Y;
            pos[3 * i + 2] = welded[i].Z;
        }
    }

    /// <summary>Drop tris that collapsed to a line/point or have a near-zero angle. Compacts position list too.</summary>
    private static void RemoveDegenerate(
        ref double[] pos, ref int posCount,
        int[] triVertex, ref int triCount,
        int[] srcTriI, out int[]? degenerateOut)
    {
        int last = triCount - 1;

        for (int triI = triCount - 1; triI >= 0; --triI)
        {
            int i0 = triVertex[3 * triI + 0];
            int i1 = triVertex[3 * triI + 1];
            int i2 = triVertex[3 * triI + 2];

            bool degenerate = (i0 == i1 || i1 == i2 || i2 == i0);

            if (!degenerate)
            {
                Double3 a = new(pos[3 * i0 + 0], pos[3 * i0 + 1], pos[3 * i0 + 2]);
                Double3 b = new(pos[3 * i1 + 0], pos[3 * i1 + 1], pos[3 * i1 + 2]);
                Double3 c = new(pos[3 * i2 + 0], pos[3 * i2 + 1], pos[3 * i2 + 2]);
                Double3 e0 = b - a;
                Double3 e1 = c - b;
                Double3 e2 = a - c;

                double area = System.Math.Sqrt(Double3.Length(Double3.Cross(e0, -e2)));
                if (area < AreaEpsilon) degenerate = true;
                else
                {
                    double l0 = Double3.Length(e0), l1 = Double3.Length(e1), l2 = Double3.Length(e2);
                    if (l0 < SideEpsilon || l1 < SideEpsilon || l2 < SideEpsilon)
                    {
                        degenerate = true;
                    }
                    else
                    {
                        e0 /= l0; e1 /= l1; e2 /= l2;
                        double a0 = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double3.Dot(e0, e2), -1.0, 1.0));
                        double a1 = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double3.Dot(e1, e0), -1.0, 1.0));
                        double a2 = System.Math.PI - System.Math.Acos(System.Math.Clamp(Double3.Dot(e2, e1), -1.0, 1.0));
                        if (a0 < AngleEpsilon || a1 < AngleEpsilon || a2 < AngleEpsilon) degenerate = true;
                    }
                }
            }

            if (degenerate)
            {
                triVertex[3 * triI + 0] = triVertex[3 * last + 0];
                triVertex[3 * triI + 1] = triVertex[3 * last + 1];
                triVertex[3 * triI + 2] = triVertex[3 * last + 2];
                (srcTriI[triI], srcTriI[last]) = (srcTriI[last], srcTriI[triI]);
                --last;
            }
        }

        triCount = last + 1;

        if (last < triVertex.Length / 3 - 1)
        {
            int count = (triVertex.Length / 3) - (last + 1);
            degenerateOut = new int[count];
            System.Array.Copy(srcTriI, last + 1, degenerateOut, 0, count);
        }
        else
        {
            degenerateOut = null;
        }

        // Compact unused vertices and re-index triangles to match.
        bool[] used = new bool[posCount];
        for (int t = 0; t < triCount; ++t)
        {
            used[triVertex[3 * t + 0]] = true;
            used[triVertex[3 * t + 1]] = true;
            used[triVertex[3 * t + 2]] = true;
        }

        int[] posRemap = new int[posCount];
        for (int i = 0; i < posCount; ++i) posRemap[i] = i;

        int dst = 0;
        while (dst < posCount && used[dst]) ++dst;

        if (dst != posCount)
        {
            int src = dst;
            while (src < posCount)
            {
                if (used[src])
                {
                    pos[3 * dst + 0] = pos[3 * src + 0];
                    pos[3 * dst + 1] = pos[3 * src + 1];
                    pos[3 * dst + 2] = pos[3 * src + 2];
                    posRemap[src] = dst++;
                }
                ++src;
            }
        }
        posCount = dst;

        for (int i = 0; i < 3 * triCount; ++i)
            triVertex[i] = posRemap[triVertex[i]];
    }

    private static T[] ResizeArray<T>(T[] array, int newLength)
    {
        if (array.Length == newLength) return array;
        var copy = new T[newLength];
        System.Array.Copy(array, copy, System.Math.Min(array.Length, newLength));
        return copy;
    }
}
