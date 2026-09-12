// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A world-space convex prism that stamps an area over already-rasterized geometry during a
/// bake (the payload of <see cref="NavMeshModifierVolume"/>, applied via Recast's convex
/// volume marking). Self-contained — no Transform or component references — so it is safe to
/// hand to a background build alongside <see cref="NavMeshGeometrySource"/>s. A volume only
/// re-marks voxels that geometry produced; it never creates walkable surface on its own, and
/// an <see cref="NavMeshAreas.NotWalkable"/> volume erases walkability inside its footprint
/// (Unity's Modifier Volume behaviour).
/// </summary>
public readonly struct NavMeshAreaVolume
{
    /// <summary>World-space convex footprint polygon; only X/Z are used.</summary>
    public readonly Float3[] Footprint;

    /// <summary>Vertical extent of the prism, in world space.</summary>
    public readonly float MinY, MaxY;

    /// <summary>The area stamped inside the volume (see <see cref="NavMeshAreas"/>).</summary>
    public readonly int Area;

    public NavMeshAreaVolume(Float3[] footprint, float minY, float maxY, int area)
    {
        Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        MinY = minY;
        MaxY = maxY;
        Area = area;
    }

    /// <summary>Conservative world AABB of the prism, for bounds filtering.</summary>
    public AABB Bounds
    {
        get
        {
            // XZ from the footprint, Y from the prism's own extent (the hull keeps only the
            // XZ-extreme corners, so their Y values don't span the prism).
            AABB footprint = AABB.FromPoints(Footprint);
            return new AABB(
                new Float3(footprint.Min.X, MinY, footprint.Min.Z),
                new Float3(footprint.Max.X, MaxY, footprint.Max.Z));
        }
    }

    /// <summary>
    /// Build the volume for an oriented box (local center/size under a world transform): the
    /// 8 corners are transformed, the vertical range is their Y span, and the footprint is the
    /// convex hull of their XZ projection. Yaw-only boxes yield a 4-gon; arbitrary rotations
    /// project to up-to-6-gons, which stay convex and are marked exactly.
    /// </summary>
    public static NavMeshAreaVolume FromOrientedBox(in Float4x4 localToWorld, Float3 center, Float3 size, int area)
    {
        Float3 half = size * 0.5f;
        var corners = new Float3[8];
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var local = new Float3(
                center.X + ((i & 1) == 0 ? -half.X : half.X),
                center.Y + ((i & 2) == 0 ? -half.Y : half.Y),
                center.Z + ((i & 4) == 0 ? -half.Z : half.Z));
            Float3 world = Float4x4.TransformPoint(local, localToWorld);
            corners[i] = world;
            minY = Math.Min(minY, (float)world.Y);
            maxY = Math.Max(maxY, (float)world.Y);
        }

        return new NavMeshAreaVolume(ConvexHullXZ(corners), minY, maxY, area);
    }

    /// <summary>2D convex hull (monotone chain) over the points' XZ projection.</summary>
    private static Float3[] ConvexHullXZ(Float3[] points)
    {
        var sorted = new List<Float3>(points);
        sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

        static double Cross(Float3 o, Float3 a, Float3 b)
            => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);

        // Monotone chain. <= 0 drops collinear (and duplicate) points, so degenerate
        // projections (an edge-on box) collapse below 3 vertices and the volume marks nothing.
        var hull = new List<Float3>(sorted.Count);
        foreach (Float3 p in sorted) // lower hull
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lowerEnd = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--) // upper hull (sorted[^1] already placed)
        {
            Float3 p = sorted[i];
            while (hull.Count >= lowerEnd && Cross(hull[^2], hull[^1], p) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1); // the upper hull's last point repeats hull[0]
        return [.. hull];
    }
}
