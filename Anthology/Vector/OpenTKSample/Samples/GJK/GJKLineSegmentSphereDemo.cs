// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

public class GJKLineSegmentSphereDemo : IDemo
{
    public string Name => "GJK: LineSegment-Sphere";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f;

        // Rotating line segment
        float rotation = timeInSeconds * 0.7f;
        float tilt = timeInSeconds * 0.5f;

        float cosRot = Maths.Cos(rotation);
        float sinRot = Maths.Sin(rotation);
        float cosTilt = Maths.Cos(tilt);
        float sinTilt = Maths.Sin(tilt);

        Float3 segmentDir = new Float3(
            cosRot * cosTilt,
            sinTilt,
            sinRot * cosTilt
        );

        float segmentLength = 0.8f + Maths.Sin(timeInSeconds * 1.8f) * 0.2f;
        Float3 segmentStart = position - segmentDir * (segmentLength * 0.5f);
        Float3 segmentEnd = position + segmentDir * (segmentLength * 0.5f);

        LineSegment lineSegment = new LineSegment((Double3)segmentStart, (Double3)segmentEnd);

        // Orbiting sphere
        float sphereTime = timeInSeconds * 1.4f;
        Float3 sphereOffset = new Float3(
            Maths.Cos(sphereTime) * 0.6f,
            Maths.Sin(sphereTime * 1.6f) * 0.4f,
            Maths.Sin(sphereTime) * 0.7f
        );

        float radius = 0.3f + Maths.Cos(timeInSeconds * 2.2f) * 0.08f;
        Sphere sphere = new Sphere((Double3)position + (Double3)sphereOffset, radius);

        // Use GJK for collision detection
        bool intersects = GJK.Intersects(lineSegment, sphere);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 1, 0, 1);

        // Draw line segment
        Gizmo.DrawLine(segmentStart, segmentEnd, color);
        // Draw endpoints
        Gizmo.DrawIntersectionPoint(segmentStart, color, 0.05f);
        Gizmo.DrawIntersectionPoint(segmentEnd, color, 0.05f);

        Gizmo.DrawSphereWireframe(sphere, color, 12);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.5f, 2.5f);
}
