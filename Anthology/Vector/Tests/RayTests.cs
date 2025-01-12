// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Echo.Test;

public class RayTests
{
    private const double TestEpsilon = 1e-6;

    [Fact]
    public void Constructor_SetsCorrectValues()
    {
        var origin = new Vector3(1, 2, 3);
        var direction = new Vector3(0, 1, 0);
        var ray = new Ray(origin, direction);

        Assert.Equal(origin, ray.origin);
        Assert.Equal(direction, ray.direction);
    }

    [Fact]
    public void Equals_IdenticalRays_ReturnsTrue()
    {
        var ray1 = new Ray(new Vector3(1, 0, 0), new Vector3(0, 1, 0));
        var ray2 = new Ray(new Vector3(1, 0, 0), new Vector3(0, 1, 0));

        Assert.True(ray1.Equals(ray2));
        Assert.True(ray1 == ray2);
    }

    [Fact]
    public void Intersects_BoundsIntersection_ReturnsDistance()
    {
        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1));
        var box = new Bounds(Vector3.zero, new Vector3(2, 2, 2));

        var result = ray.Intersects(box);

        Assert.True(result.HasValue);
        Assert.Equal(4, result.Value, TestEpsilon);
    }

    [Fact]
    public void Intersects_BoundsNoIntersection_ReturnsNull()
    {
        var ray = new Ray(new Vector3(5, 5, 5), new Vector3(1, 0, 0));
        var box = new Bounds(Vector3.zero, new Vector3(2, 2, 2));

        var result = ray.Intersects(box);

        Assert.False(result.HasValue);
    }

    [Fact]
    public void Intersects_BoundsRayInside_ReturnsZero()
    {
        var ray = new Ray(Vector3.zero, new Vector3(0, 0, 1));
        var box = new Bounds(Vector3.zero, new Vector3(2, 2, 2));

        var result = ray.Intersects(box);

        Assert.True(result.HasValue);
        Assert.Equal(0, result.Value, TestEpsilon);
    }

    [Fact]
    public void Intersects_PlaneIntersection_ReturnsDistance()
    {
        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1));
        var plane = new Plane(new Vector3(0, 0, 1), 0);

        var result = ray.Intersects(plane);

        Assert.True(result.HasValue);
        Assert.Equal(5, result.Value, TestEpsilon);
    }

    [Fact]
    public void Intersects_PlaneParallel_ReturnsNull()
    {
        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(1, 0, 0));
        var plane = new Plane(new Vector3(0, 0, 1), 0);

        var result = ray.Intersects(plane);

        Assert.False(result.HasValue);
    }

    [Fact]
    public void Intersects_TriangleIntersection_ReturnsDistance()
    {
        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1));
        var v1 = new Vector3(-1, -1, 0);
        var v2 = new Vector3(1, -1, 0);
        var v3 = new Vector3(0, 1, 0);

        var result = ray.Intersects(v1, v2, v3);

        Assert.True(result.HasValue);
        Assert.Equal(5, result.Value, TestEpsilon);
    }

    [Theory]
    [InlineData(true)]  // Test with backface culling
    [InlineData(false)] // Test without backface culling
    public void Intersects_TriangleFromBehind_ReturnsExpectedResult(bool cullBackface)
    {
        var ray = new Ray(new Vector3(0, 0, -5), new Vector3(0, 0, 1));
        var v1 = new Vector3(-1, -1, 0);
        var v2 = new Vector3(1, -1, 0);
        var v3 = new Vector3(0, 1, 0);

        var result = ray.Intersects(v1, v2, v3, cullBackface);

        if (cullBackface)
            Assert.False(result.HasValue);
        else
            Assert.True(result.HasValue);
    }

    [Fact]
    public void Intersects_TriangleMiss_ReturnsNull()
    {
        var ray = new Ray(new Vector3(5, 5, -5), new Vector3(0, 0, 1));
        var v1 = new Vector3(-1, -1, 0);
        var v2 = new Vector3(1, -1, 0);
        var v3 = new Vector3(0, 1, 0);

        var result = ray.Intersects(v1, v2, v3);

        Assert.False(result.HasValue);
    }

    [Fact]
    public void Position_ReturnsCorrectPoint()
    {
        var ray = new Ray(new Vector3(0, 0, 0), new Vector3(0, 1, 0));
        var point = ray.Position(5);

        Assert.Equal(0, point.x, TestEpsilon);
        Assert.Equal(5, point.y, TestEpsilon);
        Assert.Equal(0, point.z, TestEpsilon);
    }

    [Fact]
    public void ToString_ReturnsCorrectFormat()
    {
        var ray = new Ray(new Vector3(1, 2, 3), new Vector3(0, 1, 0));
        var result = ray.ToString();

        Assert.Contains("Position", result);
        Assert.Contains("Direction", result);
        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }
}