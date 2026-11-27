// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Vector.Geometry;

/// <summary>
/// Texture coordinates of a mesh: seams, islands, projections and moving UV points. UVs live in each corner's
/// <see cref="GeometryData.UvAttribute"/> attribute, seams on edges and pins on corners.
/// </summary>
public static class GeometryUVs
{
    /// <summary>The corner attribute marking a pinned UV point, which unwrapping leaves where it is.</summary>
    public const string PinAttribute = "uvpin";

    private const float Same = 1e-5f;

    public static Float2 Get(GeometryData.Loop corner)
        => corner.TryGetAttribute(GeometryData.UvAttribute, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue uv && uv.Data.Length >= 2
            ? new Float2(uv.Data[0], uv.Data[1]) : Float2.Zero;

    public static void Set(GeometryData.Loop corner, Float2 uv) => corner.SetAttribute(GeometryData.UvAttribute, new GeometryData.FloatAttributeValue(uv.X, uv.Y));

    public static void EnsureUV(GeometryData mesh)
    {
        if (!mesh.HasLoopAttribute(GeometryData.UvAttribute)) mesh.AddLoopAttribute(GeometryData.UvAttribute, GeometryData.AttributeBaseType.Float, 2);
    }


    // ================================================================
    //  Seams and pins
    // ================================================================

    public static bool IsSeam(GeometryData.Edge edge) => edge.IsSeam;

    public static bool IsPinned(GeometryData.Loop corner) => Flag(corner, PinAttribute);

    private static bool Flag(GeometryData.Element element, string name)
        => element.TryGetAttribute(name, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue n && n.Data.Length > 0 && n.Data[0] != 0;

    public static void SetSeams(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, bool seam)
    {
        foreach (GeometryData.Edge edge in edges) edge.IsSeam = seam;
        mesh.MarkChanged();
    }

    public static void SetPinned(IEnumerable<GeometryData.Loop> corners, bool pinned)
    {
        foreach (GeometryData.Loop corner in corners) corner.SetAttribute(PinAttribute, new GeometryData.IntAttributeValue(pinned ? 1 : 0));
    }

    /// <summary>Marks every edge flagged sharp, or whose two faces turn against each other by more than the angle, as a seam.</summary>
    public static void SeamsFromSharp(GeometryData mesh, float degrees)
        => SetSeams(mesh, [.. mesh.Edges.Where(e => e.IsSharp).Union(GeometryOperators.EdgesSharperThan(mesh, degrees))], true);

    /// <summary>Takes the UV map and its pins off the mesh. Seams are kept.</summary>
    public static void Clear(GeometryData mesh)
    {
        mesh.RemoveLoopAttribute(GeometryData.UvAttribute);
        mesh.RemoveLoopAttribute(PinAttribute);
        mesh.MarkChanged();
    }

    /// <summary>Marks every edge between faces of different materials, as the function reads them, as a seam.</summary>
    public static void SeamsFromMaterials(GeometryData mesh, Func<GeometryData.Face, int> materialOf)
        => SetSeams(mesh, mesh.Edges.Where(e => e.NeighborFaces() is { Count: 2 } f && materialOf(f[0]) != materialOf(f[1])).ToList(), true);

    /// <summary>Marks every edge whose faces' UVs do not meet along it as a seam, so the current islands are kept.</summary>
    public static void SeamsFromIslands(GeometryData mesh)
        => SetSeams(mesh, mesh.Edges.Where(e => e.NeighborFaces() is { Count: 2 } f && !Joined(e, f[0], f[1])).ToList(), true);


    /// <summary>Whether two faces have the same UVs at both ends of the edge between them.</summary>
    private static bool Joined(GeometryData.Edge edge, GeometryData.Face a, GeometryData.Face b)
    {
        foreach (GeometryData.Vertex end in (GeometryData.Vertex[])[edge.Vert1, edge.Vert2])
        {
            if (a.GetLoop(end) is not { } ca || b.GetLoop(end) is not { } cb) return false;
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
                foreach (GeometryData.Loop corner in island[i].Corners())
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

    /// <summary>A face cut into triangles in its own plane, ear clipped so a concave face is cut inside itself.</summary>
    public static List<(GeometryData.Loop, GeometryData.Loop, GeometryData.Loop)> Triangles(GeometryData.Face face)
    {
        List<GeometryData.Loop> corners = [.. face.Corners()];
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

    /// <summary>A point flattened onto the axis plane a normal faces most.</summary>
    public static Float2 Projected(Float3 point, Float3 normal)
    {
        Float3 size = new(MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));
        return size.X >= size.Y && size.X >= size.Z ? new Float2(point.Z, point.Y) : size.Y >= size.Z ? new Float2(point.X, point.Z) : new Float2(point.X, point.Y);
    }

    /// <summary>Each face takes coordinates from the axis plane it faces most, one unit of space to one of texture.</summary>
    public static void Cube(GeometryData mesh, IEnumerable<GeometryData.Face> faces)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Face face in faces)
        {
            Float3 normal = face.Normal();
            foreach (GeometryData.Loop corner in face.Corners()) Set(corner, Projected(corner.Vert.Point, normal) + new Float2(0.5f));
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
                foreach (GeometryData.Loop corner in face.Corners()) Set(corner, new Float2(corner.Vert.Point.X, corner.Vert.Point.Z) + new Float2(0.5f));
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
        List<(GeometryData.Loop Corner, Float2 UV)> corners = [.. face.Corners().Select(c => (c, project(c.Vert.Point)))];
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
            foreach (GeometryData.Loop corner in face.Corners())
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
            List<GeometryData.Loop> corners = [.. face.Corners()];
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

    /// <summary>Runs each corner's UV through a function.</summary>
    public static void Transform(GeometryData mesh, IEnumerable<GeometryData.Loop> corners, Func<Float2, Float2> change)
    {
        EnsureUV(mesh);
        foreach (GeometryData.Loop corner in corners) Set(corner, change(Get(corner)));
        mesh.MarkChanged();
    }

    /// <summary>The middle of the corners' UVs.</summary>
    public static Float2 Centre(IReadOnlyCollection<GeometryData.Loop> corners)
    {
        Float2 sum = Float2.Zero;
        foreach (GeometryData.Loop corner in corners) sum += Get(corner);
        return corners.Count > 0 ? sum / corners.Count : sum;
    }

    /// <summary>Lines the corners up on their middle along one axis, 0 for U and 1 for V.</summary>
    public static void Align(GeometryData mesh, IReadOnlyCollection<GeometryData.Loop> corners, int axis)
    {
        Float2 centre = Centre(corners);
        Transform(mesh, corners, uv => axis == 0 ? new Float2(centre.X, uv.Y) : new Float2(uv.X, centre.Y));
    }

    /// <summary>Mirrors the corners across their middle along one axis, 0 for U and 1 for V.</summary>
    public static void Flip(GeometryData mesh, IReadOnlyCollection<GeometryData.Loop> corners, int axis)
    {
        Float2 centre = Centre(corners);
        Transform(mesh, corners, uv => axis == 0 ? new Float2(2f * centre.X - uv.X, uv.Y) : new Float2(uv.X, 2f * centre.Y - uv.Y));
    }

    /// <summary>Turns the corners round their middle.</summary>
    public static void Rotate(GeometryData mesh, IReadOnlyCollection<GeometryData.Loop> corners, float radians)
    {
        Float2 centre = Centre(corners);
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        Transform(mesh, corners, uv =>
        {
            Float2 d = uv - centre;
            return centre + new Float2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
        });
    }

    /// <summary>Brings every corner to their middle.</summary>
    public static void Weld(GeometryData mesh, IReadOnlyCollection<GeometryData.Loop> corners)
    {
        Float2 centre = Centre(corners);
        Transform(mesh, corners, _ => centre);
    }

    /// <summary>Rounds the corners to the nearest texel corner of a texture of the given size.</summary>
    public static void SnapToPixels(GeometryData mesh, IEnumerable<GeometryData.Loop> corners, int width, int height)
        => Transform(mesh, corners, uv => new Float2(MathF.Round(uv.X * width) / width, MathF.Round(uv.Y * height) / height));

    /// <summary>Scales each island round its middle so all of them have as much texture per area of surface as the faces together.</summary>
    public static void AverageIslandScale(GeometryData mesh, IReadOnlyCollection<GeometryData.Face> faces)
    {
        List<List<GeometryData.Face>> islands = Islands(mesh, faces);
        var areas = new List<(float Surface, float Texture)>(islands.Count);
        float surfaceTotal = 0f, textureTotal = 0f;
        foreach (List<GeometryData.Face> island in islands)
        {
            float surface = 0f, texture = 0f;
            foreach (GeometryData.Face face in island)
            {
                (float s, float t) = Areas(face);
                surface += s;
                texture += t;
            }
            areas.Add((surface, texture));
            surfaceTotal += surface;
            textureTotal += texture;
        }
        if (surfaceTotal <= 0f || textureTotal <= 0f) return;
        float density = textureTotal / surfaceTotal;
        for (int i = 0; i < islands.Count; i++)
        {
            (float surface, float texture) = areas[i];
            if (surface <= 0f || texture <= 0f) continue;
            float scale = MathF.Sqrt(density * surface / texture);
            List<GeometryData.Loop> corners = [.. islands[i].SelectMany(f => f.Corners())];
            Float2 centre = Centre(corners);
            Transform(mesh, corners, uv => centre + (uv - centre) * scale);
        }
    }

    /// <summary>The faces' UV edges drawn as white lines on a clear image, rows from the top, four bytes a pixel.</summary>
    public static byte[] LayoutImage(IEnumerable<GeometryData.Face> faces, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        void Plot(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int at = (y * width + x) * 4;
            pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 255;
        }
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Loop corner in face.Corners())
            {
                Float2 a = Get(corner), b = Get(corner.Next!);
                float ax = a.X * width, ay = (1f - a.Y) * height, bx = b.X * width, by = (1f - b.Y) * height;
                int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(bx - ax), MathF.Abs(by - ay))) + 1;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Plot((int)MathF.Floor(ax + (bx - ax) * t), (int)MathF.Floor(ay + (by - ay) * t));
                }
            }
        return pixels;
    }

    /// <summary>For each edge, brings the UVs of the other faces along it onto those of the first face, joining them.</summary>
    public static void Stitch(GeometryData mesh, IEnumerable<GeometryData.Edge> edges, IReadOnlyCollection<GeometryData.Face> faces)
    {
        var inSet = new HashSet<GeometryData.Face>(faces);
        foreach (GeometryData.Edge edge in edges)
        {
            List<GeometryData.Face> around = [.. edge.NeighborFaces().Where(inSet.Contains)];
            for (int i = 1; i < around.Count; i++)
                foreach (GeometryData.Vertex end in (GeometryData.Vertex[])[edge.Vert1, edge.Vert2])
                    if (around[0].GetLoop(end) is { } keep && around[i].GetLoop(end) is { } moved)
                    {
                        Float2 from = Get(moved), to = Get(keep);
                        foreach (GeometryData.Loop corner in around[i].Corners())
                            if (corner.Vert == end) Set(corner, to);
                        // Every corner of that point on the moved face's island goes with it
                        foreach (GeometryData.Face face in faces)
                            if (face != around[0] && face.GetLoop(end) is { } other && Float2.Distance(Get(other), from) <= Same) Set(other, to);
                    }
        }
        mesh.MarkChanged();
    }

    // The distinct UV points of the corners, each with the corners sitting on it
    private static List<(Float2 UV, List<GeometryData.Loop> Corners)> Points(IEnumerable<GeometryData.Loop> corners)
    {
        var points = new List<(Float2, List<GeometryData.Loop>)>();
        foreach (GeometryData.Loop corner in corners)
        {
            Float2 uv = Get(corner);
            int found = points.FindIndex(p => p.Item2[0].Vert == corner.Vert && Float2.Distance(p.Item1, uv) <= 1e-3f);
            if (found >= 0) points[found].Item2.Add(corner);
            else points.Add((uv, [corner]));
        }
        return points;
    }

    // The two points furthest apart, which straighten and distribute keep where they are
    private static (Float2 A, Float2 B) Ends(List<(Float2 UV, List<GeometryData.Loop> Corners)> points)
    {
        (Float2 a, Float2 b) = (points[0].UV, points[0].UV);
        float best = -1f;
        foreach ((Float2 p, _) in points)
            foreach ((Float2 q, _) in points)
            {
                float d = Float2.Distance(p, q);
                if (d <= best) continue;
                best = d;
                (a, b) = (p, q);
            }
        return (a, b);
    }

    /// <summary>Puts the UV points on the line through the two furthest apart, each moved straight onto it.</summary>
    public static void Straighten(GeometryData mesh, IEnumerable<GeometryData.Loop> corners)
    {
        List<(Float2 UV, List<GeometryData.Loop> Corners)> points = Points(corners);
        if (points.Count < 3) return;
        (Float2 a, Float2 b) = Ends(points);
        Float2 line = b - a;
        float length = Float2.Dot(line, line);
        if (length < 1e-12f) return;
        foreach ((Float2 uv, List<GeometryData.Loop> at) in points)
        {
            Float2 onto = a + line * (Float2.Dot(uv - a, line) / length);
            foreach (GeometryData.Loop corner in at) Set(corner, onto);
        }
        mesh.MarkChanged();
    }

    /// <summary>Spaces the UV points evenly between the two furthest apart, keeping their order along that line.</summary>
    public static void Distribute(GeometryData mesh, IEnumerable<GeometryData.Loop> corners)
    {
        List<(Float2 UV, List<GeometryData.Loop> Corners)> points = Points(corners);
        if (points.Count < 3) return;
        (Float2 a, Float2 b) = Ends(points);
        Float2 line = b - a;
        List<(Float2 UV, List<GeometryData.Loop> Corners)> ordered = [.. points.OrderBy(p => Float2.Dot(p.UV - a, line))];
        for (int i = 0; i < ordered.Count; i++)
        {
            Float2 to = a + line * ((float)i / (ordered.Count - 1));
            foreach (GeometryData.Loop corner in ordered[i].Corners) Set(corner, to);
        }
        mesh.MarkChanged();
    }

    /// <summary>
    /// Moves each UV point toward the middle of the UV points it shares an edge with on the faces, by the strength from
    /// 0 to 1. Points on a border of the faces stay where they are.
    /// </summary>
    public static void Relax(GeometryData mesh, IEnumerable<GeometryData.Loop> corners, IReadOnlyCollection<GeometryData.Face> faces, float strength)
    {
        var inSet = new HashSet<GeometryData.Face>(faces);
        var moves = new List<(List<GeometryData.Loop> Corners, Float2 To)>();
        foreach ((Float2 uv, List<GeometryData.Loop> at) in Points(corners))
        {
            var neighbours = new List<Float2>();
            bool border = false;
            foreach (GeometryData.Loop corner in at)
            {
                neighbours.Add(Get(corner.Next!));
                neighbours.Add(Get(corner.Prev!));
                if (corner.Edge.NeighborFaces().Count(inSet.Contains) < 2 || corner.Prev!.Edge.NeighborFaces().Count(inSet.Contains) < 2) border = true;
            }
            if (border || neighbours.Count == 0) continue;
            List<(Float2 UV, List<GeometryData.Loop> Corners)> distinct = Points(at.SelectMany(c => (GeometryData.Loop[])[c.Next!, c.Prev!]));
            Float2 sum = Float2.Zero;
            foreach ((Float2 p, _) in distinct) sum += p;
            Float2 middle = sum / distinct.Count;
            moves.Add((at, uv + (middle - uv) * Math.Clamp(strength, 0f, 1f)));
        }
        foreach ((List<GeometryData.Loop> at, Float2 to) in moves)
            foreach (GeometryData.Loop corner in at) Set(corner, to);
        mesh.MarkChanged();
    }

    /// <summary>The faces whose UVs overlap another face's, triangle against triangle.</summary>
    public static HashSet<GeometryData.Face> OverlappingFaces(IReadOnlyCollection<GeometryData.Face> faces)
    {
        var triangles = new List<(GeometryData.Face Face, Float2 A, Float2 B, Float2 C, Float2 Min, Float2 Max)>();
        foreach (GeometryData.Face face in faces)
            foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in Triangles(face))
            {
                Float2 ua = Get(a), ub = Get(b), uc = Get(c);
                triangles.Add((face, ua, ub, uc, Maths.Min(ua, Maths.Min(ub, uc)), Maths.Max(ua, Maths.Max(ub, uc))));
            }
        var overlapping = new HashSet<GeometryData.Face>();
        for (int i = 0; i < triangles.Count; i++)
            for (int j = i + 1; j < triangles.Count; j++)
            {
                (GeometryData.Face Face, Float2 A, Float2 B, Float2 C, Float2 Min, Float2 Max) p = triangles[i];
                (GeometryData.Face Face, Float2 A, Float2 B, Float2 C, Float2 Min, Float2 Max) q = triangles[j];
                if (p.Face == q.Face) continue;
                if (p.Max.X <= q.Min.X || q.Max.X <= p.Min.X || p.Max.Y <= q.Min.Y || q.Max.Y <= p.Min.Y) continue;
                if (!TrianglesOverlap(p.A, p.B, p.C, q.A, q.B, q.C)) continue;
                overlapping.Add(p.Face);
                overlapping.Add(q.Face);
            }
        return overlapping;
    }

    // Separating axis test on the triangles' edges, touching along an edge or corner not counting
    private static bool TrianglesOverlap(Float2 a0, Float2 a1, Float2 a2, Float2 b0, Float2 b1, Float2 b2)
    {
        const float Touch = 1e-5f;
        Float2[] a = [a0, a1, a2], b = [b0, b1, b2];
        foreach (Float2[] shape in (Float2[][])[a, b])
            for (int i = 0; i < 3; i++)
            {
                Float2 edge = shape[(i + 1) % 3] - shape[i];
                var axis = new Float2(-edge.Y, edge.X);
                float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                foreach (Float2 p in a)
                {
                    float d = Float2.Dot(p, axis);
                    minA = MathF.Min(minA, d);
                    maxA = MathF.Max(maxA, d);
                }
                foreach (Float2 p in b)
                {
                    float d = Float2.Dot(p, axis);
                    minB = MathF.Min(minB, d);
                    maxB = MathF.Max(maxB, d);
                }
                float scale = MathF.Max(Float2.Length(axis), 1e-12f);
                if (maxA - minB <= Touch * scale || maxB - minA <= Touch * scale) return false;
            }
        return true;
    }

    /// <summary>How much surface a face covers, and how much texture its UVs do, both over its triangles.</summary>
    public static (float Surface, float Texture) Areas(GeometryData.Face face)
    {
        float surface = 0f, texture = 0f;
        foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in Triangles(face))
        {
            surface += 0.5f * Float3.Length(Float3.Cross(b.Vert.Point - a.Vert.Point, c.Vert.Point - a.Vert.Point));
            Float2 ua = Get(a), ub = Get(b), uc = Get(c);
            texture += 0.5f * MathF.Abs((ub.X - ua.X) * (uc.Y - ua.Y) - (uc.X - ua.X) * (ub.Y - ua.Y));
        }
        return (surface, texture);
    }

    /// <summary>
    /// Lays out the quads joined to a quad as a grid following its layout: each quad across an edge from a laid out one
    /// carries that quad's sides on, as long as the surface says. Faces with other than four corners, and quads not
    /// among the faces, are left alone. A quad with no area in UV space is given the whole square first.
    /// </summary>
    public static void FollowQuad(GeometryData mesh, GeometryData.Face active, IReadOnlyCollection<GeometryData.Face> faces)
    {
        if (active.VertCount != 4) return;
        EnsureUV(mesh);
        var inSet = new HashSet<GeometryData.Face>(faces.Where(f => f.VertCount == 4));
        List<GeometryData.Loop> start = [.. active.Corners()];
        Float2 s0 = Get(start[0]), s1 = Get(start[1]), s2 = Get(start[2]);
        if (MathF.Abs((s1.X - s0.X) * (s2.Y - s0.Y) - (s2.X - s0.X) * (s1.Y - s0.Y)) < 1e-10f) Reset(mesh, [active]);

        var placed = new Dictionary<GeometryData.Face, Dictionary<GeometryData.Vertex, Float2>>
        {
            [active] = active.Corners().ToDictionary(c => c.Vert, Get),
        };
        var queue = new Queue<GeometryData.Face>();
        queue.Enqueue(active);
        while (queue.Count > 0)
        {
            GeometryData.Face from = queue.Dequeue();
            Dictionary<GeometryData.Vertex, Float2> known = placed[from];
            foreach (GeometryData.Loop corner in from.Corners())
            {
                GeometryData.Vertex a = corner.Vert, b = corner.Next!.Vert;
                foreach (GeometryData.Face next in corner.Edge.NeighborFaces())
                {
                    if (next == from || !inSet.Contains(next) || placed.ContainsKey(next)) continue;

                    // The side of the laid out quad leaving each end of the shared edge, carried on into the next quad
                    GeometryData.Vertex beforeA = corner.Prev!.Vert, afterB = corner.Next!.Next!.Vert;
                    GeometryData.Loop atA = next.GetLoop(a)!, atB = next.GetLoop(b)!;
                    GeometryData.Vertex farA = atA.Next!.Vert == b ? atA.Prev!.Vert : atA.Next!.Vert;
                    GeometryData.Vertex farB = atB.Next!.Vert == a ? atB.Prev!.Vert : atB.Next!.Vert;
                    var uvs = new Dictionary<GeometryData.Vertex, Float2>
                    {
                        [a] = known[a],
                        [b] = known[b],
                        [farA] = Carry(known[a], known[beforeA], a.Point, beforeA.Point, farA.Point),
                        [farB] = Carry(known[b], known[afterB], b.Point, afterB.Point, farB.Point),
                    };
                    placed[next] = uvs;
                    queue.Enqueue(next);
                }
            }
        }

        foreach ((GeometryData.Face face, Dictionary<GeometryData.Vertex, Float2> uvs) in placed)
            foreach (GeometryData.Loop corner in face.Corners())
                if (uvs.TryGetValue(corner.Vert, out Float2 uv)) Set(corner, uv);
        mesh.MarkChanged();
    }

    // Steps on from a UV point away from the one behind it, as far as the surface step is next to the one behind
    private static Float2 Carry(Float2 at, Float2 behind, Float3 point, Float3 behindPoint, Float3 ahead)
    {
        float back = Float3.Length(point - behindPoint);
        float scale = back > 1e-12f ? Float3.Length(ahead - point) / back : 1f;
        return at + (at - behind) * scale;
    }
}
