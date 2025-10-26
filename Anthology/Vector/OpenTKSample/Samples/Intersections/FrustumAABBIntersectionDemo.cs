// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class FrustumAABBIntersectionDemo : IDemo
{
    public string Name => "Frustum-AABB Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.75f; // Slow down time for better visibility

        // Create a frustum centered in the grid cell
        Double3 camPos = (Double3)position + new Double3(0, 0.6, 1.0);
        Double3 lookAt = (Double3)position + new Double3(0, 0, -0.3);
        Double3 forward = Double3.Normalize(lookAt - camPos);
        Double3 up = new Double3(0, 1, 0);

        double fov = Maths.PI / 5.5;
        double aspect = 1.3;
        double nearDist = 0.2;
        double farDist = 1.8;

        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Animate AABB through the frustum - moves along the frustum's view direction
        float moveTime = timeInSeconds * 0.8f;
        Float3 aabbOffset = new Float3(
            Maths.Sin(moveTime * 1.2f) * 0.9f,
            Maths.Sin(moveTime * 0.9f) * 0.6f,
            Maths.Sin(moveTime) * 0.8f  // Moves back and forth through frustum
        );

        float scale = 0.2f + Maths.Sin(timeInSeconds * 1.5f) * 0.05f;

        AABB aabb = new AABB(
            (Double3)position + (Double3)aabbOffset + new Double3(-scale, -scale, -scale),
            (Double3)position + (Double3)aabbOffset + new Double3(scale, scale, scale)
        );

        bool intersects = frustum.Intersects(aabb);

        // Draw frustum wireframe - color changes based on intersection for debugging
        Float4 frustumColor = intersects ? new Float4(0, 1, 0, 0.8f) : new Float4(1, 0, 0, 0.8f);
        Gizmo.DrawFrustum(frustum, frustumColor);

        // Draw AABB with strong color based on intersection
        Float4 aabbColor = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Gizmo.DrawAABBSolid(aabb, aabbColor * new Float4(1, 1, 1, 0.7f));
        Gizmo.DrawAABB(aabb, aabbColor);

        // Draw a line from AABB center to frustum origin for debugging
        Gizmo.DrawLine((Float3)aabb.Center, (Float3)camPos, new Float4(1, 1, 0, 0.3f));
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}


