// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Tests;

public class UnitValueTests
{
    [Fact]
    public void Equals_BasicProperties_ReturnsTrue()
    {
        var a = UnitValue.Pixels(10);
        var b = UnitValue.Pixels(10);

        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
    }

    [Fact]
    public void Equals_InterpolatingVsNonInterpolating_ReturnsFalse()
    {
        var interpolating = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5);
        var plain = UnitValue.Pixels(10);

        Assert.False(interpolating.Equals(plain));
        Assert.False(plain.Equals(interpolating));
    }

    [Fact]
    public void Equals_InterpolatingBothWithSameData_ReturnsTrue()
    {
        var first = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5);
        var second = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5);

        Assert.True(first.Equals(second));
        Assert.True(second.Equals(first));
    }

    [Fact]
    public void Equals_InterpolatingDifferentProgress_ReturnsFalse()
    {
        var first = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5);
        var second = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.25);

        Assert.False(first.Equals(second));
        Assert.False(second.Equals(first));
    }

    [Fact]
    public void ToPx_WithInterpolation_ComputesExpectedValue()
    {
        var uv = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5);

        double result = uv.ToPx(200, 0);

        Assert.Equal(55, result, 5);
    }
}
