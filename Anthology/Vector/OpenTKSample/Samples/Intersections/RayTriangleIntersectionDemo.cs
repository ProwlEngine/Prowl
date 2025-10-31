// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class RayTriangleIntersectionDemo : IDemo
{
    public string Name => "Ray-Triangle Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // Triangle rotates on multiple axes
        float rotX = timeInSeconds * 0.4f;
        float rotY = timeInSeconds * 0.7f;
        float rotZ = timeInSeconds * 0.3f;

        // Create rotation matrices
        float cosX = Maths.Cos(rotX), sinX = Maths.Sin(rotX);
        float cosY = Maths.Cos(rotY), sinY = Maths.Sin(rotY);

        // Triangle vertices with multi-axis rotation
        Float3[] originalVerts = {
            new Float3(-0.8f, -0.6f, 0),
            new Float3(0.8f, -0.6f, 0),
            new Float3(0, 0.8f, 0)
        };

        Float3[] rotatedVerts = new Float3[3];
        for (int i = 0; i < 3; i++)
        {
            Float3 ov = originalVerts[i];
            // Rotate around Y axis
            float newX = ov.X * cosY + ov.Z * sinY;
            float newZ = -ov.X * sinY + ov.Z * cosY;
            ov = new Float3(newX, ov.Y, newZ);

            // Rotate around X axis
            float newY = ov.Y * cosX - ov.Z * sinX;
            newZ = ov.Y * sinX + ov.Z * cosX;

            rotatedVerts[i] = position + new Float3(ov.X, newY, newZ);
        }

        Triangle triangle = new Triangle(rotatedVerts[0], rotatedVerts[1], rotatedVerts[2]);

        // Calculate triangle center for better ray targeting
        Float3 triangleCenter = (rotatedVerts[0] + rotatedVerts[1] + rotatedVerts[2]) / 3.0f;

        // Ray orbits around the triangle and aims toward its center
        float orbitalTime = timeInSeconds * 0.8f;
        float orbitRadius = 1.5f + Maths.Sin(timeInSeconds * 0.6f) * 0.3f;

        Float3 rayStart = position + new Float3(
            Maths.Cos(orbitalTime) * orbitRadius,
            Maths.Sin(orbitalTime * 1.3f) * 0.8f, // Vertical movement
            Maths.Sin(orbitalTime) * orbitRadius
        );

        // Ray direction aims at triangle center with some wobble
        Float3 baseDirection = Float3.Normalize(triangleCenter - rayStart);
        Float3 wobble = new Float3(
            Maths.Sin(timeInSeconds * 2.5f) * 0.2f,
            Maths.Cos(timeInSeconds * 3.0f) * 0.2f,
            Maths.Sin(timeInSeconds * 2.0f) * 0.2f
        );
        Float3 rayDir = Float3.Normalize(baseDirection + wobble);

        Ray ray = new Ray(rayStart, rayDir);

        // Check for intersection
        bool hasHit = ray.Intersects(triangle, out float hitDistance, out float u, out float v);

        // Set ray color and length based on hit
        Float4 rayColor;
        float rayLength;

        if (hasHit)
        {
            rayColor = new Float4(0, 1, 0, 1); // Green when hitting
            rayLength = (float)hitDistance; // Stop at hit point
        }
        else
        {
            rayColor = new Float4(1, 1, 1, 1); // White when missing
            rayLength = 3.0f; // Full length
        }

        // Draw triangle with slight transparency
        Gizmo.DrawTriangle(triangle, new Float4(1, 0.5f, 0, 0.3f), true);
        Gizmo.DrawTriangle(triangle, new Float4(1, 0.5f, 0, 1), false);

        // Draw ray with dynamic color and length
        Gizmo.DrawRay(ray, rayLength, rayColor);

        if (hasHit)
        {
            Float3 hitPoint = (Float3)ray.GetPoint(hitDistance);

            // Pulsing hit point
            float pulseSize = 0.06f + Maths.Abs(Maths.Sin(timeInSeconds * 8.0f)) * 0.04f;
            Gizmo.DrawIntersectionPoint(hitPoint, new Float4(1, 1, 0, 1), pulseSize);
        }
    }

    public Float3 GetBounds() => new Float3(3.0f, 2.5f, 3.0f);
}


