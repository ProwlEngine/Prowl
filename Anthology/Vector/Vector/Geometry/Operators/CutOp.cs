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

internal static class CutOp
{
    /// <summary>
    /// Cuts faces along a plane. An edge of the faces that crosses the plane is split where it crosses, when that point
    /// passes the test given, and a face with two such points is split between them. Faces the cut only enters and
    /// does not cross are left whole. Returns the new edges across the faces.
    /// </summary>
    internal static List<Edge> Knife(GeometryData mesh, IEnumerable<Face> faces, Float3 planePoint, Float3 planeNormal, Func<Float3, bool>? within)
    {
        var result = new List<Edge>();
        float length = Float3.Length(planeNormal);
        if (length < 1e-12f) return result;
        Float3 normal = planeNormal / length;
        float Side(Vertex v) => Float3.Dot(v.Point - planePoint, normal);

        // Every edge crossing is found before anything is split, so one face's cut does not change what the next sees
        var crossings = new Dictionary<Edge, float>();
        var plans = new List<(Face Face, Edge A, Edge B)>();
        foreach (Face face in faces.Where(mesh.Contains).Distinct().ToList())
        {
            var found = new List<Edge>();
            Loop loop = face.Loop!;
            do
            {
                Edge edge = loop.Edge;
                if (!crossings.TryGetValue(edge, out float t))
                {
                    float a = Side(edge.Vert1), b = Side(edge.Vert2);
                    t = -1;
                    // A vertex on the plane counts as in front, so a cut through a vertex does not split its edges
                    if ((a >= 0) != (b >= 0))
                    {
                        float at = a / (a - b);
                        Float3 point = edge.Vert1.Point + (edge.Vert2.Point - edge.Vert1.Point) * at;
                        if (at > 1e-4f && at < 1 - 1e-4f && (within == null || within(point))) t = at;
                    }
                    crossings[edge] = t;
                }
                if (t >= 0) found.Add(edge);
                loop = loop.Next!;
            } while (loop != face.Loop);
            if (found.Count == 2) plans.Add((face, found[0], found[1]));
        }

        var made = new Dictionary<Edge, Vertex>();
        Vertex Split(Edge edge)
        {
            if (!made.TryGetValue(edge, out Vertex? vertex))
                made[edge] = vertex = GeometryOperators.SplitEdge(mesh, edge, edge.Vert1, crossings[edge], out _);
            return vertex;
        }

        foreach ((Face planned, Edge a, Edge b) in plans)
        {
            Vertex va = Split(a), vb = Split(b);
            // Splitting an edge rebuilds its faces, so the face to cut is looked up again from its new corners
            Face? face = va.NeighborFaces().FirstOrDefault(f => f.GetLoop(vb) != null);
            if (face == null) continue;
            GeometryOperators.SplitFace(mesh, face, va, vb, out Edge? cut);
            if (cut != null) result.Add(cut);
        }
        return result;
    }

    internal static List<Face> SplitFaces(GeometryData mesh, IEnumerable<Face> faces)
    {
        var region = new HashSet<Face>(faces.Where(mesh.Contains));
        var outline = new HashSet<Edge>();
        foreach (Face face in region)
            foreach (Edge edge in face.NeighborEdges())
                if (edge.NeighborFaces().Exists(f => !region.Contains(f))) outline.Add(edge);
        SplitEdges(mesh, outline, (old, rebuilt) =>
        {
            if (region.Remove(old) && rebuilt != null) region.Add(rebuilt);
        });
        return [.. region];
    }

    /// <summary>
    /// Pulls a surface apart along edges: every vertex on them is copied for each group of faces the edges separate
    /// around it, so the faces either side no longer share it. Where a run of edges ends inside the surface the end
    /// stays joined, which opens a slit. Returns the new vertices.
    /// </summary>
    /// <param name="rebuilt">Told of each face put back as a new one, and what replaced it.</param>
    internal static List<Vertex> SplitEdges(GeometryData mesh, IEnumerable<Edge> edges, Action<Face, Face?>? rebuilt = null)
    {
        var selected = new HashSet<Edge>(edges.Where(mesh.Contains));
        var copies = new List<Vertex>();
        foreach (Vertex vertex in selected.SelectMany(e => new[] { e.Vert1, e.Vert2 }).Distinct().ToList())
        {
            // The faces around the vertex fall into groups joined across the edges that are not being split
            List<Face> around = vertex.NeighborFaces();
            var group = new Dictionary<Face, int>();
            for (int i = 0; i < around.Count; i++) group[around[i]] = i;
            foreach (Edge edge in vertex.NeighborEdges())
            {
                if (selected.Contains(edge)) continue;
                List<Face> sides = edge.NeighborFaces();
                for (int i = 1; i < sides.Count; i++)
                {
                    int from = group[sides[i]], to = group[sides[0]];
                    if (from == to) continue;
                    foreach (Face face in around)
                        if (group[face] == from) group[face] = to;
                }
            }

            List<int> groups = group.Values.Distinct().ToList();
            for (int g = 1; g < groups.Count; g++)
            {
                Vertex copy = mesh.AddVertex(vertex.Point);
                GeometryOperators.AttributeLerp(mesh, copy, vertex, vertex, 1f);
                copies.Add(copy);
                foreach (Face face in around.Where(f => group[f] == groups[g]).ToList())
                {
                    Face? made = Rebuild(mesh, face, vertex, copy);
                    rebuilt?.Invoke(face, made);
                }
            }
        }
        return copies;
    }

    // Puts a face onto another vertex in place of one of its corners, keeping everything it carried
    private static Face? Rebuild(GeometryData mesh, Face face, Vertex from, Vertex to)
    {
        var verts = new List<Vertex>(face.VertCount);
        var corners = new List<Dictionary<string, GeometryData.AttributeValue>>(face.VertCount);
        var sides = new List<Dictionary<string, GeometryData.AttributeValue>>(face.VertCount);
        Loop loop = face.Loop!;
        do
        {
            verts.Add(loop.Vert == from ? to : loop.Vert);
            corners.Add(AttributeTransfer.Copy(loop.Attributes));
            sides.Add(AttributeTransfer.Copy(loop.Edge.Attributes));
            loop = loop.Next!;
        } while (loop != face.Loop);

        var attributes = AttributeTransfer.Copy(face.Attributes);
        List<Edge> old = face.NeighborEdges();
        mesh.RemoveFace(face);
        Face? rebuilt = mesh.AddFace(verts.ToArray());

        // Edges only this face used, and that its new outline does not, would be left hanging
        foreach (Edge edge in old)
            if (edge.Loop == null) mesh.RemoveEdge(edge);
        if (rebuilt == null) return null;
        rebuilt.Attributes = attributes;
        Loop corner = rebuilt.Loop!;
        for (int i = 0; i < corners.Count; i++)
        {
            corner.Attributes = corners[i];
            // Edges that now run to the copy are new, and take what the edge they replace carried
            if (corner.Vert == to || corner.Next!.Vert == to) AttributeTransfer.Apply(sides[i], corner.Edge.Attributes);
            corner = corner.Next!;
        }
        return rebuilt;
    }

    /// <summary>
    /// Fills a closed ring of edges with a grid of four sided faces. The ring needs an even number of edges. Span is
    /// how many faces run along the first side, the rest of the ring gives the other way, and offset turns where the
    /// first corner is. Points inside are placed by blending the four sides. Throws
    /// <see cref="InvalidOperationException"/> when the edges are not one ring that fits. Returns the new faces.
    /// </summary>
    internal static List<Face> GridFill(GeometryData mesh, IEnumerable<Edge> edges, int span, int offset)
    {
        var selected = new HashSet<Edge>(edges.Where(mesh.Contains));
        if (selected.Count < 4 || selected.Count % 2 != 0)
            throw new InvalidOperationException("Grid fill needs a closed ring with an even number of edges, four or more.");

        // The ring as vertices in order
        var ring = new List<Vertex>();
        Edge first = selected.First();
        Vertex at = first.Vert1;
        Edge? from = null;
        while (true)
        {
            ring.Add(at);
            List<Edge> onward = at.NeighborEdges().Where(e => selected.Contains(e) && e != from).ToList();
            if (onward.Count != 1 && !(from == null && onward.Count == 2))
                throw new InvalidOperationException("Grid fill needs the edges to run as one closed ring.");
            from = onward[0];
            at = from.OtherVertex(at);
            if (at == ring[0]) break;
            if (ring.Count > selected.Count)
                throw new InvalidOperationException("Grid fill needs the edges to run as one closed ring.");
        }
        int n = ring.Count;
        if (n != selected.Count)
            throw new InvalidOperationException("Grid fill needs the edges to run as one closed ring.");

        int half = n / 2;
        span = Math.Clamp(span, 1, half - 1);
        int rows = half - span;
        Vertex At(int i) => ring[((i + offset) % n + n) % n];

        // The four sides: bottom runs forwards, right runs on from it, top and left run backwards, so each reads left to right or bottom to top
        var grid = new Vertex[rows + 1, span + 1];
        for (int c = 0; c <= span; c++)
        {
            grid[0, c] = At(c);
            grid[rows, c] = At(span + rows + span - c);
        }
        for (int r = 0; r <= rows; r++)
        {
            grid[r, span] = At(span + r);
            grid[r, 0] = At(n - r);
        }
        for (int r = 1; r < rows; r++)
        {
            for (int c = 1; c < span; c++)
            {
                float u = (float)c / span, v = (float)r / rows;
                Float3 point = grid[0, c].Point * (1 - v) + grid[rows, c].Point * v + grid[r, 0].Point * (1 - u) + grid[r, span].Point * u
                    - (grid[0, 0].Point * ((1 - u) * (1 - v)) + grid[0, span].Point * (u * (1 - v)) + grid[rows, 0].Point * ((1 - u) * v) + grid[rows, span].Point * (u * v));
                grid[r, c] = mesh.AddVertex(point);
                GeometryOperators.AttributeLerp(mesh, grid[r, c], grid[0, c], grid[rows, c], v);
            }
        }

        // The grid runs against a face already on the ring, so the two face the same way
        Edge? along = mesh.FindEdge(grid[0, 0], grid[0, 1]);
        bool reversed = along?.Loop != null && along.Loop.Vert == grid[0, 0];
        Face? like = along?.Loop?.Face;
        var made = new List<Face>(rows * span);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < span; c++)
            {
                Vertex[] corners = reversed
                    ? new[] { grid[r, c + 1], grid[r, c], grid[r + 1, c], grid[r + 1, c + 1] }
                    : new[] { grid[r, c], grid[r, c + 1], grid[r + 1, c + 1], grid[r + 1, c] };
                Face? face = like != null ? mesh.AddFace(corners, like) : mesh.AddFace(corners);
                if (face != null) made.Add(face);
            }
        }
        return made;
    }

    /// <summary>
    /// Evens out the spacing of vertices without changing the shape: each moves towards the middle of its neighbours,
    /// but only along the surface, the part of the move that would push it in or out being dropped. Returns how many
    /// vertices moved.
    /// </summary>
    internal static int Relax(GeometryData mesh, IEnumerable<Vertex> vertices, float factor, int iterations)
    {
        List<Vertex> targets = vertices.Where(mesh.Contains).Distinct().ToList();
        if (targets.Count == 0 || factor <= 0 || iterations < 1) return 0;
        var moved = new Float3[targets.Count];
        for (int pass = 0; pass < iterations; pass++)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Vertex vertex = targets[i];
                moved[i] = vertex.Point;
                List<Edge> around = vertex.NeighborEdges();
                if (around.Count == 0) continue;
                bool border = around.Any(e => e.NeighborFaces().Count == 1);
                Float3 sum = Float3.Zero;
                int count = 0;
                foreach (Edge edge in around)
                {
                    if (border && edge.NeighborFaces().Count != 1) continue;
                    sum += edge.OtherVertex(vertex).Point;
                    count++;
                }
                if (count == 0) continue;
                Float3 move = (sum / count - vertex.Point) * Math.Clamp(factor, 0f, 1f);

                Float3 normal = vertex.Normal();
                move -= normal * Float3.Dot(move, normal);
                moved[i] = vertex.Point + move;
            }
            for (int i = 0; i < targets.Count; i++)
                targets[i].Point = moved[i];
        }
        mesh.MarkChanged();
        return targets.Count;
    }

    internal static List<Vertex> Rip(GeometryData mesh, IReadOnlyCollection<Edge> edges, Func<Float3, float>? distance)
    {
        if (edges.Count == 0) throw new InvalidOperationException("There are no edges to rip along");
        var originals = new HashSet<Vertex>();
        foreach (Edge edge in edges) originals.UnionWith([edge.Vert1, edge.Vert2]);
        var made = new HashSet<Vertex>(SplitEdges(mesh, edges));
        mesh.MarkChanged();
        return [.. distance != null && SideDistance(originals, distance) < SideDistance(made, distance) ? originals : made];
    }

    // How far the middle of the faces on one side of a rip is, leaving out the vertices of the side itself
    private static float SideDistance(HashSet<Vertex> side, Func<Float3, float> distance)
    {
        Float3 sum = Float3.Zero;
        int count = 0;
        foreach (Vertex vertex in side)
            foreach (Face face in vertex.NeighborFaces())
                foreach (Vertex corner in face.NeighborVertices())
                {
                    if (side.Contains(corner)) continue;
                    sum += corner.Point;
                    count++;
                }
        return count > 0 ? distance(sum / count) : float.PositiveInfinity;
    }
}
