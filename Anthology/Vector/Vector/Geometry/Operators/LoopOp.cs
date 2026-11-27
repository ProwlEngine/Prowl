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

internal static class LoopOp
{
    /// <summary>
    /// The edges that run on from an edge end to end, as a modeller's edge loop: through vertices where four edges
    /// meet it takes the edge across, and along a border it follows the border. In order, the edge given included.
    /// </summary>
    internal static List<Edge> EdgeLoop(Edge start)
    {
        var loop = new List<Edge> { start };
        var seen = new HashSet<Edge> { start };
        for (int side = 0; side < 2; side++)
        {
            Edge edge = start;
            Vertex vertex = side == 0 ? start.Vert2 : start.Vert1;
            while (true)
            {
                Edge? next = Onward(edge, vertex);
                if (next == null) break;
                if (!seen.Add(next)) return loop;
                if (side == 0) loop.Add(next); else loop.Insert(0, next);
                vertex = next.OtherVertex(vertex);
                edge = next;
            }
        }
        return loop;
    }

    private static Edge? Onward(Edge edge, Vertex vertex)
    {
        List<Edge> around = vertex.NeighborEdges();
        List<Face> faces = edge.NeighborFaces();
        if (faces.Count == 1)
        {
            Edge? border = null;
            foreach (Edge other in around)
            {
                if (other == edge || other.NeighborFaces().Count != 1) continue;
                if (border != null) return null;
                border = other;
            }
            return border;
        }
        if (faces.Count != 2 || around.Count != 4) return null;
        Edge? across = null;
        foreach (Edge other in around)
        {
            if (other == edge || other.NeighborFaces().Any(faces.Contains)) continue;
            if (across != null) return null;
            across = other;
        }
        return across;
    }

    /// <summary>
    /// The edges facing an edge across four sided faces, one after another, as a modeller's edge ring. Each comes with
    /// the end that lines up with the first end of the edge given, so a point part way along means the same on all of
    /// them. In order. Closed says the ring came back round to where it started.
    /// </summary>
    internal static List<(Edge Edge, Vertex From)> EdgeRing(Edge start, out bool closed)
    {
        closed = false;
        var ring = new List<(Edge, Vertex)> { (start, start.Vert1) };
        var seen = new HashSet<Edge> { start };
        for (int side = 0; side < 2; side++)
        {
            Edge edge = start;
            Vertex from = start.Vert1;
            Face? came = null;
            while (true)
            {
                Face? face = null;
                int index = 0;
                foreach (Face candidate in edge.NeighborFaces())
                {
                    if (candidate == came) continue;
                    if (came == null && index++ != side) continue;
                    face = candidate;
                    break;
                }
                if (face == null || face.VertCount != 4) break;

                // In a four sided face the edge across joins the two other corners, each next to one end of this edge
                Loop corner = face.Loop!;
                while (corner.Edge != edge) corner = corner.Next!;
                Edge across = corner.Next!.Next!.Edge;
                Vertex acrossFrom = corner.Vert == from ? corner.Prev!.Vert : corner.Next!.Next!.Vert;
                if (!seen.Add(across))
                {
                    closed = across == start;
                    return ring;
                }
                if (side == 0) ring.Add((across, acrossFrom)); else ring.Insert(0, (across, acrossFrom));
                edge = across;
                from = acrossFrom;
                came = face;
            }
        }
        return ring;
    }

    /// <summary>
    /// Cuts new edge loops across the ring of an edge: every edge of the ring gets the given number of new vertices,
    /// evenly spaced and moved along by the slide (-1 to 1, the whole gap to the next cut or end), and the faces
    /// between are split through them. Returns the new edges, the cuts.
    /// </summary>
    internal static List<Edge> Cut(GeometryData mesh, Edge edge, int cuts, float slide)
    {
        var result = new List<Edge>();
        if (cuts < 1 || !mesh.Contains(edge)) return result;
        List<(Edge Edge, Vertex From)> ring = EdgeRing(edge, out bool closed);
        if (ring.Count < 2) return result;

        float gap = 1f / (cuts + 1);
        var rows = new List<Vertex[]>(ring.Count);
        foreach ((Edge ringEdge, Vertex from) in ring)
        {
            Vertex to = ringEdge.OtherVertex(from);
            Float3 a = from.Point, b = to.Point;
            var row = new Vertex[cuts];
            Edge rest = ringEdge;
            Vertex last = from;
            for (int k = 0; k < cuts; k++)
            {
                float t = Math.Clamp((k + 1 + slide * 0.98f) * gap, 0.001f, 0.999f);
                // The rest of the edge runs from the last new vertex to the far end
                float along = Float3.Length(b - last.Point) > 1e-12f ? Float3.Length((a + (b - a) * t) - last.Point) / Float3.Length(b - last.Point) : 0.5f;
                row[k] = GeometryOperators.SplitEdge(mesh, rest, last, along, out rest);
                last = row[k];
            }
            rows.Add(row);
        }

        int pairs = closed ? ring.Count : ring.Count - 1;
        for (int i = 0; i < pairs; i++)
        {
            Vertex[] here = rows[i], there = rows[(i + 1) % ring.Count];
            for (int k = 0; k < cuts; k++)
            {
                Face? between = here[k].NeighborFaces().FirstOrDefault(f => f.GetLoop(there[k]) != null);
                if (between == null) continue;
                GeometryOperators.SplitFace(mesh, between, here[k], there[k], out Edge? made);
                if (made != null) result.Add(made);
            }
        }
        return result;
    }

    /// <summary>
    /// Slides the vertices of connected edges along the edges that leave them sideways: at 1 they reach the next
    /// vertices on one side, at -1 the next on the other. Edges must run as chains or rings through vertices with a
    /// face on both sides. Returns how many vertices moved.
    /// </summary>
    internal static int Slide(GeometryData mesh, IEnumerable<Edge> edges, float factor)
    {
        var selected = new HashSet<Edge>(edges.Where(mesh.Contains));
        var targets = new Dictionary<Vertex, (Float3 Left, Float3 Right, bool HasLeft, bool HasRight)>();
        var done = new HashSet<Edge>();

        foreach (Edge seed in selected)
        {
            if (done.Contains(seed)) continue;

            // One chain of selected edges, as a path of vertices
            var path = new List<Vertex> { seed.Vert1, seed.Vert2 };
            done.Add(seed);
            for (int side = 0; side < 2; side++)
            {
                while (true)
                {
                    Vertex tip = side == 0 ? path[path.Count - 1] : path[0];
                    Edge? next = tip.NeighborEdges().FirstOrDefault(e => selected.Contains(e) && !done.Contains(e));
                    if (next == null) break;
                    done.Add(next);
                    if (side == 0) path.Add(next.OtherVertex(tip)); else path.Insert(0, next.OtherVertex(tip));
                }
            }

            bool ring = path.Count > 2 && path[0] == path[path.Count - 1];
            int count = ring ? path.Count - 1 : path.Count;
            for (int i = 0; i < count; i++)
            {
                Vertex vertex = path[i];
                Float3 left = vertex.Point, right = vertex.Point;
                bool hasLeft = false, hasRight = false;

                // The edges of the path either side of the vertex each name a face to the left and one to the right
                for (int step = -1; step <= 0; step++)
                {
                    int a = i + step, b = a + 1;
                    if (ring) { a = (a + count) % count; b = (b + count) % count; }
                    if (a < 0 || b >= path.Count) continue;
                    Edge? along = mesh.FindEdge(path[a], path[b]);
                    if (along?.Loop == null) continue;
                    Loop loop = along.Loop;
                    do
                    {
                        bool isLeft = loop.Vert == path[a];
                        Vertex? aside = SideVertex(loop.Face, vertex, along);
                        if (aside != null)
                        {
                            if (isLeft && !hasLeft) { left = aside.Point; hasLeft = true; }
                            if (!isLeft && !hasRight) { right = aside.Point; hasRight = true; }
                        }
                        loop = loop.RadialNext!;
                    } while (loop != along.Loop);
                }
                targets[vertex] = (left, right, hasLeft, hasRight);
            }
        }

        int moved = 0;
        float amount = Math.Clamp(Math.Abs(factor), 0f, 1f);
        foreach (KeyValuePair<Vertex, (Float3 Left, Float3 Right, bool HasLeft, bool HasRight)> pair in targets)
        {
            (Float3 left, Float3 right, bool hasLeft, bool hasRight) = pair.Value;
            if (factor > 0 ? !hasLeft : !hasRight) continue;
            pair.Key.Point = pair.Key.Point + ((factor > 0 ? left : right) - pair.Key.Point) * amount;
            moved++;
        }
        if (moved > 0) mesh.MarkChanged();
        return moved;
    }

    // The vertex at the far end of the face's other edge at a vertex, the edge that is not the one given
    private static Vertex? SideVertex(Face face, Vertex vertex, Edge along)
    {
        Loop? corner = face.GetLoop(vertex);
        if (corner == null) return null;
        return corner.Edge == along ? corner.Prev!.Vert : corner.Next!.Vert;
    }

    internal static List<Edge> EdgeLoopOrOutline(Edge edge, Face? side)
    {
        List<Edge> loop = GeometryOperators.EdgeLoop(edge);
        if (loop.Count > 1) return loop;
        Face? face = side ?? edge.NeighborFaces().FirstOrDefault();
        return face?.NeighborEdges() ?? [edge];
    }

    internal static List<Edge> EdgeRingAcross(Edge edge)
    {
        List<Edge> ring = GeometryOperators.EdgeRing(edge);
        if (ring.Count > 1) return ring;

        ring = [edge];
        var seen = new HashSet<Edge> { edge };
        foreach (Face start in edge.NeighborFaces())
        {
            Edge current = edge;
            Face? face = start;
            while (face != null)
            {
                Edge? next = MostParallel(face, current);
                if (next == null || !seen.Add(next)) break;
                ring.Add(next);
                Face? from = face;
                face = next.NeighborFaces().FirstOrDefault(f => f != from);
                current = next;
            }
        }
        return ring;
    }

    // The edge of a face most nearly parallel to another, not touching it, if any is within about 25 degrees
    private static Edge? MostParallel(Face face, Edge from)
    {
        Float3 direction = Float3.NormalizeSafe(from.Vert2.Point - from.Vert1.Point, Float3.UnitY);
        Edge? best = null;
        float bestDot = 0.9f;
        foreach (Edge edge in face.NeighborEdges())
        {
            if (edge == from || edge.ContainsVertex(from.Vert1) || edge.ContainsVertex(from.Vert2)) continue;
            float dot = MathF.Abs(Float3.Dot(direction, Float3.NormalizeSafe(edge.Vert2.Point - edge.Vert1.Point, Float3.UnitY)));
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = edge;
        }
        return best;
    }

    internal static List<List<Face>> FaceRegions(IEnumerable<Face> faces)
    {
        var set = new HashSet<Face>(faces);
        var done = new HashSet<Face>();
        var regions = new List<List<Face>>();
        foreach (Face start in set)
        {
            if (!done.Add(start)) continue;
            var region = new List<Face>();
            var stack = new Stack<Face>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                Face face = stack.Pop();
                region.Add(face);
                foreach (Edge edge in face.NeighborEdges())
                    foreach (Face next in edge.NeighborFaces())
                        if (set.Contains(next) && done.Add(next)) stack.Push(next);
            }
            regions.Add(region);
        }
        return regions;
    }
}
