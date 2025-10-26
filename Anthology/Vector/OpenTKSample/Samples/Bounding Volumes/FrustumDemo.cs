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
        Double3 camPos = (Double3)position + new Double3(
            Maths.Cos(rotationAngle) * 1.2f,
            0.8f + heightOscillation,
            Maths.Sin(rotationAngle) * 1.2f
        );
        Double3 lookAt = (Double3)position;
        Double3 forward = Double3.Normalize(lookAt - camPos);
        Double3 up = new Double3(0, 1, 0);

        // Animate field of view
        double fov = Maths.PI / 3 + Maths.Sin(timeInSeconds * 0.8f) * 0.15;
        double aspect = 1.3;
        double nearDist = 0.3;
        double farDist = 1.5f + Maths.Sin(timeInSeconds * 0.4f) * 0.3f;

        // Create frustum from camera parameters
        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Draw the frustum
        Gizmo.DrawFrustum(frustum, new Float4(1, 1, 0, 1));

        // Draw camera position
        Gizmo.DrawIntersectionPoint((Float3)camPos, new Float4(0, 1, 1, 1), 0.08f);

        // Draw look-at target
        Gizmo.DrawIntersectionPoint((Float3)lookAt, new Float4(1, 0, 1, 1), 0.06f);
    }

    public Float3 GetBounds() => new Float3(3.0f, 2.5f, 3.0f);
}


