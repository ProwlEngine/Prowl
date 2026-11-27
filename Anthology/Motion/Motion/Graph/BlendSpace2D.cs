using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// A triangulated 2D blend space. Inside the triangulation a point blends the three corners of its
/// triangle by barycentric weights, outside it is projected onto the nearest hull edge and blends
/// that edge's two ends. Collinear sample sets blend along the line.
/// </summary>
public sealed class BlendSpace2D
{
    private const float Epsilon = 1e-5f;

    private readonly Float2[] _points;
    private readonly int[] _triangles;
    private readonly int[] _hull;
    private readonly bool _hullIsClosed;

    public BlendSpace2D(IReadOnlyList<Float2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            throw new ArgumentException("A 2D blend space needs at least one sample.", nameof(points));

        _points = new Float2[points.Count];
        for (int i = 0; i < _points.Length; i++)
        {
            if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
                throw new ArgumentException("Blend space samples must be finite.", nameof(points));
            for (int j = 0; j < i; j++)
                if (DistanceSquared(points[i], _points[j]) < Epsilon * Epsilon)
                    throw new ArgumentException($"Blend space samples {j} and {i} share the same position.", nameof(points));
            _points[i] = points[i];
        }

        if (IsCollinear(_points, out Float2 direction))
        {
            _triangles = Array.Empty<int>();
            _hull = SortAlong(_points, direction);
            _hullIsClosed = false;
        }
        else
        {
            _triangles = Triangulate(_points);
            _hull = ConvexHull(_points);
            _hullIsClosed = true;
        }
    }

    /// <summary>The sample positions.</summary>
    public IReadOnlyList<Float2> Points => _points;

    /// <summary>Triangle corner indices, three per triangle (empty for collinear samples).</summary>
    public IReadOnlyList<int> Triangles => _triangles;

    /// <summary>
    /// Computes the samples and weights for a point. Writes up to three (index, weight) pairs, sorted
    /// by descending weight and summing to 1, and returns how many were written.
    /// </summary>
    public int Evaluate(Float2 point, Span<(int Index, float Weight)> result)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            point = _points[0];

        int count;
        if (_points.Length == 1)
        {
            result[0] = (0, 1f);
            return 1;
        }

        if (TryFindTriangle(point, result))
        {
            count = 3;
        }
        else
        {
            ProjectOntoHull(point, out int a, out int b, out float t);
            result[0] = (a, 1f - t);
            result[1] = (b, t);
            count = 2;
        }

        return Compact(result, count);
    }

    private bool TryFindTriangle(Float2 p, Span<(int Index, float Weight)> result)
    {
        for (int i = 0; i < _triangles.Length; i += 3)
        {
            int ia = _triangles[i], ib = _triangles[i + 1], ic = _triangles[i + 2];
            if (!Barycentric(p, _points[ia], _points[ib], _points[ic], out float u, out float v, out float w))
                continue;
            if (u < -Epsilon || v < -Epsilon || w < -Epsilon)
                continue;

            float sum = MathF.Max(u, 0f) + MathF.Max(v, 0f) + MathF.Max(w, 0f);
            result[0] = (ia, MathF.Max(u, 0f) / sum);
            result[1] = (ib, MathF.Max(v, 0f) / sum);
            result[2] = (ic, MathF.Max(w, 0f) / sum);
            return true;
        }
        return false;
    }

    private void ProjectOntoHull(Float2 p, out int bestA, out int bestB, out float bestT)
    {
        bestA = _hull[0];
        bestB = _hull.Length > 1 ? _hull[1] : _hull[0];
        bestT = 0f;
        float bestDistance = float.MaxValue;

        int edges = _hullIsClosed ? _hull.Length : _hull.Length - 1;
        for (int e = 0; e < edges; e++)
        {
            int a = _hull[e];
            int b = _hull[(e + 1) % _hull.Length];
            Float2 ab = _points[b] - _points[a];
            float lengthSq = ab.X * ab.X + ab.Y * ab.Y;
            float t = lengthSq > 0f ? Math.Clamp(((p.X - _points[a].X) * ab.X + (p.Y - _points[a].Y) * ab.Y) / lengthSq, 0f, 1f) : 0f;
            var closest = new Float2(_points[a].X + ab.X * t, _points[a].Y + ab.Y * t);
            float distance = DistanceSquared(p, closest);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestA = a;
                bestB = b;
                bestT = t;
            }
        }
    }

    // Drops near zero weights, renormalizes and sorts by descending weight.
    private static int Compact(Span<(int Index, float Weight)> result, int count)
    {
        int written = 0;
        for (int i = 0; i < count; i++)
            if (result[i].Weight > Epsilon)
                result[written++] = result[i];
        if (written == 0)
        {
            result[0] = (result[0].Index, 1f);
            return 1;
        }

        float total = 0f;
        for (int i = 0; i < written; i++)
            total += result[i].Weight;
        for (int i = 0; i < written; i++)
            result[i] = (result[i].Index, result[i].Weight / total);

        for (int i = 1; i < written; i++)
            for (int j = i; j > 0 && result[j].Weight > result[j - 1].Weight; j--)
                (result[j], result[j - 1]) = (result[j - 1], result[j]);
        return written;
    }

    private static bool Barycentric(Float2 p, Float2 a, Float2 b, Float2 c, out float u, out float v, out float w)
    {
        float denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(denominator) < 1e-12f)
        {
            u = v = w = 0f;
            return false;
        }
        u = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / denominator;
        v = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / denominator;
        w = 1f - u - v;
        return true;
    }

    private static bool IsCollinear(Float2[] points, out Float2 direction)
    {
        direction = new Float2(1f, 0f);
        if (points.Length < 3)
        {
            if (points.Length == 2)
                direction = Normalize(points[1] - points[0]);
            return true;
        }

        int far = 1;
        for (int i = 2; i < points.Length; i++)
            if (DistanceSquared(points[i], points[0]) > DistanceSquared(points[far], points[0]))
                far = i;
        direction = Normalize(points[far] - points[0]);

        float scale = MathF.Sqrt(DistanceSquared(points[far], points[0]));
        foreach (Float2 p in points)
        {
            Float2 d = p - points[0];
            float cross = d.X * direction.Y - d.Y * direction.X;
            if (MathF.Abs(cross) > Epsilon * MathF.Max(1f, scale))
                return false;
        }
        return true;
    }

    private static int[] SortAlong(Float2[] points, Float2 direction)
    {
        var order = new int[points.Length];
        var keys = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            order[i] = i;
            keys[i] = points[i].X * direction.X + points[i].Y * direction.Y;
        }
        Array.Sort(keys, order);
        return order;
    }

    // Andrew's monotone chain, counter clockwise, without repeating the first point.
    private static int[] ConvexHull(Float2[] points)
    {
        var order = new int[points.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => points[a].X != points[b].X ? points[a].X.CompareTo(points[b].X) : points[a].Y.CompareTo(points[b].Y));

        var hull = new List<int>(points.Length * 2);
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            for (int k = 0; k < order.Length; k++)
            {
                int i = pass == 0 ? order[k] : order[order.Length - 1 - k];
                while (hull.Count >= start + 2 && Cross(points[hull[^2]], points[hull[^1]], points[i]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(i);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull.ToArray();
    }

    // Bowyer Watson Delaunay triangulation.
    private static int[] Triangulate(Float2[] points)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Float2 p in points)
        {
            minX = MathF.Min(minX, p.X); minY = MathF.Min(minY, p.Y);
            maxX = MathF.Max(maxX, p.X); maxY = MathF.Max(maxY, p.Y);
        }
        float spanX = MathF.Max(maxX - minX, 1e-6f), spanY = MathF.Max(maxY - minY, 1e-6f);

        // Triangulated in a unit box so stretched layouts do not slip past the enclosing triangle.
        int n = points.Length;
        var all = new Float2[n + 3];
        for (int i = 0; i < n; i++)
            all[i] = new Float2((points[i].X - minX) / spanX, (points[i].Y - minY) / spanY);
        const float size = 1000f;
        all[n] = new Float2(0.5f - size, 0.5f - size);
        all[n + 1] = new Float2(0.5f + size, 0.5f - size);
        all[n + 2] = new Float2(0.5f, 0.5f + size);

        var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };
        var bad = new List<int>();
        var boundary = new List<(int A, int B)>();

        for (int i = 0; i < n; i++)
        {
            bad.Clear();
            for (int t = 0; t < triangles.Count; t++)
                if (InCircumcircle(all[i], all[triangles[t].A], all[triangles[t].B], all[triangles[t].C]))
                    bad.Add(t);

            boundary.Clear();
            foreach (int t in bad)
            {
                (int a, int b, int c) = triangles[t];
                AddBoundaryEdge(boundary, a, b);
                AddBoundaryEdge(boundary, b, c);
                AddBoundaryEdge(boundary, c, a);
            }

            for (int k = bad.Count - 1; k >= 0; k--)
                triangles.RemoveAt(bad[k]);
            foreach ((int a, int b) in boundary)
                triangles.Add((a, b, i));
        }

        var result = new List<int>(triangles.Count * 3);
        foreach ((int a, int b, int c) in triangles)
        {
            if (a >= n || b >= n || c >= n)
                continue;
            if (MathF.Abs(Cross(all[a], all[b], all[c])) < Epsilon * Epsilon)
                continue;
            result.Add(a);
            result.Add(b);
            result.Add(c);
        }
        return result.ToArray();
    }

    private static void AddBoundaryEdge(List<(int A, int B)> edges, int a, int b)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            if ((edges[i].A == b && edges[i].B == a) || (edges[i].A == a && edges[i].B == b))
            {
                edges.RemoveAt(i);
                return;
            }
        }
        edges.Add((a, b));
    }

    private static bool InCircumcircle(Float2 p, Float2 a, Float2 b, Float2 c)
    {
        if (Cross(a, b, c) < 0f)
            (b, c) = (c, b);
        double ax = a.X - p.X, ay = a.Y - p.Y;
        double bx = b.X - p.X, by = b.Y - p.Y;
        double cx = c.X - p.X, cy = c.Y - p.Y;
        double determinant = (ax * ax + ay * ay) * (bx * cy - cx * by)
                           - (bx * bx + by * by) * (ax * cy - cx * ay)
                           + (cx * cx + cy * cy) * (ax * by - bx * ay);
        return determinant > 0.0;
    }

    private static float Cross(Float2 o, Float2 a, Float2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static float DistanceSquared(Float2 a, Float2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static Float2 Normalize(Float2 v)
    {
        float length = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return length > 0f ? new Float2(v.X / length, v.Y / length) : new Float2(1f, 0f);
    }
}
