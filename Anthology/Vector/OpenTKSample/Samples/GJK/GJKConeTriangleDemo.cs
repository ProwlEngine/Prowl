// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

public class GJKConeTriangleDemo : IDemo
{
    public string Name => "GJK: Cone-Triangle";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f;

        // Cone pointing upward with slight wobble
        Float3 coneApex = position + new Float3(0, -0.5f, 0);
        Float3 axisDir = new Float3(
            Maths.Sin(timeInSeconds * 1.2f) * 0.2f,
            1.0f,
            Maths.Cos(timeInSeconds * 1.3f) * 0.2f
        );

        float height = 1.0f + Maths.Sin(timeInSeconds * 1.5f) * 0.1f;
        float baseRadius = 0.45f;

        Cone cone = Cone.FromAxisDirection(coneApex, axisDir, height, baseRadius);

        // Rotating triangle
        float triRotation = timeInSeconds * 0.6f;
        float triTilt = timeInSeconds * 0.4f;

        Float3[] verts = new Float3[3];
        Float3[] baseVerts = {
            new Float3(-0.5f, 0, 0.3f),
            new Float3(0.5f, 0, 0.3f),
            new Float3(0, 0.7f, -0.3f)
        };

        for (int i = 0; i < 3; i++)
        {
            Float3 v = baseVerts[i];
            // Rotate around Y
            float x = v.X * Maths.Cos(triRotation) - v.Z * Maths.Sin(triRotation);
            float z = v.X * Maths.Sin(triRotation) + v.Z * Maths.Cos(triRotation);
            // Rotate around X (tilt)
            float y = v.Y * Maths.Cos(triTilt) - z * Maths.Sin(triTilt);
            z = v.Y * Maths.Sin(triTilt) + z * Maths.Cos(triTilt);

            Float3 triOffset = new Float3(
                Maths.Cos(timeInSeconds * 1.1f) * 0.6f,
                Maths.Sin(timeInSeconds * 1.4f) * 0.4f,
                Maths.Sin(timeInSeconds * 0.9f) * 0.5f
            );

            verts[i] = position + triOffset + new Float3(x, y, z);
        }

        Triangle triangle = new Triangle(verts[0], verts[1], verts[2]);

        // Use GJK for collision detection
        bool intersects = GJK.Intersects(cone, triangle);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(0, 0.7f, 1, 1);

        Gizmo.DrawConeWireframe(cone, color, 16);
        Gizmo.DrawTriangle(triangle, new Float4(color.X, color.Y, color.Z, 0.3f), true);
        Gizmo.DrawTriangle(triangle, color, false);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.5f, 2.5f);
}
