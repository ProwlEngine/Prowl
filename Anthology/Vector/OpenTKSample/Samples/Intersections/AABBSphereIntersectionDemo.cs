// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class AABBSphereIntersectionDemo : IDemo
{
    public string Name => "AABB-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // AABB follows a helical path
        float helixTime = timeInSeconds * 0.6f;
        Float3 aabbOffset = new Float3(
            Maths.Cos(helixTime) * 0.4f,
            Maths.Sin(helixTime * 2) * 0.3f, // Vertical oscillation
            Maths.Sin(helixTime) * 0.4f
        ) * 2f;

        // AABB also rotates and scales
        float scale = 1.0f + Maths.Sin(timeInSeconds * 1.5f) * 0.2f;

        AABB aabb = new AABB(
            position + aabbOffset + new Float3(-0.3f * scale, -0.3f * scale, -0.3f * scale),
            position + aabbOffset + new Float3(0.3f * scale, 0.3f * scale, 0.3f * scale)
        );

        // Sphere follows a different complex path - lemniscate (infinity symbol) in 3D
        float lemnTime = timeInSeconds * 0.9f;
        float sphereRadius = 0.6f + Maths.Sin(timeInSeconds * 2.5f) * 0.1f;
        Float3 sphereOffset = new Float3(
            (Maths.Sin(lemnTime) / (1 + Maths.Cos(lemnTime) * Maths.Cos(lemnTime))) * 0.5f,
            Maths.Cos(timeInSeconds * 1.8f) * 0.2f,
            (Maths.Sin(lemnTime) * Maths.Cos(lemnTime) / (1 + Maths.Cos(lemnTime) * Maths.Cos(lemnTime))) * 0.5f
        ) * 2f;

        Sphere sphere = new Sphere(position + sphereOffset, sphereRadius);
        bool intersects = aabb.Intersects(sphere);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Gizmo.DrawAABB(aabb, color);
        Gizmo.DrawSphereWireframe(sphere, color, 8);
    }

    public Float3 GetBounds() => new Float3(2.0f, 1.8f, 2.0f);
}


