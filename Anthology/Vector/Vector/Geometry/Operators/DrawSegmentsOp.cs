// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

using static Prowl.Vector.Geometry.Operators.EditingCommon;

namespace Prowl.Vector.Geometry.Operators;

internal static class DrawSegmentsOp
{
    /// <summary>
    /// Draws lines into the mesh. Any closed loop they make on a plane becomes a face, and lines across an
    /// existing face split it.
    /// </summary>
    /// <param name="mesh">The mesh to draw into.</param>
    /// <param name="segments">The lines to draw.</param>
    /// <param name="planeNormal">The plane the lines were drawn on, when the tool knows it.</param>
    /// <param name="viewDirection">Where the camera looks, so new faces in the air face it.</param>
    /// <param name="material">The material new faces get.</param>
    internal static void DrawSegments(GeometryData mesh, IReadOnlyList<(Float3 A, Float3 B)> segments, Float3? planeNormal, Float3 viewDirection, int material)
    {
        var drawn = segments.Where(s => Float3.Length(s.B - s.A) > Eps).ToList();
        if (drawn.Count == 0) return;

        var seeds = new List<Vertex>();
        var newEdges = new List<Edge>();
        foreach ((Float3 a, Float3 b) in drawn)
        {
            Vertex va = mesh.GetOrCreateVertex(a);
            Vertex vb = mesh.GetOrCreateVertex(b);
            seeds.Add(va);
            seeds.Add(vb);
            Edge? edge = mesh.AddEdge(va, vb, false);
            if (edge != null) newEdges.Add(edge);
        }

        var planes = new List<PlaneKey>();
        if (planeNormal is Float3 given)
            AddPlane(planes, given, drawn[0].A);

        foreach ((Float3 a, Float3 b) in drawn)
        {
            foreach (Face face in mesh.Faces)
            {
                Float3 normal = face.Normal();
                Float3 origin = face.Loop!.Vert.Point;
                if (MathF.Abs(Float3.Dot(a - origin, normal)) > Eps || MathF.Abs(Float3.Dot(b - origin, normal)) > Eps) continue;
                AddPlane(planes, normal, origin);
            }
        }

        // A line that closes a loop in the air makes a plane with the lines it joins
        foreach (Edge edge in newEdges.ToList())
        {
            if (!mesh.Contains(edge)) continue;
            Float3 direction = Float3.Normalize(edge.Vert2.Point - edge.Vert1.Point);
            foreach (Vertex end in new[] { edge.Vert1, edge.Vert2 })
            {
                foreach (Edge other in end.NeighborEdges())
                {
                    if (other == edge || other.IsHidden) continue;
                    Float3 otherDirection = Float3.Normalize(other.OtherVertex(end).Point - end.Point);
                    Float3 normal = Float3.Cross(direction, otherDirection);
                    if (Float3.Length(normal) < 1e-3f) continue;
                    AddPlane(planes, OrientNew(Float3.Normalize(normal), viewDirection), end.Point);
                }
            }
        }

        foreach (PlaneKey plane in planes)
        {
            var inPlane = drawn.Where(s => MathF.Abs(Float3.Dot(s.A - plane.Origin, plane.Normal)) <= Eps
                                        && MathF.Abs(Float3.Dot(s.B - plane.Origin, plane.Normal)) <= Eps).ToList();
            if (inPlane.Count == 0) continue;
            PlaneRebuildOp.Run(mesh, plane.Normal, plane.Origin, seeds.Where(v => mesh.Contains(v)), inPlane, material);
        }

        mesh.RemoveUnused();
        mesh.MarkChanged();
    }

    /// <summary>
    /// Joins selected vertices that share a face with new edges, in the order they run around it. Returns
    /// how many edges were drawn.
    /// </summary>
    internal static int ConnectVertices(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, int material, Float3 viewDirection)
    {
        var chosen = vertices.ToHashSet();
        var segments = new List<(Float3, Float3)>();
        foreach (Face face in mesh.Faces.ToList())
        {
            List<Vertex> loop = face.NeighborVertices();
            List<int> picked = Enumerable.Range(0, loop.Count).Where(i => chosen.Contains(loop[i])).ToList();
            if (picked.Count < 2) continue;

            for (int k = 0; k < picked.Count; k++)
            {
                if (picked.Count == 2 && k == 1) break;
                int i = picked[k], j = picked[(k + 1) % picked.Count];
                int gap = (j - i + loop.Count) % loop.Count;
                if (gap <= 1 || gap >= loop.Count - 1) continue;
                if (mesh.FindEdge(loop[i], loop[j]) != null) continue;
                segments.Add((loop[i].Point, loop[j].Point));
            }
        }

        if (segments.Count == 0) return 0;
        DrawSegments(mesh, segments, null, viewDirection, material);
        return segments.Count;
    }

    internal static List<Edge> AddLine(GeometryData mesh, IReadOnlyList<Float3> points, LineAnchor start, LineAnchor end)
    {
        var made = new List<Edge>();
        if (points.Count < 2) return made;
        var pieces = new Dictionary<Edge, List<Edge>>();
        var vertices = new Vertex[points.Count];
        vertices[0] = Resolve(mesh, start, pieces);
        vertices[^1] = Resolve(mesh, end, pieces);
        for (int i = 1; i + 1 < points.Count; i++) vertices[i] = mesh.AddVertex(points[i]);
        for (int i = 1; i < vertices.Length; i++)
            if (vertices[i] != vertices[i - 1]) made.Add(mesh.FindEdge(vertices[i - 1], vertices[i]) ?? mesh.AddEdge(vertices[i - 1], vertices[i]));
        mesh.MarkChanged();
        return made;
    }

    // The vertex an end of a line lands on. Pieces remembers what an edge was split into, so two ends on one edge both find their part
    private static Vertex Resolve(GeometryData mesh, LineAnchor anchor, Dictionary<Edge, List<Edge>> pieces)
    {
        if (anchor.Vertex != null) return anchor.Vertex;
        if (anchor.Edge == null) return mesh.AddVertex(anchor.Point);
        if (!pieces.TryGetValue(anchor.Edge, out List<Edge>? parts)) pieces[anchor.Edge] = parts = [anchor.Edge];
        Edge best = parts[0];
        float nearest = float.PositiveInfinity, factor = 0f;
        foreach (Edge part in parts)
        {
            Float3 along = part.Vert2.Point - part.Vert1.Point;
            float lengthSq = Float3.Dot(along, along);
            float t = lengthSq > 1e-12f ? Math.Clamp(Float3.Dot(anchor.Point - part.Vert1.Point, along) / lengthSq, 0f, 1f) : 0f;
            float distance = Float3.Distance(part.Vert1.Point + along * t, anchor.Point);
            if (distance >= nearest) continue;
            nearest = distance;
            best = part;
            factor = t;
        }
        if (factor < 0.001f) return best.Vert1;
        if (factor > 0.999f) return best.Vert2;
        Vertex split = GeometryOperators.SplitEdge(mesh, best, best.Vert1, factor, out Edge other);
        parts.Add(other);
        return split;
    }

    internal static Face AddQuad(GeometryData mesh, IReadOnlyList<Float3> corners, IReadOnlyList<Vertex?> existing, Float3 viewDirection)
    {
        Float3 normal = Float3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
        int[] order = Float3.Dot(normal, viewDirection) > 0f ? [3, 2, 1, 0] : [0, 1, 2, 3];
        var vertices = new Vertex[4];
        foreach (int i in order) vertices[i] = existing[i] ?? mesh.AddVertex(corners[i]);
        Face face = mesh.AddFace([.. order.Select(i => vertices[i])]) ?? throw new InvalidOperationException("The four corners do not make a face");
        mesh.MarkChanged();
        return face;
    }

    internal static Face FillChain(GeometryData mesh, IEnumerable<Edge> edges)
    {
        var around = new Dictionary<Vertex, List<Edge>>();
        foreach (Edge edge in edges)
            foreach (Vertex end in (Vertex[])[edge.Vert1, edge.Vert2])
            {
                if (!around.TryGetValue(end, out List<Edge>? list)) around[end] = list = new List<Edge>();
                list.Add(edge);
            }
        List<Vertex> tips = [.. around.Where(p => p.Value.Count == 1).Select(p => p.Key)];
        if (around.Count < 3 || around.Values.Any(l => l.Count > 2) || tips.Count is not (0 or 2))
            throw new InvalidOperationException("The edges do not run as one chain or ring");

        var path = new List<Vertex>();
        Vertex? at = tips.Count == 2 ? tips[0] : around.Keys.First();
        Edge? from = null;
        while (at != null && !path.Contains(at))
        {
            path.Add(at);
            Edge? onward = around[at].FirstOrDefault(e => e != from);
            if (onward == null) break;
            from = onward;
            at = onward.OtherVertex(at);
        }
        if (path.Count != around.Count) throw new InvalidOperationException("The edges do not run as one chain or ring");

        // Against a face already along one of its sides, so the two face the same way
        for (int i = 0; i < path.Count; i++)
            if (mesh.FindEdge(path[i], path[(i + 1) % path.Count])?.Loop is { } loop && loop.Vert == path[i])
            {
                path.Reverse();
                break;
            }
        Face made = mesh.AddFace([.. path]) ?? throw new InvalidOperationException("No face fits the edges");
        mesh.MarkChanged();
        return made;
    }
}
