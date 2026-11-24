// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry.Operators;

internal static class DissolveOp
{
    /// <summary>
    /// Replaces faces that form one patch with a single face along the patch's outline. The new face carries the
    /// first face's attributes, each corner keeps the loop attributes it had, and edges and vertices left inside
    /// are removed. Returns null, changing nothing, when the outline is not one simple loop.
    /// </summary>
    internal static GeometryData.Face? MergeFaces(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        var region = new HashSet<GeometryData.Face>(faces.Where(mesh.Contains));
        if (region.Count == 0) return null;
        if (region.Count == 1) return region.First();

        // The outline is every side only one face of the patch runs along, in the direction that face runs
        var next = new Dictionary<GeometryData.Vertex, GeometryData.Loop>();
        int outline = 0;
        foreach (GeometryData.Face face in region)
        {
            GeometryData.Loop loop = face.Loop!;
            do
            {
                if (SidesInRegion(loop.Edge, region) == 1)
                {
                    if (next.ContainsKey(loop.Vert)) return null;
                    next[loop.Vert] = loop;
                    outline++;
                }
                loop = loop.Next!;
            } while (loop != face.Loop);
        }
        if (outline < 3) return null;

        var verts = new List<GeometryData.Vertex>(outline);
        var corners = new List<Dictionary<string, GeometryData.AttributeValue>>(outline);
        GeometryData.Vertex start = next.Keys.First();
        GeometryData.Vertex at = start;
        do
        {
            if (!next.TryGetValue(at, out GeometryData.Loop? side) || verts.Count > outline) return null;
            verts.Add(at);
            corners.Add(AttributeTransfer.Copy(side.Attributes));
            at = side.Next!.Vert;
        } while (at != start);
        if (verts.Count != outline) return null;

        GeometryData.Face like = region.First();
        var faceAttributes = AttributeTransfer.Copy(like.Attributes);
        var inner = region.SelectMany(f => f.NeighborEdges()).Distinct().ToList();
        var innerVerts = region.SelectMany(f => f.NeighborVertices()).Distinct().ToList();

        foreach (GeometryData.Face face in region) mesh.RemoveFace(face);
        foreach (GeometryData.Edge edge in inner)
            if (edge.Loop == null && !IsOutline(edge, verts)) mesh.RemoveEdge(edge);
        foreach (GeometryData.Vertex vertex in innerVerts)
            if (vertex.Edge == null) mesh.RemoveVertex(vertex);

        GeometryData.Face? merged = mesh.AddFace(verts.ToArray());
        if (merged == null) return null;
        merged.Attributes = faceAttributes;
        GeometryData.Loop corner = merged.Loop!;
        foreach (var attributes in corners)
        {
            corner.Attributes = attributes;
            corner = corner.Next!;
        }
        return merged;
    }

    private static int SidesInRegion(GeometryData.Edge edge, HashSet<GeometryData.Face> region)
    {
        int count = 0;
        GeometryData.Loop? loop = edge.Loop;
        if (loop == null) return 0;
        do
        {
            if (region.Contains(loop.Face)) count++;
            loop = loop.RadialNext!;
        } while (loop != edge.Loop);
        return count;
    }

    private static bool IsOutline(GeometryData.Edge edge, List<GeometryData.Vertex> verts)
    {
        for (int i = 0; i < verts.Count; i++)
        {
            GeometryData.Vertex a = verts[i], b = verts[(i + 1) % verts.Count];
            if ((edge.Vert1 == a && edge.Vert2 == b) || (edge.Vert1 == b && edge.Vert2 == a)) return true;
        }
        return false;
    }

    /// <summary>Merges each connected patch of the faces into one face. Returns the faces that came of it.</summary>
    internal static List<GeometryData.Face> Faces(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        var left = new HashSet<GeometryData.Face>(faces.Where(mesh.Contains));
        var result = new List<GeometryData.Face>();
        while (left.Count > 0)
        {
            var patch = new List<GeometryData.Face>();
            var open = new Stack<GeometryData.Face>();
            open.Push(left.First());
            left.Remove(open.Peek());
            while (open.Count > 0)
            {
                GeometryData.Face face = open.Pop();
                patch.Add(face);
                foreach (GeometryData.Edge edge in face.NeighborEdges())
                    foreach (GeometryData.Face other in edge.NeighborFaces())
                        if (left.Remove(other)) open.Push(other);
            }
            if (MergeFaces(mesh, patch) is { } merged) result.Add(merged);
        }
        return result;
    }

    /// <summary>Merges each patch of faces lying flat on the plane of its first face. Returns how many faces came of it.</summary>
    internal static int Flat(GeometryData mesh, float degrees, float distance)
    {
        float limit = MathF.Cos(degrees * Maths.Deg2Rad);
        var left = new HashSet<GeometryData.Face>(mesh.Faces);
        var patches = new List<List<GeometryData.Face>>();
        foreach (GeometryData.Face seed in mesh.Faces)
        {
            if (!left.Contains(seed)) continue;
            Float3 normal = seed.Normal();
            if (Float3.LengthSquared(normal) < 0.5f) continue;
            left.Remove(seed);

            Float3 origin = seed.Loop!.Vert.Point;
            var patch = new List<GeometryData.Face> { seed };
            for (int i = 0; i < patch.Count; i++)
                foreach (GeometryData.Edge edge in patch[i].NeighborEdges())
                    foreach (GeometryData.Face other in edge.NeighborFaces())
                        if (left.Contains(other) && OnPlane(other, normal, origin, limit, distance))
                        {
                            left.Remove(other);
                            patch.Add(other);
                        }
            if (patch.Count > 1) patches.Add(patch);
        }

        int merged = 0;
        var corners = new HashSet<GeometryData.Vertex>();
        foreach (List<GeometryData.Face> patch in patches)
        {
            if (MergeFaces(mesh, patch) is not { } face) continue;
            merged++;
            corners.UnionWith(face.NeighborVertices());
        }
        foreach (GeometryData.Vertex vertex in corners)
            if (mesh.Contains(vertex) && vertex.NeighborEdges().Count == 2) JoinEdges(mesh, vertex);
        return merged;
    }

    // A face too thin to have a normal of its own goes with whatever plane its corners lie on
    private static bool OnPlane(GeometryData.Face face, Float3 normal, Float3 origin, float limit, float distance)
    {
        Float3 own = face.Normal();
        if (Float3.LengthSquared(own) >= 0.5f && Float3.Dot(own, normal) < limit) return false;
        foreach (GeometryData.Vertex vertex in face.NeighborVerticesEnumerable())
            if (MathF.Abs(Float3.Dot(vertex.Point - origin, normal)) > distance) return false;
        return true;
    }

    /// <summary>
    /// Removes edges by joining the two faces either side of each, then removes the vertices at their ends that
    /// are left joining only two edges. Edges without exactly two faces are left alone. Returns how many went.
    /// </summary>
    internal static int Edges(GeometryData mesh, IEnumerable<GeometryData.Edge> edges)
    {
        int dissolved = 0;
        var ends = new HashSet<GeometryData.Vertex>();
        foreach (GeometryData.Edge edge in edges.ToList())
        {
            if (!mesh.Contains(edge)) continue;
            List<GeometryData.Face> sides = edge.NeighborFaces();
            if (sides.Count != 2 || sides[0] == sides[1]) continue;
            GeometryData.Vertex a = edge.Vert1, b = edge.Vert2;
            if (MergeFaces(mesh, sides) == null) continue;
            ends.Add(a);
            ends.Add(b);
            dissolved++;
        }
        foreach (GeometryData.Vertex vertex in ends)
            if (mesh.Contains(vertex) && vertex.NeighborEdges().Count == 2) JoinEdges(mesh, vertex);
        return dissolved;
    }

    /// <summary>
    /// Removes vertices and keeps the surface closed: a vertex joining two edges leaves one edge, a vertex inside
    /// the surface leaves one face where the faces around it were. Vertices on a border with more than two
    /// edges are left alone. Returns how many went.
    /// </summary>
    internal static int Vertices(GeometryData mesh, IEnumerable<GeometryData.Vertex> vertices)
    {
        int dissolved = 0;
        foreach (GeometryData.Vertex vertex in vertices.ToList())
        {
            if (!mesh.Contains(vertex)) continue;
            List<GeometryData.Edge> edges = vertex.NeighborEdges();
            if (edges.Count == 2)
            {
                if (JoinEdges(mesh, vertex)) dissolved++;
                continue;
            }
            if (edges.Count < 2 || edges.Any(e => e.NeighborFaces().Count != 2)) continue;
            if (MergeFaces(mesh, vertex.NeighborFaces()) != null) dissolved++;
        }
        return dissolved;
    }

    // Takes a vertex out of the two edges it joins, straight or not. Faces through it lose the corner.
    private static bool JoinEdges(GeometryData mesh, GeometryData.Vertex vertex)
    {
        List<GeometryData.Edge> edges = vertex.NeighborEdges();
        if (edges.Count != 2) return false;
        GeometryData.Vertex a = edges[0].OtherVertex(vertex), b = edges[1].OtherVertex(vertex);
        if (a == b) return false;
        List<GeometryData.Face> faces = vertex.NeighborFaces();
        if (faces.Any(f => f.VertCount <= 3)) return false;

        var edgeAttributes = AttributeTransfer.Copy(edges[0].Attributes);
        foreach (GeometryData.Face face in faces)
        {
            var corners = new List<Dictionary<string, GeometryData.AttributeValue>>();
            var verts = new List<GeometryData.Vertex>();
            GeometryData.Loop loop = face.Loop!;
            do
            {
                if (loop.Vert != vertex)
                {
                    verts.Add(loop.Vert);
                    corners.Add(AttributeTransfer.Copy(loop.Attributes));
                }
                loop = loop.Next!;
            } while (loop != face.Loop);

            var faceAttributes = AttributeTransfer.Copy(face.Attributes);
            mesh.RemoveFace(face);
            GeometryData.Face? rebuilt = mesh.AddFace(verts.ToArray());
            if (rebuilt == null) continue;
            rebuilt.Attributes = faceAttributes;
            GeometryData.Loop corner = rebuilt.Loop!;
            foreach (var attributes in corners)
            {
                corner.Attributes = attributes;
                corner = corner.Next!;
            }
        }
        mesh.RemoveVertex(vertex);
        AttributeTransfer.Apply(edgeAttributes, mesh.AddEdge(a, b).Attributes);
        return true;
    }
}
