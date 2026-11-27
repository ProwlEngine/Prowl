// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class SphereSphereIntersectionDemo : IDemo
{
    public string Name => "Sphere-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // Create complex orbital motion for both spheres
        float time1 = timeInSeconds * 0.8f;
        float time2 = timeInSeconds * 1.2f; // Different speed for variety

        // Sphere 1: Figure-8 pattern in XY plane with Z oscillation
        Float3 sphere1Offset = new Float3(
            Maths.Sin(time1) * 0.6f,
            Maths.Sin(time1 * 2) * 0.3f, // Figure-8 motion
            Maths.Cos(time1 * 0.7f) * 0.2f
        ) * 2f;

        // Sphere 2: Circular orbit in XZ plane with Y bobbing
        Float3 sphere2Offset = new Float3(
            Maths.Cos(time2) * 0.5f,
            Maths.Sin(time2 * 3) * 0.25f, // Fast bobbing
            Maths.Sin(time2) * 0.5f
        ) * 2f;

        // Add some size pulsing based on proximity
        float distance = Float3.Length(sphere1Offset - sphere2Offset);
        float proximityEffect = Maths.Max(0, 1.0f - distance / 1.5f);
        float size1 = 0.7f + proximityEffect * 0.2f + Maths.Sin(timeInSeconds * 4) * 0.05f;
        float size2 = 0.7f + proximityEffect * 0.15f + Maths.Cos(timeInSeconds * 3.5f) * 0.05f;

        Sphere sphere1 = new Sphere((Float3)position + (Float3)sphere1Offset, size1);
        Sphere sphere2 = new Sphere((Float3)position + (Float3)sphere2Offset, size2);
        bool intersects = sphere1.Intersects(sphere2);

        // Color changes based on intersection and proximity
        Float4 baseColor = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Float4 sphere1Color = baseColor + new Float4(proximityEffect * 0.3f, 0, proximityEffect * 0.2f, 0);
        Float4 sphere2Color = baseColor + new Float4(0, proximityEffect * 0.2f, proximityEffect * 0.3f, 0);

        Gizmo.DrawSphereWireframe(sphere1, sphere1Color, 8);
        Gizmo.DrawSphereWireframe(sphere2, sphere2Color, 8);
    }

    public Float3 GetBounds() => new Float3(2.4f, 1.6f, 2.4f);
}


