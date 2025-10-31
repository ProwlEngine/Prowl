// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

public class GJKConeSphereDemo : IDemo
{
    public string Name => "GJK: Cone-Sphere";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f;

        // Rotating cone
        float rotation = timeInSeconds * 0.6f;
        float tilt = timeInSeconds * 0.4f;

        Float3 coneApex = position + new Float3(0, 0.6f + Maths.Sin(timeInSeconds * 1.5f) * 0.1f, 0);
        Float3 axisDir = new Float3(
            Maths.Sin(rotation) * Maths.Cos(tilt),
            -0.7f,
            Maths.Cos(rotation) * Maths.Cos(tilt)
        );

        float height = 0.8f + Maths.Sin(timeInSeconds * 1.2f) * 0.1f;
        float baseRadius = 0.4f + Maths.Cos(timeInSeconds * 1.8f) * 0.08f;

        Cone cone = Cone.FromAxisDirection(coneApex, axisDir, height, baseRadius);

        // Orbiting sphere
        float sphereTime = timeInSeconds * 1.1f;
        Float3 sphereOffset = new Float3(
            Maths.Cos(sphereTime) * 0.7f,
            Maths.Sin(sphereTime * 1.4f) * 0.5f,
            Maths.Sin(sphereTime) * 0.6f
        );

        float radius = 0.25f + Maths.Sin(timeInSeconds * 2.2f) * 0.05f;
        Sphere sphere = new Sphere(position + sphereOffset, radius);

        // Use GJK for collision detection
        bool intersects = GJK.Intersects(cone, sphere);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0.5f, 0, 1);

        Gizmo.DrawConeWireframe(cone, color, 16);
        Gizmo.DrawSphereWireframe(sphere, color, 12);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.5f, 2.5f);
}
