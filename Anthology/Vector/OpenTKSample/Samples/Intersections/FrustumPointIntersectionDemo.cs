// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample.Samples.Intersections;

public class FrustumPointIntersectionDemo : IDemo
{
    public string Name => "Frustum-Point Containment";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.75f; // Slow down time for better visibility

        // Create a frustum from view-projection matrices (demonstrating FromMatrices method)
        Float3 camPos = position + new Float3(0, 0.7f, 1.0f);
        Float3 lookAt = position + new Float3(0, 0, -0.2f);
        Float3 up = new Float3(0, 1, 0);

        float fov = Maths.PI / 5.5f;
        float aspect = 1.3f;
        float nearDist = 0.2f;
        float farDist = 1.6f;

        // Create view and projection matrices
        Float4x4 viewMatrix = Float4x4.CreateLookAt(camPos, lookAt, up);
        Float4x4 projectionMatrix = Float4x4.CreatePerspectiveFov(fov, aspect, nearDist, farDist);

        // Construct frustum from matrices instead of FromCamera
        Frustum frustum = Frustum.FromMatrices(viewMatrix, projectionMatrix);

        // Create multiple animated points orbiting in the frustum area
        int pointCount = 8;
        for (int i = 0; i < pointCount; i++)
        {
            float phase = (timeInSeconds + i * 0.4f) * 0.7f;
            float angle = (i / (float)pointCount) * 2f * (float)Maths.PI;

            // Each point follows a different orbital path
            Float3 pointOffset = new Float3(
                Maths.Cos(angle + phase * 1.5f) * (0.3f + Maths.Sin(phase * 1.2f) * 1.25f),
                Maths.Sin(phase * 1.5f) * 1.35f,
                Maths.Sin(angle + phase * 1.3f) * (0.4f + Maths.Cos(phase * 0.8f) * 1.3f) - 0.2f
            );

            Float3 point = position + pointOffset;
            bool contains = frustum.Contains(point);

            // Draw point with different colors based on containment
            Float4 pointColor = contains ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
            float pointSize = contains ? 0.08f : 0.05f;

            Gizmo.DrawIntersectionPoint(point, pointColor, pointSize);
        }

        // Draw the frustum
        Gizmo.DrawFrustum(frustum, new Float4(0.7f, 0.7f, 0.7f, 0.8f));
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}


