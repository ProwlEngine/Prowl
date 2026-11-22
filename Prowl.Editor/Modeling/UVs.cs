// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Unwrapper;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>
/// Texture coordinates of model meshes: seams, unwrapping, projections and moving UV points. UVs live in each corner's
/// <see cref="MeshGeometry.UV"/> attribute, seams on edges and pins on corners, both saved with the mesh.
/// </summary>
public static class UVs
{
    /// <summary>The edge attribute marking a seam, where unwrapping cuts the surface apart.</summary>
    public const string Seam = "seam";

    /// <summary>The corner attribute marking a pinned UV point, which unwrapping leaves where it is.</summary>
    public const string Pin = "uvpin";

    private const float Same = 1e-5f;

    public static Float2 Get(GeometryData.Loop corner)
        => corner.TryGetAttribute(MeshGeometry.UV, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue uv && uv.Data.Length >= 2
            ? new Float2(uv.Data[0], uv.Data[1]) : Float2.Zero;

    public static void Set(GeometryData.Loop corner, Float2 uv) => corner.SetAttribute(MeshGeometry.UV, new GeometryData.FloatAttributeValue(uv.X, uv.Y));

    public static void EnsureUV(GeometryData mesh)
    {
        if (!mesh.HasLoopAttribute(MeshGeometry.UV)) mesh.AddLoopAttribute(MeshGeometry.UV, GeometryData.AttributeBaseType.Float, 2);
    }

    public static IEnumerable<GeometryData.Loop> Corners(GeometryData.Face face)
    {
        GeometryData.Loop loop = face.Loop!;
        do
        {
            yield return loop;
            loop = loop.Next!;
        } while (loop != face.Loop);
    }

    /// <summary>The corner of a face at one of its points, or null when the face does not use the point.</summary>
    public static GeometryData.Loop? CornerAt(GeometryData.Face face, GeometryData.Vertex vertex)
    {
        foreach (GeometryData.Loop corner in Corners(face))
            if (corner.Vert == vertex) return corner;
        return null;
    }

    // ================================================================
    //  Seams and pins
    // ================================================================

    public static bool IsSeam(GeometryData.Edge edge) => Flag(edge, Seam);

    public static bool IsPinned(GeometryData.Loop corner) => Flag(corner, Pin);

    private static bool Flag(GeometryData.Element element, string name)
        => element.TryGetAttribute(name, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue n && n.Data.Length > 0 && n.Data[0] != 0;

    public static void SetSeams(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, bool seam)
    {
        foreach (GeometryData.Edge edge in edges) edge.SetAttribute(Seam, new GeometryData.IntAttributeValue(seam ? 1 : 0));
        mesh.MarkChanged();
    }

    public static void SetPinned(IEnumerable<GeometryData.Loop> corners, bool pinned)
    {
        foreach (GeometryData.Loop corner in corners) corner.SetAttribute(Pin, new GeometryData.IntAttributeValue(pinned ? 1 : 0));
    }

    /// <summary>Marks every sharp edge as a seam.</summary>
    public static void SeamsFromSharp(GeometryData mesh) => SetSeams(mesh, mesh.Edges.Where(e => e.IsSharp).ToList(), true);

    /// <summary>Marks every edge between faces of different material slots as a seam.</summary>
    public static void SeamsFromMaterials(GeometryData mesh)
        => SetSeams(mesh, mesh.Edges.Where(e => e.NeighborFaces() is { Count: 2 } f && Slot(f[0]) != Slot(f[1])).ToList(), true);

    /// <summary>Marks every edge whose faces' UVs do not meet along it as a seam, so the current islands are kept.</summary>
    public static void SeamsFromIslands(GeometryData mesh)
        => SetSeams(mesh, mesh.Edges.Where(e => e.NeighborFaces() is { Count: 2 } f && !Joined(e, f[0], f[1])).ToList(), true);

    private static int Slot(GeometryData.Face face)
        => face.TryGetAttribute(MeshGeometry.SubMesh, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue n && n.Data.Length > 0 ? n.Data[0] : 0;

    /// <summary>Whether two faces have the same UVs at both ends of the edge between them.</summary>
    private static bool Joined(GeometryData.Edge edge, GeometryData.Face a, GeometryData.Face b)
    {
        foreach (GeometryData.Vertex end in (GeometryData.Vertex[])[edge.Vert1, edge.Vert2])
        {
            if (CornerAt(a, end) is not { } ca || CornerAt(b, end) is not { } cb) return false;
            if (Float2.Distance(Get(ca), Get(cb)) > Same) return false;
        }
        return true;
    }

    /// <summary>The faces in groups joined in UV space, each an island of the layout.</summary>
    public static List<List<GeometryData.Face>> Islands(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        var inSet = new HashSet<GeometryData.Face>(faces);
        var seen = new HashSet<GeometryData.Face>();
        var islands = new List<List<GeometryData.Face>>();
        foreach (GeometryData.Face start in faces)
        {
            if (!seen.Add(start)) continue;
            var island = new List<GeometryData.Face> { start };
            for (int i = 0; i < island.Count; i++)
                foreach (GeometryData.Loop corner in Corners(island[i]))
                    foreach (GeometryData.Face other in corner.Edge.NeighborFaces())
                        if (other != island[i] && inSet.Contains(other) && !seen.Contains(other) && Joined(corner.Edge, island[i], other))
                        {
                            seen.Add(other);
                            island.Add(other);
                        }
            islands.Add(island);
        }
        return islands;
    }

    // ================================================================
    //  Unwrap
    // ================================================================

    /// <summary>
    /// Lays the faces out flat, cut apart at seams, and packs them into the unit square. Faces stay joined across an edge
    /// only when both are unwrapped and the edge is no seam. Pinned corners keep their UVs, and a piece with two pins or
    /// more is laid out around them.
    /// </summary>
    public static void Unwrap(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        if (faces.Count == 0) return;
        EnsureUV(mesh);
        var inSet = new HashSet<GeometryData.Face>(faces);

        var corners = new List<GeometryData.Loop>();
        var cornerIndex = new Dictionary<GeometryData.Loop, int>();
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Loop corner in Corners(face))
            {
                cornerIndex[corner] = corners.Count;
                corners.Add(corner);
            }

        // Corners at one point are one point of the layout where their faces meet across an edge that is no seam
        var parent = new int[corners.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Loop corner in Corners(face))
            {
                if (IsSeam(corner.Edge)) continue;
                foreach (GeometryData.Face other in corner.Edge.NeighborFaces())
                {
                    if (other == face || !inSet.Contains(other)) continue;
                    foreach (GeometryData.Vertex end in (GeometryData.Vertex[])[corner.Edge.Vert1, corner.Edge.Vert2])
                        if (CornerAt(face, end) is { } mine && CornerAt(other, end) is { } theirs)
                            parent[Find(cornerIndex[mine])] = Find(cornerIndex[theirs]);
                }
            }

        var pointOf = new int[corners.Count];
        var positions = new List<Double3>();
        var pins = new Dictionary<int, Double2>();
        var rootPoint = new Dictionary<int, int>();
        for (int i = 0; i < corners.Count; i++)
        {
            int root = Find(i);
            if (!rootPoint.TryGetValue(root, out int point))
            {
                point = positions.Count;
                rootPoint[root] = point;
                Float3 p = corners[i].Vert.Point;
                positions.Add(new Double3(p.X, p.Y, p.Z));
            }
            pointOf[i] = point;
            if (IsPinned(corners[i]))
            {
                Float2 uv = Get(corners[i]);
                pins[point] = new Double2(uv.X, uv.Y);
            }
        }

        var triangles = new List<int>();
        var triangleCorners = new List<GeometryData.Loop>();
        foreach (GeometryData.Face face in faces)
            foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in Triangles(face))
                foreach (GeometryData.Loop corner in (GeometryData.Loop[])[a, b, c])
                {
                    triangles.Add(pointOf[cornerIndex[corner]]);
                    triangleCorners.Add(corner);
                }

        ChartUnwrapResult result = ChartUnwrapper.Unwrap([.. positions], [.. triangles], null, pins.Count > 0 ? pins : null);
        for (int t = 0; t < result.TriangleCharts.Length; t++)
        {
            if (result.TriangleCharts[t] < 0) continue;
            for (int k = 0; k < 3; k++)
            {
                Double2 uv = result.PerCornerUVs[3 * t + k];
                GeometryData.Loop corner = triangleCorners[3 * t + k];
                if (!IsPinned(corner)) Set(corner, new Float2((float)uv.X, (float)uv.Y));
            }
        }
        mesh.MarkChanged();
    }

    /// <summary>A face cut into triangles in its own plane, ear clipped so a concave face is cut inside itself.</summary>
    public static List<(GeometryData.Loop, GeometryData.Loop, GeometryData.Loop)> Triangles(GeometryData.Face face)
    {
        List<GeometryData.Loop> corners = [.. Corners(face)];
        var result = new List<(GeometryData.Loop, GeometryData.Loop, GeometryData.Loop)>(Math.Max(0, corners.Count - 2));
        if (corners.Count > 3)
        {
            var frame = new PlaneFrame(face.Normal(), corners[0].Vert.Point);
            List<int> cut = Polygon2D.Triangulate([.. corners.Select(c => frame.To2D(c.Vert.Point))]);
            if (cut.Count == (corners.Count - 2) * 3)
            {
                for (int i = 0; i < cut.Count; i += 3) result.Add((corners[cut[i]], corners[cut[i + 1]], corners[cut[i + 2]]));
                return result;
            }
        }
        for (int i = 1; i < corners.Count - 1; i++) result.Add((corners[0], corners[i], corners[i + 1]));
        return result;
    }

    // ================================================================
    //  Projections
    // ================================================================

    /// <summary>Each face takes coordinates from the axis plane it faces most, one unit of space to one of texture.</summary>
    public static void Cube(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Face face in faces)
        {
            Float3 normal = face.Normal();
            Float3 size = new(MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            foreach (GeometryData.Loop corner in Corners(face))
            {
                Float3 p = corner.Vert.Point;
                Set(corner, axis switch
                {
                    0 => new Float2(p.Z, p.Y),
                    1 => new Float2(p.X, p.Z),
                    _ => new Float2(p.X, p.Y),
                } + new Float2(0.5f));
            }
        }
        mesh.MarkChanged();
    }

    /// <summary>
    /// Coordinates wrapped round the Y axis, U the angle and V the height. Faces looking up or down take the plane
    /// across Y instead. A face across the seam keeps its corners together rather than spanning the whole texture.
    /// </summary>
    public static void Cylinder(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Face face in faces)
        {
            if (MathF.Abs(face.Normal().Y) > 0.7f)
            {
                foreach (GeometryData.Loop corner in Corners(face)) Set(corner, new Float2(corner.Vert.Point.X, corner.Vert.Point.Z) + new Float2(0.5f));
                continue;
            }
            Float3 centre = face.Center();
            Wrapped(face, p =>
            {
                // A point on the axis takes the face's angle, so a pole does not pick one at random
                Float3 around = MathF.Abs(p.X) + MathF.Abs(p.Z) < 1e-5f ? centre : p;
                return new Float2(MathF.Atan2(around.Z, around.X) / (2f * MathF.PI) + 0.5f, p.Y + 0.5f);
            });
        }
        mesh.MarkChanged();
    }

    /// <summary>Coordinates from the direction to each point round the origin, U the angle round Y and V the height angle.</summary>
    public static void Sphere(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Face face in faces)
        {
            Float3 centre = face.Center();
            Wrapped(face, p =>
            {
                Float3 around = MathF.Abs(p.X) + MathF.Abs(p.Z) < 1e-5f ? centre : p;
                float length = Float3.Length(p);
                float height = length > 1e-6f ? MathF.Asin(Math.Clamp(p.Y / length, -1f, 1f)) / MathF.PI + 0.5f : 0.5f;
                return new Float2(MathF.Atan2(around.Z, around.X) / (2f * MathF.PI) + 0.5f, height);
            });
        }
        mesh.MarkChanged();
    }

    // Sets corners from a wrapping projection, moving the corners of a face across the wrap onto one side
    private static void Wrapped(GeometryData.Face face, Func<Float3, Float2> project)
    {
        List<(GeometryData.Loop Corner, Float2 UV)> corners = [.. Corners(face).Select(c => (c, project(c.Vert.Point)))];
        float min = corners.Min(c => c.UV.X), max = corners.Max(c => c.UV.X);
        bool across = max - min > 0.5f;
        foreach ((GeometryData.Loop corner, Float2 uv) in corners)
            Set(corner, new Float2(across && uv.X < 0.5f ? uv.X + 1f : uv.X, uv.Y));
    }

    /// <summary>The faces seen through a projection of points to the screen, fitted into the unit square.</summary>
    public static void FromView(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces, Func<Float3, Float2> toScreen)
    {
        if (faces.Count == 0) return;
        EnsureUV(mesh);
        var projected = new Dictionary<GeometryData.Loop, Float2>();
        Float2 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Loop corner in Corners(face))
            {
                Float2 at = toScreen(corner.Vert.Point);
                projected[corner] = at;
                min = Maths.Min(min, at);
                max = Maths.Max(max, at);
            }
        float size = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y), 1e-6f);
        foreach ((GeometryData.Loop corner, Float2 at) in projected) Set(corner, (at - min) / size);
        mesh.MarkChanged();
    }

    /// <summary>Each face takes the whole unit square: a quad its corners, any other its own shape fitted into it.</summary>
    public static void Reset(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        EnsureUV(mesh);
        Float2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        foreach (GeometryData.Face face in faces)
        {
            List<GeometryData.Loop> corners = [.. Corners(face)];
            if (corners.Count == 4)
            {
                for (int i = 0; i < 4; i++) Set(corners[i], square[i]);
                continue;
            }
            var frame = new PlaneFrame(face.Normal(), face.Center());
            List<Float2> flat = [.. corners.Select(c => frame.To2D(c.Vert.Point))];
            Float2 min = flat.Aggregate((a, b) => Maths.Min(a, b)), max = flat.Aggregate((a, b) => Maths.Max(a, b));
            float size = MathF.Max(MathF.Max(max.X - min.X, max.Y - min.Y), 1e-6f);
            for (int i = 0; i < corners.Count; i++) Set(corners[i], (flat[i] - min) / size);
        }
        mesh.MarkChanged();
    }

    // ================================================================
    //  Moving UV points
    // ================================================================

    /// <summary>Moves corners' UVs by an offset, or to a point when absolute.</summary>
    public static void MoveUVs(GeometryData mesh, IEnumerable<GeometryData.Loop> corners, Float2 by, bool absolute = false)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Loop corner in corners) Set(corner, absolute ? by : Get(corner) + by);
        mesh.MarkChanged();
    }
}
