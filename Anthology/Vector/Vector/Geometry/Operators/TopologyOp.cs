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

internal static class TopologyOp
{
    /// <summary>
    /// Joins two runs of edges with a strip of four sided faces. The edges must fall into exactly two chains or rings
    /// with the same number of vertices. Rings are lined up where the strip comes out shortest. Throws
    /// <see cref="InvalidOperationException"/> when they do not fit. Returns the new faces.
    /// </summary>
    internal static List<Face> Bridge(GeometryData mesh, IEnumerable<Edge> edges)
    {
        List<(List<Vertex> Path, bool Ring)> runs = Runs(mesh, edges);
        if (runs.Count != 2)
            throw new InvalidOperationException($"Bridge needs two separate runs of edges, the selection has {runs.Count}.");
        (List<Vertex> a, bool ringA) = runs[0];
        (List<Vertex> b, bool ringB) = runs[1];
        if (a.Count != b.Count || ringA != ringB)
            throw new InvalidOperationException("Bridge needs both runs to have the same number of vertices.");

        // The second run is turned and, for rings, rotated to where its vertices lie nearest the first run's
        int count = a.Count;
        List<Vertex> best = b;
        float bestCost = float.PositiveInfinity;
        for (int flip = 0; flip < 2; flip++)
        {
            List<Vertex> turned = flip == 0 ? b : Enumerable.Reverse(b).ToList();
            for (int shift = 0; shift < (ringA ? count : 1); shift++)
            {
                float cost = 0;
                for (int i = 0; i < count; i++)
                    cost += Float3.LengthSquared(a[i].Point - turned[(i + shift) % count].Point);
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = Enumerable.Range(0, count).Select(i => turned[(i + shift) % count]).ToList();
            }
        }

        var made = new List<Face>();
        int sides = ringA ? count : count - 1;
        for (int i = 0; i < sides; i++)
        {
            Vertex a0 = a[i], a1 = a[(i + 1) % count], b0 = best[i], b1 = best[(i + 1) % count];
            Edge? along = mesh.FindEdge(a0, a1);

            // The new face runs against a face already on the first run, so the two face the same way
            bool reversed = along?.Loop != null && along.Loop.Vert == a0;
            Face? like = along?.Loop?.Face;
            Vertex[] corners = reversed ? new[] { a1, a0, b0, b1 } : new[] { a0, a1, b1, b0 };
            Face? face = like != null ? mesh.AddFace(corners, like) : mesh.AddFace(corners);
            if (face != null) made.Add(face);
        }
        return made;
    }

    // The edges as separate chains and rings, each a path of vertices in order
    private static List<(List<Vertex> Path, bool Ring)> Runs(GeometryData mesh, IEnumerable<Edge> edges)
    {
        var selected = new HashSet<Edge>(edges.Where(mesh.Contains));
        var done = new HashSet<Edge>();
        var runs = new List<(List<Vertex>, bool)>();
        foreach (Edge seed in selected)
        {
            if (done.Contains(seed)) continue;
            var path = new List<Vertex> { seed.Vert1, seed.Vert2 };
            done.Add(seed);
            bool ring = false;
            for (int side = 0; side < 2 && !ring; side++)
            {
                while (true)
                {
                    Vertex tip = side == 0 ? path[path.Count - 1] : path[0];
                    Edge? next = tip.NeighborEdges().FirstOrDefault(e => selected.Contains(e) && !done.Contains(e));
                    if (next == null) break;
                    done.Add(next);
                    Vertex onward = next.OtherVertex(tip);
                    if (onward == (side == 0 ? path[0] : path[path.Count - 1]))
                    {
                        ring = true;
                        break;
                    }
                    if (side == 0) path.Add(onward); else path.Insert(0, onward);
                }
            }
            runs.Add((path, ring));
        }
        return runs;
    }

    /// <summary>
    /// Moves vertices towards the middle of the vertices they share an edge with, which evens out bumps. A vertex on
    /// a border only follows its border neighbours, so it stays on the border. Returns how many vertices moved.
    /// </summary>
    internal static int Smooth(GeometryData mesh, IEnumerable<Vertex> vertices, float factor, int iterations)
    {
        List<Vertex> targets = vertices.Where(mesh.Contains).Distinct().ToList();
        if (targets.Count == 0 || factor <= 0 || iterations < 1) return 0;
        var moved = new Float3[targets.Count];
        for (int pass = 0; pass < iterations; pass++)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                Vertex vertex = targets[i];
                List<Edge> around = vertex.NeighborEdges();
                bool border = around.Any(e => e.NeighborFaces().Count == 1);
                Float3 sum = Float3.Zero;
                int count = 0;
                foreach (Edge edge in around)
                {
                    if (border && edge.NeighborFaces().Count != 1) continue;
                    sum += edge.OtherVertex(vertex).Point;
                    count++;
                }
                moved[i] = count == 0 ? vertex.Point : vertex.Point + (sum / count - vertex.Point) * Math.Clamp(factor, 0f, 1f);
            }
            for (int i = 0; i < targets.Count; i++)
                targets[i].Point = moved[i];
        }
        mesh.MarkChanged();
        return targets.Count;
    }

    /// <summary>
    /// A copy of the mesh with its mirror image across a plane, the axis being 0, 1 or 2 for X, Y or Z and the offset
    /// where along it the plane stands. Vertices within the merge distance of the plane are shared by both halves and put on it, and the mirrored
    /// faces are turned so they face outwards. Attributes are copied to the mirrored side.
    /// </summary>
    internal static GeometryData Mirror(GeometryData source, int axis, float mergeDistance, float offset = 0f)
    {
        var result = new GeometryData();
        CopyDefinitions(source, result);

        var original = new Vertex[source.Vertices.Count];
        var mirrored = new Vertex[source.Vertices.Count];
        var index = new Dictionary<Vertex, int>(source.Vertices.Count);
        for (int i = 0; i < source.Vertices.Count; i++)
        {
            Vertex vertex = source.Vertices[i];
            index[vertex] = i;
            Float3 point = vertex.Point;
            bool onPlane = Math.Abs(point[axis] - offset) <= mergeDistance;
            if (onPlane) point[axis] = offset;
            original[i] = result.AddVertex(point);
            original[i].Attributes = AttributeTransfer.Copy(vertex.Attributes);
            if (onPlane)
            {
                mirrored[i] = original[i];
                continue;
            }
            point[axis] = 2 * offset - point[axis];
            mirrored[i] = result.AddVertex(point);
            mirrored[i].Attributes = AttributeTransfer.Copy(vertex.Attributes);
        }

        foreach (Edge edge in source.Edges)
        {
            int a = index[edge.Vert1], b = index[edge.Vert2];
            result.AddEdge(original[a], original[b]).Attributes = AttributeTransfer.Copy(edge.Attributes);
            if (mirrored[a] != mirrored[b])
                result.AddEdge(mirrored[a], mirrored[b]).Attributes = AttributeTransfer.Copy(edge.Attributes);
        }

        foreach (Face face in source.Faces)
        {
            var corners = new List<Loop>(face.VertCount);
            Loop loop = face.Loop!;
            do
            {
                corners.Add(loop);
                loop = loop.Next!;
            } while (loop != face.Loop);

            AddCopy(result, face, corners, original, index);
            // A face lying wholly in the plane is its own mirror image
            if (corners.All(c => mirrored[index[c.Vert]] == original[index[c.Vert]])) continue;
            corners.Reverse();
            AddCopy(result, face, corners, mirrored, index);
        }
        return result;
    }

    private static void AddCopy(GeometryData result, Face face, List<Loop> corners, Vertex[] map, Dictionary<Vertex, int> index)
    {
        Face? copy = result.AddFace(corners.Select(c => map[index[c.Vert]]).ToArray());
        if (copy == null) return;
        copy.Attributes = AttributeTransfer.Copy(face.Attributes);
        Loop corner = copy.Loop!;
        foreach (Loop from in corners)
        {
            corner.Attributes = AttributeTransfer.Copy(from.Attributes);
            corner = corner.Next!;
        }
    }

    internal static void CopyDefinitions(GeometryData source, GeometryData target)
    {
        foreach (var d in source.VertexAttributes) target.AddVertexAttribute(d.Name, d.Type.BaseType, d.Type.Dimensions);
        foreach (var d in source.EdgeAttributes) target.AddEdgeAttribute(d.Name, d.Type.BaseType, d.Type.Dimensions);
        foreach (var d in source.LoopAttributes) target.AddLoopAttribute(d.Name, d.Type.BaseType, d.Type.Dimensions);
        foreach (var d in source.FaceAttributes) target.AddFaceAttribute(d.Name, d.Type.BaseType, d.Type.Dimensions);
    }

    /// <summary>
    /// One level of Catmull Clark subdivision as a new mesh: every face becomes one four sided face per corner, and the
    /// surface is smoothed. Borders and edges flagged sharp keep their line, as do vertices where more than two such
    /// edges meet, and an edge with a crease keeps that much of its line. Face attributes carry to the new faces, edge attributes to both halves of an edge, vertex attributes
    /// stay on the old vertices and are averaged onto the new ones, and float loop attributes such as texture
    /// coordinates are interpolated across each face.
    /// </summary>
    internal static GeometryData CatmullClark(GeometryData source)
    {
        var result = new GeometryData();
        CopyDefinitions(source, result);

        int vertexCount = source.Vertices.Count, edgeCount = source.Edges.Count, faceCount = source.Faces.Count;
        var vertexIndex = new Dictionary<Vertex, int>(vertexCount);
        for (int i = 0; i < vertexCount; i++) vertexIndex[source.Vertices[i]] = i;
        var edgeIndex = new Dictionary<Edge, int>(edgeCount);
        for (int i = 0; i < edgeCount; i++) edgeIndex[source.Edges[i]] = i;

        var facePoints = new Float3[faceCount];
        for (int f = 0; f < faceCount; f++) facePoints[f] = source.Faces[f].Center();
        var faceIndex = new Dictionary<Face, int>(faceCount);
        for (int f = 0; f < faceCount; f++) faceIndex[source.Faces[f]] = f;

        // An edge keeps its line wholly when it is a border, has more than two faces or is flagged sharp, and in part by its crease
        var crease = new float[edgeCount];
        var edgePoints = new Float3[edgeCount];
        for (int e = 0; e < edgeCount; e++)
        {
            Edge edge = source.Edges[e];
            List<Face> faces = edge.NeighborFaces();
            crease[e] = faces.Count != 2 || edge.IsSharp ? 1f : Math.Clamp(edge.Crease, 0f, 1f);
            Float3 middle = (edge.Vert1.Point + edge.Vert2.Point) * 0.5f;
            if (faces.Count != 2)
            {
                edgePoints[e] = middle;
                continue;
            }
            Float3 smooth = (edge.Vert1.Point + edge.Vert2.Point + facePoints[faceIndex[faces[0]]] + facePoints[faceIndex[faces[1]]]) * 0.25f;
            edgePoints[e] = smooth + (middle - smooth) * crease[e];
        }

        var newVertices = new Vertex[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            Vertex vertex = source.Vertices[v];
            List<Edge> around = vertex.NeighborEdges();
            List<Edge> creases = around.Where(e => crease[edgeIndex[e]] > 0).ToList();

            // Where the vertex goes on a smooth surface
            Float3 smooth = vertex.Point;
            List<Face> faces = vertex.NeighborFaces();
            if (faces.Count > 0 && around.Count > 0)
            {
                Float3 faceAverage = Float3.Zero, edgeAverage = Float3.Zero;
                foreach (Face face in faces) faceAverage += facePoints[faceIndex[face]];
                foreach (Edge edge in around) edgeAverage += (edge.Vert1.Point + edge.Vert2.Point) * 0.5f;
                float n = around.Count;
                smooth = (faceAverage / faces.Count + edgeAverage / n * 2 + vertex.Point * (n - 3)) / n;
            }

            // Two creased edges make a line through the vertex that it stays on, more make a corner that it stays at.
            // How far it is held there is the average crease.
            Float3 point = smooth;
            if (creases.Count >= 2)
            {
                Float3 held = creases.Count == 2
                    ? vertex.Point * 0.75f + (creases[0].OtherVertex(vertex).Point + creases[1].OtherVertex(vertex).Point) * 0.125f
                    : vertex.Point;
                float weight = creases.Average(e => crease[edgeIndex[e]]);
                point = smooth + (held - smooth) * weight;
            }
            newVertices[v] = result.AddVertex(point);
            newVertices[v].Attributes = AttributeTransfer.Copy(vertex.Attributes);
        }

        var edgeVertices = new Vertex[edgeCount];
        for (int e = 0; e < edgeCount; e++)
        {
            Edge edge = source.Edges[e];
            edgeVertices[e] = result.AddVertex(edgePoints[e]);
            GeometryOperators.AttributeLerp(result, edgeVertices[e], edge.Vert1, edge.Vert2, 0.5f);
            Vertex a = newVertices[vertexIndex[edge.Vert1]], b = newVertices[vertexIndex[edge.Vert2]];
            result.AddEdge(a, edgeVertices[e]).Attributes = AttributeTransfer.Copy(edge.Attributes);
            result.AddEdge(edgeVertices[e], b).Attributes = AttributeTransfer.Copy(edge.Attributes);
        }

        var floatLoops = source.LoopAttributes.Where(d => d.Type.BaseType == GeometryData.AttributeBaseType.Float).ToList();
        for (int f = 0; f < faceCount; f++)
        {
            Face face = source.Faces[f];
            var corners = new List<Loop>(face.VertCount);
            Loop loop = face.Loop!;
            do
            {
                corners.Add(loop);
                loop = loop.Next!;
            } while (loop != face.Loop);
            int n = corners.Count;

            Vertex center = result.AddVertex(facePoints[f]);
            foreach (var definition in source.VertexAttributes)
            {
                if (definition.Type.BaseType != GeometryData.AttributeBaseType.Float) continue;
                var average = new float[definition.Type.Dimensions];
                foreach (Loop corner in corners)
                {
                    if (corner.Vert.Attributes.TryGetValue(definition.Name, out var value) && value is GeometryData.FloatAttributeValue floats)
                        for (int d = 0; d < average.Length && d < floats.Data.Length; d++) average[d] += floats.Data[d] / n;
                }
                center.Attributes[definition.Name] = new GeometryData.FloatAttributeValue(average);
            }

            for (int i = 0; i < n; i++)
            {
                Loop corner = corners[i], before = corners[(i + n - 1) % n], after = corners[(i + 1) % n];
                Face? quad = result.AddFace(
                    newVertices[vertexIndex[corner.Vert]], edgeVertices[edgeIndex[corner.Edge]], center, edgeVertices[edgeIndex[before.Edge]]);
                if (quad == null) continue;
                quad.Attributes = AttributeTransfer.Copy(face.Attributes);

                // The four corners of the new face sit at the old corner, half way to the next, the middle, and half way from the last
                Loop target = quad.Loop!;
                for (int c = 0; c < 4; c++)
                {
                    target.Attributes = AttributeTransfer.Copy(corner.Attributes);
                    foreach (var definition in floatLoops)
                    {
                        if (!corner.Attributes.TryGetValue(definition.Name, out var own) || own is not GeometryData.FloatAttributeValue ownFloats) continue;
                        var mixed = new float[ownFloats.Data.Length];
                        for (int d = 0; d < mixed.Length; d++)
                        {
                            float here = ownFloats.Data[d];
                            mixed[d] = c switch
                            {
                                0 => here,
                                1 => (here + Component(after, definition.Name, d, here)) * 0.5f,
                                3 => (here + Component(before, definition.Name, d, here)) * 0.5f,
                                _ => corners.Sum(k => Component(k, definition.Name, d, here)) / n,
                            };
                        }
                        target.Attributes[definition.Name] = new GeometryData.FloatAttributeValue(mixed);
                    }
                    target = target.Next!;
                }
            }
        }
        return result;
    }

    private static float Component(Loop loop, string name, int index, float fallback)
        => loop.Attributes.TryGetValue(name, out var value) && value is GeometryData.FloatAttributeValue floats && index < floats.Data.Length ? floats.Data[index] : fallback;
}
