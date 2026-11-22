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
    /// Joins two runs of edges with a strip of four sided faces. The edges must fall into exactly two chains or two
    /// rings. Runs of the same length are joined edge to edge. Runs of different length are joined too when they
    /// differ by an even number of vertices and the longer has at most three edges for every edge of the shorter:
    /// the strip then narrows in evenly spread places, each taking three edges down to one over two new vertices.
    /// Rings are lined up where the strip comes out shortest. Throws <see cref="InvalidOperationException"/> when
    /// the runs do not fit. Returns the new faces.
    /// </summary>
    internal static List<Face> Bridge(GeometryData mesh, IEnumerable<Edge> edges)
    {
        List<(List<Vertex> Path, bool Ring)> runs = Runs(mesh, edges);
        if (runs.Count != 2)
            throw new InvalidOperationException($"Bridge needs two separate runs of edges, the selection has {runs.Count}.");
        if (runs[0].Ring != runs[1].Ring)
            throw new InvalidOperationException("Bridge needs two rings or two open runs, not one of each.");
        bool ring = runs[0].Ring;
        (List<Vertex> wide, List<Vertex> narrow) = runs[0].Path.Count >= runs[1].Path.Count ? (runs[0].Path, runs[1].Path) : (runs[1].Path, runs[0].Path);

        if ((wide.Count - narrow.Count) % 2 != 0)
            throw new InvalidOperationException($"Bridge cannot join {wide.Count} vertices to {narrow.Count} with quads, they must differ by an even number. Add or merge one vertex.");
        int narrowings = (wide.Count - narrow.Count) / 2;
        int steps = ring ? narrow.Count : narrow.Count - 1;
        if (narrowings > steps)
            throw new InvalidOperationException($"Bridge cannot join {wide.Count} vertices to {narrow.Count}, the longer run may have at most three edges for every edge of the shorter.");

        // Where along the wide run each edge of the narrow run starts, the narrowing ones taking three wide edges
        var starts = new int[steps + 1];
        for (int i = 0; i < steps; i++)
            starts[i + 1] = starts[i] + ((i + 1) * narrowings / steps > i * narrowings / steps ? 3 : 1);

        // The narrow run is turned and, for rings, rotated to where its vertices lie nearest the ones they join
        List<Vertex> best = narrow;
        float bestCost = float.PositiveInfinity;
        for (int flip = 0; flip < 2; flip++)
        {
            List<Vertex> turned = flip == 0 ? narrow : Enumerable.Reverse(narrow).ToList();
            for (int shift = 0; shift < (ring ? narrow.Count : 1); shift++)
            {
                float cost = 0;
                for (int i = 0; i < narrow.Count; i++)
                    cost += Float3.LengthSquared(wide[starts[i] % wide.Count].Point - turned[(i + shift) % narrow.Count].Point);
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = Enumerable.Range(0, narrow.Count).Select(i => turned[(i + shift) % narrow.Count]).ToList();
            }
        }

        var made = new List<Face>();
        for (int i = 0; i < steps; i++)
        {
            Vertex Wide(int offset) => wide[(starts[i] + offset) % wide.Count];
            Vertex n0 = best[i], n1 = best[(i + 1) % narrow.Count];

            // The new faces run against the face already on the wide edge, or else on the narrow edge, so they face the same way
            Loop? wideSide = mesh.FindEdge(Wide(0), Wide(1))?.Loop, narrowSide = mesh.FindEdge(n0, n1)?.Loop;
            bool reversed = wideSide != null ? wideSide.Vert == Wide(0) : narrowSide != null && narrowSide.Vert == n1;
            Face? like = (wideSide ?? narrowSide)?.Face;
            void Add(params Vertex[] corners)
            {
                if (reversed) Array.Reverse(corners);
                Face? face = like != null ? mesh.AddFace(corners, like) : mesh.AddFace(corners);
                if (face != null) made.Add(face);
            }

            if (starts[i + 1] - starts[i] == 1)
            {
                Add(Wide(0), Wide(1), n1, n0);
                continue;
            }

            Vertex inner0 = Between(mesh, Wide(1), n0, n1), inner1 = Between(mesh, Wide(2), n1, n0);
            Add(Wide(0), Wide(1), inner0, n0);
            Add(Wide(1), Wide(2), inner1, inner0);
            Add(Wide(2), Wide(3), n1, inner1);
            Add(inner0, inner1, n1, n0);
        }
        return made;
    }

    // A new vertex half way from a vertex of the wide run to the narrow edge, a third along that edge from its near end
    private static Vertex Between(GeometryData mesh, Vertex from, Vertex near, Vertex far)
    {
        Vertex made = mesh.AddVertex((from.Point + near.Point + (far.Point - near.Point) / 3f) * 0.5f);
        made.Attributes = AttributeTransfer.Lerp(AttributeTransfer.Copy(from.Attributes), AttributeTransfer.Copy(near.Attributes), 0.5f);
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

    /// <summary>
    /// A copy of the mesh given thickness: an outer and an inner shell, apart by the thickness along each vertex's
    /// normal, joined by side faces along open borders. The offset from -1 to 1 places the shells, -1 keeping the outer
    /// one on the surface and 1 the inner one. The inner shell faces inwards.
    /// </summary>
    internal static GeometryData Solidify(GeometryData source, float thickness, float offset)
    {
        var result = new GeometryData();
        CopyDefinitions(source, result);

        int count = source.Vertices.Count;
        var outer = new Vertex[count];
        var inner = new Vertex[count];
        var index = new Dictionary<Vertex, int>(count);
        float outward = thickness * (1f + offset) * 0.5f, inward = thickness * (1f - offset) * 0.5f;
        for (int i = 0; i < count; i++)
        {
            Vertex vertex = source.Vertices[i];
            index[vertex] = i;
            Float3 normal = Float3.Zero;
            foreach (Face face in vertex.NeighborFaces()) normal += face.Normal();
            if (Float3.LengthSquared(normal) > 1e-12f) normal = Float3.Normalize(normal);
            outer[i] = result.AddVertex(vertex.Point + normal * outward);
            outer[i].Attributes = AttributeTransfer.Copy(vertex.Attributes);
            inner[i] = result.AddVertex(vertex.Point - normal * inward);
            inner[i].Attributes = AttributeTransfer.Copy(vertex.Attributes);
        }

        foreach (Edge edge in source.Edges)
        {
            int a = index[edge.Vert1], b = index[edge.Vert2];
            result.AddEdge(outer[a], outer[b]).Attributes = AttributeTransfer.Copy(edge.Attributes);
            result.AddEdge(inner[a], inner[b]).Attributes = AttributeTransfer.Copy(edge.Attributes);
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

            AddCopy(result, face, corners, outer, index);
            for (int i = 0; i < corners.Count; i++)
            {
                Loop from = corners[i], to = corners[(i + 1) % corners.Count];
                Edge? edge = source.FindEdge(from.Vert, to.Vert);
                if (edge == null || edge.NeighborFaces().Count != 1) continue;
                int a = index[from.Vert], b = index[to.Vert];
                Face? side = result.AddFace(outer[b], outer[a], inner[a], inner[b]);
                if (side == null) continue;
                side.Attributes = AttributeTransfer.Copy(face.Attributes);
                Loop corner = side.Loop!;
                foreach (Loop like in (Loop[])[to, from, from, to])
                {
                    corner.Attributes = AttributeTransfer.Copy(like.Attributes);
                    corner = corner.Next!;
                }
            }
            corners.Reverse();
            AddCopy(result, face, corners, inner, index);
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
                    foreach (var pair in corner.Attributes)
                        if (pair.Value is GeometryData.WeightsAttributeValue)
                            target.Attributes[pair.Key] = MixWeights(pair.Key, c, corner, before, after, corners);
                    target = target.Next!;
                }
            }
        }
        return result;
    }

    // Weights at a new corner mixed as float attributes are: the old corner's own, half with a neighbour, or the face's average
    private static GeometryData.WeightsAttributeValue MixWeights(string name, int corner, Loop own, Loop before, Loop after, List<Loop> corners)
    {
        var here = (GeometryData.WeightsAttributeValue)own.Attributes[name];
        GeometryData.WeightsAttributeValue Of(Loop loop) => loop.Attributes.TryGetValue(name, out var value) && value is GeometryData.WeightsAttributeValue weights ? weights : here;
        switch (corner)
        {
            case 0: return GeometryData.WeightsAttributeValue.Blend([here], [1f]);
            case 1: return GeometryData.WeightsAttributeValue.Blend([here, Of(after)], [0.5f, 0.5f]);
            case 3: return GeometryData.WeightsAttributeValue.Blend([here, Of(before)], [0.5f, 0.5f]);
            default:
                var all = new GeometryData.WeightsAttributeValue[corners.Count];
                var shares = new float[corners.Count];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = Of(corners[i]);
                    shares[i] = 1f / all.Length;
                }
                return GeometryData.WeightsAttributeValue.Blend(all, shares);
        }
    }

    private static float Component(Loop loop, string name, int index, float fallback)
        => loop.Attributes.TryGetValue(name, out var value) && value is GeometryData.FloatAttributeValue floats && index < floats.Data.Length ? floats.Data[index] : fallback;

    /// <summary>Joins pairs of triangles into quads, the pairs that turn and bend least first. Returns the quads made.</summary>
    internal static List<Face> TrisToQuads(GeometryData mesh, IEnumerable<Face>? faces, float degrees)
    {
        var triangles = (faces ?? mesh.Faces).Where(f => mesh.Contains(f) && f.VertCount == 3).ToHashSet();
        float limit = degrees * MathF.PI / 180f;
        var pairs = new List<(float Cost, Edge Edge, Face A, Face B)>();
        foreach (Edge edge in mesh.Edges)
        {
            if (edge.IsSeam || edge.IsSharp) continue;
            List<Face> sides = edge.NeighborFaces();
            if (sides.Count != 2 || !triangles.Contains(sides[0]) || !triangles.Contains(sides[1])) continue;

            float turn = MathF.Acos(Math.Clamp(Float3.Dot(sides[0].Normal(), sides[1].Normal()), -1f, 1f));
            if (turn > limit) continue;

            Vertex apexA = sides[0].NeighborVertices().First(v => v != edge.Vert1 && v != edge.Vert2);
            Vertex apexB = sides[1].NeighborVertices().First(v => v != edge.Vert1 && v != edge.Vert2);
            Float3[] quad = [apexA.Point, edge.Vert1.Point, apexB.Point, edge.Vert2.Point];
            float bend = 0f;
            for (int i = 0; i < 4; i++)
            {
                Float3 a = Float3.Normalize(quad[(i + 3) % 4] - quad[i]), b = Float3.Normalize(quad[(i + 1) % 4] - quad[i]);
                bend = MathF.Max(bend, MathF.Abs(MathF.Acos(Math.Clamp(Float3.Dot(a, b), -1f, 1f)) - MathF.PI / 2f));
            }
            if (bend <= limit) pairs.Add((turn + bend, edge, sides[0], sides[1]));
        }

        var used = new HashSet<Face>();
        var joins = new List<Edge>();
        foreach ((_, Edge edge, Face a, Face b) in pairs.OrderBy(p => p.Cost))
        {
            if (used.Contains(a) || used.Contains(b)) continue;
            used.Add(a);
            used.Add(b);
            joins.Add(edge);
        }

        var before = new HashSet<Face>(mesh.Faces);
        GeometryOperators.DissolveEdges(mesh, joins);
        return mesh.Faces.Where(f => !before.Contains(f)).ToList();
    }
}
