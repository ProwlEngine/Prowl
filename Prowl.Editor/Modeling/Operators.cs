// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// Mesh edits for <see cref="MeshEdit.Run"/>, each marking what should be selected after it. The inputs are copied
/// before anything changes, so <c>after</c> may be the selection they came from.
/// </summary>
public static class Operators
{
    /// <summary>
    /// Insets faces inside their own outlines by a distance, joining each to its old outline with new faces that carry its
    /// attributes. Neighbouring faces are inset as one region. The inset faces are selected after.
    /// </summary>
    public static void Inset(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, float distance, ElementSelection after)
    {
        if (faces.Count == 0 || distance <= 0f) return;

        var oldPoints = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.InsetFaces(mesh, [.. faces], distance, InsetMode.Shared, InsetMeasure.Distance);
        SelectFacesOnNewPoints(mesh, oldPoints, after);
    }

    /// <summary>
    /// Pulls faces out along their normals as one region, walled to where they were, so the solid grows. A negative
    /// distance pushes them in. The moved faces are selected after.
    /// </summary>
    public static void ExtrudeRegion(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, float distance, ElementSelection after)
    {
        if (faces.Count == 0) return;

        var oldPoints = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.ExtrudeFaces(mesh, [.. faces], distance, ExtrudeMode.AlongNormals);
        SelectFacesOnNewPoints(mesh, oldPoints, after);
    }

    /// <summary>
    /// Removes the mode's selected elements and joins the faces around them, which keeps the surface closed: points
    /// leave one face where their faces were, edges join the two faces beside them, faces merge into one along their
    /// outline. The joined faces are selected after.
    /// </summary>
    public static void Dissolve(GeometryData mesh, ElementSelection selection, SelectMode mode, ElementSelection after)
    {
        var oldFaces = new HashSet<GeometryData.Face>(mesh.Faces);
        var vertices = new List<GeometryData.Vertex>(selection.Vertices);
        var edges = new List<GeometryData.Edge>(selection.Edges);
        var faces = new List<GeometryData.Face>(selection.Faces);

        after.Clear();
        switch (mode)
        {
            case SelectMode.Vertex: GeometryOperators.DissolveVertices(mesh, vertices); break;
            case SelectMode.Edge: GeometryOperators.DissolveEdges(mesh, edges); break;
            case SelectMode.Face:
                after.Faces.UnionWith(GeometryOperators.DissolveFaces(mesh, faces));
                return;
        }
        SelectNewFaces(mesh, oldFaces, after);
    }

    /// <summary>
    /// Dissolves the edges whose two faces are within an angle of flat, among <paramref name="scope"/> or the whole mesh,
    /// tidying faces split in one plane. The joined faces are selected after.
    /// </summary>
    public static void LimitedDissolve(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge>? scope, float degrees, ElementSelection after)
    {
        float limit = MathF.Cos(degrees * MathF.PI / 180f);
        var flat = new List<GeometryData.Edge>();
        foreach (GeometryData.Edge edge in scope ?? (IReadOnlyCollection<GeometryData.Edge>)mesh.Edges)
        {
            List<GeometryData.Face> faces = edge.NeighborFaces();
            if (faces.Count == 2 && Float3.Dot(faces[0].Normal(), faces[1].Normal()) >= limit) flat.Add(edge);
        }
        if (flat.Count == 0) return;

        var oldFaces = new HashSet<GeometryData.Face>(mesh.Faces);
        after.Clear();
        GeometryOperators.DissolveEdges(mesh, flat);
        SelectNewFaces(mesh, oldFaces, after);
    }

    /// <summary>Joins points into one at a position. Faces that collapse away with them are removed. The joined point is selected after.</summary>
    public static void Merge(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, Float3 at, ElementSelection after)
    {
        if (vertices.Count < 2) return;
        GeometryOperators.MergeVertices(mesh, [.. vertices], at);
        after.Clear();
        SelectPointsAt(mesh, [at], after);
    }

    /// <summary>Collapses each joined group of edges to one point at its centre. The points are selected after.</summary>
    public static void Collapse(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, ElementSelection after)
    {
        if (edges.Count == 0) return;

        var centres = new List<Float3>();
        foreach (List<GeometryData.Edge> group in Groups(edges))
        {
            var points = new HashSet<GeometryData.Vertex>();
            foreach (GeometryData.Edge edge in group)
            {
                points.Add(edge.Vert1);
                points.Add(edge.Vert2);
            }
            Float3 sum = Float3.Zero;
            foreach (GeometryData.Vertex vertex in points) sum += vertex.Point;
            centres.Add(sum / points.Count);
        }

        GeometryOperators.CollapseEdges(mesh, [.. edges]);
        after.Clear();
        SelectPointsAt(mesh, centres, after);
    }

    /// <summary>Joins points that share a face with new edges, splitting the face. The new edges are selected after.</summary>
    public static void Connect(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, Float3 viewDirection, ElementSelection after)
    {
        if (vertices.Count < 2) return;

        var oldEdges = new HashSet<GeometryData.Edge>(mesh.Edges);
        GeometryOperators.ConnectVertices(mesh, [.. vertices], 0, viewDirection);
        after.Clear();
        foreach (GeometryData.Edge edge in mesh.Edges)
            if (!oldEdges.Contains(edge)) after.Edges.Add(edge);
    }

    /// <summary>Adds evenly spaced points along edges, splitting the faces beside them. The new points are selected after.</summary>
    public static void SubdivideEdges(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, int cuts, ElementSelection after)
    {
        if (edges.Count == 0 || cuts < 1) return;
        List<GeometryData.Vertex> added = GeometryOperators.SubdivideEdges(mesh, [.. edges], cuts);
        after.Clear();
        after.Vertices.UnionWith(added);
    }

    /// <summary>
    /// Moves the selected points onto one plane through their centre, facing the way the selected faces do on
    /// average, or the faces around the points when no face is selected.
    /// </summary>
    public static void Flatten(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Vertices.Count < 3) return;

        Float3 normal = Float3.Zero;
        if (selection.Faces.Count > 0)
            foreach (GeometryData.Face face in selection.Faces) normal += face.Normal();
        else
            foreach (GeometryData.Vertex vertex in selection.Vertices)
                foreach (GeometryData.Face face in vertex.NeighborFaces()) normal += face.Normal();
        if (Float3.LengthSquared(normal) < 1e-12f) return;
        normal = Float3.Normalize(normal);

        Float3 centre = Float3.Zero;
        foreach (GeometryData.Vertex vertex in selection.Vertices) centre += vertex.Point;
        centre /= selection.Vertices.Count;

        foreach (GeometryData.Vertex vertex in selection.Vertices)
            vertex.Point -= normal * Float3.Dot(vertex.Point - centre, normal);
    }

    /// <summary>
    /// Bevels edges into strips of faces, one segment a flat chamfer, more a rounded edge. Corners where bevelled
    /// edges meet are capped. The strips and caps are selected after.
    /// </summary>
    public static void Bevel(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, float width, int segments, ElementSelection after)
    {
        if (edges.Count == 0 || width <= 0f) return;
        var oldPoints = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.BevelEdges(mesh, [.. edges], width, Math.Max(1, segments));
        SelectFacesOnNewPoints(mesh, oldPoints, after);
    }

    /// <summary>Cuts corners off, each replaced by a face through points a distance along its edges. The new faces are selected after.</summary>
    public static void BevelPoints(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, float distance, ElementSelection after)
    {
        if (vertices.Count == 0 || distance <= 0f) return;
        var oldPoints = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.BevelVertices(mesh, [.. vertices], distance, relative: false);
        SelectFacesOnNewPoints(mesh, oldPoints, after);
    }

    /// <summary>
    /// Cuts loops across the ring of an edge, slid from -1 to 1 along the ring's edges. The new edges are selected after.
    /// </summary>
    public static void LoopCut(GeometryData mesh, GeometryData.Edge edge, int cuts, float slide, ElementSelection after)
    {
        List<GeometryData.Edge> made = GeometryOperators.LoopCut(mesh, edge, Math.Max(1, cuts), slide);
        after.Clear();
        after.Edges.UnionWith(made);
    }

    /// <summary>Slides the points of joined edges along the edges leaving them sideways, from -1 to 1.</summary>
    public static void SlideEdges(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, float factor)
    {
        if (edges.Count == 0) return;
        GeometryOperators.SlideEdges(mesh, [.. edges], factor);
    }

    /// <summary>
    /// Cuts along a path of points, each stretch in its own plane, splitting the faces it crosses. The edges along the
    /// cut are selected after. Refused when the cut meets nothing.
    /// </summary>
    /// <param name="along">How far along a stretch a point of its plane is, or null when it is not part of the cut.</param>
    /// <param name="cuts">Which faces may be cut, all when null.</param>
    public static void Knife(GeometryData mesh, IReadOnlyList<KnifePoint> points, IReadOnlyList<(Float3 Point, Float3 Normal)> planes,
        Func<int, Float3, float?> along, Func<GeometryData.Face, bool>? cuts, ElementSelection after)
    {
        if (points.Count < 2) return;
        List<GeometryData.Edge> made = GeometryOperators.KnifePath(mesh, points, planes, along, cuts);
        if (made.Count == 0) throw new InvalidOperationException("The cut meets nothing of the mesh");

        after.Clear();
        foreach (GeometryData.Edge edge in made)
            if (mesh.Contains(edge)) after.Edges.Add(edge);
    }

    /// <summary>The edges in groups joined at their points.</summary>
    private static List<List<GeometryData.Edge>> Groups(IReadOnlyCollection<GeometryData.Edge> edges)
    {
        var remaining = new HashSet<GeometryData.Edge>(edges);
        var groups = new List<List<GeometryData.Edge>>();
        while (remaining.Count > 0)
        {
            var group = new List<GeometryData.Edge>();
            var stack = new Stack<GeometryData.Edge>();
            foreach (GeometryData.Edge first in remaining) { stack.Push(first); break; }
            remaining.Remove(stack.Peek());
            while (stack.Count > 0)
            {
                GeometryData.Edge edge = stack.Pop();
                group.Add(edge);
                foreach (GeometryData.Vertex end in new[] { edge.Vert1, edge.Vert2 })
                    foreach (GeometryData.Edge next in end.NeighborEdges())
                        if (remaining.Remove(next)) stack.Push(next);
            }
            groups.Add(group);
        }
        return groups;
    }

    private static void SelectFacesOnNewPoints(GeometryData mesh, HashSet<GeometryData.Vertex> oldPoints, ElementSelection after)
    {
        after.Clear();
        foreach (GeometryData.Face face in mesh.Faces)
        {
            bool allNew = true;
            foreach (GeometryData.Vertex vertex in face.NeighborVertices())
                if (oldPoints.Contains(vertex)) { allNew = false; break; }
            if (allNew) after.Faces.Add(face);
        }
    }

    private static void SelectNewFaces(GeometryData mesh, HashSet<GeometryData.Face> oldFaces, ElementSelection after)
    {
        foreach (GeometryData.Face face in mesh.Faces)
            if (!oldFaces.Contains(face)) after.Faces.Add(face);
    }

    private static void SelectPointsAt(GeometryData mesh, IReadOnlyList<Float3> points, ElementSelection after)
    {
        foreach (GeometryData.Vertex vertex in mesh.Vertices)
            foreach (Float3 point in points)
                if (Float3.DistanceSquared(vertex.Point, point) < 1e-10f) { after.Vertices.Add(vertex); break; }
    }
}
