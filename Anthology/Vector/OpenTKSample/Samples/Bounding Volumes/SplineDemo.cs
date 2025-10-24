// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample;

public class SplineDemo : IDemo
{
    public string Name => "Spline Path Animation";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // Create control points for a complex 3D path
        Double3[] controlPoints = {
            (Double3)position + new Double3(-2, -1, -1.5f),
            (Double3)position + new Double3(-1, 0.5f, -0.5f),
            (Double3)position + new Double3(0, -0.5f, 1),
            (Double3)position + new Double3(1.5f, 1, 0.5f),
            (Double3)position + new Double3(2, -0.5f, -1),
            (Double3)position + new Double3(0.5f, -1.5f, 0),
            (Double3)position + new Double3(-1.5f, -0.5f, 1.5f)
        };

        // Create different types of splines
        var catmullRomSpline = Spline.CreateCatmullRom(controlPoints, closed: true, 0.5f);
        var bSpline = Spline.CreateBSpline(controlPoints, closed: true);

        // Draw spline paths
        DrawSplinePath(catmullRomSpline, new Float4(0, 1, 0.5f, 0.8f), 100); // Green-cyan
        DrawSplinePath(bSpline, new Float4(1, 0.5f, 0, 0.6f), 100); // Orange, offset slightly

        // Animate objects following the splines
        float t1 = (timeInSeconds * 0.3f) % 1.0f;
        float t2 = (timeInSeconds * 0.25f) % 1.0f;

        // Object 1: Following Catmull-Rom spline with frame visualization
        var frame1 = catmullRomSpline.EvaluateFrame(t1, Spline.UpVectorMethod.FrenetFrame);
        DrawSplineFrame(frame1, 0.3f);

        // Draw a small cube at the position
        AABB cube1 = new AABB(
            (Double3)frame1.Position + new Double3(-0.1f, -0.1f, -0.1f),
            (Double3)frame1.Position + new Double3(0.1f, 0.1f, 0.1f)
        );
        Gizmo.DrawAABBSolid(cube1, new Float4(0, 1, 0.5f, 0.9f));

        // Object 2: Following B-spline
        Float3 pos2 = (Float3)bSpline.Evaluate(t2);
        
        // Draw a small sphere
        Sphere sphere2 = new Sphere((Double3)pos2, 0.12f);
        Gizmo.DrawSphereWireframe(sphere2, new Float4(1, 0.5f, 0, 1), 8);
    }

    private void DrawSplinePath(Spline spline, Float4 color, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float t1 = i / (float)segments;
            float t2 = (i + 1) / (float)segments;

            Float3 p1 = (Float3)spline.Evaluate(t1);
            Float3 p2 = (Float3)spline.Evaluate(t2);

            Gizmo.DrawLine(p1, p2, color);
        }
    }

    private void DrawSplineFrame(Spline.SplineFrame frame, float size)
    {
        // Draw coordinate frame
        Gizmo.DrawLine((Float3)frame.Position, (Float3)frame.Position + (Float3)frame.Right * size, new Float4(1, 0, 0, 0.8f)); // Red = Right
        Gizmo.DrawLine((Float3)frame.Position, (Float3)frame.Position + (Float3)frame.Up * size, new Float4(0, 1, 0, 0.8f));    // Green = Up
        Gizmo.DrawLine((Float3)frame.Position, (Float3)frame.Position + (Float3)frame.Forward * size, new Float4(0, 0, 1, 0.8f)); // Blue = Forward
    }

    public Float3 GetBounds() => new Float3(5.5f, 4.0f, 4.0f);
}
