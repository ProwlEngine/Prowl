// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;

using Edge = Prowl.Vector.Geometry.GeometryData.Edge;
using Face = Prowl.Vector.Geometry.GeometryData.Face;
using Vertex = Prowl.Vector.Geometry.GeometryData.Vertex;

namespace Prowl.Vector.Geometry.Operators;

/// <summary>
/// Rebuilds the faces of one plane from its edges, the way SketchUp finds faces. Edges in the plane are
/// first split where they cross or touch, then every closed region they bound is found. A region becomes a
/// face when the old faces there agree it should (old faces facing opposite ways cancel out), or when it is
/// new empty space closed off by newly drawn edges. Regions with holes are split along hidden bridge edges.
/// </summary>
internal static class PlaneRebuildOp
{
    private const float Eps = GeometryData.Epsilon;

    private sealed class OldFace
    {
        public required List<Float2> Polygon;
        public required int Sign;
        public required Face Source;
    }

    private sealed class Cycle
    {
        public required List<int> Verts;
        public required float Area;
        public required int Component;
        public List<List<int>> Holes = [];
    }

    /// <param name="normal">The plane normal. New faces in empty space face this way.</param>
    /// <param name="seeds">Vertices in the plane to rebuild around. Only the connected parts of the plane they reach are touched.</param>
    /// <param name="newSegments">Freshly drawn lines. Only regions they close off become new faces.</param>
    /// <param name="dissolve">Edges to remove when the faces on both sides face the same way, merging them.</param>
    internal static void Run(GeometryData mesh, Float3 normal, Float3 origin, IEnumerable<Vertex> seeds,
        IReadOnlyList<(Float3 A, Float3 B)> newSegments, int newMaterial, IReadOnlyCollection<Edge>? dissolve = null)
    {
        var frame = new PlaneFrame(normal, origin);
        var seedSet = seeds.Where(v => InPlane(frame, v)).ToHashSet();
        if (seedSet.Count == 0) return;

        (List<Edge> firstEdges, List<Face> firstFaces) = CollectScope(mesh, frame, seedSet);
        Imprint(mesh, frame, firstEdges, firstFaces.ToHashSet());
        (List<Edge> edges, List<Face> faces) = CollectScope(mesh, frame, seedSet);
        if (edges.Count == 0) return;

        // Old faces are kept as plain polygons, the faces themselves are about to be replaced
        var olds = new List<OldFace>(faces.Count);
        var edgeSides = new Dictionary<Edge, List<int>>();
        foreach (Face face in faces)
        {
            int sign = Float3.Dot(face.Normal(), frame.Normal) >= 0f ? 1 : -1;
            olds.Add(new OldFace { Polygon = face.NeighborVertices().Select(v => frame.To2D(v.Point)).ToList(), Sign = sign, Source = face });
            foreach (Edge e in face.NeighborEdges())
            {
                if (!edgeSides.TryGetValue(e, out List<int>? sides)) edgeSides[e] = sides = [];
                sides.Add(sign);
            }
        }

        var hadFaces = edges.Where(e => e.Loop != null).ToHashSet();
        foreach (Face face in faces) mesh.RemoveFace(face);

        var graph = new List<Edge>(edges.Count);
        foreach (Edge e in edges)
        {
            if (e.Loop == null && e.IsHidden)
            {
                mesh.RemoveEdge(e);
                continue;
            }

            bool merge = dissolve != null && dissolve.Contains(e) && e.Loop == null
                && edgeSides.TryGetValue(e, out List<int>? sides) && sides.Count == 2 && sides[0] == sides[1];
            if (merge)
            {
                mesh.RemoveEdge(e);
                continue;
            }
            graph.Add(e);
        }

        BuildFaces(mesh, frame, graph, olds, newSegments, newMaterial);

        foreach (Edge e in graph)
        {
            if (e.Loop != null || !hadFaces.Contains(e) || !mesh.Edges.Contains(e)) continue;
            if (IsNewEdge(e.Vert1.Point, e.Vert2.Point, newSegments)) continue;
            mesh.RemoveEdge(e);
        }

        mesh.RemoveUnused();
        mesh.MarkChanged();
    }

    private static bool InPlane(PlaneFrame frame, Vertex v) => MathF.Abs(frame.Distance(v.Point)) <= Eps * 2f;

    /// <summary>The edges and faces of the plane connected to the seeds, through shared vertices or by lying inside one another.</summary>
    private static (List<Edge> Edges, List<Face> Faces) CollectScope(GeometryData mesh, PlaneFrame frame, HashSet<Vertex> seeds)
    {
        var inPlane = new Dictionary<Vertex, bool>();
        bool On(Vertex v)
        {
            if (!inPlane.TryGetValue(v, out bool on)) inPlane[v] = on = InPlane(frame, v);
            return on;
        }

        var planeEdges = mesh.Edges.Where(e => On(e.Vert1) && On(e.Vert2)).ToList();
        var planeFaces = mesh.Faces.Where(f => f.NeighborVerticesEnumerable().All(On)).ToList();

        var parent = new Dictionary<Vertex, Vertex>();
        Vertex Find(Vertex v)
        {
            if (!parent.TryGetValue(v, out Vertex? p)) { parent[v] = v; return v; }
            while (p != v)
            {
                Vertex grand = parent[p];
                parent[v] = grand;
                v = p;
                p = grand;
            }
            return v;
        }
        void Union(Vertex a, Vertex b)
        {
            Vertex ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        foreach (Edge e in planeEdges) Union(e.Vert1, e.Vert2);
        foreach (Vertex s in seeds) Find(s);

        // Parts of the plane that touch, cross or sit inside one another all belong in the rebuild, which
        // a bounding box overlap catches without needing to know how they meet
        var boxes = new Dictionary<Vertex, (Float2 Min, Float2 Max)>();
        foreach (Vertex v in parent.Keys.ToList())
        {
            Vertex root = Find(v);
            Float2 p = frame.To2D(v.Point);
            boxes[root] = boxes.TryGetValue(root, out var box)
                ? (new Float2(MathF.Min(box.Min.X, p.X), MathF.Min(box.Min.Y, p.Y)), new Float2(MathF.Max(box.Max.X, p.X), MathF.Max(box.Max.Y, p.Y)))
                : (p, p);
        }

        var touched = seeds.Select(Find).ToHashSet();
        var pending = boxes.Keys.Where(r => !touched.Contains(r)).ToList();
        bool grew = true;
        while (grew && pending.Count > 0)
        {
            grew = false;
            foreach (Vertex root in touched.ToList())
            {
                var a = boxes[root];
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var b = boxes[pending[i]];
                    if (a.Min.X > b.Max.X + Eps || b.Min.X > a.Max.X + Eps || a.Min.Y > b.Max.Y + Eps || b.Min.Y > a.Max.Y + Eps) continue;
                    touched.Add(pending[i]);
                    pending.RemoveAt(i);
                    grew = true;
                }
            }
        }

        return (planeEdges.Where(e => touched.Contains(Find(e.Vert1))).ToList(),
                planeFaces.Where(f => touched.Contains(Find(f.Loop!.Vert))).ToList());
    }
    /// <summary>Splits edges where vertices sit on them and where they cross each other.</summary>
    private static void Imprint(GeometryData mesh, PlaneFrame frame, List<Edge> scope, HashSet<Face> scopeFaces)
    {
        // Hidden edges only join faces of this plane, unless a face of another plane hangs off them, then they are a real border
        var list = scope.Where(e => !e.IsHidden || e.NeighborFaces().Any(f => !scopeFaces.Contains(f))).ToList();
        var verts = list.SelectMany(e => new[] { e.Vert1, e.Vert2 }).ToHashSet();

        void Split(int index, Vertex v)
        {
            Edge e = list[index];
            Vertex a = e.Vert1, b = e.Vert2;
            mesh.InsertVertexInEdge(e, v);
            list[index] = mesh.FindEdge(a, v)!;
            list.Add(mesh.FindEdge(v, b)!);
        }

        for (int guard = 0; guard < 64; guard++)
        {
            bool changed = false;

            for (int i = 0; i < list.Count; i++)
            {
                bool split;
                do
                {
                    split = false;
                    Edge e = list[i];
                    foreach (Vertex v in verts)
                    {
                        if (v == e.Vert1 || v == e.Vert2) continue;
                        if (!GeometryData.OnSegmentInterior(v.Point, e.Vert1.Point, e.Vert2.Point, Eps)) continue;
                        Split(i, v);
                        split = changed = true;
                        break;
                    }
                } while (split);
            }

            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    Edge e1 = list[i], e2 = list[j];
                    if (e1.ContainsVertex(e2.Vert1) || e1.ContainsVertex(e2.Vert2)) continue;

                    Float2 a = frame.To2D(e1.Vert1.Point), b = frame.To2D(e1.Vert2.Point);
                    Float2 c = frame.To2D(e2.Vert1.Point), d = frame.To2D(e2.Vert2.Point);
                    if (!Polygon2D.ProperIntersection(a, b, c, d, Eps, out float t, out _)) continue;

                    Float3 point = Maths.Lerp(e1.Vert1.Point, e1.Vert2.Point, t);
                    Vertex v = mesh.FindVertex(point) ?? mesh.AddVertex(point);
                    if (!e1.ContainsVertex(v)) Split(i, v);
                    if (!list[j].ContainsVertex(v)) Split(j, v);
                    verts.Add(v);
                    changed = true;
                }
            }

            if (!changed) break;
        }
    }

    private static bool IsNewEdge(Float3 a, Float3 b, IReadOnlyList<(Float3 A, Float3 B)> segments)
    {
        foreach ((Float3 sa, Float3 sb) in segments)
        {
            if (DistanceToSegment(a, sa, sb) <= Eps && DistanceToSegment(b, sa, sb) <= Eps) return true;
        }
        return false;
    }

    private static float DistanceToSegment(Float3 p, Float3 a, Float3 b)
    {
        Float3 ab = b - a;
        float lengthSq = Float3.LengthSquared(ab);
        float t = lengthSq > 1e-12f ? Math.Clamp(Float3.Dot(p - a, ab) / lengthSq, 0f, 1f) : 0f;
        return Float3.Length(a + ab * t - p);
    }

    private static void BuildFaces(GeometryData mesh, PlaneFrame frame, List<Edge> graphEdges, List<OldFace> olds,
        IReadOnlyList<(Float3 A, Float3 B)> newSegments, int newMaterial)
    {
        var index = new Dictionary<Vertex, int>();
        var verts = new List<Vertex>();
        int Index(Vertex v)
        {
            if (index.TryGetValue(v, out int i)) return i;
            index[v] = verts.Count;
            verts.Add(v);
            return verts.Count - 1;
        }

        var edgeList = graphEdges.Select(e => (A: Index(e.Vert1), B: Index(e.Vert2))).ToList();
        Float2[] pts = verts.Select(v => frame.To2D(v.Point)).ToArray();

        // Dangling lines bound nothing, so they are left out of the face search
        var alive = Enumerable.Repeat(true, edgeList.Count).ToArray();
        var degree = new int[verts.Count];
        foreach ((int a, int b) in edgeList) { degree[a]++; degree[b]++; }
        var incident = new List<int>[verts.Count];
        for (int i = 0; i < verts.Count; i++) incident[i] = [];
        for (int e = 0; e < edgeList.Count; e++) { incident[edgeList[e].A].Add(e); incident[edgeList[e].B].Add(e); }
        var queue = new Queue<int>(Enumerable.Range(0, verts.Count).Where(i => degree[i] <= 1));
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            foreach (int e in incident[v])
            {
                if (!alive[e]) continue;
                alive[e] = false;
                int other = edgeList[e].A == v ? edgeList[e].B : edgeList[e].A;
                degree[v]--;
                if (--degree[other] == 1) queue.Enqueue(other);
            }
        }

        // Outgoing half edges around each vertex, counter clockwise by angle
        var outgoing = new List<int>[verts.Count];
        for (int i = 0; i < verts.Count; i++) outgoing[i] = [];
        var heFrom = new List<int>();
        var heTo = new List<int>();
        for (int e = 0; e < edgeList.Count; e++)
        {
            if (!alive[e]) continue;
            (int a, int b) = edgeList[e];
            outgoing[a].Add(heFrom.Count); heFrom.Add(a); heTo.Add(b);
            outgoing[b].Add(heFrom.Count); heFrom.Add(b); heTo.Add(a);
        }
        float Angle(int he)
        {
            Float2 d = pts[heTo[he]] - pts[heFrom[he]];
            return MathF.Atan2(d.Y, d.X);
        }
        var position = new int[heFrom.Count];
        for (int v = 0; v < verts.Count; v++)
        {
            outgoing[v].Sort((x, y) => Angle(x).CompareTo(Angle(y)));
            for (int k = 0; k < outgoing[v].Count; k++) position[outgoing[v][k]] = k;
        }
        int Twin(int he) => he ^ 1;
        int Next(int he)
        {
            List<int> around = outgoing[heTo[he]];
            return around[(position[Twin(he)] - 1 + around.Count) % around.Count];
        }

        // Components of the live graph, to tell holes from the faces around them
        var component = Enumerable.Range(0, verts.Count).ToArray();
        int Root(int x) { while (component[x] != x) x = component[x] = component[component[x]]; return x; }
        for (int he = 0; he < heFrom.Count; he += 2)
        {
            int ra = Root(heFrom[he]), rb = Root(heTo[he]);
            if (ra != rb) component[ra] = rb;
        }

        var visited = new bool[heFrom.Count];
        var positive = new List<Cycle>();
        var negative = new List<Cycle>();
        for (int start = 0; start < heFrom.Count; start++)
        {
            if (visited[start]) continue;
            var cycle = new List<int>();
            int he = start;
            int guard = heFrom.Count + 1;
            while (!visited[he] && guard-- > 0)
            {
                visited[he] = true;
                cycle.Add(heFrom[he]);
                he = Next(he);
            }
            if (cycle.Count < 3) continue;

            float area = Polygon2D.SignedArea(cycle.Select(i => pts[i]).ToList());
            var c = new Cycle { Verts = cycle, Area = area, Component = Root(cycle[0]) };
            if (area < -1e-9f) SplitNegativePinches(c, pts, negative);
            else if (area > 1e-9f) SplitPinches(c, pts, positive);
        }

        foreach (Cycle hole in negative)
        {
            Cycle? best = null;
            foreach (Cycle outer in positive)
            {
                if (outer.Component == hole.Component) continue;
                if (best != null && outer.Area >= best.Area) continue;
                if (Polygon2D.Contains(outer.Verts.Select(i => pts[i]).ToList(), pts[hole.Verts[0]])) best = outer;
            }
            best?.Holes.Add(hole.Verts);
        }

        foreach (Cycle cycle in positive)
        {
            var bridges = new List<(int, int)>();
            var holes = cycle.Holes.Select(h => { var r = new List<int>(h); r.Reverse(); return r; }).ToList();
            List<List<int>> pieces = Bridge(mesh, verts, pts, cycle.Verts, holes, bridges);
            if (pieces.Count == 0) continue;

            Float2 sample = InteriorPoint(pieces[0], pts);
            List<OldFace> covering = olds.Where(o => Polygon2D.Contains(o.Polygon, sample)).ToList();
            int winding = covering.Sum(o => o.Sign);

            int sign;
            Face? source = null;
            if (winding != 0)
            {
                sign = Math.Sign(winding);
                source = covering.First(o => o.Sign == sign).Source;
            }
            else if (covering.Count == 0 && TouchesNew(cycle, verts, newSegments))
            {
                sign = 1;
            }
            else continue;

            foreach (List<int> piece in pieces)
            {
                List<Vertex> faceVerts = piece.Select(i => verts[i]).ToList();
                if (sign < 0) faceVerts.Reverse();
                if (source != null) mesh.AddFace(faceVerts, source);
                else mesh.AddFace(faceVerts, newMaterial);
            }
            foreach ((int a, int b) in bridges)
            {
                Edge? bridge = mesh.FindEdge(verts[a], verts[b]);
                if (bridge != null) bridge.IsHidden = true;
            }
        }
    }

    /// <summary>A hole outline pinched at a vertex is really two holes touching there.</summary>
    private static void SplitNegativePinches(Cycle cycle, Float2[] pts, List<Cycle> result)
    {
        var work = new Stack<Cycle>();
        work.Push(cycle);
        while (work.Count > 0)
        {
            Cycle current = work.Pop();
            List<int> main = current.Verts;
            var seen = new Dictionary<int, int>();
            bool split = false;
            for (int i = 0; i < main.Count && !split; i++)
            {
                if (!seen.TryGetValue(main[i], out int first))
                {
                    seen[main[i]] = i;
                    continue;
                }

                List<int> sub = main.GetRange(first, i - first);
                main.RemoveRange(first, i - first);
                float area = Polygon2D.SignedArea(sub.Select(v => pts[v]).ToList());
                if (sub.Count >= 3 && area < -1e-9f) work.Push(new Cycle { Verts = sub, Area = area, Component = current.Component });
                split = true;
            }

            if (split)
            {
                current.Area = Polygon2D.SignedArea(main.Select(v => pts[v]).ToList());
                work.Push(current);
            }
            else if (main.Count >= 3 && current.Area < -1e-9f)
            {
                result.Add(current);
            }
        }
    }

    /// <summary>
    /// A loop that visits a vertex twice is pinched there and is really several simple loops. Counter
    /// clockwise ones become cycles of their own, clockwise ones are holes of the smallest one around them.
    /// </summary>
    private static void SplitPinches(Cycle cycle, Float2[] pts, List<Cycle> result)
    {
        var loops = new List<List<int>>();
        var work = new Stack<List<int>>();
        work.Push(cycle.Verts);
        while (work.Count > 0)
        {
            List<int> main = work.Pop();
            var seen = new Dictionary<int, int>();
            bool split = false;
            for (int i = 0; i < main.Count && !split; i++)
            {
                if (!seen.TryGetValue(main[i], out int first))
                {
                    seen[main[i]] = i;
                    continue;
                }

                List<int> sub = main.GetRange(first, i - first);
                main.RemoveRange(first, i - first);
                work.Push(sub);
                work.Push(main);
                split = true;
            }
            if (!split && main.Count >= 3) loops.Add(main);
        }

        var positives = new List<Cycle>();
        var holes = new List<List<int>>();
        foreach (List<int> loop in loops)
        {
            float area = Polygon2D.SignedArea(loop.Select(v => pts[v]).ToList());
            if (area > 1e-9f) positives.Add(new Cycle { Verts = loop, Area = area, Component = cycle.Component });
            else if (area < -1e-9f) holes.Add(loop);
        }

        foreach (List<int> hole in holes)
        {
            Cycle? owner = null;
            foreach (Cycle p in positives)
                if ((owner == null || p.Area < owner.Area) && Holds(p.Verts, hole, pts)) owner = p;
            owner?.Holes.Add(hole);
        }
        result.AddRange(positives);
    }
    private static bool TouchesNew(Cycle cycle, List<Vertex> verts, IReadOnlyList<(Float3 A, Float3 B)> segments)
    {
        if (segments.Count == 0) return false;
        IEnumerable<List<int>> loops = cycle.Holes.Prepend(cycle.Verts);
        foreach (List<int> loop in loops)
            for (int i = 0; i < loop.Count; i++)
                if (IsNewEdge(verts[loop[i]].Point, verts[loop[(i + 1) % loop.Count]].Point, segments)) return true;
        return false;
    }

    private static Float2 InteriorPoint(List<int> polygon, Float2[] pts)
    {
        List<Float2> poly = polygon.Select(i => pts[i]).ToList();
        List<int> tris = Polygon2D.Triangulate(poly);
        Float2 best = Polygon2D.Centroid(poly);
        float bestArea = 0f;
        for (int t = 0; t < tris.Count; t += 3)
        {
            Float2 a = poly[tris[t]], b = poly[tris[t + 1]], c = poly[tris[t + 2]];
            float area = MathF.Abs(Polygon2D.Cross(a, b, c));
            if (area > bestArea)
            {
                bestArea = area;
                best = (a + b + c) / 3f;
            }
        }
        return best;
    }

    /// <summary>
    /// Cuts each hole out of the outline with two bridges, splitting the region into simple polygons.
    /// Holes come in counter clockwise. Returns the polygons, counter clockwise.
    /// </summary>
    private static List<List<int>> Bridge(GeometryData mesh, List<Vertex> verts, Float2[] pts, List<int> outer, List<List<int>> holes, List<(int, int)> bridges)
    {
        var regions = new List<List<int>> { outer };
        var pending = holes.OrderByDescending(h => MathF.Abs(Polygon2D.SignedArea(h.Select(i => pts[i]).ToList()))).ToList();

        while (pending.Count > 0)
        {
            List<int> hole = pending[0];
            pending.RemoveAt(0);

            int regionIndex = regions.FindIndex(r => Holds(r, hole, pts));
            if (regionIndex < 0) continue;
            List<int> region = regions[regionIndex];

            var obstacles = new List<(int, int)>();
            foreach (List<int> loop in regions.Concat(pending).Append(hole))
                for (int i = 0; i < loop.Count; i++)
                    obstacles.Add((loop[i], loop[(i + 1) % loop.Count]));

            List<Float2> regionPoly = region.Select(i => pts[i]).ToList();
            List<Float2> holePoly = hole.Select(i => pts[i]).ToList();
            float expected = Polygon2D.SignedArea(regionPoly) - Polygon2D.SignedArea(holePoly);

            bool Valid(int o, int h, (int, int)? other)
            {
                Float2 p = pts[o], q = pts[h];
                if (Float2.Length(q - p) <= Eps) return false;
                if (mesh.FindEdge(verts[o], verts[h]) != null) return false;
                foreach ((int c, int d) in obstacles)
                {
                    if (c == o || d == o || c == h || d == h) continue;
                    if (Polygon2D.SegmentsTouch(p, q, pts[c], pts[d], Eps * 0.5f)) return false;
                }
                if (other is (int oc, int od) && Polygon2D.SegmentsTouch(p, q, pts[oc], pts[od], Eps * 0.5f)) return false;
                Float2 mid = (p + q) * 0.5f;
                return Polygon2D.Contains(regionPoly, mid) && !Polygon2D.Contains(holePoly, mid);
            }

            var pairs = new List<(int O, int H, float D)>();
            foreach (int o in region.Distinct())
                foreach (int h in hole)
                    pairs.Add((o, h, Float2.Length(pts[o] - pts[h])));
            pairs.Sort((x, y) => x.D.CompareTo(y.D));

            int shared = hole.Where(h => region.Contains(h)).Select(h => (int?)h).FirstOrDefault() ?? -1;
            bool Simple(List<int> piece) => piece.Distinct().Count() == piece.Count;
            bool Sound(List<int> a, List<int> b, bool strict)
            {
                float areaA = Polygon2D.SignedArea(a.Select(i => pts[i]).ToList());
                float areaB = Polygon2D.SignedArea(b.Select(i => pts[i]).ToList());
                if (areaA <= 1e-9f || areaB <= 1e-9f) return false;
                if (MathF.Abs(areaA + areaB - expected) > MathF.Max(1e-6f, MathF.Abs(expected) * 1e-3f)) return false;
                return !strict || (Simple(a) && Simple(b));
            }

            List<int>? pieceA = null, pieceB = null;
            foreach (bool strict in new[] { true, false })
            {
                if (shared >= 0)
                {
                    // A hole touching the outline at a vertex is cut loose by a single bridge
                    foreach ((int o, int h, _) in pairs)
                    {
                        if (o == shared || h == shared || !Valid(o, h, null)) continue;
                        List<int> p1 = Dedup(Walk(region, o, shared, true).Concat(Walk(hole, shared, h, false)));
                        List<int> p2 = Dedup(Walk(hole, h, shared, false).Concat(Walk(region, shared, o, true)));
                        if (!Sound(p1, p2, strict)) continue;
                        pieceA = p1;
                        pieceB = p2;
                        bridges.Add((o, h));
                        break;
                    }
                }
                else
                {
                    int tried = 0;
                    foreach ((int o0, int i0, _) in pairs)
                    {
                        if (tried > 64) break;
                        if (!Valid(o0, i0, null)) continue;
                        tried++;

                        foreach ((int o1, int i1, _) in pairs)
                        {
                            if (o1 == o0 || i1 == i0 || !Valid(o1, i1, (o0, i0))) continue;

                            List<int> a = Walk(region, o0, o1, true).Concat(Walk(hole, i1, i0, false)).ToList();
                            List<int> b = Walk(region, o1, o0, true).Concat(Walk(hole, i0, i1, false)).ToList();
                            if (!Sound(a, b, strict)) continue;

                            pieceA = a;
                            pieceB = b;
                            bridges.Add((o0, i0));
                            bridges.Add((o1, i1));
                            break;
                        }
                        if (pieceA != null) break;
                    }
                }
                if (pieceA != null) break;
            }

            if (pieceA == null || pieceB == null) continue;
            regions[regionIndex] = pieceA;
            regions.Add(pieceB);
        }
        return regions;
    }

    /// <summary>Whether a hole lies inside a region, judged from a point on the hole that is not shared with the region outline.</summary>
    private static bool Holds(List<int> region, List<int> hole, Float2[] pts)
    {
        List<Float2> poly = region.Select(i => pts[i]).ToList();
        for (int i = 0; i < hole.Count; i++)
        {
            int a = hole[i], b = hole[(i + 1) % hole.Count];
            if (region.Contains(a) || region.Contains(b)) continue;
            return Polygon2D.Contains(poly, (pts[a] + pts[b]) * 0.5f);
        }
        return Polygon2D.Contains(poly, (pts[hole[0]] + pts[hole[1 % hole.Count]]) * 0.5f);
    }

    private static List<int> Dedup(IEnumerable<int> loop)
    {
        var result = new List<int>();
        foreach (int i in loop)
            if (result.Count == 0 || result[^1] != i) result.Add(i);
        while (result.Count > 1 && result[0] == result[^1]) result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>The loop from one entry to another inclusive, forwards or backwards.</summary>
    private static IEnumerable<int> Walk(List<int> loop, int from, int to, bool forward)
    {
        int n = loop.Count;
        int i = loop.IndexOf(from);
        for (int step = 0; step <= n; step++)
        {
            yield return loop[i];
            if (loop[i] == to) yield break;
            i = forward ? (i + 1) % n : (i - 1 + n) % n;
        }
    }
}
