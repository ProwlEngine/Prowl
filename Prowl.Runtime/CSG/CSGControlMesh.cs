// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>A brush's shape as explicit points and polygons, with the plane set derived from it. Polygons can be pulled out of plane, see <see cref="IsPlanar"/>.</summary>
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

    /// <summary>Derives a control mesh from a plane set by building the solid and reading its corners back.</summary>
    public static CSGControlMesh? FromPlanes(IReadOnlyList<Plane> planes, float weldTolerance = 1e-4f)
    {
        ConvexSolidResult result = GeometryGenerator.ConvexFromPlanes(planes, weldTolerance);
        if (!result.IsValid) return null;

        GeometryData solid = result.Solid!;

        var pts = new List<Float3>(solid.Vertices.Count);
        foreach (GeometryData.Vertex v in solid.Vertices) pts.Add(v.Point);

        var polys = new List<int[]>(solid.Faces.Count);
        foreach (GeometryData.Face face in solid.Faces)
        {
            var ring = face.NeighborVertices();
            if (ring.Count < 3) continue;

            var indices = new int[ring.Count];
            for (int i = 0; i < ring.Count; i++) indices[i] = ring[i].Index;
            polys.Add(indices);
        }

        return polys.Count >= 4 ? new CSGControlMesh(pts, polys) : null;
    }

    public CSGControlMesh Clone()
        => new(new List<Float3>(points), polygons.ConvertAll(p => (int[])p.Clone()));

    /// <summary>One outward plane per polygon, in polygon order.</summary>
    public Plane[] ToPlanes()
    {
        var result = new Plane[polygons.Count];
        Float3 centre = Centre();

        for (int i = 0; i < polygons.Count; i++)
        {
            if (!FitPolygon(i, centre, out Plane plane))
            {
                // A collapsed polygon repeats a neighbour, the duplicate half space is dropped when the solid is built.
                plane = i > 0 ? result[i - 1] : new Plane(Float3.UnitY, 0f);
            }
            result[i] = plane;
        }

        return result;
    }

    /// <summary>Whether every polygon is still flat.</summary>
    public bool IsPlanar(float tolerance = 1e-3f)
    {
        Float3 centre = Centre();

        for (int i = 0; i < polygons.Count; i++)
        {
            if (!FitPolygon(i, centre, out Plane plane)) return false;

            foreach (int index in polygons[i])
            {
                if (MathF.Abs(plane.GetSignedDistanceToPoint(points[index])) > tolerance) return false;
            }
        }

        return true;
    }

    /// <summary>Fits a polygon's plane by Newell's method, turned to face away from the interior.</summary>
    private bool FitPolygon(int polygon, Float3 interior, out Plane plane)
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

        // Winding is unreliable after arbitrary edits, so orient by the interior point.
        if (Float3.Dot(normal, centroid - interior) < 0f) normal = -normal;

        plane = new Plane(normal, Float3.Dot(normal, centroid));
        return true;
    }

    private Float3 Centre()
    {
        if (points.Count == 0) return Float3.Zero;

        Float3 sum = Float3.Zero;
        foreach (Float3 p in points) sum += p;
        return sum / points.Count;
    }

    /// <summary>Moves the listed points and nothing else.</summary>
    public void MovePoints(IReadOnlyCollection<int> indices, Float3 delta)
    {
        foreach (int i in indices)
            if (i >= 0 && i < points.Count) points[i] += delta;
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
        => FitPolygon(polygon, Centre(), out Plane plane) ? plane.Normal : Float3.UnitY;

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

    /// <summary>Whether the derived plane set still builds a solid.</summary>
    public bool IsValid() => GeometryGenerator.ConvexFromPlanes(ToPlanes()).IsValid;
}
