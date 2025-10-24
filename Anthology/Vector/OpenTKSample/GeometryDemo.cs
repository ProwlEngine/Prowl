// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using OpenTKSample.Samples.Intersections;
using OpenTKSample.Samples;

using Prowl.Vector;

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
        // BMesh / GeometryData Demos
        new SubdividedCubeDemo(),
        new CubeSubdivisionComparison(),
        new GeometryDataVisualizationDemo(),

        // Basic Shapes
        new SphereDemo(),
        new AABBDemo(),
        new TriangleDemo(),
        new RayDemo(),
        new PlaneDemo(),
        //new LineSegmentDemo(),
        new SplineDemo(),
        new SplineTypesDemo(),
        new FrustumDemo(),

        // Intersection Demos
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

        // GJK Collision Detection
        new GJKFrustumSphereDemo(),
        new GJKLineSegmentSphereDemo(),
        new GJKConeSphereDemo(),
        new GJKConeAABBDemo(),
        new GJKConeTriangleDemo(),
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


