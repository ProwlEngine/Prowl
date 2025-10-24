// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples.Intersections;

public class RaySphereIntersectionDemo : IDemo
{
    public string Name => "Ray-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // Sphere follows a complex 3D path - lemniscate (infinity symbol) with vertical movement
        float sphereTime = timeInSeconds * 0.7f;
        Float3 sphereOffset = new Float3(
            (float)(Maths.Sin(sphereTime) / (1 + Maths.Cos(sphereTime) * Maths.Cos(sphereTime))) * 0.6f,
            Maths.Sin(sphereTime * 1.5f) * 0.4f, // Vertical bobbing
            (float)(Maths.Sin(sphereTime) * Maths.Cos(sphereTime) / (1 + Maths.Cos(sphereTime) * Maths.Cos(sphereTime))) * 0.4f
        );

        // Sphere also pulses in size
        float sphereRadius = 0.6f + Maths.Sin(timeInSeconds * 2.0f) * 0.15f;
        Sphere sphere = new Sphere(position + sphereOffset, sphereRadius);

        // Ray orbits around the center, aiming toward the sphere with some lead prediction
        float rayTime = timeInSeconds * 1.2f;
        float orbitRadius = 1.8f + Maths.Cos(timeInSeconds * 0.4f) * 0.3f;

        Float3 rayStart = position + new Float3(
            Maths.Cos(rayTime) * orbitRadius,
            Maths.Sin(rayTime * 0.8f) * 0.6f + 0.2f, // Vertical movement with offset
            Maths.Sin(rayTime) * orbitRadius
        );

        // Calculate ray direction - aim at predicted sphere position with some wobble
        Float3 sphereVelocity = new Float3(
            (float)(Maths.Cos(sphereTime) * (1 - Maths.Cos(sphereTime) * Maths.Cos(sphereTime)) - Maths.Sin(sphereTime) * 2f * Maths.Cos(sphereTime) * Maths.Sin(sphereTime)) /
            ((1f + Maths.Cos(sphereTime) * Maths.Cos(sphereTime)) * (1f + Maths.Cos(sphereTime) * Maths.Cos(sphereTime))) * 0.6f * 0.7f,
            (float)Maths.Cos(sphereTime * 1.5f) * 0.4f * 1.5f,
            0 // Simplified Z velocity
        );

        Float3 predictedSpherePos = (Float3)sphere.Center + sphereVelocity * 0.3f; // Lead the target
        Float3 baseDirection = Float3.Normalize(predictedSpherePos - rayStart);

        // Add some wobble to make it more interesting
        Float3 wobble = new Float3(
            Maths.Sin(timeInSeconds * 3.5f) * 0.15f,
            Maths.Cos(timeInSeconds * 4.0f) * 0.12f,
            Maths.Sin(timeInSeconds * 2.8f) * 0.18f
        );
        Float3 rayDir = Float3.Normalize(baseDirection + wobble);

        Ray ray = new Ray(rayStart, rayDir);

        // Check for intersection
        bool hasHit = ray.Intersects(sphere, out double t0, out double t1);

        // Dynamic ray properties based on intersection
        Float4 rayColor;
        float rayLength;

        if (hasHit)
        {
            rayColor = new Float4(0, 1, 0.3f, 1); // Green-cyan when hitting
            rayLength = (float)t0; // Stop at first intersection point
        }
        else
        {
            rayColor = new Float4(1, 0.8f, 0.2f, 1); // Orange when missing
            rayLength = 3.5f; // Full length
        }

        // Draw sphere with transparency
        Float4 sphereColor = hasHit ?
            new Float4(0.2f, 1, 0.4f, 0.7f) :
            new Float4(0.4f, 0.6f, 1, 0.5f);

        // Draw solid sphere
        Gizmo.DrawSphereWireframe(sphere, sphereColor, 12);

        // Draw ray with dynamic properties
        Gizmo.DrawRay(ray, rayLength, rayColor);

        // intersection visualization
        if (hasHit)
        {
            Float3 hitPoint1 = (Float3)ray.GetPoint(t0);
            Float3 hitPoint2 = (Float3)ray.GetPoint(t1);

            // hit points
            Gizmo.DrawIntersectionPoint(hitPoint1, new Float4(1, 1, 0, 1), 0.06f);

            if (Maths.Abs(t1 - t0) > 0.001f) // Only draw second point if different
            {
                Gizmo.DrawIntersectionPoint(hitPoint2, new Float4(1, 0.8f, 0, 1), 0.06f);

                // Draw line segment inside sphere
                Gizmo.DrawLine(hitPoint1, hitPoint2, new Float4(1, 1, 1, 0.8f));
            }

            // Draw normal vectors at hit points
            Float3 normal1 = Float3.Normalize((Float3)hitPoint1 - (Float3)sphere.Center);
            Float3 normal2 = Float3.Normalize((Float3)hitPoint2 - (Float3)sphere.Center);

            Gizmo.DrawLine(hitPoint1, hitPoint1 + normal1 * 0.3f, new Float4(0, 1, 1, 0.8f));
            if (Maths.Abs(t1 - t0) > 0.001f)
                Gizmo.DrawLine(hitPoint2, hitPoint2 + normal2 * 0.3f, new Float4(0, 0.8f, 1, 0.8f));
        }
    }

    public Float3 GetBounds() => new Float3(5.0f, 3.0f, 4.0f);
}


