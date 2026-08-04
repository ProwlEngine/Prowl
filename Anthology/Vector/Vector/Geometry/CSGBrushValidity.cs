// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry;

/// <summary>What is wrong with a brush shape, if anything.</summary>
[Flags]
public enum BrushValidity
{
    Valid = 0,
    /// <summary>Fewer than four faces, so no volume is enclosed.</summary>
    TooFewFaces = 1 << 0,
    /// <summary>An edge is not shared by exactly two faces - the surface has a hole or a seam.</summary>
    NotClosed = 1 << 1,
    /// <summary>A vertex lies outside a face plane: concave or self-intersecting. CSG assumes convex
    /// brushes, so this gives wrong results rather than merely ugly ones.</summary>
    NotConvex = 1 << 2,
    /// <summary>A face is degenerate - zero area, or fewer than three distinct corners.</summary>
    DegenerateFace = 1 << 3,
    /// <summary>The shape encloses no measurable volume.</summary>
    ZeroVolume = 1 << 4,
    /// <summary>Face windings disagree, so the solid is inside-out in places.</summary>
    InconsistentWinding = 1 << 5,
}

/// <summary>
/// Validity checks for brush shapes, so a broken brush can be reported rather than silently
/// producing junk geometry.
/// </summary>
public static class CSGBrushValidity
{
    /// <summary>How far a vertex may sit outside a face plane before the shape counts as
    /// non-convex is set by <paramref name="planarTolerance"/>.</summary>
    public static BrushValidity Check(GeometryData shape, float planarTolerance = 1e-3f)
    {
        var result = BrushValidity.Valid;
        if (shape == null) return BrushValidity.TooFewFaces;

        if (shape.Faces.Count < 4)
            result |= BrushValidity.TooFewFaces;

        // Closed surface: every edge used by exactly two faces.
        foreach (var edge in shape.Edges)
        {
            if (edge.NeighborFaces().Count != 2) { result |= BrushValidity.NotClosed; break; }
        }

        var planes = new List<Plane>(shape.Faces.Count);
        var centroids = new List<Float3>(shape.Faces.Count);

        foreach (var face in shape.Faces)
        {
            var verts = face.NeighborVertices();
            if (verts.Count < 3) { result |= BrushValidity.DegenerateFace; continue; }

            // Newell's method: correct for n-gons, and its magnitude is twice the face area.
            Float3 normal = Float3.Zero;
            Float3 centroid = Float3.Zero;
            for (int i = 0; i < verts.Count; i++)
            {
                Float3 a = verts[i].Point, b = verts[(i + 1) % verts.Count].Point;
                normal.X += (a.Y - b.Y) * (a.Z + b.Z);
                normal.Y += (a.Z - b.Z) * (a.X + b.X);
                normal.Z += (a.X - b.X) * (a.Y + b.Y);
                centroid += a;
            }

            if (Float3.LengthSquared(normal) < 1e-12f) { result |= BrushValidity.DegenerateFace; continue; }

            normal = Float3.Normalize(normal);
            centroid /= verts.Count;
            planes.Add(new Plane(normal, Float3.Dot(normal, centroid)));
            centroids.Add(centroid);
        }

        if (planes.Count < 4)
            return result | BrushValidity.TooFewFaces;

        // Convexity: with outward faces, every vertex sits on or behind every face plane.
        foreach (var plane in planes)
        {
            foreach (var vert in shape.Vertices)
            {
                if (plane.GetSignedDistanceToPoint(vert.Point) > planarTolerance)
                {
                    result |= BrushValidity.NotConvex;
                    break;
                }
            }
            if ((result & BrushValidity.NotConvex) != 0) break;
        }

        // Consistent outward winding: face planes should point away from the shape's centre.
        Float3 center = Float3.Zero;
        foreach (var v in shape.Vertices) center += v.Point;
        if (shape.Vertices.Count > 0) center /= shape.Vertices.Count;

        int outward = 0;
        for (int i = 0; i < planes.Count; i++)
            if (Float3.Dot(centroids[i] - center, planes[i].Normal) > 0f) outward++;

        // All-inward is a uniformly flipped solid, which is recoverable; a mixture is not.
        if (outward != 0 && outward != planes.Count)
            result |= BrushValidity.InconsistentWinding;

        if (ApproximateVolume(shape, center) < 1e-9f)
            result |= BrushValidity.ZeroVolume;

        return result;
    }

    /// <summary>Convenience for the common question.</summary>
    public static bool IsValid(GeometryData shape, float planarTolerance = 1e-3f)
        => Check(shape, planarTolerance) == BrushValidity.Valid;

    /// <summary>Signed volume via the divergence theorem, fanning each face from the shape centre.</summary>
    private static float ApproximateVolume(GeometryData shape, Float3 center)
    {
        float volume = 0f;
        foreach (var face in shape.Faces)
        {
            var verts = face.NeighborVertices();
            for (int i = 1; i < verts.Count - 1; i++)
            {
                Float3 a = verts[0].Point - center;
                Float3 b = verts[i].Point - center;
                Float3 c = verts[i + 1].Point - center;
                volume += Float3.Dot(a, Float3.Cross(b, c)) / 6f;
            }
        }
        return MathF.Abs(volume);
    }
}
