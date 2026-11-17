// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Loop = Prowl.Vector.Geometry.GeometryData.Loop;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Geometry.Operators;

internal static class KnifeOp
{
    // How near a plane a vertex has to be to count as on it, as a part of the length of the edge being looked at
    private const float OnPlane = 1e-4f;

    /// <summary>
    /// Cuts a mesh along a path of straight stretches. See <see cref="GeometryOperators.KnifePath"/>.
    /// </summary>
    internal static List<Edge> Cut(GeometryData mesh, IReadOnlyList<KnifePoint> points, IReadOnlyList<(Float3 Point, Float3 Normal)> planes,
        Func<int, Float3, float?> along, Func<Face, bool>? cuts)
    {
        var result = new List<Edge>();
        if (points.Count < 2 || planes.Count < points.Count - 1) return result;

        // Points inside faces become vertices first, while the faces they were found on are still there
        var nodes = new Vertex?[points.Count];
        var inside = new HashSet<Vertex>();
        for (int i = 0; i < points.Count; i++)
        {
            KnifePoint point = points[i];
            if (point.Vertex != null || point.Edge != null || point.Face == null || !mesh.Contains(point.Face)) continue;
            Vertex made = mesh.AddVertex(point.Point);
            Vertex nearest = point.Face.NeighborVertices().OrderBy(v => Float3.LengthSquared(v.Point - point.Point)).First();
            AttributeTransfer.Apply(nearest.Attributes, made.Attributes);
            nodes[i] = made;
            inside.Add(made);
        }
        var pieces = new Dictionary<Edge, List<Edge>>();
        for (int i = 0; i < points.Count; i++)
        {
            KnifePoint point = points[i];
            if (point.Vertex != null) nodes[i] = mesh.Contains(point.Vertex) ? point.Vertex : null;
            else if (point.Edge != null && mesh.Contains(point.Edge)) nodes[i] = OnEdge(mesh, point.Edge, point.Point, pieces);
        }

        // The vertices the cut runs through, in order. A null is a gap, where the path is off the mesh.
        var chain = new List<Vertex?>();
        void Add(Vertex? vertex)
        {
            if (chain.Count == 0 || chain[chain.Count - 1] != vertex) chain.Add(vertex);
        }
        Add(nodes[0]);
        for (int s = 0; s + 1 < points.Count; s++)
        {
            var stretch = new List<Vertex>();
            if (nodes[s] != null) stretch.Add(nodes[s]!);
            stretch.AddRange(Crossings(mesh, s, planes[s], along, cuts, inside, nodes[s], nodes[s + 1]));
            if (nodes[s + 1] != null) stretch.Add(nodes[s + 1]!);
            foreach (Vertex vertex in stretch) Add(vertex);
            Add(nodes[s + 1]);
            Across(mesh, stretch.Where(v => !inside.Contains(v)).Distinct().ToList(), cuts, result);
        }
        Through(mesh, chain, inside, cuts, result);
        return result.Distinct().ToList();
    }

    // Cuts the faces one stretch crosses from outline to outline. The vertices are in order along the stretch, and each
    // face is cut between those of them it has that follow one another. With faces behind faces the vertices of
    // different faces come mixed, so it is the order within a face that counts.
    private static void Across(GeometryData mesh, List<Vertex> stretch, Func<Face, bool>? cuts, List<Edge> result)
    {
        var onFace = new Dictionary<Face, List<Vertex>>();
        foreach (Vertex vertex in stretch)
        {
            foreach (Face face in vertex.NeighborFaces())
            {
                if (cuts != null && !cuts(face)) continue;
                if (!onFace.TryGetValue(face, out List<Vertex>? list)) onFace[face] = list = new List<Vertex>();
                list.Add(vertex);
            }
        }
        var pairs = new List<(Vertex A, Vertex B)>();
        foreach (List<Vertex> list in onFace.Values)
            for (int i = 1; i < list.Count; i++)
                if (!pairs.Contains((list[i - 1], list[i]))) pairs.Add((list[i - 1], list[i]));
        foreach ((Vertex a, Vertex b) in pairs)
            Join(mesh, a, b, cuts, result);
    }

    // Joins up the points inside faces. A run of them is tied to the vertex before it and the vertex after it on the
    // outline of the face they lie in, and the face is split along that. A run with only one such end, where the path
    // starts or stops inside a face, is left as edges lying on it.
    private static void Through(GeometryData mesh, List<Vertex?> chain, HashSet<Vertex> inside, Func<Face, bool>? cuts, List<Edge> result)
    {
        Vertex? Outline(int from, int step, Vertex point)
        {
            for (int k = from; k >= 0 && k < chain.Count && chain[k] is Vertex vertex && !inside.Contains(vertex); k += step)
                if (vertex.NeighborFaces().Any(f => (cuts == null || cuts(f)) && Lies(f, point.Point, vertex.Point))) return vertex;
            return null;
        }

        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i] == null || !inside.Contains(chain[i]!)) continue;
            int end = i;
            while (end + 1 < chain.Count && chain[end + 1] != null && inside.Contains(chain[end + 1]!)) end++;

            var path = new List<Vertex>();
            Vertex? before = Outline(i - 1, -1, chain[i]!), after = Outline(end + 1, 1, chain[end]!);
            if (before != null) path.Add(before);
            for (int k = i; k <= end; k++) path.Add(chain[k]!);
            if (after != null) path.Add(after);
            if (before != null && after != null) SplitAlong(mesh, path, cuts, result);
            else Wire(mesh, path, result);
            i = end;
        }
    }

    // Where one stretch meets the mesh between its ends: edges it crosses are split there, vertices it runs through are taken as they are
    private static List<Vertex> Crossings(GeometryData mesh, int stretch, (Float3 Point, Float3 Normal) plane, Func<int, Float3, float?> along,
        Func<Face, bool>? cuts, HashSet<Vertex> inside, Vertex? from, Vertex? to)
    {
        var found = new List<(float T, Vertex Vertex)>();
        float length = Float3.Length(plane.Normal);
        if (length < 1e-12f) return new List<Vertex>();
        Float3 normal = plane.Normal / length;
        float Side(Vertex v) => Float3.Dot(v.Point - plane.Point, normal);

        var through = new HashSet<Vertex>();
        var crossed = new List<(Edge Edge, float At, float T)>();
        foreach (Edge edge in mesh.Edges)
        {
            Vertex a = edge.Vert1, b = edge.Vert2;
            if (inside.Contains(a) || inside.Contains(b)) continue;
            List<Face> sides = edge.NeighborFaces();
            if (sides.Count == 0 || (cuts != null && !sides.Any(cuts))) continue;

            float sa = Side(a), sb = Side(b);
            float near = OnPlane * Float3.Length(b.Point - a.Point);
            bool onA = MathF.Abs(sa) <= near, onB = MathF.Abs(sb) <= near;
            if (onA) through.Add(a);
            if (onB) through.Add(b);
            if (onA || onB || (sa > 0) == (sb > 0)) continue;

            float at = sa / (sa - sb);
            if (along(stretch, a.Point + (b.Point - a.Point) * at) is float t) crossed.Add((edge, at, t));
        }

        foreach (Vertex vertex in through)
            if (vertex != from && vertex != to && along(stretch, vertex.Point) is float t) found.Add((t, vertex));
        foreach ((Edge edge, float at, float t) in crossed)
            found.Add((t, GeometryOperators.SplitEdge(mesh, edge, edge.Vert1, at, out _)));
        found.Sort((x, y) => x.T.CompareTo(y.T));
        return found.ConvertAll(f => f.Vertex);
    }

    // The vertex a point on an edge becomes. The edge is split there unless the point is at one of its ends. Pieces
    // remembers what an edge was split into, so two points on one edge both find their part.
    private static Vertex OnEdge(GeometryData mesh, Edge edge, Float3 point, Dictionary<Edge, List<Edge>> pieces)
    {
        if (!pieces.TryGetValue(edge, out List<Edge>? parts)) pieces[edge] = parts = new List<Edge> { edge };
        Edge best = parts[0];
        float nearest = float.PositiveInfinity, factor = 0;
        foreach (Edge part in parts)
        {
            Float3 run = part.Vert2.Point - part.Vert1.Point;
            float lengthSquared = Float3.LengthSquared(run);
            float t = lengthSquared > 1e-12f ? Maths.Clamp(Float3.Dot(point - part.Vert1.Point, run) / lengthSquared, 0f, 1f) : 0;
            float distance = Float3.Length(part.Vert1.Point + run * t - point);
            if (distance >= nearest) continue;
            (nearest, best, factor) = (distance, part, t);
        }
        if (factor < 0.001f) return best.Vert1;
        if (factor > 0.999f) return best.Vert2;
        Vertex split = GeometryOperators.SplitEdge(mesh, best, best.Vert1, factor, out Edge other);
        parts.Add(other);
        return split;
    }

    private static void Wire(GeometryData mesh, List<Vertex> path, List<Edge> result)
    {
        for (int i = 1; i < path.Count; i++)
            if (path[i] != path[i - 1]) result.Add(mesh.FindEdge(path[i - 1], path[i]) ?? mesh.AddEdge(path[i - 1], path[i]));
    }

    private static void Join(GeometryData mesh, Vertex a, Vertex b, Func<Face, bool>? cuts, List<Edge> result)
    {
        if (mesh.FindEdge(a, b) is Edge existing)
        {
            result.Add(existing);
            return;
        }
        Face? face = a.NeighborFaces().FirstOrDefault(f => f.GetLoop(b) != null && (cuts == null || cuts(f)) && Holds(f, (a.Point + b.Point) * 0.5f));
        if (face == null) return;
        GeometryOperators.SplitFace(mesh, face, a, b, out Edge? cut);
        if (cut != null) result.Add(cut);
    }

    // Splits the face two outline vertices share along a path through points inside it. The path is first, then the points, then last.
    private static void SplitAlong(GeometryData mesh, List<Vertex> path, Func<Face, bool>? cuts, List<Edge> result)
    {
        Vertex first = path[0], last = path[path.Count - 1];
        Face? face = first == last ? null : first.NeighborFaces()
            .Where(f => f.GetLoop(last) != null && (cuts == null || cuts(f)))
            .OrderBy(f => MathF.Abs(Frame(f).Distance(path[1].Point)))
            .FirstOrDefault();
        if (face == null)
        {
            Wire(mesh, path, result);
            return;
        }

        var near = new List<Vertex>();
        var far = new List<Vertex>();
        Loop from = face.GetLoop(first)!, to = face.GetLoop(last)!;
        for (Loop loop = from; ; loop = loop.Next!)
        {
            near.Add(loop.Vert);
            if (loop == to) break;
        }
        for (Loop loop = to; ; loop = loop.Next!)
        {
            far.Add(loop.Vert);
            if (loop == from) break;
        }
        for (int i = path.Count - 2; i >= 1; i--) near.Add(path[i]);
        for (int i = 1; i < path.Count - 1; i++) far.Add(path[i]);

        FaceSnapshot snapshot = FaceSnapshot.Capture(mesh, face);
        mesh.RemoveFace(face);
        Wire(mesh, path, result);
        foreach (List<Vertex> outline in new[] { near, far })
        {
            Face? half = mesh.AddFace(outline.ToArray());
            if (half == null) continue;
            snapshot.ApplyFaceAttributes(half);
            snapshot.ApplyLoopsByPosition(mesh, half);
        }
    }

    private static PlaneFrame Frame(Face face)
    {
        List<Vertex> corners = face.NeighborVertices();
        Float3 normal = Float3.Zero;
        for (int i = 0; i < corners.Count; i++)
            normal += Float3.Cross(corners[i].Point, corners[(i + 1) % corners.Count].Point);
        return new PlaneFrame(Float3.LengthSquared(normal) > 1e-12f ? Float3.Normalize(normal) : new Float3(0, 0, 1), corners[0].Point);
    }

    // Whether a point lies on a face: inside its outline and near its plane, measured against how far the point is from a corner
    private static bool Lies(Face face, Float3 point, Float3 corner)
        => MathF.Abs(Frame(face).Distance(point)) <= 0.02f * Float3.Length(point - corner) + 1e-6f && Holds(face, point);

    // Whether a point is inside a face's outline, looking straight at the face. A line across a face that is not
    // convex can leave it and come back, and the part outside must not be cut.
    private static bool Holds(Face face, Float3 point)
    {
        PlaneFrame frame = Frame(face);
        return Polygon2D.Contains(face.NeighborVertices().ConvertAll(v => frame.To2D(v.Point)), frame.To2D(point));
    }
}
