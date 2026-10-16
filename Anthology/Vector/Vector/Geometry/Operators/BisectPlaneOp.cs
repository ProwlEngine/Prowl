// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;


namespace Prowl.Vector.Geometry.Operators;

internal static class BisectPlaneOp
{
    /// <summary>
    /// Cuts the mesh along a plane: edges that cross it are split and faces on both sides are divided along the
    /// line. A face the plane crosses more than once, as a concave face can be, is cut until no face straddles it.
    /// </summary>
    internal static void BisectPlane(GeometryData mesh, Plane plane, float epsilon = 0.0001f, bool snapToPlane = true)
    {
        var side = new Dictionary<GeometryData.Vertex, int>();
        var distances = new Dictionary<GeometryData.Vertex, float>();

        foreach (var v in mesh.Vertices.ToArray())
        {
            float distance = plane.GetSignedDistanceToPoint(v.Point);
            distances[v] = distance;
            if (distance <= -epsilon) side[v] = -1;
            else if (distance >= epsilon) side[v] = 1;
            else
            {
                side[v] = 0;
                if (snapToPlane) v.Point = plane.ClosestPointOnPlane(v.Point);
            }
        }

        var crossing = new List<(GeometryData.Edge Edge, float Factor)>();
        foreach (var edge in mesh.Edges.ToArray())
        {
            if (!side.TryGetValue(edge.Vert1, out int s1) || !side.TryGetValue(edge.Vert2, out int s2)) continue;
            if (s1 == 0 || s2 == 0 || s1 == s2) continue;

            float d1 = distances[edge.Vert1], d2 = distances[edge.Vert2];
            crossing.Add((edge, d1 / (d1 - d2)));
        }

        foreach (var (edge, factor) in crossing)
        {
            var made = SplitEdgeOp.SplitEdge(mesh, edge, edge.Vert1, factor, out _);
            side[made] = 0;
            distances[made] = 0f;
        }

        var queue = new Queue<GeometryData.Face>(mesh.Faces);
        while (queue.Count > 0)
        {
            var face = queue.Dequeue();
            if (!mesh.Faces.Contains(face)) continue;

            var pair = FindCut(face, side, plane);
            if (pair == null) continue;

            var (first, second) = SplitFaceOp.SplitFaceBoth(mesh, face, pair.Value.A, pair.Value.B, out _);
            if (first != null) queue.Enqueue(first);
            if (second != null) queue.Enqueue(second);
        }
    }

    /// <summary>The two corners on the plane to cut a face between, when it has corners on both sides of the plane.</summary>
    private static (GeometryData.Vertex A, GeometryData.Vertex B)? FindCut(GeometryData.Face face, Dictionary<GeometryData.Vertex, int> side, Plane plane)
    {
        List<GeometryData.Vertex> verts = face.NeighborVertices();
        bool positive = false, negative = false;
        var onPlane = new List<int>();
        for (int i = 0; i < verts.Count; i++)
        {
            if (!side.TryGetValue(verts[i], out int s)) continue;
            if (s > 0) positive = true;
            else if (s < 0) negative = true;
            else onPlane.Add(i);
        }
        if (!positive || !negative || onPlane.Count < 2) return null;

        var frame = new PlaneFrame(face.Normal(), verts[0].Point);
        List<Float2> flat = verts.Select(v => frame.To2D(v.Point)).ToList();

        // Prefer the cut that is farthest round the face, so a pair of corners on one side of it come last
        var candidates = new List<(int A, int B, int Gap)>();
        for (int i = 0; i < onPlane.Count; i++)
            for (int j = i + 1; j < onPlane.Count; j++)
            {
                int gap = Math.Min(onPlane[j] - onPlane[i], verts.Count - (onPlane[j] - onPlane[i]));
                if (gap > 1) candidates.Add((onPlane[i], onPlane[j], gap));
            }
        candidates.Sort((x, y) => y.Gap.CompareTo(x.Gap));

        // A cut that leaves each piece on one side is best, but any cut along the plane inside the face makes progress
        foreach (bool cleanOnly in new[] { true, false })
            foreach (var (a, b, _) in candidates)
            {
                if (cleanOnly && !SeparatesSides(verts, a, b, side)) continue;
                if (!ChordInside(flat, a, b)) continue;
                return (verts[a], verts[b]);
            }
        return null;
    }

    private static bool ChordInside(List<Float2> polygon, int a, int b)
    {
        if (!Polygon2D.Contains(polygon, (polygon[a] + polygon[b]) * 0.5f)) return false;
        for (int i = 0; i < polygon.Count; i++)
        {
            int j = (i + 1) % polygon.Count;
            if (i == a || i == b || j == a || j == b) continue;
            if (Polygon2D.ProperIntersection(polygon[a], polygon[b], polygon[i], polygon[j], 1e-6f, out _, out _)) return false;
        }
        return true;
    }

    /// <summary>True when each of the two pieces a cut makes has corners on only one side of the plane, and they are opposite sides.</summary>
    private static bool SeparatesSides(List<GeometryData.Vertex> verts, int a, int b, Dictionary<GeometryData.Vertex, int> side)
    {
        (bool Positive, bool Negative) Walk(int from, int to)
        {
            bool positive = false, negative = false;
            for (int i = (from + 1) % verts.Count; i != to; i = (i + 1) % verts.Count)
            {
                if (!side.TryGetValue(verts[i], out int s)) continue;
                if (s > 0) positive = true;
                if (s < 0) negative = true;
            }
            return (positive, negative);
        }

        var one = Walk(a, b);
        var other = Walk(b, a);
        bool oneClean = one.Positive != one.Negative;
        bool otherClean = other.Positive != other.Negative;
        return oneClean && otherClean && (one.Positive && other.Negative || one.Negative && other.Positive);
    }
}
