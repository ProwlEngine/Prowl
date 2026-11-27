// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>What a ray hit in a model's generated geometry.</summary>
public readonly struct CSGPickHit
{
    /// <summary>The brush the hit face came from.</summary>
    public readonly int BrushId;

    /// <summary>Index of that brush's own face, and so of its plane and surface data.</summary>
    public readonly int SourceFaceIndex;

    /// <summary>Where the ray met the surface, in model space.</summary>
    public readonly Float3 Point;

    /// <summary>Outward normal of the hit face, in model space.</summary>
    public readonly Float3 Normal;

    public readonly float Distance;

    public CSGPickHit(int brushId, int sourceFaceIndex, Float3 point, Float3 normal, float distance)
    {
        BrushId = brushId;
        SourceFaceIndex = sourceFaceIndex;
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    public bool IsValid => BrushId >= 0;
}

/// <summary>Picking against a model's generated output polygons, which record the brush and face they came from.</summary>
public static class CSGPicking
{
    /// <summary>Nearest hit along the ray, in the geometry's own space.</summary>
    public static bool Raycast(GeometryData? geometry, Float3 origin, Float3 direction, out CSGPickHit hit)
    {
        hit = default;
        if (geometry == null || geometry.Faces.Count == 0) return false;

        float best = float.MaxValue;
        bool found = false;

        foreach (GeometryData.Face face in geometry.Faces)
        {
            if (!RaycastFace(face, origin, direction, out float distance, out Float3 point, out Float3 normal))
                continue;

            if (distance >= best) continue;

            best = distance;
            hit = new CSGPickHit(CSGScene.GetBrushId(face), CSGScene.GetSourceFaceIndex(face),
                                 point, normal, distance);
            found = true;
        }

        return found;
    }

    /// <summary>Every hit along the ray, nearest first.</summary>
    public static List<CSGPickHit> RaycastAll(GeometryData? geometry, Float3 origin, Float3 direction)
    {
        var hits = new List<CSGPickHit>();
        if (geometry == null) return hits;

        foreach (GeometryData.Face face in geometry.Faces)
        {
            if (RaycastFace(face, origin, direction, out float distance, out Float3 point, out Float3 normal))
                hits.Add(new CSGPickHit(CSGScene.GetBrushId(face), CSGScene.GetSourceFaceIndex(face),
                                        point, normal, distance));
        }

        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    /// <summary>Ray against a convex polygon from either side. The normal comes back facing the ray.</summary>
    public static bool RaycastPolygon(IReadOnlyList<Float3> ring, Float3 normal, Float3 origin, Float3 direction,
                                      out float distance, out Float3 point, out Float3 facing)
    {
        distance = 0f;
        point = Float3.Zero;
        facing = Float3.Dot(normal, direction) > 0f ? -normal : normal;
        if (ring.Count < 3) return false;

        float denom = Float3.Dot(facing, direction);
        if (denom > -1e-9f) return false;

        distance = (Float3.Dot(facing, ring[0]) - Float3.Dot(facing, origin)) / denom;
        if (distance < 0f) return false;

        point = origin + direction * distance;

        // Inside every edge, measured against the ring's own winding whichever way it runs
        Float3 wound = Float3.Zero;
        for (int i = 0; i < ring.Count; i++) wound += Float3.Cross(ring[i], ring[(i + 1) % ring.Count]);

        for (int i = 0; i < ring.Count; i++)
        {
            Float3 a = ring[i], b = ring[(i + 1) % ring.Count];
            if (Float3.Dot(Float3.Cross(b - a, point - a), wound) < -1e-5f) return false;
        }
        return true;
    }

    /// <summary>Ray against one convex output polygon.</summary>
    private static bool RaycastFace(GeometryData.Face face, Float3 origin, Float3 direction,
                                    out float distance, out Float3 point, out Float3 normal)
    {
        distance = 0f;
        point = Float3.Zero;
        normal = Float3.UnitY;

        List<GeometryData.Vertex> ring = face.NeighborVertices();
        if (ring.Count < 3) return false;

        normal = face.Normal();
        if (Float3.LengthSquared(normal) < 0.5f) return false;

        // Back faces are seen through, which is what lets a ray from inside a solid reach the room around it
        float denom = Float3.Dot(normal, direction);
        if (denom > -1e-9f) return false;

        float d = Float3.Dot(normal, ring[0].Point);
        distance = (d - Float3.Dot(normal, origin)) / denom;
        if (distance < 0f) return false;

        point = origin + direction * distance;

        for (int i = 0; i < ring.Count; i++)
        {
            Float3 a = ring[i].Point, b = ring[(i + 1) % ring.Count].Point;
            if (Float3.Dot(Float3.Cross(b - a, point - a), normal) < -1e-5f) return false;
        }

        return true;
    }
}
