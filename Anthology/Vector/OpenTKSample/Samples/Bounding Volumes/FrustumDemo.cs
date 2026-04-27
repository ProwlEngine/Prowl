// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample;

public class FrustumDemo : IDemo
{
    public string Name => "Frustum";

    public void Draw(Float3 position, float timeInSeconds)
    {
        // Animate camera parameters
        float rotationAngle = timeInSeconds * 0.3f;
        float heightOscillation = Maths.Sin(timeInSeconds * 0.5f) * 0.2f;

        // Camera positioned close to the grid cell, looking at the center
        Float3 camPos = position + new Float3(
            Maths.Cos(rotationAngle) * 1.2f,
            0.8f + heightOscillation,
            Maths.Sin(rotationAngle) * 1.2f
        );
        Float3 lookAt = position;
        Float3 forward = Float3.Normalize(lookAt - camPos);
        Float3 up = new Float3(0, 1, 0);

        // Animate field of view
        float fov = Maths.PI / 3 + Maths.Sin(timeInSeconds * 0.8f) * 0.15f;
        float aspect = 1.3f;
        float nearDist = 0.3f;
        float farDist = 1.5f + Maths.Sin(timeInSeconds * 0.4f) * 0.3f;

        // Create frustum from camera parameters
        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Draw the frustum
        Gizmo.DrawFrustum(frustum, new Float4(1, 1, 0, 1));

        // Draw camera position
        Gizmo.DrawIntersectionPoint(camPos, new Float4(0, 1, 1, 1), 0.08f);

        // Draw look-at target
        Gizmo.DrawIntersectionPoint(lookAt, new Float4(1, 0, 1, 1), 0.06f);
    }

    public Float3 GetBounds() => new Float3(3.0f, 2.5f, 3.0f);
}


