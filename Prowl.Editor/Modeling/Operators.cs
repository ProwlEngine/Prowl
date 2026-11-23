// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>Where an end of a drawn line sits, in the mesh's space: on a point of the mesh, on one of its edges, or free.</summary>
public readonly record struct LineAnchor(Float3 Point, GeometryData.Vertex? Vertex = null, GeometryData.Edge? Edge = null)
{
    public bool Snapped => Vertex != null || Edge != null;
}

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
    /// Pulls edges out into a strip of faces joining each to a copy at the same place, so a grab can move the copies off.
    /// The copied edges and their points are selected after.
    /// </summary>
    public static void ExtrudeEdges(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, ElementSelection after)
    {
        after.Clear();
        var copyOf = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        GeometryData.Vertex Copy(GeometryData.Vertex v)
        {
            if (!copyOf.TryGetValue(v, out GeometryData.Vertex? copy)) copyOf[v] = copy = mesh.AddVertex(v.Point);
            return copy;
        }
        foreach (GeometryData.Edge edge in edges.ToList())
        {
            if (edge.IsHidden || !mesh.Contains(edge)) continue;
            GeometryData.Vertex a = edge.Vert1, b = edge.Vert2, ac = Copy(a), bc = Copy(b);
            // Wound against the face already along the edge, so the surface stays one way round
            bool reversed = edge.Loop != null && edge.Loop.Vert == a;
            if (reversed) mesh.AddFace(b, a, ac, bc);
            else mesh.AddFace(a, b, bc, ac);
            if (mesh.FindEdge(ac, bc) is { } copied) after.Edges.Add(copied);
        }
        after.Vertices.UnionWith(copyOf.Values);
        mesh.MarkChanged();
    }

    /// <summary>
    /// Pulls the surface apart along the selected edges, each point on them copied for every side the edges separate.
    /// One side is selected after, so a grab can pull it away: the copies, or with <paramref name="distance"/> the side
    /// whose faces are nearer by it.
    /// </summary>
    public static void Rip(GeometryData mesh, ElementSelection selection, Func<Float3, float>? distance = null)
    {
        if (selection.Edges.Count == 0) throw new InvalidOperationException("Select the edges to rip along");
        var originals = new HashSet<GeometryData.Vertex>();
        foreach (GeometryData.Edge edge in selection.Edges) originals.UnionWith([edge.Vert1, edge.Vert2]);
        var made = new HashSet<GeometryData.Vertex>(GeometryOperators.SplitEdges(mesh, [.. selection.Edges]));
        HashSet<GeometryData.Vertex> side = distance != null && SideDistance(originals, distance) < SideDistance(made, distance) ? originals : made;
        selection.Clear();
        selection.Vertices.UnionWith(side);
        foreach (GeometryData.Vertex vertex in side)
            foreach (GeometryData.Edge edge in vertex.NeighborEdges())
                if (side.Contains(edge.OtherVertex(vertex))) selection.Edges.Add(edge);
        mesh.MarkChanged();
    }

    // How far the middle of the faces on one side of a rip is, leaving out the points of the side itself
    private static float SideDistance(HashSet<GeometryData.Vertex> side, Func<Float3, float> distance)
    {
        Float3 sum = Float3.Zero;
        int count = 0;
        foreach (GeometryData.Vertex vertex in side)
            foreach (GeometryData.Face face in vertex.NeighborFaces())
                foreach (GeometryData.Vertex corner in face.NeighborVertices())
                {
                    if (side.Contains(corner)) continue;
                    sum += corner.Point;
                    count++;
                }
        return count > 0 ? distance(sum / count) : float.PositiveInfinity;
    }

    /// <summary>
    /// Copies the selected faces, or edges and points when no face is selected, in place. The copies are selected after,
    /// for a grab to move them off.
    /// </summary>
    public static void Duplicate(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Vertices.Count == 0) return;
        GeometryData copy = GeometryOperators.Extract(mesh, selection.Vertices, selection.Edges, selection.Faces);
        var vertices = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        var edges = new HashSet<GeometryData.Edge>(mesh.Edges);
        var faces = new HashSet<GeometryData.Face>(mesh.Faces);
        GeometryOperators.Merge(mesh, copy);
        selection.Clear();
        selection.Vertices.UnionWith(mesh.Vertices.Where(v => !vertices.Contains(v)));
        selection.Edges.UnionWith(mesh.Edges.Where(e => !edges.Contains(e)));
        selection.Faces.UnionWith(mesh.Faces.Where(f => !faces.Contains(f)));
        mesh.MarkChanged();
    }

    /// <summary>Sets how much of their line the edges keep through smooth subdivision, from 0 to 1.</summary>
    public static void Crease(IEnumerable<GeometryData.Edge> edges, float amount)
    {
        foreach (GeometryData.Edge edge in edges) edge.Crease = Math.Clamp(amount, 0f, 1f);
    }

    /// <summary>
    /// Shades the faces smooth, every edge between them soft, or flat, every edge of them sharp, and works their corner
    /// normals out again.
    /// </summary>
    public static void Shade(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, bool smooth)
    {
        var points = new HashSet<GeometryData.Vertex>();
        foreach (GeometryData.Face face in faces)
        {
            points.UnionWith(face.NeighborVertices());
            foreach (GeometryData.Edge edge in face.NeighborEdges())
            {
                if (edge.IsHidden) continue;
                edge.IsSoft = smooth;
                edge.IsSharp = !smooth;
            }
        }
        if (mesh.HasLoopAttribute(Prowl.Runtime.Resources.MeshGeometry.Normal)) CornerNormals(mesh, points);
        mesh.MarkChanged();
    }

    /// <summary>
    /// Fills a ring of edges with a grid of four sided faces, which are selected after. The span sets how many edges
    /// make one side of the grid, the offset turns which edge it starts at.
    /// </summary>
    public static void GridFill(GeometryData mesh, ElementSelection selection, int span = 1, int offset = 0)
    {
        List<GeometryData.Face> made = GeometryOperators.GridFill(mesh, [.. selection.Edges], Math.Max(1, span), offset);
        selection.Clear();
        foreach (GeometryData.Face face in made)
        {
            selection.Faces.Add(face);
            selection.Vertices.UnionWith(face.NeighborVertices());
            foreach (GeometryData.Edge edge in face.NeighborEdges()) selection.Edges.Add(edge);
        }
        mesh.MarkChanged();
    }

    /// <summary>Turns faces so each surface agrees about which way is out and closed shells face outwards, with normals worked out again.</summary>
    public static void RecalculateOutside(GeometryData mesh)
    {
        if (GeometryOperators.OrientFaces(mesh) == 0) return;
        if (mesh.HasLoopAttribute(Prowl.Runtime.Resources.MeshGeometry.Normal)) CornerNormals(mesh);
        mesh.MarkChanged();
    }

    /// <summary>Turns the faces over, with their normals worked out again.</summary>
    public static void Flip(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        if (faces.Count == 0) return;
        var points = new HashSet<GeometryData.Vertex>(faces.SelectMany(f => f.NeighborVertices()));
        GeometryOperators.FlipFaces(mesh, faces);
        if (mesh.HasLoopAttribute(Prowl.Runtime.Resources.MeshGeometry.Normal)) CornerNormals(mesh, points);
        mesh.MarkChanged();
    }

    /// <summary>The faces whose corner count is not four.</summary>
    public static void SelectNonQuads(GeometryData mesh, ElementSelection selection)
    {
        selection.Clear();
        foreach (GeometryData.Face face in mesh.Faces)
        {
            if (face.LogicalFace() is { } whole && whole.Count > 1) continue;
            if (face.VertCount == 4) continue;
            selection.Faces.Add(face);
            selection.Vertices.UnionWith(face.NeighborVertices());
            foreach (GeometryData.Edge edge in face.NeighborEdges()) selection.Edges.Add(edge);
        }
    }

    /// <summary>
    /// Adds a run of edges through points in the mesh's space. An end on a point uses it, an end on an edge splits the
    /// edge there, unless that is at one of its ends. The new edges are selected after.
    /// </summary>
    public static List<GeometryData.Edge> AddLine(GeometryData mesh, IReadOnlyList<Float3> points, LineAnchor start, LineAnchor end, ElementSelection after)
    {
        var made = new List<GeometryData.Edge>();
        if (points.Count < 2) return made;
        var pieces = new Dictionary<GeometryData.Edge, List<GeometryData.Edge>>();
        var vertices = new GeometryData.Vertex[points.Count];
        vertices[0] = Resolve(mesh, start, pieces);
        vertices[^1] = Resolve(mesh, end, pieces);
        for (int i = 1; i + 1 < points.Count; i++) vertices[i] = mesh.AddVertex(points[i]);
        for (int i = 1; i < vertices.Length; i++)
            if (vertices[i] != vertices[i - 1]) made.Add(mesh.FindEdge(vertices[i - 1], vertices[i]) ?? mesh.AddEdge(vertices[i - 1], vertices[i]));
        after.Clear();
        foreach (GeometryData.Edge edge in made)
        {
            after.Edges.Add(edge);
            after.Vertices.UnionWith([edge.Vert1, edge.Vert2]);
        }
        mesh.MarkChanged();
        return made;
    }

    // The point an end of a line lands on. Pieces remembers what an edge was split into, so two ends on one edge both find their part
    private static GeometryData.Vertex Resolve(GeometryData mesh, LineAnchor anchor, Dictionary<GeometryData.Edge, List<GeometryData.Edge>> pieces)
    {
        if (anchor.Vertex != null) return anchor.Vertex;
        if (anchor.Edge == null) return mesh.AddVertex(anchor.Point);
        if (!pieces.TryGetValue(anchor.Edge, out List<GeometryData.Edge>? parts)) pieces[anchor.Edge] = parts = [anchor.Edge];
        GeometryData.Edge best = parts[0];
        float nearest = float.PositiveInfinity, factor = 0f;
        foreach (GeometryData.Edge part in parts)
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
        GeometryData.Vertex split = GeometryOperators.SplitEdge(mesh, best, best.Vert1, factor, out GeometryData.Edge other);
        parts.Add(other);
        return split;
    }

    /// <summary>
    /// Adds a four sided face through corners in the mesh's space, using the points given where there are some, turned
    /// to face against the view direction. It is selected after.
    /// </summary>
    public static GeometryData.Face AddQuad(GeometryData mesh, IReadOnlyList<Float3> corners, IReadOnlyList<GeometryData.Vertex?> existing, Float3 viewDirection,
                                            ElementSelection after)
    {
        Float3 normal = Float3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
        int[] order = Float3.Dot(normal, viewDirection) > 0f ? [3, 2, 1, 0] : [0, 1, 2, 3];
        var vertices = new GeometryData.Vertex[4];
        foreach (int i in order) vertices[i] = existing[i] ?? mesh.AddVertex(corners[i]);
        GeometryData.Face face = mesh.AddFace([.. order.Select(i => vertices[i])]) ?? throw new InvalidOperationException("The four corners do not make a face");
        after.Clear();
        after.Faces.Add(face);
        after.Vertices.UnionWith(face.NeighborVertices());
        foreach (GeometryData.Edge edge in face.NeighborEdges()) after.Edges.Add(edge);
        mesh.MarkChanged();
        return face;
    }

    /// <summary>
    /// Moves points onto the nearest surface along their own normal, either way, within reach, then out by the offset.
    /// <paramref name="hit"/> gives how far along a world ray the nearest other surface is, within a distance, or null.
    /// Returns how many points moved.
    /// </summary>
    public static int Shrinkwrap(GeometryData mesh, IEnumerable<GeometryData.Vertex> points, Float4x4 toWorld, Func<Ray, float, float?> hit, float offset, float reach)
    {
        Float4x4 toLocal = toWorld.Invert();
        // Every normal is taken first, so moving one point does not turn the next one's
        var rays = new List<(GeometryData.Vertex Vertex, Float3 Normal)>();
        foreach (GeometryData.Vertex vertex in points)
        {
            Float3 normal = Float3.Zero;
            foreach (GeometryData.Face face in vertex.NeighborFaces()) normal += face.Normal();
            if (Float3.LengthSquared(normal) > 1e-12f) rays.Add((vertex, Float3.Normalize(normal)));
        }
        int moved = 0;
        foreach ((GeometryData.Vertex vertex, Float3 normal) in rays)
        {
            Float3 world = Float4x4.TransformPoint(vertex.Point, toWorld);
            Float3 direction = Float3.Normalize(Float4x4.TransformNormal(normal, toWorld));
            float best = float.PositiveInfinity;
            Float3 landed = world;
            foreach (float sign in (float[])[1f, -1f])
                if (hit(new Ray(world, direction * sign), MathF.Min(best, reach)) is { } distance && distance < best)
                {
                    best = distance;
                    landed = world + direction * (sign * distance);
                }
            if (float.IsPositiveInfinity(best)) continue;
            vertex.Point = Float4x4.TransformPoint(landed + direction * offset, toLocal);
            moved++;
        }
        mesh.MarkChanged();
        return moved;
    }

    /// <summary>Joins two chains or rings of edges with a strip of faces, which are selected after.</summary>
    public static void BridgeEdges(GeometryData mesh, ElementSelection selection)
    {
        List<GeometryData.Face> made = GeometryOperators.BridgeEdges(mesh, [.. selection.Edges]);
        selection.Clear();
        foreach (GeometryData.Face face in made)
        {
            selection.Faces.Add(face);
            selection.Vertices.UnionWith(face.NeighborVertices());
            foreach (GeometryData.Edge edge in face.NeighborEdges()) selection.Edges.Add(edge);
        }
        mesh.MarkChanged();
    }

    /// <summary>
    /// Fills the selection: two points get an edge between them, a chain or ring of edges gets a face through it, turned
    /// to face the way of a face already beside it. The new face is selected after.
    /// </summary>
    public static void Fill(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Vertices.Count == 2 && selection.Edges.Count == 0)
        {
            GeometryData.Vertex[] pair = [.. selection.Vertices];
            if (mesh.FindEdge(pair[0], pair[1]) == null) mesh.AddEdge(pair[0], pair[1]);
            mesh.MarkChanged();
            return;
        }

        var around = new Dictionary<GeometryData.Vertex, List<GeometryData.Edge>>();
        foreach (GeometryData.Edge edge in selection.Edges)
            foreach (GeometryData.Vertex end in (GeometryData.Vertex[])[edge.Vert1, edge.Vert2])
            {
                if (!around.TryGetValue(end, out List<GeometryData.Edge>? list)) around[end] = list = new List<GeometryData.Edge>();
                list.Add(edge);
            }
        List<GeometryData.Vertex> tips = [.. around.Where(p => p.Value.Count == 1).Select(p => p.Key)];
        if (around.Count < 3 || around.Values.Any(l => l.Count > 2) || tips.Count is not (0 or 2))
            throw new InvalidOperationException("The selected edges do not run as one chain or ring");

        var path = new List<GeometryData.Vertex>();
        GeometryData.Vertex? at = tips.Count == 2 ? tips[0] : around.Keys.First();
        GeometryData.Edge? from = null;
        while (at != null && !path.Contains(at))
        {
            path.Add(at);
            GeometryData.Edge? onward = around[at].FirstOrDefault(e => e != from);
            if (onward == null) break;
            from = onward;
            at = onward.OtherVertex(at);
        }
        if (path.Count != around.Count) throw new InvalidOperationException("The selected edges do not run as one chain or ring");

        // Against a face already along one of its sides, so the two face the same way
        for (int i = 0; i < path.Count; i++)
            if (mesh.FindEdge(path[i], path[(i + 1) % path.Count])?.Loop is { } loop && loop.Vert == path[i])
            {
                path.Reverse();
                break;
            }
        GeometryData.Face made = mesh.AddFace([.. path]) ?? throw new InvalidOperationException("No face fits the selection");
        selection.Clear();
        selection.Faces.Add(made);
        selection.Vertices.UnionWith(path);
        foreach (GeometryData.Edge edge in made.NeighborEdges()) selection.Edges.Add(edge);
        mesh.MarkChanged();
    }

    /// <summary>Gives each point a copy at the same place joined to it by an edge, so a grab can draw the edges out. The copies are selected after.</summary>
    public static void ExtrudePoints(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> points, ElementSelection after)
    {
        after.Clear();
        foreach (GeometryData.Vertex point in points)
        {
            GeometryData.Vertex copy = mesh.AddVertex(point.Point);
            mesh.AddEdge(point, copy);
            after.Vertices.Add(copy);
        }
        mesh.MarkChanged();
    }

    /// <summary>
    /// Pulls faces out along their normals as one region, walled to where they were, so the solid grows. A negative
    /// distance pushes them in. The moved faces are selected after.
    /// </summary>
    public static void ExtrudeRegion(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, float distance, ElementSelection after,
                                     ExtrudeMode mode = ExtrudeMode.AlongNormals)
    {
        if (faces.Count == 0) return;

        var oldPoints = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.ExtrudeFaces(mesh, [.. faces], distance, mode);
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
    public static void Bevel(GeometryData mesh, IReadOnlyCollection<GeometryData.Edge> edges, float width, int segments, ElementSelection after, float profile = 0.5f)
    {
        if (edges.Count == 0 || width <= 0f) return;
        var oldPoints = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.BevelEdges(mesh, [.. edges], width, Math.Max(1, segments), profile);
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

    /// <summary>Cuts faces with points along their edges, four sided ones into grids. The faces made are selected after.</summary>
    public static void SubdivideFaces(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, int cuts, ElementSelection after)
    {
        if (faces.Count == 0 || cuts < 1) return;
        List<GeometryData.Face> made = GeometryOperators.SubdivideFaces(mesh, [.. faces], cuts);
        after.Clear();
        after.Faces.UnionWith(made);
    }

    /// <summary>Splits faces into triangles. The triangles are selected after.</summary>
    public static void TriangulateFaces(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, ElementSelection after)
    {
        if (faces.Count == 0) return;
        List<GeometryData.Face> made = GeometryOperators.Triangulate(mesh, [.. faces]);
        after.Clear();
        after.Faces.UnionWith(made);
    }

    /// <summary>Joins pairs of triangles into quads where they turn and bend less than an angle. The quads are selected after.</summary>
    public static void TrisToQuads(GeometryData mesh, IReadOnlyCollection<GeometryData.Face>? faces, float degrees, ElementSelection after)
    {
        List<GeometryData.Face> made = GeometryOperators.TrisToQuads(mesh, faces == null ? null : [.. faces], degrees);
        if (made.Count == 0) return;
        after.Clear();
        after.Faces.UnionWith(made);
    }

    /// <summary>
    /// Removes two faces and joins their outlines with a strip of faces, cut into segments across its length. Outlines
    /// whose point counts do not fit get points added along the shorter one's longest edges first. The strip is
    /// selected after.
    /// </summary>
    public static void Bridge(GeometryData mesh, GeometryData.Face first, GeometryData.Face second, int segments, ElementSelection after)
    {
        var a = new HashSet<GeometryData.Vertex>(first.NeighborVertices());
        var b = new HashSet<GeometryData.Vertex>(second.NeighborVertices());
        if (a.Overlaps(b)) throw new InvalidOperationException("Faces sharing a point cannot be bridged");
        GeometryOperators.DeleteFaces(mesh, [first, second]);

        while (!BridgeFits(a.Count, b.Count))
        {
            HashSet<GeometryData.Vertex> shorter = a.Count <= b.Count ? a : b;
            GeometryData.Edge? longest = null;
            foreach (GeometryData.Edge edge in Outline(mesh, shorter))
                if (longest == null || Float3.Distance(edge.Vert1.Point, edge.Vert2.Point) > Float3.Distance(longest.Vert1.Point, longest.Vert2.Point)) longest = edge;
            if (longest == null) throw new InvalidOperationException("An outline to bridge has no edges");
            shorter.UnionWith(GeometryOperators.SubdivideEdges(mesh, [longest], 1));
        }

        var edges = new List<GeometryData.Edge>(Outline(mesh, a));
        edges.AddRange(Outline(mesh, b));
        List<GeometryData.Face> made = GeometryOperators.BridgeEdges(mesh, edges);
        after.Clear();
        if (segments <= 1 || Rung(made, a, b) is not { } rung)
        {
            after.Faces.UnionWith(made);
            return;
        }

        // Loops cut across the strip's rungs make the segments, and the strip is every face standing on its points
        var strip = new HashSet<GeometryData.Vertex>(a);
        strip.UnionWith(b);
        var before = new HashSet<GeometryData.Vertex>(mesh.Vertices);
        GeometryOperators.LoopCut(mesh, rung, segments - 1);
        foreach (GeometryData.Vertex vertex in mesh.Vertices)
            if (!before.Contains(vertex)) strip.Add(vertex);
        foreach (GeometryData.Face face in mesh.Faces)
        {
            bool all = true;
            foreach (GeometryData.Vertex vertex in face.NeighborVertices())
                if (!strip.Contains(vertex)) { all = false; break; }
            if (all) after.Faces.Add(face);
        }
    }

    /// <summary>An edge of the strip running from one outline to the other.</summary>
    private static GeometryData.Edge? Rung(List<GeometryData.Face> strip, HashSet<GeometryData.Vertex> a, HashSet<GeometryData.Vertex> b)
    {
        foreach (GeometryData.Face face in strip)
            foreach (GeometryData.Edge edge in face.NeighborEdges())
                if (a.Contains(edge.Vert1) && b.Contains(edge.Vert2) || b.Contains(edge.Vert1) && a.Contains(edge.Vert2)) return edge;
        return null;
    }

    private static bool BridgeFits(int a, int b) => (a - b) % 2 == 0 && Math.Max(a, b) <= 3 * Math.Min(a, b);

    /// <summary>The open edges running between points of one outline.</summary>
    private static List<GeometryData.Edge> Outline(GeometryData mesh, HashSet<GeometryData.Vertex> points)
    {
        var edges = new List<GeometryData.Edge>();
        foreach (GeometryData.Edge edge in mesh.Edges)
            if (points.Contains(edge.Vert1) && points.Contains(edge.Vert2) && edge.NeighborFaces().Count == 1) edges.Add(edge);
        return edges;
    }

    /// <summary>
    /// Makes the mesh the same on both sides of a plane across an axis: cut at the plane, the far side and anything lying
    /// in the plane removed, and the kept side mirrored over, welded at the seam.
    /// </summary>
    /// <param name="axis">0, 1 or 2 for the plane across X, Y or Z.</param>
    /// <param name="keepPositive">Keep the side towards the positive end of the axis.</param>
    /// <param name="offset">Where along the axis the plane stands.</param>
    public static void Symmetrize(GeometryData mesh, int axis, bool keepPositive, float offset, IReadOnlyList<string>? bones = null)
    {
        const float Near = 1e-4f;
        Float3 normal = axis == 0 ? Float3.UnitX : axis == 1 ? Float3.UnitY : Float3.UnitZ;
        GeometryOperators.BisectPlane(mesh, new Plane(normal, offset));

        float side = keepPositive ? 1f : -1f;
        var gone = new List<GeometryData.Face>();
        foreach (GeometryData.Face face in mesh.Faces)
        {
            bool far = false, flat = true;
            foreach (GeometryData.Vertex vertex in face.NeighborVertices())
            {
                float distance = (Float3.Dot(vertex.Point, normal) - offset) * side;
                if (distance < -Near) far = true;
                if (MathF.Abs(distance) > Near) flat = false;
            }
            if (far || flat) gone.Add(face);
        }
        GeometryOperators.DeleteFaces(mesh, gone);

        var images = new List<GeometryData.Face>();
        GeometryData mirrored = GeometryOperators.MirrorCopy(mesh, axis, Near * 10f, offset, images);
        TurnMirrored(images, axis, bones ?? []);
        mesh.Restore(mirrored.Capture());
    }

    private static readonly (string Left, string Right)[] s_sideWords = [("Left", "Right"), ("left", "right"), ("LEFT", "RIGHT")];
    private static readonly (string Left, string Right)[] s_sideEnds = [(".L", ".R"), ("_L", "_R"), (".l", ".r"), ("_l", "_r"), (" L", " R")];
    private static readonly (string Left, string Right)[] s_sideStarts = [("L_", "R_"), ("l_", "r_"), ("L.", "R.")];

    /// <summary>The name of a bone's mirror image, by the left or right marking in its name, or null when it has none.</summary>
    public static string? MirrorName(string name)
    {
        foreach ((string left, string right) in s_sideEnds)
        {
            if (name.EndsWith(left, StringComparison.Ordinal)) return name[..^left.Length] + right;
            if (name.EndsWith(right, StringComparison.Ordinal)) return name[..^right.Length] + left;
        }
        foreach ((string left, string right) in s_sideStarts)
        {
            if (name.StartsWith(left, StringComparison.Ordinal)) return right + name[left.Length..];
            if (name.StartsWith(right, StringComparison.Ordinal)) return left + name[right.Length..];
        }
        foreach ((string left, string right) in s_sideWords)
        {
            int at = name.IndexOf(left, StringComparison.Ordinal);
            if (at >= 0) return name[..at] + right + name[(at + left.Length)..];
            at = name.IndexOf(right, StringComparison.Ordinal);
            if (at >= 0) return name[..at] + left + name[(at + right.Length)..];
        }
        return null;
    }

    /// <summary>
    /// Turns the attributes of mirrored faces over to their side: blend shape deltas reflected across the axis, and skin
    /// moved to each bone's mirror image by name, a bone with none keeping its own.
    /// </summary>
    public static void TurnMirrored(IEnumerable<GeometryData.Face> mirrored, int axis, IReadOnlyList<string> bones)
    {
        const string Indices = Prowl.Runtime.Resources.MeshGeometry.BoneIndices;
        string prefix = Prowl.Runtime.Resources.MeshGeometry.BlendShapePrefix;
        int[] other = new int[bones.Count];
        for (int i = 0; i < other.Length; i++)
        {
            string? image = MirrorName(bones[i]);
            int found = image == null ? -1 : IndexOf(bones, image);
            other[i] = found >= 0 ? found : i;
        }
        int Swap(int bone) => bone >= 0 && bone < other.Length ? other[bone] : bone;

        foreach (GeometryData.Face face in mirrored)
            ForEachCorner(face, loop =>
            {
                foreach (KeyValuePair<string, GeometryData.AttributeValue> pair in loop.Attributes.ToList())
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal) && pair.Value is GeometryData.FloatAttributeValue delta && axis < delta.Data.Length)
                    {
                        var turned = (float[])delta.Data.Clone();
                        turned[axis] = -turned[axis];
                        loop.SetAttribute(pair.Key, new GeometryData.FloatAttributeValue(turned));
                    }
                    else if (pair.Key == Skin && pair.Value is GeometryData.WeightsAttributeValue skin)
                        loop.SetAttribute(Skin, new GeometryData.WeightsAttributeValue([.. skin.Groups.Select(Swap)], (float[])skin.Weights.Clone()));
                    else if (pair.Key == Indices && pair.Value is GeometryData.IntAttributeValue ids)
                        loop.SetAttribute(Indices, new GeometryData.IntAttributeValue([.. ids.Data.Select(Swap)]));
                }
            });
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
            if (names[i] == name) return i;
        return -1;
    }

    /// <summary>
    /// Joins points within a distance of each other, among <paramref name="scope"/> or all points. The scope's points
    /// left are selected after.
    /// </summary>
    public static void MergeByDistance(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex>? scope, float distance, ElementSelection after)
    {
        if (distance <= 0f) return;
        GeometryOperators.MergeByDistance(mesh, scope == null ? null : [.. scope], distance);
        after.Clear();
        if (scope == null) return;
        foreach (GeometryData.Vertex vertex in scope)
            if (mesh.Contains(vertex)) after.Vertices.Add(vertex);
    }

    /// <summary>Moves points towards the middle of the points they share an edge with, by a strength from 0 to 1. They stay selected.</summary>
    public static void Smooth(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, float strength, ElementSelection after)
    {
        if (vertices.Count == 0 || strength <= 0f) return;
        var points = new List<GeometryData.Vertex>(vertices);
        GeometryOperators.SmoothVertices(mesh, points, Math.Clamp(strength, 0f, 1f));
        after.Clear();
        after.Vertices.UnionWith(points);
    }

    /// <summary>Rounds the whole mesh, cutting every face into quads and smoothing, once for each level. Nothing is selected after.</summary>
    public static void SubdivideSmooth(GeometryData mesh, int levels, ElementSelection after)
    {
        if (levels <= 0) return;
        mesh.Restore(GeometryOperators.SubdivideSmooth(mesh, Math.Min(levels, 6)).Capture());
        after.Clear();
        mesh.MarkChanged();
    }

    /// <summary>
    /// Evens out the spacing of points, moving each towards the middle of its neighbours along the surface rather than
    /// through it, by a strength from 0 to 1. They stay selected.
    /// </summary>
    public static void Relax(GeometryData mesh, IReadOnlyCollection<GeometryData.Vertex> vertices, float strength, ElementSelection after)
    {
        if (vertices.Count == 0 || strength <= 0f) return;
        var points = new List<GeometryData.Vertex>(vertices);
        GeometryOperators.RelaxVertices(mesh, points, Math.Clamp(strength, 0f, 1f));
        after.Clear();
        after.Vertices.UnionWith(points);
    }

    /// <summary>
    /// Gives every corner a normal: the faces around its point that are joined to its face across soft edges, averaged by
    /// area, so the surface is smooth across soft edges and creased across any other.
    /// </summary>
    public static void CornerNormals(GeometryData mesh, ISet<GeometryData.Vertex>? only = null)
    {
        const string Normal = Prowl.Runtime.Resources.MeshGeometry.Normal;
        if (!mesh.HasLoopAttribute(Normal)) mesh.AddLoopAttribute(Normal, GeometryData.AttributeBaseType.Float, 3);

        var areaNormals = new Dictionary<GeometryData.Face, Float3>(mesh.Faces.Count);
        foreach (GeometryData.Face face in mesh.Faces) areaNormals[face] = AreaNormal(face);

        var fan = new HashSet<GeometryData.Face>();
        var stack = new Stack<GeometryData.Face>();
        foreach (GeometryData.Face face in mesh.Faces)
        {
            GeometryData.Loop loop = face.Loop!;
            do
            {
                GeometryData.Vertex point = loop.Vert;
                if (only != null && !only.Contains(point))
                {
                    loop = loop.Next!;
                    continue;
                }
                fan.Clear();
                fan.Add(face);
                stack.Push(face);
                while (stack.Count > 0)
                {
                    GeometryData.Face at = stack.Pop();
                    foreach (GeometryData.Edge edge in at.NeighborEdges())
                    {
                        if (!edge.ContainsVertex(point) || !edge.IsSoft || edge.IsSharp) continue;
                        foreach (GeometryData.Face next in edge.NeighborFaces())
                            if (fan.Add(next)) stack.Push(next);
                    }
                }

                Float3 sum = Float3.Zero;
                foreach (GeometryData.Face member in fan) sum += areaNormals[member];
                Float3 normal = Float3.LengthSquared(sum) > 1e-20f ? Float3.Normalize(sum) : Float3.UnitY;
                loop.Attributes[Normal] = new GeometryData.FloatAttributeValue(normal.X, normal.Y, normal.Z);
                loop = loop.Next!;
            } while (loop != face.Loop);
        }
    }

    // Twice the face's area along its normal, by Newell's method, so bigger faces count for more
    private static Float3 AreaNormal(GeometryData.Face face)
    {
        List<GeometryData.Vertex> ring = face.NeighborVertices();
        Float3 normal = Float3.Zero;
        for (int i = 0; i < ring.Count; i++)
        {
            Float3 a = ring[i].Point, b = ring[(i + 1) % ring.Count].Point;
            normal += new Float3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return normal;
    }

    /// <summary>
    /// Takes faces out of a mesh into a mesh of their own, keeping every attribute. Points left with no face are dropped
    /// from both.
    /// </summary>
    public static GeometryData Separate(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        var taken = new HashSet<int>();
        foreach (GeometryData.Face face in faces)
            if (mesh.Contains(face)) taken.Add(face.Index);

        var part = new GeometryData();
        part.Restore(mesh.Capture());
        var others = new List<GeometryData.Face>();
        foreach (GeometryData.Face face in part.Faces)
            if (!taken.Contains(face.Index)) others.Add(face);
        GeometryOperators.DeleteFaces(part, others);
        GeometryOperators.DeleteFaces(mesh, [.. faces]);
        DropLoosePoints(part);
        DropLoosePoints(mesh);
        return part;
    }

    private static void DropLoosePoints(GeometryData mesh)
    {
        var loose = new List<GeometryData.Vertex>();
        foreach (GeometryData.Vertex vertex in mesh.Vertices)
            if (vertex.NeighborFaces().Count == 0) loose.Add(vertex);
        foreach (GeometryData.Vertex vertex in loose) mesh.RemoveVertex(vertex);
    }

    /// <summary>
    /// Copies another mesh into a target, moved into the target's space. A transform that mirrors flips the copy's faces
    /// so they still face out. Material slots are merged by material, the copy's faces renumbered to the target's slots,
    /// and a corner color one mesh lacks is white rather than black.
    /// </summary>
    /// <param name="targetSlots">The target's slots, gaining any material only the other has.</param>
    public static void Join(GeometryData target, List<Guid> targetSlots, GeometryData other, IReadOnlyList<Guid> otherSlots, Float4x4 otherToTarget)
    {
        const string SubMesh = Prowl.Runtime.Resources.MeshGeometry.SubMesh, Color = Prowl.Runtime.Resources.MeshGeometry.VertexColor;
        const string Normal = Prowl.Runtime.Resources.MeshGeometry.Normal, Tangent = Prowl.Runtime.Resources.MeshGeometry.Tangent;

        var copy = new GeometryData();
        copy.Restore(other.Capture());
        foreach (GeometryData.Vertex vertex in copy.Vertices) vertex.Point = Float4x4.TransformPoint(vertex.Point, otherToTarget);
        foreach (GeometryData.Loop loop in copy.Loops)
            foreach (string name in new[] { Normal, Tangent })
                if (loop.TryGetAttribute(name, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue { Data.Length: >= 3 } f)
                {
                    Float3 turned = Float3.NormalizeSafe(Float4x4.TransformNormal(new Float3(f.Data[0], f.Data[1], f.Data[2]), otherToTarget), Float3.UnitY);
                    float[] data = (float[])f.Data.Clone();
                    data[0] = turned.X;
                    data[1] = turned.Y;
                    data[2] = turned.Z;
                    loop.SetAttribute(name, new GeometryData.FloatAttributeValue(data));
                }
        if (Float4x4.Determinant(otherToTarget) < 0f) GeometryOperators.FlipFaces(copy, [.. copy.Faces]);

        var slotOf = new int[Math.Max(1, otherSlots.Count)];
        for (int i = 0; i < otherSlots.Count; i++)
        {
            int at = targetSlots.IndexOf(otherSlots[i]);
            if (at < 0)
            {
                at = targetSlots.Count;
                targetSlots.Add(otherSlots[i]);
            }
            slotOf[i] = at;
        }
        if (otherSlots.Count == 0) slotOf[0] = 0;
        foreach (GeometryData.Face face in copy.Faces)
        {
            int slot = face.TryGetAttribute(SubMesh, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue n && n.Data.Length > 0 ? n.Data[0] : 0;
            face.SetAttribute(SubMesh, new GeometryData.IntAttributeValue(slot >= 0 && slot < slotOf.Length ? slotOf[slot] : 0));
        }

        bool targetColored = target.HasLoopAttribute(Color), otherColored = copy.HasLoopAttribute(Color);
        int existingLoops = target.Loops.Count;
        GeometryOperators.Merge(target, copy);
        if (targetColored != otherColored)
        {
            for (int i = 0; i < target.Loops.Count; i++)
            {
                bool lacked = i < existingLoops ? !targetColored : !otherColored;
                if (lacked) target.Loops[i].SetAttribute(Color, new GeometryData.FloatAttributeValue(1f, 1f, 1f, 1f));
            }
        }
    }

    /// <summary>The corner attribute holding a mesh's skin as one weights value while an edit runs, so it blends whole.</summary>
    public const string Skin = "skin";

    /// <summary>
    /// Turns a mesh's bone index and weight attributes into one weights value a corner, which operators blend as a whole
    /// rather than mixing weights apart from their bones. False when the mesh has no skin.
    /// </summary>
    public static bool SkinToWeights(GeometryData mesh)
    {
        const string Indices = Prowl.Runtime.Resources.MeshGeometry.BoneIndices, Weights = Prowl.Runtime.Resources.MeshGeometry.BoneWeights;
        if (!mesh.HasLoopAttribute(Indices) || !mesh.HasLoopAttribute(Weights)) return false;
        var groups = new List<int>(4);
        var weights = new List<float>(4);
        foreach (GeometryData.Loop loop in mesh.Loops)
        {
            groups.Clear();
            weights.Clear();
            if (loop.TryGetAttribute(Indices, out GeometryData.AttributeValue? i) && i is GeometryData.IntAttributeValue ids
                && loop.TryGetAttribute(Weights, out GeometryData.AttributeValue? w) && w is GeometryData.FloatAttributeValue ws)
                for (int k = 0; k < Math.Min(ids.Data.Length, ws.Data.Length); k++)
                {
                    if (ws.Data[k] <= 0f) continue;
                    int at = groups.IndexOf(ids.Data[k]);
                    if (at >= 0) weights[at] += ws.Data[k];
                    else
                    {
                        groups.Add(ids.Data[k]);
                        weights.Add(ws.Data[k]);
                    }
                }
            loop.SetAttribute(Skin, new GeometryData.WeightsAttributeValue([.. groups], [.. weights]));
        }
        mesh.RemoveLoopAttribute(Indices);
        mesh.RemoveLoopAttribute(Weights);
        return true;
    }

    /// <summary>
    /// Puts a skin held as weights values back into bone index and weight attributes: the four heaviest bones of each
    /// corner, scaled to add up to one. A corner with no weights value is left with none, for the edit's tidy up to fill.
    /// </summary>
    public static void WeightsToSkin(GeometryData mesh)
    {
        const string Indices = Prowl.Runtime.Resources.MeshGeometry.BoneIndices, Weights = Prowl.Runtime.Resources.MeshGeometry.BoneWeights;
        mesh.AddLoopAttribute(Indices, GeometryData.AttributeBaseType.Int, 4);
        mesh.AddLoopAttribute(Weights, GeometryData.AttributeBaseType.Float, 4);
        foreach (GeometryData.Loop loop in mesh.Loops)
        {
            if (!loop.TryGetAttribute(Skin, out GeometryData.AttributeValue? value) || value is not GeometryData.WeightsAttributeValue skin) continue;
            loop.Attributes.Remove(Skin);
            var slots = new (int Bone, float Weight)[skin.Groups.Length];
            for (int k = 0; k < slots.Length; k++) slots[k] = (skin.Groups[k], skin.Weights[k]);
            Array.Sort(slots, (a, b) => b.Weight.CompareTo(a.Weight));
            var kept = new (int Bone, float Weight)[4];
            float total = 0f;
            for (int k = 0; k < Math.Min(4, slots.Length); k++) total += slots[k].Weight;
            for (int k = 0; k < 4; k++) kept[k] = k < slots.Length && total > 0f ? (slots[k].Bone, slots[k].Weight / total) : (0, 0f);
            Array.Sort(kept, (a, b) => a.Bone != b.Bone ? a.Bone.CompareTo(b.Bone) : a.Weight.CompareTo(b.Weight));
            loop.SetAttribute(Indices, new GeometryData.IntAttributeValue(kept[0].Bone, kept[1].Bone, kept[2].Bone, kept[3].Bone));
            loop.SetAttribute(Weights, new GeometryData.FloatAttributeValue(kept[0].Weight, kept[1].Weight, kept[2].Weight, kept[3].Weight));
        }
    }

    /// <summary>
    /// Readies another object's skeleton and blend shapes to join a target's, before their meshes join. Bones and shapes
    /// are matched by name and the other's missing ones added, a bone moved into the target's space. The other mesh's
    /// bone indices and shape attributes are renumbered to the target's. Where one side has a skeleton and the other
    /// none, every corner of the other is weighted fully to the first bone at the top.
    /// </summary>
    public static void MergeSkins(GeometryData target, List<PMeshBone> targetBones, List<PMeshShape> targetShapes,
                                  GeometryData other, IReadOnlyList<PMeshBone> otherBones, IReadOnlyList<PMeshShape> otherShapes, Float4x4 otherToTarget)
    {
        const string Indices = Prowl.Runtime.Resources.MeshGeometry.BoneIndices, Weights = Prowl.Runtime.Resources.MeshGeometry.BoneWeights;
        bool targetHadBones = targetBones.Count > 0;

        int[] boneOf = new int[otherBones.Count];
        Float4x4 back = otherToTarget.Invert();
        var added = new List<int>();
        for (int i = 0; i < otherBones.Count; i++)
        {
            PMeshBone bone = otherBones[i];
            int found = targetBones.FindIndex(b => b.Name == bone.Name);
            if (found < 0)
            {
                found = targetBones.Count;
                targetBones.Add(new PMeshBone { Name = bone.Name, Position = bone.Position, Rotation = bone.Rotation, Scale = bone.Scale, BindPose = bone.BindPose * back });
                added.Add(i);
            }
            boneOf[i] = found;
        }
        foreach (int i in added)
        {
            PMeshBone from = otherBones[i], to = targetBones[boneOf[i]];
            to.Parent = from.Parent >= 0 && from.Parent < boneOf.Length ? boneOf[from.Parent] : -1;
            if (to.Parent >= 0) continue;
            Float3 x = otherToTarget.c0.XYZ, y = otherToTarget.c1.XYZ, z = otherToTarget.c2.XYZ;
            to.Position = Float4x4.TransformPoint(from.Position, otherToTarget);
            to.Rotation = Quaternion.FromMatrix(new Float3x3(Float3.Normalize(x), Float3.Normalize(y), Float3.Normalize(z))) * from.Rotation;
            to.Scale = from.Scale * new Float3(Float3.Length(x), Float3.Length(y), Float3.Length(z));
        }

        if (targetBones.Count > 0)
        {
            int top = Math.Max(0, targetBones.FindIndex(b => b.Parent < 0));
            if (otherBones.Count == 0 || !other.HasLoopAttribute(Indices)) WeightTo(other, top);
            else
                foreach (GeometryData.Loop loop in other.Loops)
                    if (loop.TryGetAttribute(Indices, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue ids)
                    {
                        var renumbered = new int[ids.Data.Length];
                        for (int k = 0; k < renumbered.Length; k++)
                            renumbered[k] = ids.Data[k] >= 0 && ids.Data[k] < boneOf.Length ? boneOf[ids.Data[k]] : 0;
                        loop.SetAttribute(Indices, new GeometryData.IntAttributeValue(renumbered));
                    }
            if (!targetHadBones || !target.HasLoopAttribute(Indices)) WeightTo(target, top);
        }

        int[] shapeOf = new int[otherShapes.Count];
        for (int i = 0; i < otherShapes.Count; i++)
        {
            int found = targetShapes.FindIndex(s => s.Name == otherShapes[i].Name);
            if (found < 0)
            {
                found = targetShapes.Count;
                targetShapes.Add(new PMeshShape { Name = otherShapes[i].Name, Weight = otherShapes[i].Weight, FrameWeights = (float[])otherShapes[i].FrameWeights.Clone() });
            }
            shapeOf[i] = found;
        }
        RenumberShapes(other, shapeOf);
    }

    // Declares the skin attributes where missing and weights every corner fully to one bone
    private static void WeightTo(GeometryData mesh, int bone)
    {
        const string Indices = Prowl.Runtime.Resources.MeshGeometry.BoneIndices, Weights = Prowl.Runtime.Resources.MeshGeometry.BoneWeights;
        if (!mesh.HasLoopAttribute(Indices)) mesh.AddLoopAttribute(Indices, GeometryData.AttributeBaseType.Int, 4);
        if (!mesh.HasLoopAttribute(Weights)) mesh.AddLoopAttribute(Weights, GeometryData.AttributeBaseType.Float, 4);
        foreach (GeometryData.Loop loop in mesh.Loops)
        {
            loop.SetAttribute(Indices, new GeometryData.IntAttributeValue(0, 0, 0, bone));
            loop.SetAttribute(Weights, new GeometryData.FloatAttributeValue(0f, 0f, 0f, 1f));
        }
    }

    // Moves every blend shape attribute of the mesh from its shape's number to the one given for it
    private static void RenumberShapes(GeometryData mesh, int[] shapeOf)
    {
        string prefix = Prowl.Runtime.Resources.MeshGeometry.BlendShapePrefix;
        var moves = new List<(string From, string To, GeometryData.AttributeBaseType Type, int Size)>();
        foreach (GeometryData.AttributeDefinition definition in mesh.LoopAttributes)
        {
            if (!definition.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string rest = definition.Name[prefix.Length..];
            int slash = rest.IndexOf('/');
            if (slash < 0 || !int.TryParse(rest[..slash], out int shape) || shape >= shapeOf.Length || shapeOf[shape] == shape) continue;
            moves.Add((definition.Name, prefix + shapeOf[shape] + rest[slash..], definition.Type.BaseType, definition.Type.Dimensions));
        }
        if (moves.Count == 0) return;

        var values = new Dictionary<(GeometryData.Loop, string), GeometryData.AttributeValue>();
        foreach (GeometryData.Loop loop in mesh.Loops)
            foreach ((string from, _, _, _) in moves)
                if (loop.TryGetAttribute(from, out GeometryData.AttributeValue? value)) values[(loop, from)] = GeometryData.AttributeValue.Copy(value!);
        foreach ((string from, _, _, _) in moves) mesh.RemoveLoopAttribute(from);
        foreach ((_, string to, GeometryData.AttributeBaseType type, int size) in moves)
            if (!mesh.HasLoopAttribute(to)) mesh.AddLoopAttribute(to, type, size);
        foreach (((GeometryData.Loop loop, string from), GeometryData.AttributeValue value) in values)
            loop.SetAttribute(moves.Find(m => m.From == from).To, value);
    }

    /// <summary>
    /// Tidies a mesh after an edit, from where its points were and which faces it had before. Corner normals are worked
    /// out again around every point that moved or is new, when the mesh has normals. A face made from nothing takes the
    /// material slot of the old face sharing most edges with it, the texture coordinates of old corners at the same
    /// points or else a flat projection, and white where it has no color. Its skin weights and blend shape deltas come
    /// from an old corner at the same point, or for a new point from the nearest old point it is joined to by an edge.
    /// </summary>
    public static void FinishEdit(GeometryData mesh, IReadOnlyDictionary<GeometryData.Vertex, Float3> pointsBefore, IReadOnlySet<GeometryData.Face> facesBefore)
    {
        const string SubMesh = Prowl.Runtime.Resources.MeshGeometry.SubMesh, UV = Prowl.Runtime.Resources.MeshGeometry.UV;
        const string Color = Prowl.Runtime.Resources.MeshGeometry.VertexColor, Normal = Prowl.Runtime.Resources.MeshGeometry.Normal;

        var made = new List<GeometryData.Face>();
        foreach (GeometryData.Face face in mesh.Faces)
            if (!facesBefore.Contains(face)) made.Add(face);

        foreach (GeometryData.Face face in made)
        {
            GeometryData.Face? neighbour = OldNeighbour(face, facesBefore);
            if (!face.TryGetAttribute(SubMesh, out _) && neighbour != null && neighbour.TryGetAttribute(SubMesh, out GeometryData.AttributeValue? slot))
                face.SetAttribute(SubMesh, slot!);

            if (mesh.HasLoopAttribute(UV) && AllCorners(face, UV, IsZero))
            {
                Float3 normal = face.Normal();
                ForEachCorner(face, loop =>
                {
                    if (OldCornerAt(loop.Vert, neighbour, facesBefore) is { } old && old.TryGetAttribute(UV, out GeometryData.AttributeValue? uv)) loop.SetAttribute(UV, uv!);
                    else
                    {
                        Float2 flat = Projected(loop.Vert.Point, normal);
                        loop.SetAttribute(UV, new GeometryData.FloatAttributeValue(flat.X, flat.Y));
                    }
                });
            }
            if (mesh.HasLoopAttribute(Color) && AllCorners(face, Color, IsZero))
                ForEachCorner(face, loop => loop.SetAttribute(Color, new GeometryData.FloatAttributeValue(1f, 1f, 1f, 1f)));
        }
        CarryFromOldCorners(mesh, made, pointsBefore, facesBefore);

        if (!mesh.HasLoopAttribute(Normal)) return;
        var moved = new HashSet<GeometryData.Vertex>();
        foreach (GeometryData.Vertex vertex in mesh.Vertices)
            if (!pointsBefore.TryGetValue(vertex, out Float3 was) || was != vertex.Point) moved.Add(vertex);
        foreach (GeometryData.Face face in made) moved.UnionWith(face.NeighborVertices());
        if (moved.Count == 0) return;

        // A point's normal turns with every face round it, so the points of those faces are worked out again too
        var dirty = new HashSet<GeometryData.Vertex>(moved);
        foreach (GeometryData.Vertex vertex in moved)
            foreach (GeometryData.Face face in vertex.NeighborFaces()) dirty.UnionWith(face.NeighborVertices());
        CornerNormals(mesh, dirty);
    }

    /// <summary>
    /// Gives the corners of new faces the skin and blend shape values an old corner holds: one at the same point, or
    /// for a point the edit made, one at the nearest old point an edge joins it to. Weights are only filled where a
    /// corner has none, deltas wherever they are all zero.
    /// </summary>
    private static void CarryFromOldCorners(GeometryData mesh, List<GeometryData.Face> made, IReadOnlyDictionary<GeometryData.Vertex, Float3> pointsBefore,
                                            IReadOnlySet<GeometryData.Face> facesBefore)
    {
        const string Indices = Prowl.Runtime.Resources.MeshGeometry.BoneIndices, Weights = Prowl.Runtime.Resources.MeshGeometry.BoneWeights;
        var names = new List<string>();
        bool skinned = mesh.HasLoopAttribute(Indices) && mesh.HasLoopAttribute(Weights);
        if (skinned) names.AddRange([Indices, Weights]);
        foreach (GeometryData.AttributeDefinition definition in mesh.LoopAttributes)
            if (definition.Name.StartsWith(Prowl.Runtime.Resources.MeshGeometry.BlendShapePrefix, StringComparison.Ordinal)) names.Add(definition.Name);
        if (names.Count == 0) return;

        foreach (GeometryData.Face face in made)
            ForEachCorner(face, loop =>
            {
                bool needsSkin = skinned && IsZeroAt(loop, Weights);
                if (!needsSkin && !names.Exists(n => n != Indices && n != Weights && IsZeroAt(loop, n))) return;
                if (SourceCorner(loop.Vert, pointsBefore, facesBefore) is not { } source) return;
                foreach (string name in names)
                {
                    bool skin = name == Indices || name == Weights;
                    if (skin ? !needsSkin : !IsZeroAt(loop, name)) continue;
                    if (source.TryGetAttribute(name, out GeometryData.AttributeValue? value)) loop.SetAttribute(name, GeometryData.AttributeValue.Copy(value!));
                }
            });
    }

    private static bool IsZeroAt(GeometryData.Loop loop, string name)
        => !loop.TryGetAttribute(name, out GeometryData.AttributeValue? value) || IsZero(value!);

    // An old corner at the point, or at the nearest old point joined to it by an edge
    private static GeometryData.Loop? SourceCorner(GeometryData.Vertex vertex, IReadOnlyDictionary<GeometryData.Vertex, Float3> pointsBefore, IReadOnlySet<GeometryData.Face> facesBefore)
    {
        if (pointsBefore.ContainsKey(vertex) && OldCornerAt(vertex, null, facesBefore) is { } here) return here;
        GeometryData.Loop? best = null;
        float nearest = float.MaxValue;
        foreach (GeometryData.Edge edge in vertex.NeighborEdges())
        {
            GeometryData.Vertex other = edge.OtherVertex(vertex);
            if (!pointsBefore.ContainsKey(other)) continue;
            float distance = Float3.Distance(other.Point, vertex.Point);
            if (distance >= nearest || OldCornerAt(other, null, facesBefore) is not { } corner) continue;
            nearest = distance;
            best = corner;
        }
        return best;
    }

    private static GeometryData.Face? OldNeighbour(GeometryData.Face face, IReadOnlySet<GeometryData.Face> old)
    {
        var shared = new Dictionary<GeometryData.Face, int>();
        foreach (GeometryData.Edge edge in face.NeighborEdges())
            foreach (GeometryData.Face other in edge.NeighborFaces())
                if (other != face && old.Contains(other)) shared[other] = shared.TryGetValue(other, out int n) ? n + 1 : 1;
        GeometryData.Face? best = null;
        int most = 0;
        foreach ((GeometryData.Face other, int count) in shared)
            if (count > most) (best, most) = (other, count);
        return best;
    }

    private static GeometryData.Loop? OldCornerAt(GeometryData.Vertex vertex, GeometryData.Face? preferred, IReadOnlySet<GeometryData.Face> old)
    {
        if (preferred != null && CornerOf(preferred, vertex) is { } corner) return corner;
        foreach (GeometryData.Face face in vertex.NeighborFaces())
            if (old.Contains(face) && CornerOf(face, vertex) is { } found) return found;
        return null;
    }

    private static GeometryData.Loop? CornerOf(GeometryData.Face face, GeometryData.Vertex vertex)
    {
        GeometryData.Loop loop = face.Loop!;
        do
        {
            if (loop.Vert == vertex) return loop;
            loop = loop.Next!;
        } while (loop != face.Loop);
        return null;
    }

    private static Float2 Projected(Float3 point, Float3 normal)
    {
        Float3 size = new(MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));
        return size.X >= size.Y && size.X >= size.Z ? new Float2(point.Z, point.Y) : size.Y >= size.Z ? new Float2(point.X, point.Z) : new Float2(point.X, point.Y);
    }

    private static bool IsZero(GeometryData.AttributeValue? value)
        => value is not GeometryData.FloatAttributeValue f || Array.TrueForAll(f.Data, x => x == 0f);

    private static bool AllCorners(GeometryData.Face face, string name, Func<GeometryData.AttributeValue?, bool> test)
    {
        GeometryData.Loop loop = face.Loop!;
        do
        {
            if (!test(loop.TryGetAttribute(name, out GeometryData.AttributeValue? value) ? value : null)) return false;
            loop = loop.Next!;
        } while (loop != face.Loop);
        return true;
    }

    private static void ForEachCorner(GeometryData.Face face, Action<GeometryData.Loop> act)
    {
        GeometryData.Loop loop = face.Loop!;
        do
        {
            act(loop);
            loop = loop.Next!;
        } while (loop != face.Loop);
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
