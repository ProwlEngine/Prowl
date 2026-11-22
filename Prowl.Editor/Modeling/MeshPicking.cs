// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>Finding the parts of a mesh under a ray, for picking and for telling what is in sight.</summary>
public static class MeshPicking
{
    /// <summary>
    /// The nearest face a world ray meets, either side, and how far along the ray in world units. Faces are tested as fans
    /// of triangles from their first corner.
    /// </summary>
    public static bool Raycast(GeometryData mesh, Float4x4 toWorld, Ray ray, out GeometryData.Face? hit, out float distance,
                               Func<GeometryData.Face, bool>? skip = null)
    {
        hit = null;
        distance = float.MaxValue;
        Float4x4 toLocal = toWorld.Invert();
        Float3 origin = Float4x4.TransformPoint(ray.Origin, toLocal);
        Float3 direction = Float3.NormalizeSafe(Float4x4.TransformNormal(ray.Direction, toLocal), Float3.UnitZ);

        foreach (GeometryData.Face face in mesh.Faces)
        {
            if (skip != null && skip(face) || face.Loop == null) continue;
            GeometryData.Loop first = face.Loop;
            GeometryData.Loop loop = first.Next!;
            while (loop.Next != null && loop.Next != first)
            {
                if (Intersection.RayTriangle(origin, direction, first.Vert.Point, loop.Vert.Point, loop.Next.Vert.Point, out float t, out _, out _))
                {
                    float world = Float3.Distance(ray.Origin, Float4x4.TransformPoint(origin + direction * t, toWorld));
                    if (world < distance)
                    {
                        distance = world;
                        hit = face;
                    }
                }
                loop = loop.Next;
            }
        }
        return hit != null;
    }

    /// <summary>
    /// Whether a point of the mesh is in sight from a camera at <paramref name="eye"/>: nothing of the meshes tested lies
    /// between them. A point on the face it belongs to counts as seen.
    /// </summary>
    public static bool Seen(Float3 worldPoint, Float3 eye, Func<Ray, float> nearestHit)
    {
        Float3 toPoint = worldPoint - eye;
        float length = Float3.Length(toPoint);
        if (length < 1e-6f) return true;
        float hit = nearestHit(new Ray(eye, toPoint / length));
        return hit >= length - MathF.Max(1e-3f, length * 1e-4f);
    }
}
