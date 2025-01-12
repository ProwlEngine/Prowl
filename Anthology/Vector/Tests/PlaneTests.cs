// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Echo.Test;

public class PlaneTests
{
    private const double TestEpsilon = 1e-6;

    [Fact]
    public void Constructor_Vector4_CreatesCorrectPlane()
    {
        var vector = new Vector4(1, 0, 0, 5);
        var plane = new Plane(vector);

        Assert.Equal(1, plane.normal.x);
        Assert.Equal(0, plane.normal.y);
        Assert.Equal(0, plane.normal.z);
        Assert.Equal(5, plane.distance);
    }

    [Fact]
    public void Constructor_ThreePoints_CreatesCorrectPlane()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, 0, 0);
        var c = new Vector3(0, 1, 0);
        var plane = new Plane(a, b, c);

        Assert.Equal(0, plane.normal.x, TestEpsilon);
        Assert.Equal(0, plane.normal.y, TestEpsilon);
        Assert.Equal(1, plane.normal.z, TestEpsilon);
        Assert.Equal(0, plane.distance, TestEpsilon);
    }

    [Fact]
    public void Dot_Vector4_ReturnsCorrectValue()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 2);
        var vector = new Vector4(3, 0, 0, 1);

        double result = plane.Dot(vector);
        Assert.Equal(5, result, TestEpsilon); // (1 * 3) + (0 * 0) + (0 * 0) + (2 * 1) = 5
    }

    [Fact]
    public void DotCoordinate_Vector3_ReturnsCorrectValue()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 2);
        var point = new Vector3(3, 0, 0);

        double result = plane.DotCoordinate(point);
        Assert.Equal(5, result, TestEpsilon); // (1 * 3) + (0 * 0) + (0 * 0) + 2 = 5
    }

    [Fact]
    public void DotNormal_Vector3_ReturnsCorrectValue()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 2);
        var vector = new Vector3(3, 0, 0);

        double result = plane.DotNormal(vector);
        Assert.Equal(3, result, TestEpsilon); // (1 * 3) + (0 * 0) + (0 * 0) = 3
    }

    [Theory]
    [InlineData(3, 0, 0, true)]  // Point in front of plane
    [InlineData(-3, 0, 0, false)] // Point behind plane
    public void GetSide_ReturnsCorrectResult(double x, double y, double z, bool expectedSide)
    {
        var plane = new Plane(new Vector3(1, 0, 0), 0);
        var point = new Vector3(x, y, z);

        Assert.Equal(expectedSide, plane.GetSide(point));
    }

    [Fact]
    public void GetDistanceToPoint_ReturnsCorrectDistance()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 0);
        var point = new Vector3(5, 0, 0);

        double distance = plane.GetDistanceToPoint(point);
        Assert.Equal(5, distance, TestEpsilon);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]   // Point on plane
    [InlineData(1, 0, 0, false)]  // Point not on plane
    public void IsOnPlane_ReturnsCorrectResult(double x, double y, double z, bool expectedResult)
    {
        var plane = new Plane(new Vector3(1, 0, 0), 0);
        var point = new Vector3(x, y, z);

        Assert.Equal(expectedResult, plane.IsOnPlane(point));
    }

    [Fact]
    public void Set3Points_SetsPlaneCorrectly()
    {
        var plane = new Plane(Vector3.zero, 0);
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(1, 0, 0);
        var c = new Vector3(0, 1, 0);

        plane.Set3Points(a, b, c);

        Assert.Equal(0, plane.normal.x, TestEpsilon);
        Assert.Equal(0, plane.normal.y, TestEpsilon);
        Assert.Equal(1, plane.normal.z, TestEpsilon);
        Assert.Equal(0, plane.distance, TestEpsilon);
    }

    [Fact]
    public void DoesLineIntersectPlane_IntersectingLine_ReturnsTrue()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 0);
        var lineStart = new Vector3(-1, 0, 0);
        var lineEnd = new Vector3(1, 0, 0);

        bool intersects = plane.DoesLineIntersectPlane(lineStart, lineEnd, out Vector3 intersection);

        Assert.True(intersects);
        Assert.Equal(0, intersection.x, TestEpsilon);
        Assert.Equal(0, intersection.y, TestEpsilon);
        Assert.Equal(0, intersection.z, TestEpsilon);
    }

    [Fact]
    public void DoesLineIntersectPlane_ParallelLine_ReturnsFalse()
    {
        var plane = new Plane(new Vector3(1, 0, 0), 0);
        var lineStart = new Vector3(1, 0, 0);
        var lineEnd = new Vector3(1, 1, 0);

        bool intersects = plane.DoesLineIntersectPlane(lineStart, lineEnd, out _);

        Assert.False(intersects);
    }

    [Fact]
    public void Equals_IdenticalPlanes_ReturnsTrue()
    {
        var plane1 = new Plane(new Vector3(1, 0, 0), 2);
        var plane2 = new Plane(new Vector3(1, 0, 0), 2);

        Assert.True(plane1.Equals(plane2));
        Assert.True(plane1 == plane2);
    }
}