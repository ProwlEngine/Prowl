// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry;

/// <summary>Tests and measurements on polygons laid out in two dimensions.</summary>
public static class Polygon2D
{
    /// <summary>The z of the cross product of two vectors in the plane, positive when b turns counter clockwise from a.</summary>
    public static float Cross(Float2 a, Float2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>The cross product of a and b taken from o, positive when o, a, b turn counter clockwise.</summary>
    public static float Cross(Float2 o, Float2 a, Float2 b) => Cross(a - o, b - o);

    /// <summary>The area of a polygon, positive when it runs counter clockwise.</summary>
    public static float SignedArea(IReadOnlyList<Float2> poly)
    {
        float area = 0f;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            area += Cross(poly[j], poly[i]);
        return area * 0.5f;
    }

    /// <summary>The average of the corners.</summary>
    public static Float2 Centroid(IReadOnlyList<Float2> poly)
    {
        Float2 sum = Float2.Zero;
        foreach (Float2 p in poly) sum += p;
        return sum / poly.Count;
    }

    /// <summary>Even odd point in polygon test. Points on the boundary may land either way.</summary>
    public static bool Contains(IReadOnlyList<Float2> poly, Float2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Float2 a = poly[i], b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>The distance from a point to a segment, with how far along the segment the nearest point is, from 0 to 1.</summary>
    public static float DistanceToSegment(Float2 p, Float2 a, Float2 b, out float t)
    {
        Float2 ab = b - a;
        float lengthSq = ab.X * ab.X + ab.Y * ab.Y;
        t = lengthSq > 1e-12f ? Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSq, 0f, 1f) : 0f;
        Float2 d = a + ab * t - p;
        return MathF.Sqrt(d.X * d.X + d.Y * d.Y);
    }

    /// <summary>Whether two segments cross at a point inside both, away from their ends by more than epsilon.</summary>
    public static bool ProperIntersection(Float2 a, Float2 b, Float2 c, Float2 d, float epsilon, out float t, out float s)
    {
        t = s = 0f;
        Float2 r = b - a, q = d - c;
        float denom = Cross(r, q);
        if (MathF.Abs(denom) < 1e-12f) return false;

        Float2 ac = c - a;
        t = Cross(ac, q) / denom;
        s = Cross(ac, r) / denom;

        float lengthR = MathF.Sqrt(r.X * r.X + r.Y * r.Y), lengthQ = MathF.Sqrt(q.X * q.X + q.Y * q.Y);
        float et = epsilon / MathF.Max(lengthR, 1e-12f), es = epsilon / MathF.Max(lengthQ, 1e-12f);
        return t > et && t < 1f - et && s > es && s < 1f - es;
    }

    /// <summary>Whether two segments touch or cross anywhere, ends included.</summary>
    public static bool SegmentsTouch(Float2 a, Float2 b, Float2 c, Float2 d, float epsilon)
    {
        if (ProperIntersection(a, b, c, d, -epsilon, out _, out _)) return true;
        return DistanceToSegment(a, c, d, out _) < epsilon || DistanceToSegment(b, c, d, out _) < epsilon
            || DistanceToSegment(c, a, b, out _) < epsilon || DistanceToSegment(d, a, b, out _) < epsilon;
    }

    /// <summary>
    /// Ear clipping for a simple counter clockwise polygon. Returns index triples, counter clockwise.
    /// Repeated points, as in a polygon pinched at one vertex, are tolerated.
    /// </summary>
    public static List<int> Triangulate(IReadOnlyList<Float2> poly)
    {
        var triangles = new List<int>((poly.Count - 2) * 3);
        if (poly.Count < 3) return triangles;

        var remaining = new List<int>(poly.Count);
        for (int i = 0; i < poly.Count; i++) remaining.Add(i);
        if (SignedArea(poly) < 0f) remaining.Reverse();

        int guard = poly.Count * poly.Count + 16;
        while (remaining.Count > 3 && guard-- > 0)
        {
            int ear = FindEar(poly, remaining);
            if (ear < 0)
            {
                // Nothing clips cleanly, which only happens with degenerate input, so take the most convex corner
                ear = MostConvex(poly, remaining);
            }

            int n = remaining.Count;
            int prev = remaining[(ear - 1 + n) % n], cur = remaining[ear], next = remaining[(ear + 1) % n];
            if (MathF.Abs(Cross(poly[prev], poly[cur], poly[next])) > 1e-12f)
                triangles.AddRange([prev, cur, next]);
            remaining.RemoveAt(ear);
        }

        if (remaining.Count == 3 && MathF.Abs(Cross(poly[remaining[0]], poly[remaining[1]], poly[remaining[2]])) > 1e-12f)
            triangles.AddRange([remaining[0], remaining[1], remaining[2]]);

        return triangles;
    }

    private static int FindEar(IReadOnlyList<Float2> poly, List<int> remaining)
    {
        int n = remaining.Count;
        for (int i = 0; i < n; i++)
        {
            int prev = remaining[(i - 1 + n) % n], cur = remaining[i], next = remaining[(i + 1) % n];
            Float2 a = poly[prev], b = poly[cur], c = poly[next];
            if (Cross(a, b, c) <= 1e-12f) continue;

            bool blocked = false;
            for (int k = 0; k < n && !blocked; k++)
            {
                int other = remaining[k];
                if (other == prev || other == cur || other == next) continue;
                Float2 p = poly[other];
                if (Same(p, a) || Same(p, b) || Same(p, c)) continue;
                blocked = InTriangle(p, a, b, c);
            }

            if (!blocked) return i;
        }
        return -1;
    }

    private static int MostConvex(IReadOnlyList<Float2> poly, List<int> remaining)
    {
        int n = remaining.Count, best = 0;
        float bestCross = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            float cross = Cross(poly[remaining[(i - 1 + n) % n]], poly[remaining[i]], poly[remaining[(i + 1) % n]]);
            if (cross > bestCross) { bestCross = cross; best = i; }
        }
        return best;
    }

    private static bool Same(Float2 a, Float2 b) => MathF.Abs(a.X - b.X) < 1e-7f && MathF.Abs(a.Y - b.Y) < 1e-7f;

    private static bool InTriangle(Float2 p, Float2 a, Float2 b, Float2 c)
        => Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f;
}
