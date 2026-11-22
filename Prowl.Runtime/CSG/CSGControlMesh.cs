// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>
/// A brush's shape as points and polygons: any closed solid, concave or with bent polygons. Each polygon is wound so its
/// normal points out. Planes are only derived from it, see <see cref="ToPlanes"/>.
/// </summary>
public sealed class CSGControlMesh
{
    [SerializeField] private List<Float3> points = new();

    /// <summary>Each polygon as indices into <see cref="Points"/>, wound so its normal points out.</summary>
    [SerializeField] private List<int[]> polygons = new();

    public IReadOnlyList<Float3> Points => points;
    public IReadOnlyList<int[]> Polygons => polygons;

    public CSGControlMesh() { }

    private CSGControlMesh(List<Float3> pts, List<int[]> polys)
    {
        points = pts;
        polygons = polys;
    }

    /// <summary>The convex solid a plane set encloses, as points and polygons, or null when it encloses nothing.</summary>
    public static CSGControlMesh? FromPlanes(IReadOnlyList<Plane> planes, float weldTolerance = 1e-4f)
        => FromPlanes(planes, out _, weldTolerance);

    /// <summary>As the other overload, also saying which plane each polygon lies on. Planes that make no polygon are left out.</summary>
    public static CSGControlMesh? FromPlanes(IReadOnlyList<Plane> planes, out int[] planeOfPolygon, float weldTolerance = 1e-4f)
    {
        planeOfPolygon = [];
        ConvexSolidResult result = GeometryGenerator.ConvexFromPlanes(planes, weldTolerance);
        if (!result.IsValid) return null;

        CSGControlMesh? mesh = FromMesh(result.Solid!);
        if (mesh == null) return null;

        planeOfPolygon = new int[mesh.polygons.Count];
        for (int i = 0; i < planeOfPolygon.Length; i++)
        {
            Float3 normal = mesh.PolygonNormal(i), centre = mesh.PolygonCentre(i);
            float best = float.MaxValue;
            for (int plane = 0; plane < planes.Count; plane++)
            {
                float score = (1f - Float3.Dot(normal, planes[plane].Normal)) + MathF.Abs(planes[plane].GetSignedDistanceToPoint(centre));
                if (score >= best) continue;
                best = score;
                planeOfPolygon[i] = plane;
            }
        }
        return mesh;
    }

    /// <summary>A closed mesh's points and faces, in face order, or null when it has too few faces to enclose anything.</summary>
    public static CSGControlMesh? FromMesh(GeometryData solid)
    {
        var pts = new List<Float3>(solid.Vertices.Count);
        var index = new Dictionary<GeometryData.Vertex, int>(solid.Vertices.Count);
        foreach (GeometryData.Vertex v in solid.Vertices)
        {
            index[v] = pts.Count;
            pts.Add(v.Point);
        }

        var polys = new List<int[]>(solid.Faces.Count);
        foreach (GeometryData.Face face in solid.Faces)
        {
            var ring = face.NeighborVertices();
            if (ring.Count < 3) continue;

            var indices = new int[ring.Count];
            for (int i = 0; i < ring.Count; i++) indices[i] = index[ring[i]];
            polys.Add(indices);
        }

        return polys.Count >= 4 ? new CSGControlMesh(pts, polys) : null;
    }

    /// <summary>Points and polygons as given, each polygon indices into the points, wound to face out.</summary>
    public static CSGControlMesh FromPolygons(List<Float3> points, List<int[]> polygons) => new(points, polygons);

    /// <summary>
    /// A ring of points swept along an offset into a closed prism, the ring any shape. Null when it encloses nothing.
    /// </summary>
    public static CSGControlMesh? Prism(IReadOnlyList<Float3> ring, Float3 offset)
    {
        var corners = new List<Float3>(ring.Count);
        foreach (Float3 p in ring)
            if (corners.Count == 0 || Float3.DistanceSquared(corners[^1], p) > 1e-10f) corners.Add(p);
        if (corners.Count > 1 && Float3.DistanceSquared(corners[0], corners[^1]) <= 1e-10f) corners.RemoveAt(corners.Count - 1);
        if (corners.Count < 3 || Float3.LengthSquared(offset) < 1e-8f) return null;

        int n = corners.Count;
        var points = new List<Float3>(n * 2);
        points.AddRange(corners);
        foreach (Float3 p in corners) points.Add(p + offset);

        var polygons = new List<int[]>(n + 2);
        var start = new int[n];
        var end = new int[n];
        for (int i = 0; i < n; i++)
        {
            start[i] = n - 1 - i;
            end[i] = n + i;
            polygons.Add([i, (i + 1) % n, n + (i + 1) % n, n + i]);
        }
        polygons.Add(start);
        polygons.Add(end);

        // Wound whichever way the ring ran and the offset went, then turned to face out
        var prism = new CSGControlMesh(points, polygons);
        if (prism.Volume() < 0f) prism.ReverseWinding();
        return prism.IsValid() ? prism : null;
    }

    /// <summary>
    /// The nearest polygon a ray enters through, any shape of polygon, and how far along the ray. With
    /// <paramref name="bothSides"/> it is the nearest polygon crossed either way, so a ray from inside finds the one it leaves through.
    /// </summary>
    public bool Raycast(Float3 origin, Float3 direction, out float distance, out int polygon, bool bothSides = false)
    {
        distance = float.MaxValue;
        polygon = -1;

        for (int p = 0; p < polygons.Count; p++)
        {
            if (!FitPolygon(p, out Plane plane)) continue;
            float denom = Float3.Dot(plane.Normal, direction);
            if (bothSides ? MathF.Abs(denom) < 1e-9f : denom > -1e-9f) continue;

            float t = (plane.D - Float3.Dot(plane.Normal, origin)) / denom;
            if (t < 0f || t >= distance || !Inside(polygons[p], plane.Normal, origin + direction * t)) continue;

            distance = t;
            polygon = p;
        }
        return polygon >= 0;
    }

    /// <summary>Whether a point on a polygon's plane lies inside it, by counting crossings in the plane.</summary>
    private bool Inside(int[] ring, Float3 normal, Float3 point)
    {
        Float3 helper = MathF.Abs(normal.Y) < 0.9f ? Float3.UnitY : Float3.UnitX;
        Float3 u = Float3.Normalize(Float3.Cross(helper, normal)), v = Float3.Cross(normal, u);
        float px = Float3.Dot(point, u), py = Float3.Dot(point, v);

        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            Float3 a = points[ring[i]], b = points[ring[j]];
            float ax = Float3.Dot(a, u), ay = Float3.Dot(a, v), bx = Float3.Dot(b, u), by = Float3.Dot(b, v);
            if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax) inside = !inside;
        }
        return inside;
    }

    /// <summary>Whether the solid is convex: every polygon flat and every point behind every polygon's plane.</summary>
    public bool IsConvex(float tolerance = 1e-4f)
    {
        if (!IsPlanar(tolerance)) return false;
        foreach (Plane plane in ToPlanes())
            foreach (Float3 p in points)
                if (plane.GetSignedDistanceToPoint(p) > tolerance) return false;
        return true;
    }

    public CSGControlMesh Clone()
        => new(new List<Float3>(points), polygons.ConvertAll(p => (int[])p.Clone()));

    /// <summary>
    /// The solid as a mesh, one face per polygon. <paramref name="faceToPolygon"/> says which polygon each face came
    /// from, as a degenerate polygon makes no face.
    /// </summary>
    public GeometryData ToMesh(out int[] faceToPolygon)
    {
        var mesh = new GeometryData();
        var vertices = new GeometryData.Vertex[points.Count];
        for (int i = 0; i < points.Count; i++) vertices[i] = mesh.AddVertex(points[i]);

        var map = new List<int>(polygons.Count);
        for (int p = 0; p < polygons.Count; p++)
        {
            int[] ring = polygons[p];
            if (ring.Length < 3) continue;

            var corners = new GeometryData.Vertex[ring.Length];
            for (int i = 0; i < ring.Length; i++) corners[i] = vertices[ring[i]];
            if (mesh.AddFace(corners) != null) map.Add(p);
        }

        faceToPolygon = [.. map];
        return mesh;
    }

    /// <summary>One outward plane per polygon, fitted through its corners, in polygon order.</summary>
    public Plane[] ToPlanes()
    {
        var result = new Plane[polygons.Count];

        for (int i = 0; i < polygons.Count; i++)
        {
            if (!FitPolygon(i, out Plane plane))
            {
                // A collapsed polygon repeats a neighbour, so anything reading planes still finds one there
                plane = i > 0 ? result[i - 1] : new Plane(Float3.UnitY, 0f);
            }
            result[i] = plane;
        }

        return result;
    }

    /// <summary>Whether every polygon is flat.</summary>
    public bool IsPlanar(float tolerance = 1e-3f)
    {
        for (int i = 0; i < polygons.Count; i++)
        {
            if (!FitPolygon(i, out Plane plane)) return false;

            foreach (int index in polygons[i])
            {
                if (MathF.Abs(plane.GetSignedDistanceToPoint(points[index])) > tolerance) return false;
            }
        }

        return true;
    }

    /// <summary>Fits a polygon's plane by Newell's method, facing the way the polygon winds.</summary>
    private bool FitPolygon(int polygon, out Plane plane)
    {
        int[] ring = polygons[polygon];
        plane = default;
        if (ring.Length < 3) return false;

        Float3 normal = Float3.Zero;
        Float3 centroid = Float3.Zero;

        for (int i = 0; i < ring.Length; i++)
        {
            Float3 a = points[ring[i]], b = points[ring[(i + 1) % ring.Length]];
            normal.X += (a.Y - b.Y) * (a.Z + b.Z);
            normal.Y += (a.Z - b.Z) * (a.X + b.X);
            normal.Z += (a.X - b.X) * (a.Y + b.Y);
            centroid += a;
        }

        if (Float3.LengthSquared(normal) < 1e-12f) return false;

        normal = Float3.Normalize(normal);
        centroid /= ring.Length;

        plane = new Plane(normal, Float3.Dot(normal, centroid));
        return true;
    }

    /// <summary>The volume the polygons enclose, positive when they face out.</summary>
    public float Volume()
    {
        float total = 0f;
        foreach (int[] ring in polygons)
            for (int i = 1; i + 1 < ring.Length; i++)
                total += Float3.Dot(points[ring[0]], Float3.Cross(points[ring[i]], points[ring[i + 1]]));
        return total / 6f;
    }

    /// <summary>Whether every edge is shared by exactly two polygons, running opposite ways in each.</summary>
    public bool IsClosed()
    {
        var directed = new Dictionary<(int, int), int>();
        foreach (int[] ring in polygons)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                var edge = (ring[i], ring[(i + 1) % ring.Length]);
                directed[edge] = directed.TryGetValue(edge, out int n) ? n + 1 : 1;
            }
        }

        foreach (((int a, int b), int count) in directed)
            if (count != 1 || !directed.TryGetValue((b, a), out int back) || back != 1) return false;
        return directed.Count > 0;
    }

    /// <summary>Whether the polygons enclose a solid: closed, facing out, with room inside.</summary>
    public bool IsValid() => IsClosed() && Volume() > 1e-9f;

    /// <summary>Turns every polygon round, as a mirroring transform needs to keep them facing out.</summary>
    public void ReverseWinding()
    {
        foreach (int[] ring in polygons) Array.Reverse(ring);
    }

    /// <summary>Moves the listed points and nothing else.</summary>
    public void MovePoints(IReadOnlyCollection<int> indices, Float3 delta)
    {
        foreach (int i in indices)
            if (i >= 0 && i < points.Count) points[i] += delta;
    }

    /// <summary>Puts every point through a transform, turning the polygons round when it mirrors so they still face out.</summary>
    public void TransformPoints(Float4x4 transform)
    {
        for (int i = 0; i < points.Count; i++) points[i] = Float4x4.TransformPoint(points[i], transform);
        if (Float4x4.Determinant(transform) < 0f) ReverseWinding();
    }

    public void SetPoint(int index, Float3 position)
    {
        if (index >= 0 && index < points.Count) points[index] = position;
    }

    /// <summary>The undirected edges of the mesh, each once, as index pairs.</summary>
    public List<(int a, int b)> Edges()
    {
        var seen = new HashSet<(int, int)>();
        var result = new List<(int, int)>();

        foreach (int[] ring in polygons)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                int a = ring[i], b = ring[(i + 1) % ring.Length];
                var key = a < b ? (a, b) : (b, a);

                if (seen.Add(key)) result.Add(key);
            }
        }

        return result;
    }

    /// <summary>The points of one polygon, for drawing or for selecting a whole face.</summary>
    public IReadOnlyList<int> PolygonPoints(int polygon)
        => polygon >= 0 && polygon < polygons.Count ? polygons[polygon] : Array.Empty<int>();

    /// <summary>Polygons meeting at a point.</summary>
    public List<int> PolygonsAtPoint(int point)
    {
        var result = new List<int>();
        for (int i = 0; i < polygons.Count; i++)
            if (Array.IndexOf(polygons[i], point) >= 0) result.Add(i);
        return result;
    }

    /// <summary>The two polygons either side of an edge.</summary>
    public List<int> PolygonsAtEdge(int a, int b)
    {
        var result = new List<int>();
        for (int i = 0; i < polygons.Count; i++)
            if (Array.IndexOf(polygons[i], a) >= 0 && Array.IndexOf(polygons[i], b) >= 0) result.Add(i);
        return result;
    }

    /// <summary>Outward normal of one polygon.</summary>
    public Float3 PolygonNormal(int polygon)
        => FitPolygon(polygon, out Plane plane) ? plane.Normal : Float3.UnitY;

    /// <summary>Centre of one polygon's corners.</summary>
    public Float3 PolygonCentre(int polygon)
    {
        IReadOnlyList<int> ring = PolygonPoints(polygon);
        if (ring.Count == 0) return Float3.Zero;

        Float3 sum = Float3.Zero;
        foreach (int i in ring) sum += points[i];
        return sum / ring.Count;
    }

    /// <summary>Scales a polygon's corners about its own centre.</summary>
    public void ScalePolygon(int polygon, float factor)
    {
        Float3 centre = PolygonCentre(polygon);
        foreach (int i in PolygonPoints(polygon))
            points[i] = centre + (points[i] - centre) * factor;
    }

    /// <summary>Turns a set of points about an axis.</summary>
    public void RotatePoints(IReadOnlyCollection<int> indices, Float3 origin, Float3 axis, float degrees)
    {
        Quaternion rotation = Quaternion.AxisAngle(Float3.NormalizeSafe(axis, Float3.UnitY), degrees * (MathF.PI / 180f));

        foreach (int i in indices)
            if (i >= 0 && i < points.Count) points[i] = origin + rotation * (points[i] - origin);
    }
}
