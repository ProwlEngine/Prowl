// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample;

public interface IDemo
{
    string Name { get; }
    void Draw(Float3 position, float timeInSeconds);
    Float3 GetBounds();
}

public static class GeometryDemo
{
    private static readonly IDemo[] demos = new IDemo[]
    {
        new SphereDemo(),
        new AABBDemo(),
        new TriangleDemo(),
        new RayDemo(),
        new PlaneDemo(),
        //new LineSegmentDemo(),
        new SplineDemo(),
        new SplineTypesDemo(),
        new FrustumDemo(),

        new SphereSphereIntersectionDemo(),
        //new SphereTriangleIntersectionDemo(),


        new RaySphereIntersectionDemo(),
        //new RayAABBIntersectionDemo(),
        new RayTriangleIntersectionDemo(),

        new AABBAABBIntersectionDemo(),
        new AABBSphereIntersectionDemo(),
        //new AABBPointIntersectionDemo(),

        new FrustumAABBIntersectionDemo(),
        new FrustumSphereIntersectionDemo(),
        new FrustumPointIntersectionDemo(),
    };

    public static void DrawGeometryDemo(float timeInSeconds)
    {
        var grid = CalculateOptimalGrid(demos.Length);
        float spacing = 6.0f;

        // Draw demos
        for (int i = 0; i < demos.Length; i++)
        {
            int row = i / grid.cols;
            int col = i % grid.cols;

            Float3 position = new Float3(
                (col + 0.5f) * spacing,
                0,
                (row + 0.5f) * spacing
            );

            demos[i].Draw(position, timeInSeconds);
        }

        // Draw grid separators
        DrawGridSeparators(grid, spacing);

        // Draw coordinate axes
        DrawCoordinateAxes();
    }

    private static (int rows, int cols) CalculateOptimalGrid(int itemCount)
    {
        int cols = (int)Maths.Ceiling(Maths.Sqrt(itemCount));
        int rows = (int)Maths.Ceiling((double)itemCount / cols);
        return (rows, cols);
    }

    private static void DrawGridSeparators((int rows, int cols) grid, float spacing)
    {
        Float4 separatorColor = new Float4(0.3f, 0.3f, 0.3f, 1);
        float width = grid.cols * spacing;
        float height = grid.rows * spacing;

        // Vertical lines
        for (int i = 0; i <= grid.cols; i++)
        {
            float x = i * spacing;
            Gizmo.DrawLine(
                new Float3(x, 0, 0),
                new Float3(x, 0, height),
                separatorColor
            );
        }

        // Horizontal lines
        for (int i = 0; i <= grid.rows; i++)
        {
            float z = i * spacing;
            Gizmo.DrawLine(
                new Float3(width, 0, z),
                new Float3(0, 0, z),
                separatorColor
            );
        }
    }

    private static void DrawCoordinateAxes()
    {
        Float3 origin = new Float3(0, -2.5f, 0);
        Gizmo.DrawLine(origin, origin + Float3.UnitX * 2, new Float4(1, 0, 0, 1)); // X-axis (red)
        Gizmo.DrawLine(origin, origin + Float3.UnitY * 2, new Float4(0, 1, 0, 1)); // Y-axis (green)
        Gizmo.DrawLine(origin, origin + Float3.UnitZ * 2, new Float4(0, 0, 1, 1)); // Z-axis (blue)
    }
}


#region Basic Shapes

// Basic Shape Demos
public class SphereDemo : IDemo
{
    public string Name => "Sphere";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float bounce = Maths.Abs(Maths.Sin(timeInSeconds * 1.5f)) * 0.3f;
        float sizeWave = Maths.Sin(timeInSeconds * 0.5f) * 0.2f;

        Float3 spherePos = position + new Float3(0, bounce, 0);
        Gizmo.DrawSphereWireframe(
            new Sphere((Double3)spherePos, 1.0f + sizeWave * 0.2f),
            new Float4(0, 0, 1, 1),
            12
        );
    }

    public Float3 GetBounds() => new Float3(2.4f, 2.6f, 2.4f);
}

public class AABBDemo : IDemo
{
    public string Name => "AABB";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float scaleWave = Maths.Sin(timeInSeconds * 0.8f) * 0.3f;
        float scale = 1.0f + scaleWave * 0.9f;

        Gizmo.DrawAABB(new AABB(
            (Double3)position + new Double3(-0.75f * scale, -0.75f, -0.75f * scale),
            (Double3)position + new Double3(0.75f * scale, 0.75f, 0.75f * scale)
        ), new Float4(0, 1, 0, 1));
    }

    public Float3 GetBounds() => new Float3(2.0f, 1.5f, 2.0f);
}

public class TriangleDemo : IDemo
{
    public string Name => "Triangle";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float rotation = timeInSeconds * 0.3f;
        float cosRot = Maths.Cos(rotation);
        float sinRot = Maths.Sin(rotation);

        Triangle triangle = new Triangle(
            (Double3)position + new Double3(cosRot * 0 - sinRot * (-1), -1, cosRot * (-0.5f) - sinRot * 0),
            (Double3)position + new Double3(cosRot * 1 - sinRot * 1, 1, cosRot * (-0.5f) - sinRot * 1),
            (Double3)position + new Double3(cosRot * (-1) - sinRot * 1, 1, cosRot * 0.5f - sinRot * (-1))
        );

        Gizmo.DrawTriangle(triangle, new Float4(0.5f, 0.5f, 1, 1f), true);
        Gizmo.DrawTriangle(triangle, new Float4(1, 0, 0, 1), false);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}

public class RayDemo : IDemo
{
    public string Name => "Ray";

    public void Draw(Float3 position, float timeInSeconds)
    {
        float fastWave = Maths.Sin(timeInSeconds * 2.0f) * 0.1f;
        float slowWave = Maths.Sin(timeInSeconds * 0.5f) * 0.2f;

        Float3 rayDir = Float3.Normalize(new Float3(0.5f + fastWave * 10f, 1, 0.2f + slowWave * 10f));
        Ray ray = new Ray((Double3)position + new Double3(0, -1, 0), (Double3)rayDir);

        Gizmo.DrawRay(ray, 3.0f, new Float4(1, 1, 0, 1));
    }

    public Float3 GetBounds() => new Float3(2.0f, 3.0f, 2.0f);
}

public class PlaneDemo : IDemo
{
    public string Name => "Plane";

    public void Draw(Float3 position, float timeInSeconds)
    {
        Float3 planeNormal = Float3.Normalize(new Float3(0, 1, 0));
        Gizmo.DrawPlane(position, planeNormal, new Float3(2, 0, 2), new Float4(0, 1, 1, 1), 8);
    }

    public Float3 GetBounds() => new Float3(4.0f, 1.0f, 4.0f);
}

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

#endregion

#region Intersections

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

        Sphere sphere1 = new Sphere((Double3)position + (Double3)sphere1Offset, size1);
        Sphere sphere2 = new Sphere((Double3)position + (Double3)sphere2Offset, size2);
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

public class AABBSphereIntersectionDemo : IDemo
{
    public string Name => "AABB-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // AABB follows a helical path
        float helixTime = timeInSeconds * 0.6f;
        Float3 aabbOffset = new Float3(
            Maths.Cos(helixTime) * 0.4f,
            Maths.Sin(helixTime * 2) * 0.3f, // Vertical oscillation
            Maths.Sin(helixTime) * 0.4f
        ) * 2f;

        // AABB also rotates and scales
        float scale = 1.0f + Maths.Sin(timeInSeconds * 1.5f) * 0.2f;

        AABB aabb = new AABB(
            (Double3)position + (Double3)aabbOffset + new Double3(-0.3f * scale, -0.3f * scale, -0.3f * scale),
            (Double3)position + (Double3)aabbOffset + new Double3(0.3f * scale, 0.3f * scale, 0.3f * scale)
        );

        // Sphere follows a different complex path - lemniscate (infinity symbol) in 3D
        float lemnTime = timeInSeconds * 0.9f;
        float sphereRadius = 0.6f + Maths.Sin(timeInSeconds * 2.5f) * 0.1f;
        Float3 sphereOffset = new Float3(
            (float)(Maths.Sin(lemnTime) / (1 + Maths.Cos(lemnTime) * Maths.Cos(lemnTime))) * 0.5f,
            Maths.Cos(timeInSeconds * 1.8f) * 0.2f,
            (float)(Maths.Sin(lemnTime) * Maths.Cos(lemnTime) / (1 + Maths.Cos(lemnTime) * Maths.Cos(lemnTime))) * 0.5f
        ) * 2f;

        Sphere sphere = new Sphere((Double3)position + (Double3)sphereOffset, sphereRadius);
        bool intersects = aabb.Intersects(sphere);

        Float4 color = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Gizmo.DrawAABB(aabb, color);
        Gizmo.DrawSphereWireframe(sphere, color, 8);
    }

    public Float3 GetBounds() => new Float3(2.0f, 1.8f, 2.0f);
}

public class RayTriangleIntersectionDemo : IDemo
{
    public string Name => "Ray-Triangle Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // Triangle rotates on multiple axes
        float rotX = timeInSeconds * 0.4f;
        float rotY = timeInSeconds * 0.7f;
        float rotZ = timeInSeconds * 0.3f;

        // Create rotation matrices
        float cosX = Maths.Cos(rotX), sinX = Maths.Sin(rotX);
        float cosY = Maths.Cos(rotY), sinY = Maths.Sin(rotY);

        // Triangle vertices with multi-axis rotation
        Float3[] originalVerts = {
            new Float3(-0.8f, -0.6f, 0),
            new Float3(0.8f, -0.6f, 0),
            new Float3(0, 0.8f, 0)
        };

        Float3[] rotatedVerts = new Float3[3];
        for (int i = 0; i < 3; i++)
        {
            Float3 ov = originalVerts[i];
            // Rotate around Y axis
            float newX = ov.X * cosY + ov.Z * sinY;
            float newZ = -ov.X * sinY + ov.Z * cosY;
            ov = new Float3(newX, ov.Y, newZ);

            // Rotate around X axis
            float newY = ov.Y * cosX - ov.Z * sinX;
            newZ = ov.Y * sinX + ov.Z * cosX;

            rotatedVerts[i] = position + new Float3(ov.X, newY, newZ);
        }

        Triangle triangle = new Triangle(rotatedVerts[0], rotatedVerts[1], rotatedVerts[2]);

        // Calculate triangle center for better ray targeting
        Float3 triangleCenter = (rotatedVerts[0] + rotatedVerts[1] + rotatedVerts[2]) / 3.0f;

        // Ray orbits around the triangle and aims toward its center
        float orbitalTime = timeInSeconds * 0.8f;
        float orbitRadius = 1.5f + Maths.Sin(timeInSeconds * 0.6f) * 0.3f;

        Float3 rayStart = position + new Float3(
            Maths.Cos(orbitalTime) * orbitRadius,
            Maths.Sin(orbitalTime * 1.3f) * 0.8f, // Vertical movement
            Maths.Sin(orbitalTime) * orbitRadius
        );

        // Ray direction aims at triangle center with some wobble
        Float3 baseDirection = Float3.Normalize(triangleCenter - rayStart);
        Float3 wobble = new Float3(
            Maths.Sin(timeInSeconds * 2.5f) * 0.2f,
            Maths.Cos(timeInSeconds * 3.0f) * 0.2f,
            Maths.Sin(timeInSeconds * 2.0f) * 0.2f
        );
        Float3 rayDir = Float3.Normalize(baseDirection + wobble);

        Ray ray = new Ray(rayStart, rayDir);

        // Check for intersection
        bool hasHit = ray.Intersects(triangle, out double hitDistance, out double u, out double v);

        // Set ray color and length based on hit
        Float4 rayColor;
        float rayLength;

        if (hasHit)
        {
            rayColor = new Float4(0, 1, 0, 1); // Green when hitting
            rayLength = (float)hitDistance; // Stop at hit point
        }
        else
        {
            rayColor = new Float4(1, 1, 1, 1); // White when missing
            rayLength = 3.0f; // Full length
        }

        // Draw triangle with slight transparency
        Gizmo.DrawTriangle(triangle, new Float4(1, 0.5f, 0, 0.3f), true);
        Gizmo.DrawTriangle(triangle, new Float4(1, 0.5f, 0, 1), false);

        // Draw ray with dynamic color and length
        Gizmo.DrawRay(ray, rayLength, rayColor);

        if (hasHit)
        {
            Float3 hitPoint = (Float3)ray.GetPoint(hitDistance);

            // Pulsing hit point
            float pulseSize = 0.06f + Maths.Abs(Maths.Sin(timeInSeconds * 8.0f)) * 0.04f;
            Gizmo.DrawIntersectionPoint(hitPoint, new Float4(1, 1, 0, 1), pulseSize);
        }
    }

    public Float3 GetBounds() => new Float3(3.0f, 2.5f, 3.0f);
}

public class AABBAABBIntersectionDemo : IDemo
{
    public string Name => "AABB-AABB Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        // First AABB follows a figure-8 pattern
        float time1 = timeInSeconds * 0.6f;
        Float3 aabb1Offset = new Float3(
            Maths.Sin(time1) * 0.5f,
            Maths.Sin(time1 * 2) * 0.2f, // Figure-8 in XY
            Maths.Cos(time1 * 0.8f) * 0.3f
        );

        // First AABB also scales slightly
        float scale1 = 1.0f + Maths.Sin(timeInSeconds * 1.2f) * 0.15f;

        // Second AABB follows a circular orbit with different timing
        float time2 = timeInSeconds * 0.8f;
        Float3 aabb2Offset = new Float3(
            Maths.Cos(time2) * 0.6f,
            Maths.Sin(time2 * 1.5f) * 0.25f, // Vertical bobbing
            Maths.Sin(time2) * 0.4f
        );

        // Second AABB rotates its size in a different pattern
        float scale2 = 1.0f + Maths.Cos(timeInSeconds * 0.9f) * 0.2f;

        // Create the AABBs
        AABB aabb1 = new AABB(
            position + aabb1Offset + new Float3(-0.4f * scale1, -0.3f * scale1, -0.35f * scale1),
            position + aabb1Offset + new Float3(0.4f * scale1, 0.3f * scale1, 0.35f * scale1)
        );

        AABB aabb2 = new AABB(
            position + aabb2Offset + new Float3(-0.35f * scale2, -0.35f * scale2, -0.4f * scale2),
            position + aabb2Offset + new Float3(0.35f * scale2, 0.35f * scale2, 0.4f * scale2)
        );

        bool intersects = aabb1.Intersects(aabb2);

        // Color based on intersection with some pulsing
        Float4 aabb1Color = intersects ?
            new Float4(0, 0.7f, 0, 0.6f) :
            new Float4(0.7f, 0.2f, 0.2f, 0.6f);
        Float4 aabb2Color = intersects ?
            new Float4(0, 0.7f * 0.8f, 0.7f * 0.2f, 0.6f) :
            new Float4(0.2f, 0.2f, 0.7f, 0.6f);

        // Draw the main AABBs
        Gizmo.DrawAABBSolid(aabb1, aabb1Color);
        Gizmo.DrawAABBSolid(aabb2, aabb2Color);

        // If intersecting, calculate and draw the clipped/intersection AABB
        if (intersects)
        {
            AABB clipped = aabb1.ClippedBy(aabb2);

            // Draw bright wireframe for intersection
            Gizmo.DrawAABB(clipped, new Float4(1, 1, 0, 1));
        }
    }

    public Float3 GetBounds() => new Float3(3.0f, 2.5f, 3.0f);
}

public class RaySphereIntersectionDemo : IDemo
{
    public string Name => "Ray-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.25f; // Slow down time for better visibility

        // Sphere follows a complex 3D path - lemniscate (infinity symbol) with vertical movement
        float sphereTime = timeInSeconds * 0.7f;
        Float3 sphereOffset = new Float3(
            (float)(Maths.Sin(sphereTime) / (1 + Maths.Cos(sphereTime) * Maths.Cos(sphereTime))) * 0.6f,
            Maths.Sin(sphereTime * 1.5f) * 0.4f, // Vertical bobbing
            (float)(Maths.Sin(sphereTime) * Maths.Cos(sphereTime) / (1 + Maths.Cos(sphereTime) * Maths.Cos(sphereTime))) * 0.4f
        );

        // Sphere also pulses in size
        float sphereRadius = 0.6f + Maths.Sin(timeInSeconds * 2.0f) * 0.15f;
        Sphere sphere = new Sphere(position + sphereOffset, sphereRadius);

        // Ray orbits around the center, aiming toward the sphere with some lead prediction
        float rayTime = timeInSeconds * 1.2f;
        float orbitRadius = 1.8f + Maths.Cos(timeInSeconds * 0.4f) * 0.3f;

        Float3 rayStart = position + new Float3(
            Maths.Cos(rayTime) * orbitRadius,
            Maths.Sin(rayTime * 0.8f) * 0.6f + 0.2f, // Vertical movement with offset
            Maths.Sin(rayTime) * orbitRadius
        );

        // Calculate ray direction - aim at predicted sphere position with some wobble
        Float3 sphereVelocity = new Float3(
            (float)(Maths.Cos(sphereTime) * (1 - Maths.Cos(sphereTime) * Maths.Cos(sphereTime)) - Maths.Sin(sphereTime) * 2f * Maths.Cos(sphereTime) * Maths.Sin(sphereTime)) /
            ((1f + Maths.Cos(sphereTime) * Maths.Cos(sphereTime)) * (1f + Maths.Cos(sphereTime) * Maths.Cos(sphereTime))) * 0.6f * 0.7f,
            (float)Maths.Cos(sphereTime * 1.5f) * 0.4f * 1.5f,
            0 // Simplified Z velocity
        );

        Float3 predictedSpherePos = (Float3)sphere.Center + sphereVelocity * 0.3f; // Lead the target
        Float3 baseDirection = Float3.Normalize(predictedSpherePos - rayStart);

        // Add some wobble to make it more interesting
        Float3 wobble = new Float3(
            Maths.Sin(timeInSeconds * 3.5f) * 0.15f,
            Maths.Cos(timeInSeconds * 4.0f) * 0.12f,
            Maths.Sin(timeInSeconds * 2.8f) * 0.18f
        );
        Float3 rayDir = Float3.Normalize(baseDirection + wobble);

        Ray ray = new Ray(rayStart, rayDir);

        // Check for intersection
        bool hasHit = ray.Intersects(sphere, out double t0, out double t1);

        // Dynamic ray properties based on intersection
        Float4 rayColor;
        float rayLength;

        if (hasHit)
        {
            rayColor = new Float4(0, 1, 0.3f, 1); // Green-cyan when hitting
            rayLength = (float)t0; // Stop at first intersection point
        }
        else
        {
            rayColor = new Float4(1, 0.8f, 0.2f, 1); // Orange when missing
            rayLength = 3.5f; // Full length
        }

        // Draw sphere with transparency
        Float4 sphereColor = hasHit ?
            new Float4(0.2f, 1, 0.4f, 0.7f) :
            new Float4(0.4f, 0.6f, 1, 0.5f);

        // Draw solid sphere
        Gizmo.DrawSphereWireframe(sphere, sphereColor, 12);

        // Draw ray with dynamic properties
        Gizmo.DrawRay(ray, rayLength, rayColor);

        // intersection visualization
        if (hasHit)
        {
            Float3 hitPoint1 = (Float3)ray.GetPoint(t0);
            Float3 hitPoint2 = (Float3)ray.GetPoint(t1);

            // hit points
            Gizmo.DrawIntersectionPoint(hitPoint1, new Float4(1, 1, 0, 1), 0.06f);

            if (Maths.Abs(t1 - t0) > 0.001f) // Only draw second point if different
            {
                Gizmo.DrawIntersectionPoint(hitPoint2, new Float4(1, 0.8f, 0, 1), 0.06f);

                // Draw line segment inside sphere
                Gizmo.DrawLine(hitPoint1, hitPoint2, new Float4(1, 1, 1, 0.8f));
            }

            // Draw normal vectors at hit points
            Float3 normal1 = Float3.Normalize((Float3)hitPoint1 - (Float3)sphere.Center);
            Float3 normal2 = Float3.Normalize((Float3)hitPoint2 - (Float3)sphere.Center);

            Gizmo.DrawLine(hitPoint1, hitPoint1 + normal1 * 0.3f, new Float4(0, 1, 1, 0.8f));
            if (Maths.Abs(t1 - t0) > 0.001f)
                Gizmo.DrawLine(hitPoint2, hitPoint2 + normal2 * 0.3f, new Float4(0, 0.8f, 1, 0.8f));
        }
    }

    public Float3 GetBounds() => new Float3(5.0f, 3.0f, 4.0f);
}

public class FrustumAABBIntersectionDemo : IDemo
{
    public string Name => "Frustum-AABB Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.75f; // Slow down time for better visibility

        // Create a frustum centered in the grid cell
        Double3 camPos = (Double3)position + new Double3(0, 0.6, 1.0);
        Double3 lookAt = (Double3)position + new Double3(0, 0, -0.3);
        Double3 forward = Double3.Normalize(lookAt - camPos);
        Double3 up = new Double3(0, 1, 0);

        double fov = Maths.PI / 5.5;
        double aspect = 1.3;
        double nearDist = 0.2;
        double farDist = 1.8;

        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Animate AABB through the frustum - moves along the frustum's view direction
        float moveTime = timeInSeconds * 0.8f;
        Float3 aabbOffset = new Float3(
            Maths.Sin(moveTime * 1.2f) * 0.9f,
            Maths.Sin(moveTime * 0.9f) * 0.6f,
            Maths.Sin(moveTime) * 0.8f  // Moves back and forth through frustum
        );

        float scale = 0.2f + Maths.Sin(timeInSeconds * 1.5f) * 0.05f;

        AABB aabb = new AABB(
            (Double3)position + (Double3)aabbOffset + new Double3(-scale, -scale, -scale),
            (Double3)position + (Double3)aabbOffset + new Double3(scale, scale, scale)
        );

        bool intersects = frustum.Intersects(aabb);

        // Draw frustum wireframe - color changes based on intersection for debugging
        Float4 frustumColor = intersects ? new Float4(0, 1, 0, 0.8f) : new Float4(1, 0, 0, 0.8f);
        Gizmo.DrawFrustum(frustum, frustumColor);

        // Draw AABB with strong color based on intersection
        Float4 aabbColor = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Gizmo.DrawAABBSolid(aabb, aabbColor * new Float4(1, 1, 1, 0.7f));
        Gizmo.DrawAABB(aabb, aabbColor);

        // Draw a line from AABB center to frustum origin for debugging
        Gizmo.DrawLine((Float3)aabb.Center, (Float3)camPos, new Float4(1, 1, 0, 0.3f));
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}

public class FrustumSphereIntersectionDemo : IDemo
{
    public string Name => "Frustum-Sphere Intersection";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.75f; // Slow down time for better visibility

        // Create a frustum centered in the grid cell
        float frustumRotation = timeInSeconds * 0.2f;
        Double3 camPos = (Double3)position + new Double3(
            Maths.Sin(frustumRotation) * 0.15f,
            0.7,
            0.9 + Maths.Cos(frustumRotation) * 0.1f
        );
        Double3 lookAt = (Double3)position + new Double3(0, 0, -0.2);
        Double3 forward = Double3.Normalize(lookAt - camPos);
        Double3 up = new Double3(0, 1, 0);

        double fov = Maths.PI / 5.5;
        double aspect = 1.3;
        double nearDist = 0.2;
        double farDist = 1.6;

        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

        // Animate sphere moving through the frustum
        float sphereTime = timeInSeconds * 0.9f;
        Float3 sphereOffset = new Float3(
            Maths.Sin(sphereTime * 1.3f) * 0.8f,
            Maths.Sin(sphereTime * 1.1f) * 0.6f,
            Maths.Sin(sphereTime) * 0.7f  // Moves back and forth through frustum
        );

        float radius = 0.25f + Maths.Sin(timeInSeconds * 2.0f) * 0.05f;
        Sphere sphere = new Sphere((Double3)position + (Double3)sphereOffset, radius);

        bool intersects = frustum.Intersects(sphere);

        // Draw frustum
        Float4 frustumColor = new Float4(0.7f, 0.7f, 0.7f, 0.8f);
        Gizmo.DrawFrustum(frustum, frustumColor);

        // Draw sphere with color based on intersection
        Float4 sphereColor = intersects ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
        Gizmo.DrawSphereWireframe(sphere, sphereColor, 12);
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}

public class FrustumPointIntersectionDemo : IDemo
{
    public string Name => "Frustum-Point Containment";

    public void Draw(Float3 position, float timeInSeconds)
    {
        timeInSeconds *= 0.75f; // Slow down time for better visibility

        // Create a frustum centered in the grid cell
        Double3 camPos = (Double3)position + new Double3(0, 0.7, 1.0);
        Double3 lookAt = (Double3)position + new Double3(0, 0, -0.2);
        Double3 forward = Double3.Normalize(lookAt - camPos);
        Double3 up = new Double3(0, 1, 0);

        double fov = Maths.PI / 5.5;
        double aspect = 1.3;
        double nearDist = 0.2;
        double farDist = 1.6;

        Frustum frustum = Frustum.FromCamera(camPos, forward, up, fov, aspect, nearDist, farDist);

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

            Double3 point = (Double3)position + (Double3)pointOffset;
            bool contains = frustum.Contains(point);

            // Draw point with different colors based on containment
            Float4 pointColor = contains ? new Float4(0, 1, 0, 1) : new Float4(1, 0, 0, 1);
            float pointSize = contains ? 0.08f : 0.05f;

            Gizmo.DrawIntersectionPoint((Float3)point, pointColor, pointSize);
        }

        // Draw the frustum
        Gizmo.DrawFrustum(frustum, new Float4(0.7f, 0.7f, 0.7f, 0.8f));
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.5f);
}

#endregion
