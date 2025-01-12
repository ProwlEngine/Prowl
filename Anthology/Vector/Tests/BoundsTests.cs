// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Echo.Test;

public class BoundsTests
{
    [Fact]
    public void Constructor_WithCenterAndSize_SetsCorrectMinMax()
    {
        var center = new Vector3(1, 2, 3);
        var size = new Vector3(2, 4, 6);
        var bounds = new Bounds(center, size);

        Assert.Equal(new Vector3(0, 0, 0), bounds.min);
        Assert.Equal(new Vector3(2, 4, 6), bounds.max);
    }

    [Fact]
    public void Properties_SetAndGet_WorkCorrectly()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));

        // Test center
        Assert.Equal(Vector3.zero, bounds.center);
        bounds.center = new Vector3(1, 1, 1);
        Assert.Equal(new Vector3(1, 1, 1), bounds.center);

        // Test size
        Assert.Equal(new Vector3(2, 2, 2), bounds.size);
        bounds.size = new Vector3(4, 4, 4);
        Assert.Equal(new Vector3(4, 4, 4), bounds.size);

        // Test extents
        Assert.Equal(new Vector3(2, 2, 2), bounds.extents);
    }

    [Theory]
    [InlineData(0, 0, 0, 3, 3, 3, 1, 1, 1, ContainmentType.Contains)]
    [InlineData(0, 0, 0, 2, 2, 2, 1, 1, 1, ContainmentType.Intersects)]
    [InlineData(5, 5, 5, 1, 1, 1, 0, 0, 0, ContainmentType.Disjoint)]
    public void Contains_Point_ReturnsCorrectContainmentType(
        double centerX, double centerY, double centerZ,
        double sizeX, double sizeY, double sizeZ,
        double pointX, double pointY, double pointZ,
        ContainmentType expected)
    {
        var bounds = new Bounds(new Vector3(centerX, centerY, centerZ),
                              new Vector3(sizeX, sizeY, sizeZ));
        var point = new Vector3(pointX, pointY, pointZ);

        var result = bounds.Contains(point);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Contains_Bounds_ReturnsCorrectContainmentType()
    {
        var outerBounds = new Bounds(Vector3.zero, new Vector3(4, 4, 4));
        var innerBounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var disjointBounds = new Bounds(new Vector3(10, 10, 10), new Vector3(2, 2, 2));
        var intersectingBounds = new Bounds(new Vector3(1, 1, 1), new Vector3(4, 4, 4));

        Assert.Equal(ContainmentType.Contains, outerBounds.Contains(innerBounds));
        Assert.Equal(ContainmentType.Disjoint, outerBounds.Contains(disjointBounds));
        Assert.Equal(ContainmentType.Intersects, outerBounds.Contains(intersectingBounds));
    }

    [Fact]
    public void CreateFromPoints_ValidPoints_CreatesCorrectBounds()
    {
        var points = new[]
        {
            new Vector3(-1, -1, -1),
            new Vector3(1, 1, 1),
            new Vector3(0, 0, 0)
        };

        var bounds = Bounds.CreateFromPoints(points);

        Assert.Equal(new Vector3(-1, -1, -1), bounds.min);
        Assert.Equal(new Vector3(1, 1, 1), bounds.max);
    }

    [Fact]
    public void CreateFromPoints_EmptyCollection_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Bounds.CreateFromPoints(Array.Empty<Vector3>()));
    }

    [Fact]
    public void Encapsulate_Point_ExpandsBoundsCorrectly()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var point = new Vector3(2, 2, 2);

        bounds.Encapsulate(point);

        Assert.Equal(new Vector3(2, 2, 2), bounds.max);
    }

    [Fact]
    public void Expand_Amount_ExpandsBoundsCorrectly()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        bounds.Expand(2);

        Assert.Equal(new Vector3(2, 2, 2), bounds.extents);
    }

    [Fact]
    public void GetCorners_ReturnsCorrectCorners()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var corners = bounds.GetCorners();

        Assert.Equal(8, corners.Length);

        Assert.Contains(new Vector3(-1, -1, -1), corners);
        Assert.Contains(new Vector3(1, 1, 1), corners);
        Assert.Contains(new Vector3(1, -1, -1), corners);
        Assert.Contains(new Vector3(-1, 1, 1), corners);
        Assert.Contains(new Vector3(-1, 1, -1), corners);
        Assert.Contains(new Vector3(1, -1, 1), corners);
        Assert.Contains(new Vector3(-1, -1, 1), corners);
        Assert.Contains(new Vector3(1, 1, -1), corners);
    }

    [Fact]
    public void Intersects_OverlappingBounds_ReturnsTrue()
    {
        var bounds1 = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var bounds2 = new Bounds(new Vector3(1, 1, 1), new Vector3(2, 2, 2));

        Assert.True(bounds1.Intersects(bounds2));
    }

    [Fact]
    public void Intersects_DisjointBounds_ReturnsFalse()
    {
        var bounds1 = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var bounds2 = new Bounds(new Vector3(10, 10, 10), new Vector3(2, 2, 2));

        Assert.False(bounds1.Intersects(bounds2));
    }

    [Fact]
    public void Transform_Matrix_TransformsBoundsCorrectly()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var matrix = Matrix4x4.CreateTranslation(new Vector3(1, 1, 1));

        var transformed = bounds.Transform(matrix);

        Assert.Equal(new Vector3(1, 1, 1), transformed.center);
    }

    [Fact]
    public void Equals_SameBounds_ReturnsTrue()
    {
        var bounds1 = new Bounds(Vector3.zero, new Vector3(2, 2, 2));
        var bounds2 = new Bounds(Vector3.zero, new Vector3(2, 2, 2));

        Assert.True(bounds1.Equals(bounds2));
        Assert.True(bounds1 == bounds2);
    }
}