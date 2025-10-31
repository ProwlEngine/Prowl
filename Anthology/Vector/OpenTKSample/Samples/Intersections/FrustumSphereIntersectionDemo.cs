// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class FrustumSphereIntersectionDemo : IDemo
{
    public string Name => "Frustum-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.75f; // Slow down time for better visibility

        // Create a frustum centered in the grid cell
        float frustumRotation = timeInSeconds * 0.2f;
        Float3 camPos = position + new Float3(
            Maths.Sin(frustumRotation) * 0.15f,
            0.7f,
            0.9f + Maths.Cos(frustumRotation) * 0.1f
        );
        Float3 lookAt = (Float3)position + new Float3(0, 0, -0.2f);
        Float3 forward = Float3.Normalize(lookAt - camPos);
        Float3 up = new Float3(0, 1, 0);

        float fov = Maths.PI / 5.5f;
        float aspect = 1.3f;
        float nearDist = 0.2f;
        float farDist = 1.6f;

        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Animate sphere moving through the frustum
        float sphereTime = timeInSeconds * 0.9f;
        Float3 sphereOffset = new Float3(
            Maths.Sin(sphereTime * 1.3f) * 0.8f,
            Maths.Sin(sphereTime * 1.1f) * 0.6f,
            Maths.Sin(sphereTime) * 0.7f  // Moves back and forth through frustum
        );

        float radius = 0.25f + Maths.Sin(timeInSeconds * 2.0f) * 0.05f;
        Sphere sphere = new Sphere(position + sphereOffset, radius);

        bool intersects = frustum.Intersects(sphere);

        // Draw frustum
        Float4 frustumColor = new Float4(0.7f, 0.7f, 0.7f, 0.8f);
        Gizmo.DrawFrustum(frustum, frustumColor);

        // Draw sphere with color based on intersection
        Float4 sphereColor = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Gizmo.DrawSphereWireframe(sphere, sphereColor, 12);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}


