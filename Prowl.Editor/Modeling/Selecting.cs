// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>Which elements clicks pick and commands act on.</summary>
public enum SelectMode
{
    Vertex,
    Edge,
    Face,
}

/// <summary>
/// Selection commands on a mesh. A selection keeps all three element sets in step: in vertex mode the vertices lead and
/// edges and faces follow when all their corners are in, in edge mode the edges lead, in face mode the faces lead.
/// </summary>
public static class Selecting
{
    /// <summary>Fills in the sets that follow from the one the mode leads with.</summary>
    public static void Normalize(ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Vertex:
                selection.Edges.Clear();
                selection.Faces.Clear();
                foreach (GeometryData.Vertex vertex in selection.Vertices)
                {
                    foreach (GeometryData.Edge edge in vertex.NeighborEdges())
                        if (selection.Vertices.Contains(edge.OtherVertex(vertex))) selection.Edges.Add(edge);
                    foreach (GeometryData.Face face in vertex.NeighborFaces())
                        if (AllIn(face.NeighborVertices(), selection.Vertices)) selection.Faces.Add(face);
                }
                break;

            case SelectMode.Edge:
                selection.Vertices.Clear();
                selection.Faces.Clear();
                foreach (GeometryData.Edge edge in selection.Edges)
                {
                    selection.Vertices.Add(edge.Vert1);
                    selection.Vertices.Add(edge.Vert2);
                    foreach (GeometryData.Face face in edge.NeighborFaces())
                        if (AllIn(face.NeighborEdges(), selection.Edges)) selection.Faces.Add(face);
                }
                break;

            case SelectMode.Face:
                selection.Vertices.Clear();
                selection.Edges.Clear();
                foreach (GeometryData.Face face in selection.Faces)
                {
                    foreach (GeometryData.Vertex vertex in face.NeighborVertices()) selection.Vertices.Add(vertex);
                    foreach (GeometryData.Edge edge in face.NeighborEdges()) selection.Edges.Add(edge);
                }
                break;
        }
    }

    /// <summary>Converts a selection made in one mode to another: going up keeps what is whole, going down takes the parts.</summary>
    public static void ToMode(ElementSelection selection, SelectMode from, SelectMode to)
    {
        Normalize(selection, from);
        Normalize(selection, to);
    }

    public static void All(GeometryData mesh, ElementSelection selection, SelectMode mode)
    {
        selection.Clear();
        switch (mode)
        {
            case SelectMode.Vertex: selection.Vertices.UnionWith(mesh.Vertices); break;
            case SelectMode.Edge: selection.Edges.UnionWith(mesh.Edges); break;
            case SelectMode.Face: selection.Faces.UnionWith(mesh.Faces); break;
        }
        Normalize(selection, mode);
    }

    public static void Invert(GeometryData mesh, ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Vertex: Flip(selection.Vertices, mesh.Vertices); break;
            case SelectMode.Edge: Flip(selection.Edges, mesh.Edges); break;
            case SelectMode.Face: Flip(selection.Faces, mesh.Faces); break;
        }
        Normalize(selection, mode);
    }

    /// <summary>Grows the selection by the elements next to it: points along an edge, edges at a point, faces across an edge.</summary>
    public static void More(ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Vertex:
                foreach (GeometryData.Vertex vertex in new List<GeometryData.Vertex>(selection.Vertices))
                    foreach (GeometryData.Edge edge in vertex.NeighborEdges())
                        selection.Vertices.Add(edge.OtherVertex(vertex));
                break;

            case SelectMode.Edge:
                foreach (GeometryData.Edge edge in new List<GeometryData.Edge>(selection.Edges))
                {
                    selection.Edges.UnionWith(edge.Vert1.NeighborEdges());
                    selection.Edges.UnionWith(edge.Vert2.NeighborEdges());
                }
                break;

            case SelectMode.Face:
                foreach (GeometryData.Face face in new List<GeometryData.Face>(selection.Faces))
                    foreach (GeometryData.Edge edge in face.NeighborEdges())
                        selection.Faces.UnionWith(edge.NeighborFaces());
                break;
        }
        Normalize(selection, mode);
    }

    /// <summary>Shrinks the selection by its border: the elements with an unselected neighbour go.</summary>
    public static void Less(GeometryData mesh, ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Vertex:
                selection.Vertices.RemoveWhere(vertex =>
                {
                    foreach (GeometryData.Edge edge in vertex.NeighborEdges())
                        if (!selection.Vertices.Contains(edge.OtherVertex(vertex))) return true;
                    return false;
                });
                break;

            case SelectMode.Edge:
                var edges = new HashSet<GeometryData.Edge>(selection.Edges);
                selection.Edges.RemoveWhere(edge => !AllIn(edge.Vert1.NeighborEdges(), edges) || !AllIn(edge.Vert2.NeighborEdges(), edges));
                break;

            case SelectMode.Face:
                var faces = new HashSet<GeometryData.Face>(selection.Faces);
                selection.Faces.RemoveWhere(face =>
                {
                    foreach (GeometryData.Edge edge in face.NeighborEdges())
                        if (!AllIn(edge.NeighborFaces(), faces)) return true;
                    return false;
                });
                break;
        }
        Normalize(selection, mode);
    }

    /// <summary>Selects every element joined to the selection, whole shells at a time.</summary>
    public static void Linked(ElementSelection selection, SelectMode mode)
    {
        Normalize(selection, mode);
        var reached = new HashSet<GeometryData.Vertex>(selection.Vertices);
        var stack = new Stack<GeometryData.Vertex>(reached);
        while (stack.Count > 0)
        {
            foreach (GeometryData.Edge edge in stack.Pop().NeighborEdges())
                foreach (GeometryData.Vertex end in new[] { edge.Vert1, edge.Vert2 })
                    if (reached.Add(end)) stack.Push(end);
        }

        selection.Clear();
        selection.Vertices.UnionWith(reached);
        Normalize(selection, SelectMode.Vertex);
    }

    /// <summary>
    /// The edge loop through an edge. Where an edge loop stops at once, at a corner with three edges or beside an n
    /// gon, it is the outline of the face on the cursor's side instead, which is what a loop means on a brush.
    /// </summary>
    public static List<GeometryData.Edge> Loop(GeometryData.Edge edge, GeometryData.Face? side)
    {
        List<GeometryData.Edge> loop = GeometryOperators.EdgeLoop(edge);
        if (loop.Count > 1) return loop;

        GeometryData.Face? face = side;
        if (face == null)
            foreach (GeometryData.Face neighbour in edge.NeighborFaces()) { face = neighbour; break; }
        return face?.NeighborEdges() ?? [edge];
    }

    /// <summary>
    /// The edge ring through an edge. Where an edge ring stops at once, beside a face that is not four sided, it
    /// walks the faces round taking the edge most parallel to the last, as the upright edges of a prism.
    /// </summary>
    public static List<GeometryData.Edge> Ring(GeometryData.Edge edge)
    {
        List<GeometryData.Edge> ring = GeometryOperators.EdgeRing(edge);
        if (ring.Count > 1) return ring;

        ring = [edge];
        var seen = new HashSet<GeometryData.Edge> { edge };
        foreach (GeometryData.Face start in edge.NeighborFaces())
        {
            GeometryData.Edge current = edge;
            GeometryData.Face? face = start;
            while (face != null)
            {
                GeometryData.Edge? next = Parallel(face, current);
                if (next == null || !seen.Add(next)) break;
                ring.Add(next);

                GeometryData.Face? across = null;
                foreach (GeometryData.Face other in next.NeighborFaces())
                    if (other != face) { across = other; break; }
                current = next;
                face = across;
            }
        }
        return ring;
    }

    /// <summary>Adds the elements like the selected ones: faces facing the same way, edges as long, points with as many edges.</summary>
    public static void Similar(GeometryData mesh, ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Face:
                var normals = new List<Float3>();
                foreach (GeometryData.Face face in selection.Faces) normals.Add(face.Normal());
                foreach (GeometryData.Face face in mesh.Faces)
                {
                    Float3 normal = face.Normal();
                    foreach (Float3 n in normals)
                        if (Float3.Dot(n, normal) > 0.996f) { selection.Faces.Add(face); break; }
                }
                break;

            case SelectMode.Edge:
                var lengths = new List<float>();
                foreach (GeometryData.Edge edge in selection.Edges) lengths.Add(Length(edge));
                foreach (GeometryData.Edge edge in mesh.Edges)
                {
                    float length = Length(edge);
                    foreach (float l in lengths)
                        if (MathF.Abs(length - l) <= l * 0.05f) { selection.Edges.Add(edge); break; }
                }
                break;

            case SelectMode.Vertex:
                var counts = new HashSet<int>();
                foreach (GeometryData.Vertex vertex in selection.Vertices) counts.Add(vertex.NeighborEdges().Count);
                foreach (GeometryData.Vertex vertex in mesh.Vertices)
                    if (counts.Contains(vertex.NeighborEdges().Count)) selection.Vertices.Add(vertex);
                break;
        }
        Normalize(selection, mode);
    }

    /// <summary>
    /// Hides the selection and empties it. What the mode leads with is hidden, along with what can no longer be seen
    /// whole: in vertex mode the edges and faces at a hidden point, otherwise the edges and points every face or edge
    /// using them is hidden, so hiding a face leaves its neighbours whole.
    /// </summary>
    public static void Hide(ElementSelection hidden, ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Vertex:
                hidden.Vertices.UnionWith(selection.Vertices);
                foreach (GeometryData.Vertex vertex in selection.Vertices)
                {
                    hidden.Edges.UnionWith(vertex.NeighborEdges());
                    hidden.Faces.UnionWith(vertex.NeighborFaces());
                }
                break;

            case SelectMode.Edge:
                hidden.Edges.UnionWith(selection.Edges);
                foreach (GeometryData.Edge edge in selection.Edges)
                {
                    hidden.Faces.UnionWith(edge.NeighborFaces());
                    foreach (GeometryData.Vertex end in new[] { edge.Vert1, edge.Vert2 })
                        if (AllIn(end.NeighborEdges(), hidden.Edges)) hidden.Vertices.Add(end);
                }
                break;

            case SelectMode.Face:
                hidden.Faces.UnionWith(selection.Faces);
                foreach (GeometryData.Face face in selection.Faces)
                {
                    foreach (GeometryData.Edge edge in face.NeighborEdges())
                        if (AllIn(edge.NeighborFaces(), hidden.Faces)) hidden.Edges.Add(edge);
                    foreach (GeometryData.Vertex vertex in face.NeighborVertices())
                        if (AllIn(vertex.NeighborFaces(), hidden.Faces)) hidden.Vertices.Add(vertex);
                }
                break;
        }
        selection.Clear();
    }

    /// <summary>Takes the hidden elements out of the selection, so commands over the whole mesh skip them.</summary>
    public static void Without(ElementSelection selection, ElementSelection hidden, SelectMode mode)
    {
        Remove();
        Normalize(selection, mode);
        Remove();

        void Remove()
        {
            selection.Vertices.ExceptWith(hidden.Vertices);
            selection.Edges.ExceptWith(hidden.Edges);
            selection.Faces.ExceptWith(hidden.Faces);
        }
    }

    private static GeometryData.Edge? Parallel(GeometryData.Face face, GeometryData.Edge from)
    {
        Float3 direction = Float3.NormalizeSafe(from.Vert2.Point - from.Vert1.Point, Float3.UnitY);
        GeometryData.Edge? best = null;
        float bestDot = 0.9f;
        foreach (GeometryData.Edge edge in face.NeighborEdges())
        {
            if (edge == from || edge.ContainsVertex(from.Vert1) || edge.ContainsVertex(from.Vert2)) continue;
            float dot = MathF.Abs(Float3.Dot(direction, Float3.NormalizeSafe(edge.Vert2.Point - edge.Vert1.Point, Float3.UnitY)));
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = edge;
        }
        return best;
    }

    private static float Length(GeometryData.Edge edge) => Float3.Distance(edge.Vert1.Point, edge.Vert2.Point);

    private static bool AllIn<T>(IEnumerable<T> items, HashSet<T> set)
    {
        foreach (T item in items)
            if (!set.Contains(item)) return false;
        return true;
    }

    private static void Flip<T>(HashSet<T> selected, IEnumerable<T> all)
    {
        var flipped = new HashSet<T>();
        foreach (T item in all)
            if (!selected.Contains(item)) flipped.Add(item);
        selected.Clear();
        selected.UnionWith(flipped);
    }
}
