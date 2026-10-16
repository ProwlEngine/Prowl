// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector.Geometry.Operators;

namespace Prowl.Vector.Geometry;

/// <summary>
/// What modelling needs on top of the plain topology. Edges can be hidden, which makes the faces either side of
/// them read as one face: that is how a face gets a hole, a concave outline split into simple pieces, or a bend.
/// Edges can be soft, which only smooths the shading across them. Faces carry a material index, an optional
/// texture mapping, and can be hidden from view. All of these live in the ordinary attribute dictionaries, so
/// any operator that carries attributes carries them too.
/// </summary>
public partial class GeometryData
{
    /// <summary>Distance under which modelling treats points as the same, or as lying on a plane or an edge.</summary>
    public const float Epsilon = 1e-4f;

    /// <summary>Edge and face flag: a hidden edge joins its faces into one, a hidden face is not drawn.</summary>
    public const string HiddenAttribute = "Hidden";

    /// <summary>Edge flag: shading is smooth across the edge.</summary>
    public const string SoftAttribute = "Soft";

    /// <summary>Face index into whatever list of materials the mesh is shown with.</summary>
    public const string MaterialAttribute = "Material";

    /// <summary>Face texture mapping, eight floats: U, Uo, V, Vo.</summary>
    public const string MappingAttribute = "UvMapping";

    /// <summary>Goes up whenever elements are added or removed, or <see cref="MarkChanged"/> is called, so views know to rebuild.</summary>
    public int Version { get; private set; }

    /// <summary>Records a change that adding and removing elements does not, such as moving vertices.</summary>
    public void MarkChanged() => Version++;

    private static bool ReadFlag(Dictionary<string, AttributeValue> attributes, string name) =>
        attributes.TryGetValue(name, out var value) && value is IntAttributeValue i && i.Data.Length > 0 && i.Data[0] != 0;

    private static void WriteFlag(Dictionary<string, AttributeValue> attributes, string name, bool value) =>
        attributes[name] = new IntAttributeValue(value ? 1 : 0);

    public partial class Edge
    {
        /// <summary>A hidden edge joins the faces either side into one face. It is never drawn or picked.</summary>
        public bool IsHidden
        {
            get => ReadFlag(Attributes, HiddenAttribute);
            set => WriteFlag(Attributes, HiddenAttribute, value);
        }

        /// <summary>A soft edge is shaded smoothly across, and drawn lightly.</summary>
        public bool IsSoft
        {
            get => ReadFlag(Attributes, SoftAttribute);
            set => WriteFlag(Attributes, SoftAttribute, value);
        }

        /// <summary>Whether the edge is drawn and can be picked: not hidden, and not only on hidden faces.</summary>
        public bool IsShown
        {
            get
            {
                if (IsHidden) return false;
                if (Loop == null) return true;
                var it = Loop;
                do
                {
                    if (!it.Face.IsHidden) return true;
                    it = it.RadialNext!;
                } while (it != Loop);
                return false;
            }
        }

        /// <summary>How many faces use the edge.</summary>
        public int FaceCount
        {
            get
            {
                if (Loop == null) return 0;
                int count = 0;
                var it = Loop;
                do { count++; it = it.RadialNext!; } while (it != Loop);
                return count;
            }
        }

        /// <summary>The cosine of the angle between the two faces of the edge, or null when it does not have exactly two.</summary>
        public float? DihedralCosine()
        {
            List<Face> faces = NeighborFaces();
            if (faces.Count != 2) return null;
            Float3 a = faces[0].Normal(), b = faces[1].Normal();
            if (Float3.LengthSquared(a) < 0.5f || Float3.LengthSquared(b) < 0.5f) return null;
            return Float3.Dot(a, b);
        }
    }

    public partial class Face
    {
        /// <summary>Index of the face's material, zero when it has none.</summary>
        public int Material
        {
            get => Attributes.TryGetValue(MaterialAttribute, out var value) && value is IntAttributeValue i && i.Data.Length > 0 ? i.Data[0] : 0;
            set => Attributes[MaterialAttribute] = new IntAttributeValue(value);
        }

        /// <summary>A hidden face stays in the mesh but is not drawn or picked, to get at what is behind it.</summary>
        public bool IsHidden
        {
            get => ReadFlag(Attributes, HiddenAttribute);
            set => WriteFlag(Attributes, HiddenAttribute, value);
        }

        /// <summary>How the texture is laid on the face, or null for the material's default projection.</summary>
        public UvMapping? Mapping
        {
            get
            {
                if (!Attributes.TryGetValue(MappingAttribute, out var value) || value is not FloatAttributeValue f || f.Data.Length < 8) return null;
                float[] d = f.Data;
                return new UvMapping(new Float3(d[0], d[1], d[2]), d[3], new Float3(d[4], d[5], d[6]), d[7]);
            }
            set
            {
                if (value is not UvMapping m)
                {
                    Attributes.Remove(MappingAttribute);
                    return;
                }
                Attributes[MappingAttribute] = new FloatAttributeValue(m.U.X, m.U.Y, m.U.Z, m.Uo, m.V.X, m.V.Y, m.V.Z, m.Vo);
            }
        }

        /// <summary>Whether every corner lies within the tolerance of the plane of the face.</summary>
        public bool IsPlanar(float epsilon = Epsilon)
        {
            Float3 normal = Normal();
            if (Float3.LengthSquared(normal) < 0.5f) return false;
            Float3 origin = Loop!.Vert.Point;
            foreach (Vertex v in NeighborVerticesEnumerable())
                if (MathF.Abs(Float3.Dot(v.Point - origin, normal)) > epsilon) return false;
            return true;
        }

        /// <summary>Whether the other face lies in the same plane facing the same way.</summary>
        public bool IsCoplanarWith(Face other)
        {
            Float3 a = Normal(), b = other.Normal();
            if (Float3.Dot(a, b) < 1f - 1e-5f) return false;
            Float3 origin = Loop!.Vert.Point;
            return other.NeighborVerticesEnumerable().All(v => MathF.Abs(Float3.Dot(v.Point - origin, a)) <= Epsilon);
        }

        /// <summary>This face and every face joined to it through hidden edges, which together read as one face.</summary>
        public List<Face> LogicalFace()
        {
            var result = new List<Face> { this };
            var seen = new HashSet<Face> { this };
            for (int i = 0; i < result.Count; i++)
                foreach (Edge e in result[i].NeighborEdges())
                {
                    if (!e.IsHidden) continue;
                    foreach (Face other in e.NeighborFaces())
                        if (seen.Add(other)) result.Add(other);
                }
            return result;
        }

        /// <summary>The area of the face.</summary>
        public float Area()
        {
            Float3 sum = Float3.Zero;
            var it = Loop;
            if (it == null) return 0f;
            do
            {
                sum += Float3.Cross(it.Vert.Point, it.Next!.Vert.Point);
                it = it.Next;
            } while (it != Loop);
            return Float3.Length(sum) * 0.5f;
        }
    }

    #region Editing

    /// <summary>Removes every element, keeping the attribute definitions.</summary>
    public void Clear()
    {
        Vertices.Clear();
        Edges.Clear();
        Loops.Clear();
        Faces.Clear();
        Version++;
    }

    /// <summary>
    /// Adds an edge with the given flags. When the edge already exists it is returned as it is, except that
    /// adding a visible edge over a hidden one makes it visible. Returns null when both ends are the same vertex.
    /// </summary>
    public Edge? AddEdge(Vertex a, Vertex b, bool hidden, bool soft = false)
    {
        if (a == b) return null;
        Edge? existing = FindEdge(a, b);
        if (existing != null)
        {
            if (!hidden && existing.IsHidden) existing.IsHidden = false;
            return existing;
        }

        Edge edge = AddEdge(a, b);
        edge.IsHidden = hidden;
        edge.IsSoft = soft;
        return edge;
    }

    /// <summary>Adds a face with a material. Consecutive repeated corners are dropped, and fewer than three corners adds nothing.</summary>
    public Face? AddFace(IReadOnlyList<Vertex> vertices, int material)
    {
        Face? face = AddCleanFace(vertices);
        if (face != null) face.Material = material;
        return face;
    }

    /// <summary>
    /// Adds a face that takes everything another face carried: its attributes, and loop attributes sampled
    /// from it at each corner's position. The other face may already have been removed.
    /// </summary>
    public Face? AddFace(IReadOnlyList<Vertex> vertices, Face? like)
    {
        FaceSnapshot? source = like != null && LoopAttributes.Count > 0 ? FaceSnapshot.Capture(this, like) : null;
        Face? face = AddCleanFace(vertices);
        if (face == null || like == null) return face;

        AttributeTransfer.Apply(like.Attributes, face.Attributes);
        source?.ApplyLoopsByPosition(this, face);
        return face;
    }

    private Face? AddCleanFace(IReadOnlyList<Vertex> vertices)
    {
        var clean = new List<Vertex>(vertices.Count);
        foreach (Vertex v in vertices)
            if (clean.Count == 0 || clean[clean.Count - 1] != v) clean.Add(v);
        while (clean.Count > 1 && clean[0] == clean[clean.Count - 1]) clean.RemoveAt(clean.Count - 1);
        return clean.Count < 3 ? null : AddFace(clean.ToArray());
    }

    /// <summary>Replaces a face with a new outline that carries what the face did.</summary>
    public Face? ReplaceFace(Face face, IReadOnlyList<Vertex> vertices)
    {
        RemoveFace(face);
        return AddFace(vertices, face);
    }

    /// <summary>The vertex nearest a point, if one is within the tolerance.</summary>
    public Vertex? FindVertex(Float3 point, float epsilon = Epsilon)
    {
        float best = epsilon * epsilon;
        Vertex? found = null;
        foreach (Vertex v in Vertices)
        {
            float d = Float3.LengthSquared(v.Point - point);
            if (d <= best) { best = d; found = v; }
        }
        return found;
    }

    /// <summary>An edge the point lies on, strictly between its ends.</summary>
    public Edge? FindEdgeThrough(Float3 point, float epsilon = Epsilon)
    {
        foreach (Edge e in Edges)
            if (OnSegmentInterior(point, e.Vert1.Point, e.Vert2.Point, epsilon)) return e;
        return null;
    }

    /// <summary>Whether a point lies on a segment, within the tolerance, and not at either end.</summary>
    public static bool OnSegmentInterior(Float3 p, Float3 a, Float3 b, float epsilon)
    {
        Float3 ab = b - a;
        float lengthSq = Float3.LengthSquared(ab);
        if (lengthSq < epsilon * epsilon) return false;
        float t = Float3.Dot(p - a, ab) / lengthSq;
        float length = MathF.Sqrt(lengthSq);
        if (t * length <= epsilon || (1f - t) * length <= epsilon) return false;
        return Float3.LengthSquared(a + ab * t - p) <= epsilon * epsilon;
    }

    /// <summary>A vertex at the point: an existing one, a new one inserted into an edge it lies on, or a new loose one.</summary>
    public Vertex GetOrCreateVertex(Float3 point)
    {
        Vertex? existing = FindVertex(point);
        if (existing != null) return existing;

        Vertex vertex = AddVertex(point);
        Edge? edge = FindEdgeThrough(point);
        if (edge != null) InsertVertexInEdge(edge, vertex);
        return vertex;
    }

    /// <summary>Splits an edge at a vertex. Every face that used the edge gets the vertex between its ends, and both halves keep the edge's attributes.</summary>
    public void InsertVertexInEdge(Edge edge, Vertex vertex)
    {
        Vertex a = edge.Vert1, b = edge.Vert2;
        if (vertex == a || vertex == b) return;
        var attributes = AttributeTransfer.Copy(edge.Attributes);

        var faces = new List<(List<Vertex> Verts, Face Like)>();
        foreach (Face face in edge.NeighborFaces())
        {
            List<Vertex> verts = face.NeighborVertices();
            var updated = new List<Vertex>(verts.Count + 1);
            for (int i = 0; i < verts.Count; i++)
            {
                updated.Add(verts[i]);
                Vertex next = verts[(i + 1) % verts.Count];
                if ((verts[i] == a && next == b) || (verts[i] == b && next == a))
                    updated.Add(vertex);
            }
            faces.Add((updated, face));
        }

        RemoveEdge(edge);
        AttributeTransfer.Apply(attributes, AddEdge(a, vertex).Attributes);
        AttributeTransfer.Apply(attributes, AddEdge(vertex, b).Attributes);
        foreach ((List<Vertex> verts, Face like) in faces)
            AddFace(verts, like);
    }

    /// <summary>Welds one vertex into another. The faces and edges of the first are rebuilt on the second, and faces that collapse are dropped.</summary>
    public void MergeVertex(Vertex from, Vertex into)
    {
        if (from == into) return;

        var faces = new List<(List<Vertex> Verts, Face Like)>();
        foreach (Face face in from.NeighborFaces())
        {
            List<Vertex> verts = face.NeighborVertices();
            for (int i = 0; i < verts.Count; i++)
                if (verts[i] == from) verts[i] = into;
            faces.Add((verts, face));
        }

        var edges = from.NeighborEdges().Select(e => (Other: e.OtherVertex(from), Attributes: AttributeTransfer.Copy(e.Attributes))).ToList();

        RemoveVertex(from);

        foreach ((Vertex other, var attributes) in edges)
        {
            if (other == into) continue;
            bool existed = FindEdge(into, other) != null;
            Edge edge = AddEdge(into, other);
            if (!existed) AttributeTransfer.Apply(attributes, edge.Attributes);
            else if (!ReadFlag(attributes, HiddenAttribute)) edge.IsHidden = false;
        }

        foreach ((List<Vertex> verts, Face like) in faces)
            if (!Collapses(verts)) AddFace(verts, like);
    }

    private static bool Collapses(List<Vertex> verts)
    {
        var clean = new List<Vertex>(verts.Count);
        foreach (Vertex v in verts)
            if (clean.Count == 0 || clean[clean.Count - 1] != v) clean.Add(v);
        while (clean.Count > 1 && clean[0] == clean[clean.Count - 1]) clean.RemoveAt(clean.Count - 1);
        return clean.Count < 3 || clean.Distinct().Count() != clean.Count;
    }

    /// <summary>Welds each of the vertices onto any other vertex at the same position. Returns the vertices that remain in their place.</summary>
    public List<Vertex> MergeCoincident(IEnumerable<Vertex> vertices)
    {
        var result = new HashSet<Vertex>();
        foreach (Vertex v in vertices.ToList())
        {
            if (!Vertices.Contains(v)) continue;
            Vertex kept = v;
            foreach (Vertex other in Vertices)
            {
                if (other == v || Float3.LengthSquared(other.Point - v.Point) > Epsilon * Epsilon) continue;
                MergeVertex(v, other);
                kept = other;
                break;
            }
            result.Add(kept);
        }
        result.RemoveWhere(v => !Vertices.Contains(v));
        return result.ToList();
    }

    /// <summary>
    /// Removes a vertex that only joins two edges running in a straight line, so the two edges become one.
    /// Returns whether it was removed.
    /// </summary>
    public bool DissolveVertex(Vertex vertex)
    {
        List<Edge> edges = vertex.NeighborEdges();
        if (edges.Count != 2) return false;

        Vertex a = edges[0].OtherVertex(vertex), b = edges[1].OtherVertex(vertex);
        if (a == b || FindEdge(a, b) != null) return false;
        Float3 da = Float3.Normalize(a.Point - vertex.Point), db = Float3.Normalize(b.Point - vertex.Point);
        if (Float3.Dot(da, db) > -0.99999f) return false;
        if (Float3.Length(Float3.Cross(a.Point - vertex.Point, b.Point - vertex.Point)) > Epsilon * Float3.Length(a.Point - b.Point)) return false;

        bool hidden = edges[0].IsHidden && edges[1].IsHidden;
        var attributes = AttributeTransfer.Copy(edges[0].Attributes);
        var faces = new List<(List<Vertex> Verts, Face Like)>();
        foreach (Face face in vertex.NeighborFaces())
        {
            List<Vertex> verts = face.NeighborVertices();
            if (verts.Count(v => v == vertex) != 1) return false;
            verts.Remove(vertex);
            faces.Add((verts, face));
        }

        RemoveVertex(vertex);
        Edge joined = AddEdge(a, b);
        AttributeTransfer.Apply(attributes, joined.Attributes);
        joined.IsHidden = hidden;
        foreach ((List<Vertex> verts, Face like) in faces)
            AddFace(verts, like);
        return true;
    }

    /// <summary>Drops hidden edges no face uses any more, and vertices with no edges.</summary>
    public void RemoveUnused()
    {
        foreach (Edge e in Edges.ToList())
            if (e.Loop == null && e.IsHidden) RemoveEdge(e);
        foreach (Vertex v in Vertices.ToList())
            if (v.Edge == null) RemoveVertex(v);
    }

    #endregion

    #region Regions

    /// <summary>Edges with exactly one of the faces on them, the outline of a region, each with the face it borders.</summary>
    public static List<(Vertex A, Vertex B, Edge Edge, Face Face)> Boundary(IReadOnlyCollection<Face> region)
    {
        var set = region as HashSet<Face> ?? new HashSet<Face>(region);
        var result = new List<(Vertex, Vertex, Edge, Face)>();
        foreach (Face face in set)
        {
            var it = face.Loop!;
            do
            {
                int inside = 0;
                foreach (Face f in it.Edge.NeighborFaces())
                    if (set.Contains(f)) inside++;
                if (inside == 1) result.Add((it.Vert, it.Next!.Vert, it.Edge, face));
                it = it.Next!;
            } while (it != face.Loop);
        }
        return result;
    }

    /// <summary>The outline of a region as closed loops, each in the direction its faces run.</summary>
    public static List<List<Vertex>> Outlines(IReadOnlyCollection<Face> region)
    {
        var next = new Dictionary<Vertex, Vertex>();
        foreach ((Vertex a, Vertex b, _, _) in Boundary(region))
            if (!next.ContainsKey(a)) next[a] = b;

        var loops = new List<List<Vertex>>();
        var used = new HashSet<Vertex>();
        foreach (Vertex start in next.Keys)
        {
            if (used.Contains(start)) continue;
            var loop = new List<Vertex>();
            Vertex v = start;
            while (used.Add(v))
            {
                loop.Add(v);
                if (!next.TryGetValue(v, out Vertex? n)) break;
                v = n;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>
    /// The outer outline of a flat region moved inwards by a distance, or outwards when negative.
    /// Null when the result would cross itself or collapse.
    /// </summary>
    public static List<Float3>? OffsetOutline(IReadOnlyCollection<Face> region, float distance)
    {
        Float3 normal = region.First().Normal();
        List<List<Vertex>> loops = Outlines(region);
        if (loops.Count == 0) return null;

        var frame = new PlaneFrame(normal, loops[0][0].Point);
        List<Float2> outer = loops.Select(l => l.Select(v => frame.To2D(v.Point)).ToList())
            .OrderByDescending(p => MathF.Abs(Polygon2D.SignedArea(p))).First();
        if (Polygon2D.SignedArea(outer) < 0f) outer.Reverse();

        int n = outer.Count;
        var result = new List<Float2>(n);
        for (int i = 0; i < n; i++)
        {
            Float2 prev = outer[(i - 1 + n) % n], cur = outer[i], next = outer[(i + 1) % n];
            Float2 d1 = Normalize(cur - prev), d2 = Normalize(next - cur);
            Float2 n1 = new(-d1.Y, d1.X), n2 = new(-d2.Y, d2.X);
            Float2 miter = Normalize(n1 + n2);
            float along = miter.X * n1.X + miter.Y * n1.Y;
            if (along < 0.1f) return null;
            result.Add(cur + miter * (distance / along));
        }

        if (Polygon2D.SignedArea(result) <= 1e-6f) return null;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (Polygon2D.ProperIntersection(result[i], result[(i + 1) % n], result[j], result[(j + 1) % n], 0f, out _, out _)) return null;
            }

        return result.Select(frame.To3D).ToList();
    }

    private static Float2 Normalize(Float2 v)
    {
        float length = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return length > 1e-12f ? v / length : Float2.Zero;
    }

    /// <summary>The average position of some vertices.</summary>
    public static Float3 Centroid(IEnumerable<Vertex> vertices)
    {
        Float3 sum = Float3.Zero;
        int count = 0;
        foreach (Vertex v in vertices)
        {
            sum += v.Point;
            count++;
        }
        return count > 0 ? sum / count : Float3.Zero;
    }

    #endregion

    #region Measurements

    /// <summary>The volume enclosed by the faces, positive when they face outwards. Only meaningful for a closed mesh.</summary>
    public float Volume()
    {
        double volume = 0.0;
        foreach (Face f in Faces)
        {
            List<Float3> p = f.NeighborVertices().Select(v => v.Point).ToList();
            if (p.Count < 3) continue;
            Float3 normal = f.Normal();
            if (Float3.LengthSquared(normal) < 0.5f) continue;
            var frame = new PlaneFrame(normal, p[0]);
            List<int> tris = Polygon2D.Triangulate(p.Select(frame.To2D).ToList());
            for (int t = 0; t < tris.Count; t += 3)
                volume += Float3.Dot(p[tris[t]], Float3.Cross(p[tris[t + 1]], p[tris[t + 2]])) / 6.0;
        }
        return (float)volume;
    }

    /// <summary>The total area of every face.</summary>
    public float SurfaceArea() => Faces.Sum(f => f.Area());

    /// <summary>Whether every edge has exactly two faces, so the surface has no holes and nothing pokes out.</summary>
    public bool IsClosed() => Faces.Count > 0 && Edges.All(e => e.FaceCount == 2);

    /// <summary>Counts visible edges with one face, with more than two, and with none.</summary>
    public (int Open, int NonManifold, int Loose) EdgeCounts()
    {
        int open = 0, bad = 0, loose = 0;
        foreach (Edge e in Edges)
        {
            if (e.IsHidden) continue;
            int n = e.FaceCount;
            if (n == 0) loose++;
            else if (n == 1) open++;
            else if (n > 2) bad++;
        }
        return (open, bad, loose);
    }

    /// <summary>
    /// Looks for damage editing can leave behind: degenerate, bent or overlapping faces, corners used twice,
    /// stray hidden edges, references to removed elements, and vertices on top of each other. Returns a
    /// description of the first problem found, or null when there is none.
    /// </summary>
    public string? FindDefect()
    {
        foreach (Face f in Faces)
        {
            List<Vertex> verts = f.NeighborVertices();
            if (verts.Count < 3) return $"face with {verts.Count} vertices";
            if (verts.Distinct().Count() != verts.Count) return $"face repeats a vertex at {verts[0].Point}";
            if (!f.IsPlanar(2e-4f)) return $"non planar face at {verts[0].Point}";
            if (f.Area() < 5e-7f) return $"zero area face at {verts[0].Point}";
            foreach (Edge e in f.NeighborEdges())
                if (!Edges.Contains(e)) return "face uses an edge that is no longer in the mesh";
            foreach (Vertex v in verts)
                if (!Vertices.Contains(v)) return "face uses a vertex that is no longer in the mesh";
        }

        foreach (Edge e in Edges)
        {
            if (Float3.Length(e.Vert1.Point - e.Vert2.Point) < Epsilon * 0.5f) return $"zero length edge at {e.Vert1.Point}";
            if (e.IsHidden && e.FaceCount == 0) return $"orphan hidden edge at {e.Vert1.Point}";
        }

        for (int i = 0; i < Vertices.Count; i++)
            for (int j = i + 1; j < Vertices.Count; j++)
                if (Float3.Length(Vertices[i].Point - Vertices[j].Point) < Epsilon * 0.5f)
                    return $"two vertices at {Vertices[i].Point}";

        // Faces overlapping on the same plane facing the same way are doubled up
        for (int i = 0; i < Faces.Count; i++)
            for (int j = i + 1; j < Faces.Count; j++)
            {
                Face a = Faces[i], b = Faces[j];
                if (!a.IsCoplanarWith(b)) continue;
                var frame = new PlaneFrame(a.Normal(), a.Loop!.Vert.Point);
                List<Float2> pa = a.NeighborVertices().Select(v => frame.To2D(v.Point)).ToList();
                List<Float2> pb = b.NeighborVertices().Select(v => frame.To2D(v.Point)).ToList();
                if (Overlap(pa, pb)) return $"overlapping faces near {a.Loop!.Vert.Point}";
            }
        return null;
    }

    private static bool Overlap(List<Float2> a, List<Float2> b)
    {
        List<int> tris = Polygon2D.Triangulate(a);
        List<int> trisB = Polygon2D.Triangulate(b);
        for (int i = 0; i < tris.Count; i += 3)
        {
            Float2 p0 = a[tris[i]], p1 = a[tris[i + 1]], p2 = a[tris[i + 2]];
            Float2[] samples = [(p0 + p1 + p2) / 3f, p0 * 0.6f + p1 * 0.2f + p2 * 0.2f, p0 * 0.2f + p1 * 0.6f + p2 * 0.2f, p0 * 0.2f + p1 * 0.2f + p2 * 0.6f];
            foreach (Float2 p in samples)
                for (int j = 0; j < trisB.Count; j += 3)
                    if (StrictlyInside(p, b[trisB[j]], b[trisB[j + 1]], b[trisB[j + 2]])) return true;
        }
        return false;
    }

    private static bool StrictlyInside(Float2 p, Float2 a, Float2 b, Float2 c)
    {
        const float e = 1e-5f;
        return Polygon2D.Cross(a, b, p) > e && Polygon2D.Cross(b, c, p) > e && Polygon2D.Cross(c, a, p) > e;
    }

    #endregion

    #region Snapshots

    /// <summary>A copy of the whole mesh, every attribute included, to come back to with <see cref="Restore"/>.</summary>
    public GeometrySnapshot Capture() => GeometrySnapshot.From(this);

    /// <summary>Puts the mesh back the way it was when the snapshot was taken. The elements are new objects.</summary>
    public void Restore(GeometrySnapshot snapshot)
    {
        Vertices.Clear();
        Edges.Clear();
        Loops.Clear();
        Faces.Clear();
        ReplaceDefinitions(VertexAttributes, snapshot.VertexDefinitions);
        ReplaceDefinitions(EdgeAttributes, snapshot.EdgeDefinitions);
        ReplaceDefinitions(LoopAttributes, snapshot.LoopDefinitions);
        ReplaceDefinitions(FaceAttributes, snapshot.FaceDefinitions);

        var verts = new Vertex[snapshot.Points.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = new Vertex(snapshot.Points[i]) { Attributes = AttributeTransfer.Copy(snapshot.VertexData[i]) };
            Vertices.Add(verts[i]);
        }
        foreach (var (a, b, attributes) in snapshot.EdgeData)
            AddEdge(verts[a], verts[b]).Attributes = AttributeTransfer.Copy(attributes);
        foreach (var (indices, attributes, loops) in snapshot.FaceData)
        {
            Face? face = AddFace(indices.Select(i => verts[i]).ToArray(), fillAttributes: false);
            if (face == null) continue;
            face.Attributes = AttributeTransfer.Copy(attributes);
            var loop = face.Loop!;
            for (int i = 0; i < loops.Length; i++)
            {
                loop.Attributes = AttributeTransfer.Copy(loops[i]);
                loop = loop.Next!;
            }
        }
        Version++;
    }

    private static void ReplaceDefinitions(List<AttributeDefinition> target, AttributeDefinition[] source)
    {
        target.Clear();
        target.AddRange(source);
    }

    #endregion
}

/// <summary>A copy of a <see cref="GeometryData"/> at a moment, for undo.</summary>
public sealed class GeometrySnapshot
{
    internal GeometryData.AttributeDefinition[] VertexDefinitions = [], EdgeDefinitions = [], LoopDefinitions = [], FaceDefinitions = [];
    internal Float3[] Points = [];
    internal Dictionary<string, GeometryData.AttributeValue>[] VertexData = [];
    internal (int A, int B, Dictionary<string, GeometryData.AttributeValue> Attributes)[] EdgeData = [];
    internal (int[] Indices, Dictionary<string, GeometryData.AttributeValue> Attributes, Dictionary<string, GeometryData.AttributeValue>[] Loops)[] FaceData = [];

    /// <summary>How many vertices the mesh had.</summary>
    public int VertexCount => Points.Length;

    /// <summary>How many faces the mesh had.</summary>
    public int FaceCount => FaceData.Length;

    internal static GeometrySnapshot From(GeometryData data)
    {
        var index = new Dictionary<GeometryData.Vertex, int>(data.Vertices.Count);
        for (int i = 0; i < data.Vertices.Count; i++) index[data.Vertices[i]] = i;

        return new GeometrySnapshot
        {
            VertexDefinitions = data.VertexAttributes.ToArray(),
            EdgeDefinitions = data.EdgeAttributes.ToArray(),
            LoopDefinitions = data.LoopAttributes.ToArray(),
            FaceDefinitions = data.FaceAttributes.ToArray(),
            Points = data.Vertices.Select(v => v.Point).ToArray(),
            VertexData = data.Vertices.Select(v => AttributeTransfer.Copy(v.Attributes)).ToArray(),
            EdgeData = data.Edges.Select(e => (index[e.Vert1], index[e.Vert2], AttributeTransfer.Copy(e.Attributes))).ToArray(),
            FaceData = data.Faces.Select(f =>
            {
                var indices = new List<int>();
                var loops = new List<Dictionary<string, GeometryData.AttributeValue>>();
                var loop = f.Loop!;
                do
                {
                    indices.Add(index[loop.Vert]);
                    loops.Add(AttributeTransfer.Copy(loop.Attributes));
                    loop = loop.Next!;
                } while (loop != f.Loop);
                return (indices.ToArray(), AttributeTransfer.Copy(f.Attributes), loops.ToArray());
            }).ToArray(),
        };
    }
}
