// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class AABBAABBIntersectionDemo : IDemo
{
    public string Name => "AABB-AABB Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        // First AABB follows a figure-8 pattern
        float time1 = timeInSeconds * 0.6f;
        Float3 aabb1Offset = new Float3(
            Maths.Sin(time1) * 0.5f,
            Maths.Sin(time1 * 2) * 0.2f, // Figure-8 in XY
            Maths.Cos(time1 * 0.8f) * 0.3f
        );

        // First AABB also scales slightly
        float scale1 = 1.0f + Maths.Sin(timeInSeconds * 1.2f) * 0.15f;

        // Second AABB follows a circular orbit with different timing
        float time2 = timeInSeconds * 0.8f;
        Float3 aabb2Offset = new Float3(
            Maths.Cos(time2) * 0.6f,
            Maths.Sin(time2 * 1.5f) * 0.25f, // Vertical bobbing
            Maths.Sin(time2) * 0.4f
        );

        // Second AABB rotates its size in a different pattern
        float scale2 = 1.0f + Maths.Cos(timeInSeconds * 0.9f) * 0.2f;

        // Create the AABBs
        AABB aabb1 = new AABB(
            position + aabb1Offset + new Float3(-0.4f * scale1, -0.3f * scale1, -0.35f * scale1),
            position + aabb1Offset + new Float3(0.4f * scale1, 0.3f * scale1, 0.35f * scale1)
        );

        AABB aabb2 = new AABB(
            position + aabb2Offset + new Float3(-0.35f * scale2, -0.35f * scale2, -0.4f * scale2),
            position + aabb2Offset + new Float3(0.35f * scale2, 0.35f * scale2, 0.4f * scale2)
        );

        bool intersects = aabb1.Intersects(aabb2);

        // Color based on intersection with some pulsing
        Float4 aabb1Color = intersects ?
            new Float4(0, 0.7f, 0, 0.6f) :
            new Float4(0.7f, 0.2f, 0.2f, 0.6f);
        Float4 aabb2Color = intersects ?
            new Float4(0, 0.7f * 0.8f, 0.7f * 0.2f, 0.6f) :
            new Float4(0.2f, 0.2f, 0.7f, 0.6f);

        // Draw the main AABBs
        Gizmo.DrawAABBSolid(aabb1, aabb1Color);
        Gizmo.DrawAABBSolid(aabb2, aabb2Color);

        // If intersecting, calculate and draw the clipped/intersection AABB
        if (intersects)
        {
            AABB clipped = aabb1.ClippedBy(aabb2);

            // Draw bright wireframe for intersection
            Gizmo.DrawAABB(clipped, new Float4(1, 1, 0, 1));
        }
    }

    public Float3 GetBounds() => new Float3(3.0f, 2.5f, 3.0f);
}


