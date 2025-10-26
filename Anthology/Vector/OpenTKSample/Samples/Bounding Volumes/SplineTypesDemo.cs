// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace OpenTKSample;

public class SplineTypesDemo : IDemo
{
    public string Name => "Spline Type Comparison";

    public void Draw(Float3 position, float timeInSeconds)
    {
        // Animated control points in X-Z plane
        Double3[] baseControlPoints = {
            new Double3(-1.5f, 0, -0.5f),
            new Double3(-0.5f, 0, 1),
            new Double3(0.5f, 0, -0.8f),
            new Double3(1.5f, 0, 0.5f)
        };

        // Add animation to control points
        Double3[] animatedPoints = new Double3[baseControlPoints.Length];
        for (int i = 0; i < baseControlPoints.Length; i++)
        {
            float phase = timeInSeconds * 0.5f + i * 0.7f;
            Double3 animation = new Double3(
                (float)Math.Sin(phase) * 0.1f,
                (float)Math.Cos(phase * 1.3f) * 0.05f, // Small Y movement for visual interest
                (float)Math.Sin(phase * 0.8f) * 0.1f
            );
            animatedPoints[i] = (Double3)(position + (Float3)baseControlPoints[i] + (Float3)animation);
        }

        // Create different spline types
        var linearSpline = Spline.CreateLinear(animatedPoints);
        var catmullRomSpline = Spline.CreateCatmullRom(animatedPoints, false, 0.5f);
        var bezierSpline = Spline.CreateBezier(animatedPoints);

        // Offset each spline along Z-axis for comparison
        float[] zOffsets = { 1.25f, 0.0f, -1.25f };
        Spline[] splines = { linearSpline, catmullRomSpline, bezierSpline };
        Float4[] colors = {
            new Float4(1, 0, 0, 0.8f),    // Red - Linear
            new Float4(0, 1, 0, 0.8f),    // Green - Catmull-Rom
            new Float4(1, 0, 1, 0.8f)     // Magenta - Bezier
        };

        // Draw each spline type
        for (int splineIndex = 0; splineIndex < splines.Length; splineIndex++)
        {
            var spline = splines[splineIndex];
            var color = colors[splineIndex];
            float zOffset = zOffsets[splineIndex];

            // Draw the spline path
            for (int i = 0; i < 100; i++)
            {
                float t1 = i / 100.0f;
                float t2 = (i + 1) / 100.0f;

                Float3 p1 = (Float3)spline.Evaluate(t1) + new Float3(0, 0, zOffset);
                Float3 p2 = (Float3)spline.Evaluate(t2) + new Float3(0, 0, zOffset);

                Gizmo.DrawLine(p1, p2, color);
            }

            // Draw control points for this spline
            Float3[] controlPoints = animatedPoints.Select(p => (Float3)p).ToArray();
            for (int i = 0; i < controlPoints.Length; i++)
            {
                Float3 pointPos = controlPoints[i] + new Float3(0, 0, zOffset);
                Gizmo.DrawIntersectionPoint(pointPos, new Float4(color.X, color.Y, color.Z, 0.6f), 0.03f);

                if (i < controlPoints.Length - 1)
                {
                    Float3 p1 = controlPoints[i] + new Float3(0, 0, zOffset);
                    Float3 p2 = controlPoints[i + 1] + new Float3(0, 0, zOffset);
                    Gizmo.DrawLine(p1, p2, new Float4(color.X, color.Y, color.Z, 0.3f));
                }
            }

            // Animate a point along each spline
            float t = (timeInSeconds * 0.4f) % 1.0f;
            Float3 currentPos = (Float3)spline.Evaluate(t) + new Float3(0, 0, zOffset);
            Float3 tangent = Float3.Normalize((Float3)spline.EvaluateDerivative(t));

            // Draw point
            Gizmo.DrawIntersectionPoint(currentPos, color, 0.07f);
        }
    }

    public Float3 GetBounds() => new Float3(6.0f, 2.0f, 6.0f);
}
