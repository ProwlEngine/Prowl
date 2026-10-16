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
/// Beveling of edges and vertices, including any number of edges meeting at corners. Each selected edge
/// becomes a strip of faces, each corner where they meet is capped, and the faces either side of the edge
/// are pulled back to meet the strip. Works on any manifold surface, open or closed.
/// </summary>
internal static class BevelOp
{
    internal sealed class Options
    {
        public float Width = 0.1f;

        /// <summary>How many strips an edge becomes. One is a flat chamfer, more make a rounded edge.</summary>
        public int Segments = 1;

        /// <summary>The shape of the rounding. 0 is flat, 0.5 a circle, higher is squarer.</summary>
        public float Profile = 0.5f;
    }

    private const float MinAngle = 0.04f;

    // The faces and edges around a vertex, in turn. Between[i] is the face between Order[i] and the next edge.
    private sealed class Fan
    {
        public Vertex Vertex = null!;
        public readonly List<Edge> Order = [];
        public readonly List<Face?> Between = [];
        public bool Cyclic;
        public int N => Order.Count;

        public Face? After(int i)
        {
            if (Cyclic) return Between[((i % N) + N) % N];
            return i >= 0 && i < N - 1 ? Between[i] : null;
        }
    }

    private sealed class Sector
    {
        public int A, B;
        public readonly List<int> Faces = [];
        public readonly List<int> Unselected = [];
        public bool Gap;

        /// <summary>The faces here share a plane and only hidden edges lie between them, so they act as one face.</summary>
        public bool Flat;
        public int M => Unselected.Count;

        /// <summary>The unselected faces wrap more than a half turn round the vertex, so the cut leaves a gap in them rather than trimming a corner.</summary>
        public bool Reflex;

        /// <summary>The vertex stays as the point those wrapping faces meet at, and the corner is filled in.</summary>
        public bool Hub => Reflex && M >= 2;

        /// <summary>Where each unselected edge in the sector now ends, since the vertex itself is cut away.</summary>
        public readonly List<Vertex> Anchor = [];
        public Vertex VA = null!, VB = null!;

        /// <summary>Set when the one edge between the strips is cut at two different places.</summary>
        public Edge? U;
        public float Sa, Sb;
        public bool Split => M == 1 && !Flat && VA != VB;
    }

    private sealed class FanData
    {
        public Fan Fan = null!;
        public readonly List<int> Selected = [];
        public readonly List<Sector> Sectors = [];
    }

    private static Fan BuildFan(Vertex v)
    {
        var fan = new Fan { Vertex = v };
        var corners = new Dictionary<Edge, List<(Face Face, Edge Other)>>();
        foreach (Face face in v.NeighborFaces())
        {
            var loop = face.GetLoop(v);
            if (loop == null) continue;
            if (face.NeighborVertices().Count(x => x == v) != 1) throw new InvalidOperationException("A face touches the same corner twice, bevel cannot continue there");
            Edge en = loop.Edge, ep = loop.Prev!.Edge;
            if (!corners.TryGetValue(en, out var listN)) corners[en] = listN = [];
            listN.Add((face, ep));
            if (!corners.TryGetValue(ep, out var listP)) corners[ep] = listP = [];
            listP.Add((face, en));
        }

        if (corners.Values.Any(l => l.Count > 2)) throw new InvalidOperationException("Bevel needs a surface where no edge has more than two faces");
        if (corners.Count == 0) return fan;

        Edge start = corners.Keys.FirstOrDefault(e => corners[e].Count == 1)!;
        fan.Cyclic = start == null;
        start ??= corners.Keys.First();

        Edge current = start;
        Face? previous = null;
        var visited = new HashSet<Edge>();
        while (visited.Add(current))
        {
            fan.Order.Add(current);
            var next = corners[current].FirstOrDefault(o => o.Face != previous);
            if (next.Face == null) break;

            fan.Between.Add(next.Face);
            previous = next.Face;
            current = next.Other;
            if (current == start) break;
        }
        if (fan.Order.Count != corners.Count) throw new InvalidOperationException("Bevel cannot work at a vertex where the surface pinches together");
        if (fan.Cyclic && fan.Between.Count != fan.Order.Count) throw new InvalidOperationException("Bevel cannot work at a vertex where the surface pinches together");
        return fan;
    }

    private static Float3 DirAlong(Vertex v, Edge e) => Float3.Normalize(e.OtherVertex(v).Point - v.Point);

    // Unit vector in the face's plane, perpendicular to the edge, pointing into the face
    private static Float3 Inward(Vertex v, Face face, Edge e)
    {
        var loop = face.GetLoop(v)!;
        Float3 along = DirAlong(v, e);
        Float3 travel = loop.Edge == e ? along : -along;
        return Float3.Normalize(Float3.Cross(Float3.Normalize(face.Normal()), travel));
    }

    /// <summary>
    /// Keeps a bevel all or nothing. Until the mesh starts changing, a failure only has to drop the vertices
    /// the bevel added. Once it has started, the mesh as it was is kept so a failure can put it back. When the
    /// bevel finishes, the faces around what it made are checked, so a broken result is never left behind.
    /// </summary>
    private sealed class Guard
    {
        private readonly GeometryData _mesh;
        private readonly HashSet<Vertex> _old;
        private readonly int _count;
        private GeometrySnapshot? _before;

        public Guard(GeometryData mesh)
        {
            _mesh = mesh;
            _old = mesh.Vertices.ToHashSet();
            _count = mesh.Vertices.Count;
        }

        /// <summary>Called right before the first face is removed.</summary>
        public void Begin() => _before = _mesh.Capture();

        public void Undo()
        {
            if (_before != null) _mesh.Restore(_before);
            for (int i = _mesh.Vertices.Count - 1; i >= _count; i--)
                if (_mesh.Vertices[i].Edge == null) _mesh.Vertices.RemoveAt(i);
            _mesh.MarkChanged();
        }

        public void Verify()
        {
            const string message = "The bevel could not build clean faces here, try a smaller width";
            var faces = _mesh.Vertices.Where(v => !_old.Contains(v)).SelectMany(v => v.NeighborFaces()).Distinct();
            foreach (Face face in faces)
            {
                List<Vertex> verts = face.NeighborVertices();
                if (verts.Count < 3 || verts.Distinct().Count() != verts.Count) throw new InvalidOperationException(message);
                if (face.Area() < 1e-8f || !face.IsPlanar(2e-4f)) throw new InvalidOperationException(message);
                foreach (Edge e in face.NeighborEdges())
                    if (e.FaceCount > 2 || Float3.Length(e.Vert1.Point - e.Vert2.Point) < GeometryData.Epsilon * 0.5f)
                        throw new InvalidOperationException(message);
            }
        }
    }

    private static int Guarded(GeometryData mesh, Func<Guard, int> bevel)
    {
        var guard = new Guard(mesh);
        try
        {
            int result = bevel(guard);
            guard.Verify();
            return result;
        }
        catch
        {
            guard.Undo();
            throw;
        }
    }

    /// <summary>Bevels edges. Returns how many faces were added. On failure it throws and leaves the mesh as it was.</summary>
    internal static int Edges(GeometryData mesh, IReadOnlyCollection<Edge> edges, Options options) =>
        Guarded(mesh, guard => BevelEdges(mesh, edges, options, guard));

    private static int BevelEdges(GeometryData mesh, IReadOnlyCollection<Edge> edges, Options options, Guard guard)
    {
        if (options.Width <= 1e-6f) throw new InvalidOperationException("The bevel width must be above zero");
        int firstNew = mesh.Vertices.Count;
        float w = options.Width;
        int n = Math.Max(1, options.Segments);

        var selected = new HashSet<Edge>();
        foreach (Edge e in edges)
        {
            if (!mesh.Edges.Contains(e) || e.IsHidden) continue;
            List<Face> faces = e.NeighborFaces();
            if (faces.Count != 2) throw new InvalidOperationException("Bevel works on edges between two faces, select edges that are not on an open border");
            if (faces[0] == faces[1]) throw new InvalidOperationException("An edge with the same face on both sides cannot be beveled");
            if (faces[0].IsCoplanarWith(faces[1])) continue;
            selected.Add(e);
        }
        if (selected.Count == 0) throw new InvalidOperationException("No edges to bevel, flat edges between faces in one plane are skipped");

        var vertices = selected.SelectMany(e => new[] { e.Vert1, e.Vert2 }).Distinct().ToList();
        var datas = new Dictionary<Vertex, FanData>();
        foreach (Vertex v in vertices)
        {
            var data = new FanData { Fan = BuildFan(v) };
            Fan fan = data.Fan;
            for (int i = 0; i < fan.N; i++)
                if (selected.Contains(fan.Order[i])) data.Selected.Add(i);
            if (data.Selected.Count == 0) continue;
            BuildSectors(data);
            datas[v] = data;
        }

        // Where each sector's boundary vertices go
        foreach (FanData data in datas.Values) PlaceSectors(mesh, data, w);
        CheckWidths(datas, selected);
        var firstFace = selected.ToDictionary(e => e, e => e.NeighborFaces()[0]);

        // Profile polylines for every selected edge at each end
        var profiles = new Dictionary<(Edge, Vertex), List<Vertex>>();
        foreach (Edge e in selected)
        {
            Face f1 = e.NeighborFaces()[0];
            foreach (Vertex end in new[] { e.Vert1, e.Vert2 })
            {
                FanData data = datas[end];
                int pos = data.Fan.Order.IndexOf(e);
                Sector forward = data.Sectors.First(s => s.A == pos);
                Sector backward = data.Sectors.First(s => s.B == pos);
                bool f1Forward = data.Fan.After(pos) == f1;
                Vertex first = f1Forward ? forward.VA : backward.VB;
                Vertex last = f1Forward ? backward.VB : forward.VA;
                profiles[(e, end)] = BuildProfile(mesh, end, first, last, n, options.Profile);
            }
        }

        // Two strips meeting in a mitre end on the very same curve, so they share its points
        foreach (FanData data in datas.Values)
        {
            if (data.Selected.Count != 2) continue;
            Edge e0 = data.Fan.Order[data.Selected[0]], e1 = data.Fan.Order[data.Selected[1]];
            List<Vertex> p0 = Oriented(data, e0, profiles, firstFace), p1 = Oriented(data, e1, profiles, firstFace);
            if (p0.Count != p1.Count || p0.Count < 3) continue;
            bool same = true;
            for (int i = 0; i < p0.Count && same; i++)
                same = Float3.Length(p0[i].Point - p1[p1.Count - 1 - i].Point) < GeometryData.Epsilon;
            if (!same) continue;

            List<Vertex> replacement = Enumerable.Reverse(p0).ToList();
            bool f1Forward = data.Fan.After(data.Selected[1]) == firstFace[e1];
            profiles[(e1, data.Fan.Vertex)] = f1Forward ? Enumerable.Reverse(replacement).ToList() : replacement;
        }

        // The new outline of every face touching a bevelled vertex
        var rewritten = new Dictionary<Face, List<Vertex>>();
        foreach (FanData data in datas.Values)
        {
            Fan fan = data.Fan;
            for (int i = 0; i < fan.N; i++)
            {
                Face? face = fan.After(i);
                if (face == null || rewritten.ContainsKey(face)) continue;
                rewritten[face] = [];
            }
        }
        foreach (Face face in rewritten.Keys.ToList())
        {
            List<Vertex> old = face.NeighborVertices();
            var loop = new List<Vertex>();
            foreach (Vertex c in old)
            {
                if (datas.TryGetValue(c, out FanData? data)) loop.AddRange(CornerSequence(data, face));
                else loop.Add(c);
            }
            rewritten[face] = loop;
        }

        foreach ((Face face, List<Vertex> loop) in rewritten) ValidateLoop(mesh, face, loop);

        var snapshots = rewritten.Keys.ToDictionary(f => f, f => FaceSnapshot.Capture(mesh, f));
        var originalFlags = new List<(Vertex A, Vertex B, bool Hidden, bool Soft, Edge Edge, Dictionary<string, GeometryData.AttributeValue> Attributes)>();
        var incident = new List<Edge>();
        foreach (Vertex v in datas.Keys)
            foreach (Edge e in v.NeighborEdges())
                if (e.Loop != null && !incident.Contains(e))
                {
                    incident.Add(e);
                    originalFlags.Add((e.Vert1, e.Vert2, e.IsHidden, e.IsSoft, e, AttributeTransfer.Copy(e.Attributes)));
                }

        var stripInfo = selected.ToDictionary(e => e, e => (Forward: firstFace[e].GetLoop(e.Vert1)!.Edge == e, Look: firstFace[e]));
        var stripSources = selected.ToDictionary(e => e, e => e.NeighborFaces().Select(f => snapshots[f]).ToList());
        var created = new List<Face>();

        guard.Begin();
        foreach (Face face in rewritten.Keys) mesh.RemoveFace(face);
        foreach (Edge e in incident)
            if (mesh.Edges.Contains(e)) mesh.RemoveEdge(e);

        foreach ((Face face, List<Vertex> loop) in rewritten)
        {
            Face? made = mesh.AddFace(loop, face);
            if (made == null) continue;
            created.Add(made);
        }

        // Strips along each edge
        int stripCount = 0;
        foreach (Edge e in selected)
        {
            Vertex a = e.Vert1, b = e.Vert2;
            List<Vertex> pa = profiles[(e, a)], pb = profiles[(e, b)];
            (bool f1Forward, Face look) = stripInfo[e];
            for (int j = 0; j + 1 < pa.Count; j++)
            {
                List<Vertex> quad = f1Forward ? [pb[j], pa[j], pa[j + 1], pb[j + 1]] : [pa[j], pb[j], pb[j + 1], pa[j + 1]];
                Face? strip = mesh.AddFace(quad, look);
                if (strip == null) continue;
                strip.Mapping = null;
                created.Add(strip);
                Inherit(mesh, strip, stripSources[e]);
                stripCount++;
                if (n > 1)
                {
                    foreach (Edge? column in new[] { mesh.FindEdge(pa[j], pb[j]), mesh.FindEdge(pa[j + 1], pb[j + 1]) })
                        if (column != null) column.IsSoft = true;
                }
            }
        }

        // Corners
        foreach (FanData data in datas.Values)
        {
            List<Vertex> loop = CapLoop(data, profiles, firstFace);
            if (loop.Count < 3) continue;

            Float3 outward = Float3.Zero;
            for (int i = 0; i < data.Fan.N; i++)
                if (data.Fan.After(i) is Face around) outward += around.Normal();

            int before = created.Count;
            MakeCap(mesh, loop, outward, data.Fan.Vertex.Point, rewritten.Keys.First(), n > 1, created);
            InheritFromFan(mesh, created, before, data.Fan, snapshots);
        }

        // Pieces of edges that were cut keep what they were
        foreach (Sector s in datas.Values.SelectMany(d => d.Sectors))
        {
            if (!s.Split || s.U == null) continue;
            var original = originalFlags.FirstOrDefault(o => o.Edge == s.U);
            if (!original.Hidden && !original.Soft) continue;
            if (mesh.FindEdge(s.VA, s.VB) is Edge middle)
            {
                if (original.Hidden) middle.IsHidden = true;
                if (original.Soft) middle.IsSoft = true;
            }
        }

        bool customEdgeAttributes = mesh.EdgeAttributes.Count > 2;
        foreach ((Vertex a, Vertex b, bool hidden, bool soft, Edge _, var attributes) in originalFlags)
        {
            if (!hidden && !soft && !customEdgeAttributes) continue;
            foreach (Vertex ta in Terminals(datas, a))
                foreach (Vertex tb in Terminals(datas, b))
                {
                    if (ta == tb) continue;
                    Edge? piece = mesh.Vertices.Contains(ta) && mesh.Vertices.Contains(tb) ? mesh.FindEdge(ta, tb) : null;
                    if (piece == null || selected.Any(s => s.Vert1 == a && s.Vert2 == b || s.Vert1 == b && s.Vert2 == a)) continue;
                    AttributeTransfer.Apply(attributes, piece.Attributes);
                }
        }

        WeldNew(mesh, firstNew);
        foreach (Face f in created.ToList())
            if (mesh.Faces.Contains(f) && !f.IsPlanar(5e-5f)) EditingCommon.Fold(mesh, f);

        mesh.RemoveUnused();
        mesh.MarkChanged();
        return stripCount;
    }

    // New faces take what the face they grew out of carried, with texture coordinates continuing across it
    // The face attributes come from the first source, and each corner samples whichever source it lies closest to the plane of
    private static void Inherit(GeometryData mesh, Face target, IReadOnlyList<FaceSnapshot> sources)
    {
        if (mesh.LoopAttributes.Count == 0) return;
        FaceSnapshot.ApplyLoops(target, v =>
        {
            FaceSnapshot best = sources[0];
            float bestDistance = best.PlaneDistance(v.Point);
            for (int i = 1; i < sources.Count; i++)
            {
                float distance = sources[i].PlaneDistance(v.Point);
                if (distance < bestDistance - 1e-6f) { best = sources[i]; bestDistance = distance; }
            }
            return best.Sample(mesh, v.Point);
        });
    }

    private static void InheritFromFan(GeometryData mesh, List<Face> created, int from, Fan fan, Dictionary<Face, FaceSnapshot> snapshots)
    {
        var sources = Enumerable.Range(0, fan.N).Select(fan.After).Where(f => f != null && snapshots.ContainsKey(f)).Select(f => snapshots[f!]).ToList();
        if (sources.Count == 0) return;
        for (int i = from; i < created.Count; i++) Inherit(mesh, created[i], sources);
    }

    // A face pulled back by a bevel must stay a simple shape and must not run into anything else on its plane
    private static void ValidateLoop(GeometryData mesh, Face face, List<Vertex> raw)
    {
        const string message = "The bevel width is too large, it reaches other geometry";
        var loop = new List<Vertex>();
        foreach (Vertex v in raw)
            if (loop.Count == 0 || loop[^1] != v) loop.Add(v);
        while (loop.Count > 1 && loop[0] == loop[^1]) loop.RemoveAt(loop.Count - 1);
        if (loop.Count < 3) throw new InvalidOperationException(message);

        var frame = new PlaneFrame(face.Normal(), face.Loop!.Vert.Point);
        List<Float2> flat = loop.Select(v => frame.To2D(v.Point)).ToList();
        if (Polygon2D.SignedArea(flat) <= 1e-8f) throw new InvalidOperationException(message);

        int n = loop.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (Polygon2D.ProperIntersection(flat[i], flat[(i + 1) % n], flat[j], flat[(j + 1) % n], GeometryData.Epsilon, out _, out _))
                    throw new InvalidOperationException(message);
            }

        var own = face.NeighborEdges().ToHashSet();
        for (int i = 0; i < n; i++)
        {
            Vertex a = loop[i], b = loop[(i + 1) % n];
            if (mesh.FindEdge(a, b) is Edge existing && own.Contains(existing)) continue;
            foreach (Edge other in mesh.Edges)
            {
                if (own.Contains(other) || other.ContainsVertex(a) || other.ContainsVertex(b)) continue;
                if (MathF.Abs(frame.Distance(other.Vert1.Point)) > GeometryData.Epsilon * 2f || MathF.Abs(frame.Distance(other.Vert2.Point)) > GeometryData.Epsilon * 2f) continue;
                if (Polygon2D.ProperIntersection(flat[i], flat[(i + 1) % n], frame.To2D(other.Vert1.Point), frame.To2D(other.Vert2.Point), GeometryData.Epsilon, out _, out _))
                    throw new InvalidOperationException(message);
            }
        }
    }

    // New vertices that land on top of existing ones, such as the points of a second bevel, join them
    private static void WeldNew(GeometryData mesh, int firstNew)
    {
        var fresh = mesh.Vertices.Skip(firstNew).ToList();
        if (fresh.Count == 0) return;
        MoveVerticesOp.WeldByDistance(mesh, mesh.Vertices.Where(v => v.Edge != null).ToList(), 3f * GeometryData.Epsilon);
    }


    // Every vertex an old vertex can have become, which is itself when it was not bevelled
    private static IEnumerable<Vertex> Terminals(Dictionary<Vertex, FanData> datas, Vertex v)
    {
        if (!datas.TryGetValue(v, out FanData? data))
        {
            yield return v;
            yield break;
        }
        yield return v;
        foreach (Sector s in data.Sectors)
        {
            yield return s.VA;
            yield return s.VB;
        }
    }

    // How far the faces of a sector turn round the vertex, adding up the angle each has there
    private static float TurnOf(Fan fan, Sector sector)
    {
        Vertex v = fan.Vertex;
        float total = 0f;
        // Only faces with both their edges at the vertex uncut count, the two beside the cut edges are trimmed instead
        foreach (int p in sector.Faces.Skip(1).SkipLast(1))
        {
            Face face = fan.After(p)!;
            var loop = face.GetLoop(v)!;
            Float3 toNext = Float3.Normalize(loop.Next!.Vert.Point - v.Point), toPrev = Float3.Normalize(loop.Prev!.Vert.Point - v.Point);
            Float3 normal = Float3.Normalize(face.Normal());
            float angle = MathF.Atan2(Float3.Dot(Float3.Cross(toNext, toPrev), normal), Float3.Dot(toNext, toPrev));
            if (angle < 0f) angle += MathF.PI * 2f;
            total += angle;
        }
        return total;
    }

    private static void BuildSectors(FanData data)
    {
        Fan fan = data.Fan;
        int k = data.Selected.Count, n = fan.N;
        for (int j = 0; j < k; j++)
        {
            int a = data.Selected[j], b = data.Selected[(j + 1) % k];
            var sector = new Sector { A = a, B = b };
            if (fan.Cyclic)
            {
                int count = (b - a + n) % n;
                if (count == 0) count = n;
                for (int t = 0; t < count; t++) sector.Faces.Add((a + t) % n);
                for (int t = 1; t < count; t++) sector.Unselected.Add((a + t) % n);
            }
            else if (j < k - 1)
            {
                for (int p = a; p < b; p++) sector.Faces.Add(p);
                for (int p = a + 1; p < b; p++) sector.Unselected.Add(p);
            }
            else
            {
                sector.Gap = true;
                for (int p = a; p <= n - 2; p++) sector.Faces.Add(p);
                for (int p = 0; p < data.Selected[0]; p++) sector.Faces.Add(p);
                for (int p = a + 1; p <= n - 1; p++) sector.Unselected.Add(p);
                for (int p = 0; p < data.Selected[0]; p++) sector.Unselected.Add(p);
            }
            data.Sectors.Add(sector);
        }
    }

    private static void PlaceSectors(GeometryData mesh, FanData data, float w)
    {
        Fan fan = data.Fan;
        Vertex v = fan.Vertex;
        foreach (Sector sector in data.Sectors)
        {
            Edge ea = fan.Order[sector.A], eb = fan.Order[sector.B];
            Face firstFace = fan.After(sector.Faces[0])!, lastFace = fan.After(sector.Faces[^1])!;
            Float3 inA = Inward(v, firstFace, ea), inB = Inward(v, lastFace, eb);

            bool flat = sector.M >= 1 && sector.Unselected.All(p => fan.Order[p].IsHidden)
                && sector.Faces.All(p => firstFace.IsCoplanarWith(fan.After(p)!));
            if (sector.M == 0 || flat)
            {
                sector.Flat = flat;
                float c = Float3.Dot(inA, inB);
                if (c < -0.95f) throw new InvalidOperationException("Two beveled edges meet in a straight fold, bevel cannot join them");
                Vertex miter = mesh.AddVertex(v.Point + (inA + inB) * (w / (1f + c)));
                sector.VA = sector.VB = miter;
                continue;
            }

            Edge u1 = fan.Order[sector.Unselected[0]], um = fan.Order[sector.Unselected[^1]];
            Float3 d1 = DirAlong(v, u1), dm = DirAlong(v, um);
            float cosA = Float3.Dot(d1, inA), cosB = Float3.Dot(dm, inB);
            if (cosA < MinAngle || cosB < MinAngle)
                throw new InvalidOperationException("A beveled edge meets another edge at too sharp or too reflex an angle for this width");

            sector.Reflex = TurnOf(fan, sector) > MathF.PI + 0.02f;
            float sa = w / cosA, sb = w / cosB;
            if (sector.M == 1)
            {
                if (MathF.Abs(sa - sb) <= 1e-4f * MathF.Max(sa, 1e-3f))
                {
                    sector.VA = sector.VB = mesh.AddVertex(v.Point + d1 * ((sa + sb) * 0.5f));
                }
                else
                {
                    sector.VA = mesh.AddVertex(v.Point + d1 * sa);
                    sector.VB = mesh.AddVertex(v.Point + d1 * sb);
                    sector.U = u1;
                    sector.Sa = sa;
                    sector.Sb = sb;
                }
            }
            else
            {
                sector.VA = mesh.AddVertex(v.Point + d1 * sa);
                sector.VB = mesh.AddVertex(v.Point + dm * sb);
                int firstRun = fan.N - 1 - sector.A;
                for (int j = 0; j < sector.M && !sector.Hub; j++)
                {
                    bool towardA = j == 0 || (j != sector.M - 1 && (sector.Gap ? j < firstRun : j < sector.M / 2.0));
                    sector.Anchor.Add(towardA ? sector.VA : sector.VB);
                }
            }
        }
    }

    private static void CheckWidths(Dictionary<Vertex, FanData> datas, HashSet<Edge> selected)
    {
        var used = new Dictionary<Edge, float>();
        foreach (FanData data in datas.Values)
        {
            Fan fan = data.Fan;
            Vertex v = fan.Vertex;
            foreach (Sector s in data.Sectors)
            {
                if (s.M == 0 || s.Flat) continue;
                foreach ((int pos, Vertex t) in new[] { (s.Unselected[0], s.VA), (s.Unselected[^1], s.VB) })
                {
                    Edge u = fan.Order[pos];
                    float length = Float3.Length(t.Point - v.Point);
                    if (s.Split) length = MathF.Max(length, Float3.Length(s.VB.Point - v.Point));
                    used[u] = used.TryGetValue(u, out float before) ? before + length : length;
                    if (s.M == 1) break;
                }
            }
        }

        foreach ((Edge edge, float length) in used)
        {
            float full = Float3.Length(edge.Vert1.Point - edge.Vert2.Point);
            if (length >= full * 0.999f) throw new InvalidOperationException("The bevel width is too large for the edges around it");
        }

        foreach (Edge e in selected)
        {
            Vertex a = e.Vert1, b = e.Vert2;
            Float3 dir = Float3.Normalize(b.Point - a.Point);
            float full = Float3.Length(b.Point - a.Point);
            foreach (Face face in e.NeighborFaces())
            {
                float along = 0f;
                foreach ((Vertex end, Vertex other, Float3 d) in new[] { (a, b, dir), (b, a, -dir) })
                {
                    FanData data = datas[end];
                    int pos = data.Fan.Order.IndexOf(e);
                    Sector forward = data.Sectors.First(s => s.A == pos), backward = data.Sectors.First(s => s.B == pos);
                    Vertex side = data.Fan.After(pos) == face ? forward.VA : backward.VB;
                    along += Float3.Dot(side.Point - end.Point, d);
                }
                if (along >= full * 0.999f) throw new InvalidOperationException("The bevel width is too large for the edge");
            }
        }
    }

    private static List<Vertex> BuildProfile(GeometryData mesh, Vertex apex, Vertex first, Vertex last, int n, float profile)
    {
        var result = new List<Vertex> { first };
        if (first == last) return result;
        if (n > 1)
        {
            Float3 p0 = first.Point, p2 = last.Point, c = apex.Point;
            Float3 u0 = Float3.Normalize(p0 - c), u2 = Float3.Normalize(p2 - c);
            float gamma = MathF.Acos(Math.Clamp(Float3.Dot(u0, u2), -1f, 1f));
            float weight = MathF.Max(0f, MathF.Sin(gamma * 0.5f) * 2f * profile);

            Float3 Curve(float t)
            {
                float a = (1f - t) * (1f - t), b = 2f * (1f - t) * t * weight, d = t * t;
                return (p0 * a + c * b + p2 * d) / (a + b + d);
            }

            // Spread the points evenly along the curve rather than evenly in its parameter
            const int samples = 48;
            var ts = new float[samples + 1];
            var lengths = new float[samples + 1];
            Float3 previous = p0;
            for (int i = 1; i <= samples; i++)
            {
                Float3 p = Curve(i / (float)samples);
                lengths[i] = lengths[i - 1] + Float3.Length(p - previous);
                ts[i] = i / (float)samples;
                previous = p;
            }
            for (int j = 1; j < n; j++)
            {
                float target = lengths[samples] * j / n;
                int k = 1;
                while (k < samples && lengths[k] < target) k++;
                float span = lengths[k] - lengths[k - 1];
                float t = ts[k - 1] + (span > 1e-9f ? (target - lengths[k - 1]) / span : 0f) / samples;
                result.Add(mesh.AddVertex(Curve(t)));
            }
        }
        result.Add(last);
        return result;
    }

    private static List<Vertex> CornerSequence(FanData data, Face face)
    {
        Fan fan = data.Fan;
        Vertex v = fan.Vertex;
        int i = Enumerable.Range(0, fan.N).First(p => fan.After(p) == face);
        int next = (i + 1) % fan.N;
        bool selA = data.Selected.Contains(i), selB = data.Selected.Contains(next);

        var sequence = new List<Vertex>();
        if (selA && selB)
        {
            sequence.Add(data.Sectors.First(s => s.A == i).VA);
        }
        else if (selA)
        {
            Sector s = data.Sectors.First(x => x.A == i);
            sequence.Add(s.VA);
            if (s.Split && s.Sb > s.Sa) sequence.Add(s.VB);
        }
        else if (selB)
        {
            Sector s = data.Sectors.First(x => x.B == next);
            if (s.Split && s.Sa > s.Sb) sequence.Add(s.VA);
            sequence.Add(s.VB);
        }
        else
        {
            Sector sector = data.Sectors.First(s => s.Faces.Contains(i));
            if (sector.Flat)
            {
                sequence.Add(sector.VA);
            }
            else if (sector.Hub)
            {
                int index = sector.Unselected.IndexOf(i);
                if (index == 0) sequence.Add(sector.VA);
                sequence.Add(v);
                if (index + 1 == sector.M - 1) sequence.Add(sector.VB);
            }
            else
            {
                int index = sector.Unselected.IndexOf(i);
                Vertex left = sector.Anchor[index], right = sector.Anchor[index + 1];
                sequence.Add(left);
                if (right != left) sequence.Add(right);
            }
        }

        // The sequence runs from the earlier edge to the later one, the face may go the other way round
        var loop = face.GetLoop(v)!;
        bool forward = loop.Prev!.Edge == fan.Order[i];
        if (!forward) sequence.Reverse();
        return sequence;
    }

    private static List<Vertex> Oriented(FanData data, Edge e, Dictionary<(Edge, Vertex), List<Vertex>> profiles, Dictionary<Edge, Face> firstFace)
    {
        int pos = data.Fan.Order.IndexOf(e);
        List<Vertex> poly = profiles[(e, data.Fan.Vertex)];
        bool f1Forward = data.Fan.After(pos) == firstFace[e];
        return f1Forward ? Enumerable.Reverse(poly).ToList() : poly.ToList();
    }

    private static List<Vertex> CapLoop(FanData data, Dictionary<(Edge, Vertex), List<Vertex>> profiles, Dictionary<Edge, Face> firstFace)
    {
        Fan fan = data.Fan;
        var loop = new List<Vertex>();
        int k = data.Selected.Count;
        for (int j = 0; j < k; j++)
        {
            int pos = data.Selected[j];
            Edge e = fan.Order[pos];
            Sector forward = data.Sectors.First(s => s.A == pos), backward = data.Sectors.First(s => s.B == pos);
            List<Vertex> poly = profiles[(e, fan.Vertex)];

            // Run it from the backward side to the forward side
            bool f1Forward = fan.After(pos) == firstFace[e];
            IEnumerable<Vertex> ordered = f1Forward ? poly.AsEnumerable().Reverse() : poly;
            loop.AddRange(ordered);
            if (forward.Hub) loop.Add(fan.Vertex);
        }

        var clean = new List<Vertex>();
        foreach (Vertex x in loop)
        {
            if (clean.Count > 0 && clean[^1] == x) continue;
            if (clean.Count > 1 && clean[^2] == x) clean.RemoveAt(clean.Count - 1);
            else clean.Add(x);
        }

        // Paths that go out and come straight back leave nothing to fill
        bool changed = true;
        while (changed && clean.Count > 2)
        {
            changed = false;
            while (clean.Count > 1 && clean[0] == clean[^1]) { clean.RemoveAt(clean.Count - 1); changed = true; }
            for (int i = 0; i < clean.Count && clean.Count > 2; i++)
            {
                if (clean[(i + clean.Count - 1) % clean.Count] != clean[(i + 1) % clean.Count]) continue;
                clean.RemoveAt(i);
                changed = true;
                break;
            }
        }
        return clean;
    }

    // The cap stands across the faces around it, so their texture mapping does not carry over
    private static void MakeCap(GeometryData mesh, List<Vertex> loop, Float3 outward, Float3 origin, Face look, bool smooth, List<Face> created)
    {
        Float3 normal = Float3.Zero;
        for (int i = 0; i < loop.Count; i++) normal += Float3.Cross(loop[i].Point, loop[(i + 1) % loop.Count].Point);
        if (Float3.Dot(normal, outward) < 0f)
        {
            loop.Reverse();
            normal = -normal;
        }

        bool flat = loop.Count <= 3 || (!smooth && loop.All(v => MathF.Abs(Float3.Dot(Float3.Normalize(normal), v.Point - loop[0].Point)) < 1e-3f));
        if (flat)
        {
            Face? cap = mesh.AddFace(loop, look);
            if (cap == null) return;
            cap.Mapping = null;
            created.Add(cap);
            return;
        }

        var frame = new PlaneFrame(normal, loop[0].Point);
        List<Float2> flatLoop = loop.Select(v => frame.To2D(v.Point)).ToList();
        bool convex = true;
        for (int i = 0; i < flatLoop.Count && convex; i++)
            convex = Polygon2D.Cross(flatLoop[i], flatLoop[(i + 1) % flatLoop.Count], flatLoop[(i + 2) % flatLoop.Count]) >= -1e-9f;

        var triangles = new List<List<Vertex>>();
        Vertex? center = null;
        if (convex)
        {
            Float3 average = Float3.Zero;
            foreach (Vertex x in loop) average += x.Point;
            average /= loop.Count;
            center = mesh.AddVertex(average + (origin - average) * 0.3f);
            for (int i = 0; i < loop.Count; i++) triangles.Add([center, loop[i], loop[(i + 1) % loop.Count]]);
        }
        else
        {
            List<int> ears = Polygon2D.Triangulate(flatLoop);
            for (int t = 0; t < ears.Count; t += 3) triangles.Add([loop[ears[t]], loop[ears[t + 1]], loop[ears[t + 2]]]);
        }

        foreach (List<Vertex> triangle in triangles)
        {
            if (mesh.AddFace(triangle, look) is not Face piece) continue;
            piece.Mapping = null;
            created.Add(piece);
        }

        // The seams between the pieces are only there to build the surface
        foreach (List<Vertex> triangle in triangles)
            for (int i = 0; i < 3; i++)
            {
                Vertex a = triangle[i], b = triangle[(i + 1) % 3];
                bool outline = loop.Contains(a) && loop.Contains(b) && Math.Abs(loop.IndexOf(a) - loop.IndexOf(b)) is 1 || (loop.Contains(a) && loop.Contains(b) && Math.Abs(loop.IndexOf(a) - loop.IndexOf(b)) == loop.Count - 1);
                if (!outline && mesh.FindEdge(a, b) is Edge seam) seam.IsSoft = true;
            }
    }
    /// <summary>
    /// Cuts corners off, replacing each vertex with a face. The width is a distance along each edge, or with
    /// <paramref name="relative"/> a fraction of each edge's length. Returns how many corners were cut. On
    /// failure it throws and leaves the mesh as it was.
    /// </summary>
    internal static int Vertices(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, float width, bool relative = false) =>
        Guarded(mesh, guard => BevelVertices(mesh, vertices, width, relative, guard));

    private static int BevelVertices(GeometryData mesh, IReadOnlyCollection<Vertex> vertices, float width, bool relative, Guard guard)
    {
        if (width <= 1e-6f) throw new InvalidOperationException("The bevel width must be above zero");
        int firstNew = mesh.Vertices.Count;
        var targets = vertices.Where(mesh.Vertices.Contains).Where(v => v.NeighborEdges().Any(e => e.Loop != null)).ToList();
        if (targets.Count == 0) throw new InvalidOperationException("No vertices to bevel");

        var fans = targets.ToDictionary(v => v, BuildFan);
        var cuts = new Dictionary<(Vertex, Edge), Vertex>();
        foreach ((Vertex v, Fan fan) in fans)
            foreach (Edge e in fan.Order)
            {
                float length = Float3.Length(e.OtherVertex(v).Point - v.Point);
                float cut = relative ? width * length : width;
                if (cut >= length * 0.999f) throw new InvalidOperationException("The bevel width is too large for the edges around the vertex");
                cuts[(v, e)] = mesh.AddVertex(v.Point + DirAlong(v, e) * cut);
            }

        foreach (Edge e in fans.Values.SelectMany(f => f.Order).Distinct())
        {
            foreach (Vertex end in new[] { e.Vert1, e.Vert2 })
            {
                Vertex other = e.OtherVertex(end);
                if (!fans.ContainsKey(end) || !fans.ContainsKey(other)) continue;
                float together = Float3.Length(cuts[(end, e)].Point - end.Point) + Float3.Length(cuts[(other, e)].Point - other.Point);
                if (Float3.Length(cuts[(end, e)].Point - cuts[(other, e)].Point) > 1e-5f && together >= Float3.Length(end.Point - other.Point) * 0.999f)
                    throw new InvalidOperationException("The bevel width is too large for the edges between the vertices");
            }
        }

        var rewritten = new Dictionary<Face, List<Vertex>>();
        foreach ((Vertex v, Fan fan) in fans)
            for (int i = 0; i < fan.N; i++)
                if (fan.After(i) is Face f && !rewritten.ContainsKey(f)) rewritten[f] = [];
        foreach (Face face in rewritten.Keys.ToList())
        {
            var loop = new List<Vertex>();
            foreach (Vertex c in face.NeighborVertices())
            {
                if (!fans.TryGetValue(c, out Fan? fan)) { loop.Add(c); continue; }
                var l = face.GetLoop(c)!;
                Edge incoming = l.Prev!.Edge, outgoing = l.Edge;
                loop.Add(cuts[(c, incoming)]);
                loop.Add(cuts[(c, outgoing)]);
            }
            rewritten[face] = loop;
        }

        foreach ((Face face, List<Vertex> loop) in rewritten) ValidateLoop(mesh, face, loop);

        var snapshots = rewritten.Keys.ToDictionary(f => f, f => FaceSnapshot.Capture(mesh, f));
        var flags = new List<(Vertex A, Vertex B, bool Hidden, bool Soft, Dictionary<string, GeometryData.AttributeValue> Attributes)>();
        var incident = new List<Edge>();
        foreach (Vertex v in fans.Keys)
            foreach (Edge e in v.NeighborEdges())
                if (e.Loop != null && !incident.Contains(e))
                {
                    incident.Add(e);
                    flags.Add((e.Vert1, e.Vert2, e.IsHidden, e.IsSoft, AttributeTransfer.Copy(e.Attributes)));
                }

        guard.Begin();
        foreach (Face face in rewritten.Keys) mesh.RemoveFace(face);
        foreach (Edge e in incident)
            if (mesh.Edges.Contains(e)) mesh.RemoveEdge(e);

        var created = new List<Face>();
        foreach ((Face face, List<Vertex> loop) in rewritten)
        {
            if (mesh.AddFace(loop, face) is not Face made) continue;
            created.Add(made);
        }

        foreach ((Vertex v, Fan fan) in fans)
        {
            var cap = fan.Order.Select(e => cuts[(v, e)]).ToList();
            Float3 outward = Float3.Zero;
            for (int i = 0; i < fan.N; i++)
                if (fan.After(i) is Face around) outward += around.Normal();
            int before = created.Count;
            MakeCap(mesh, cap, outward, v.Point, rewritten.Keys.First(), false, created);
            InheritFromFan(mesh, created, before, fan, snapshots);
        }

        bool customEdgeAttributes = mesh.EdgeAttributes.Count > 2;
        foreach ((Vertex a, Vertex b, bool hidden, bool soft, var attributes) in flags)
        {
            if (!hidden && !soft && !customEdgeAttributes) continue;
            Vertex ta = fans.ContainsKey(a) ? cuts.First(c => c.Key.Item1 == a && c.Key.Item2.ContainsVertex(b)).Value : a;
            Vertex tb = fans.ContainsKey(b) ? cuts.First(c => c.Key.Item1 == b && c.Key.Item2.ContainsVertex(a)).Value : b;
            if (mesh.FindEdge(ta, tb) is Edge piece) AttributeTransfer.Apply(attributes, piece.Attributes);
        }

        WeldNew(mesh, firstNew);
        foreach (Face f in created.ToList())
            if (mesh.Faces.Contains(f) && !f.IsPlanar(5e-5f)) EditingCommon.Fold(mesh, f);

        mesh.RemoveUnused();
        mesh.MarkChanged();
        return targets.Count;
    }
}
