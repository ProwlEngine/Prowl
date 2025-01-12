// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Echo.Test;

public class MathDTests
{
    private const double TestEpsilon = 1e-6;

    [Fact]
    public void IsValid_HandlesSpecialValues()
    {
        Assert.True(MathD.IsValid(1.0));
        Assert.True(MathD.IsValid(-1.0));
        Assert.False(MathD.IsValid(double.NaN));
        Assert.False(MathD.IsValid(double.PositiveInfinity));
        Assert.False(MathD.IsValid(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 0.0)]
    public void Frac_ReturnsCorrectValue(double input, double expected)
    {
        Assert.Equal(expected, MathD.Frac(input), TestEpsilon);
    }

    [Theory]
    [InlineData(3.5, 2.0, 1.5)]
    [InlineData(-1.0, 2.0, 1.0)]
    [InlineData(5.0, 2.0, 1.0)]
    public void Repeat_ReturnsCorrectValue(double value, double length, double expected)
    {
        Assert.Equal(expected, MathD.Repeat(value, length), TestEpsilon);
    }

    [Theory]
    [InlineData(1.5, 2.0, 1.5)]
    [InlineData(2.5, 2.0, 1.5)]
    [InlineData(3.5, 2.0, 0.5)]
    public void PingPong_ReturnsCorrectValue(double t, double length, double expected)
    {
        Assert.Equal(expected, MathD.PingPong(t, length), TestEpsilon);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    public void Smooth01_ReturnsCorrectValue(double input, double expected)
    {
        double result = MathD.Smooth01(input);
        Assert.True(result >= 0 && result <= 1);
        Assert.Equal(expected, result, TestEpsilon);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.5, 0.5)]
    [InlineData(0.0, 10.0, 0.25, 2.5)]
    public void Lerp_ReturnsCorrectValue(double a, double b, double t, double expected)
    {
        Assert.Equal(expected, MathD.Lerp(a, b, t), TestEpsilon);
    }

    [Theory]
    [InlineData(0.0, 10.0, 5.0, 0.5)]
    [InlineData(0.0, 10.0, 0.0, 0.0)]
    [InlineData(0.0, 10.0, 10.0, 1.0)]
    public void InverseLerp_ReturnsCorrectValue(double a, double b, double value, double expected)
    {
        Assert.Equal(expected, MathD.InverseLerp(a, b, value), TestEpsilon);
    }

    [Fact]
    public void MoveTowards_ReturnsCorrectValue()
    {
        Assert.Equal(2.0, MathD.MoveTowards(1.0, 5.0, 1.0), TestEpsilon);
        Assert.Equal(5.0, MathD.MoveTowards(4.9, 5.0, 0.2), TestEpsilon);
        Assert.Equal(-0.5, MathD.MoveTowards(1.0, -2.0, 1.5), TestEpsilon);
    }

    [Fact]
    public void ClampMagnitude_Vector2_ReturnsCorrectValue()
    {
        var vector = new Vector2(3, 4); // magnitude 5
        var result1 = MathD.ClampMagnitude(vector, 2, 4);
        var result2 = MathD.ClampMagnitude(vector, 6, 8);

        Assert.Equal(4, result1.magnitude, TestEpsilon);
        Assert.Equal(6, result2.magnitude, TestEpsilon);
    }

    [Fact]
    public void LerpAngle_ReturnsCorrectValue()
    {
        double start = 0;
        double end = MathD.PI;
        double result = MathD.LerpAngle(start, end, 0.5);
        Assert.Equal(MathD.PI / 2, result, TestEpsilon);
    }

    [Fact]
    public void GetClosestPointOnLine_ReturnsCorrectPoint()
    {
        var lineStart = new Vector2(0, 0);
        var lineEnd = new Vector2(10, 0);
        var point = new Vector2(5, 5);

        var result = MathD.GetClosestPointOnLine(point, lineStart, lineEnd);
        Assert.Equal(5, result.x, TestEpsilon);
        Assert.Equal(0, result.y, TestEpsilon);
    }

    [Fact]
    public void DoesLineIntersectLine_IntersectingLines_ReturnsTrue()
    {
        var startA = new Vector2(0, 0);
        var endA = new Vector2(10, 10);
        var startB = new Vector2(0, 10);
        var endB = new Vector2(10, 0);

        bool intersects = MathD.DoesLineIntersectLine(startA, endA, startB, endB, out Vector2 intersection);
        Assert.True(intersects);
        Assert.Equal(5, intersection.x, TestEpsilon);
        Assert.Equal(5, intersection.y, TestEpsilon);
    }

    [Fact]
    public void IsPointInTriangle_2D_ReturnsCorrectResult()
    {
        var a = new Vector2(0, 0);
        var b = new Vector2(10, 0);
        var c = new Vector2(5, 10);

        var insidePoint = new Vector2(5, 5);
        var outsidePoint = new Vector2(20, 20);

        Assert.True(MathD.IsPointInTriangle(insidePoint, a, b, c));
        Assert.False(MathD.IsPointInTriangle(outsidePoint, a, b, c));
    }

    [Fact]
    public void IsPointInTriangle_3D_ReturnsCorrectResult()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(10, 0, 0);
        var c = new Vector3(5, 10, 0);

        var insidePoint = new Vector3(5, 5, 0);
        var outsidePoint = new Vector3(20, 20, 0);

        Assert.True(MathD.IsPointInTriangle(insidePoint, a, b, c));
        Assert.False(MathD.IsPointInTriangle(outsidePoint, a, b, c));
    }

    [Fact]
    public void RayIntersectsTriangle_ReturnsCorrectResult()
    {
        var origin = new Vector3(5, 5, 5);
        var direction = new Vector3(0, 0, -1);
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(10, 0, 0);
        var c = new Vector3(5, 10, 0);

        bool intersects = MathD.RayIntersectsTriangle(origin, direction, a, b, c, out Vector3 intersection);
        Assert.True(intersects);
        Assert.Equal(5, intersection.x, TestEpsilon);
        Assert.Equal(5, intersection.y, TestEpsilon);
        Assert.Equal(0, intersection.z, TestEpsilon);
    }

    [Fact]
    public void NormalizeEulerAngleDegrees_ReturnsNormalizedAngles()
    {
        var input = new Vector3(-720, 400, -30);
        var result = MathD.NormalizeEulerAngleDegrees(input);

        Assert.Equal(0, result.x, TestEpsilon);
        Assert.Equal(40, result.y, TestEpsilon);
        Assert.Equal(330, result.z, TestEpsilon);
    }
}
