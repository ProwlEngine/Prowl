// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

public class GJKFrustumSphereDemo : IDemo
{
    public string Name => "GJK: Frustum-Sphere";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f;

        // Create animated frustum
        float rotation = timeInSeconds * 0.4f;
        Float3 camPos = position + new Float3(
            Maths.Cos(rotation) * 0.3f,
            0.5f,
            Maths.Sin(rotation) * 0.3f
        );
        Float3 lookAt = position;
        Float3 forward = Float3.Normalize(lookAt - camPos);
        Float3 up = new Float3(0, 1, 0);

        float fov = Maths.PI / 4 + Maths.Sin(timeInSeconds * 1.5f) * 0.1f;
        float aspect = 1.2f;
        float nearDist = 0.2f;
        float farDist = 1.2f;

        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Orbiting sphere
        float sphereTime = timeInSeconds * 1.3f;
        Float3 sphereOffset = new Float3(
            Maths.Cos(sphereTime) * 0.7f,
            Maths.Sin(sphereTime * 1.5f) * 0.5f,
            Maths.Sin(sphereTime) * 0.6f
        );

        float radius = 0.25f + Maths.Sin(timeInSeconds * 2.5f) * 0.05f;
        Sphere sphere = new Sphere(position + sphereOffset, radius);

        // Use GJK for collision detection
        bool intersects = GJK.Intersects(frustum, sphere);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0.3f, 1, 1);

        Gizmo.DrawFrustum(frustum, new Float4(color.X, color.Y, color.Z, 0.6f));
        Gizmo.DrawSphereWireframe(sphere, color, 12);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.5f, 2.5f);
}
