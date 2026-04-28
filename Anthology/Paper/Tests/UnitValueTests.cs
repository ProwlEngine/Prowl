// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;

namespace Tests;

public class UnitValueTests
{
    [Fact]
    public void Equals_StructuralOnComponents()
    {
        Assert.Equal(UnitValue.Pixels(10), UnitValue.Pixels(10));
        Assert.NotEqual(UnitValue.Pixels(10), UnitValue.Pixels(11));
        Assert.Equal(UnitValue.Auto, UnitValue.Auto);
        Assert.Equal(UnitValue.Stretch(2), UnitValue.Stretch(2));
        Assert.NotEqual(UnitValue.Auto, UnitValue.Stretch(1));
    }

    [Fact]
    public void Lerp_InterpolatesEachComponentLinearly()
    {
        var lerped = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5f);
        // Px: 10 -> 0 at t=0.5 = 5; Pct: 0 -> 50 at t=0.5 = 25
        Assert.Equal(5f, lerped.Px);
        Assert.Equal(25f, lerped.Pct);
        Assert.Equal(0f, lerped.Grow);
        Assert.Equal(0f, lerped.AutoFactor);
    }

    [Fact]
    public void Lerp_AtEndpointsRoundTripsExactly()
    {
        Assert.Equal(UnitValue.Auto, UnitValue.Lerp(UnitValue.Pixels(0), UnitValue.Auto, 1f));
        Assert.Equal(UnitValue.Pixels(0), UnitValue.Lerp(UnitValue.Pixels(0), UnitValue.Auto, 0f));
        Assert.Equal(UnitValue.Stretch(2), UnitValue.Lerp(UnitValue.Pixels(100), UnitValue.Stretch(2), 1f));
    }

    [Fact]
    public void ToPx_WithInterpolation_ComputesExpectedValue()
    {
        var uv = UnitValue.Lerp(UnitValue.Pixels(10), UnitValue.Percentage(50), 0.5f);
        // Floor: Px(5) + Pct(25)/100 * 200 = 5 + 50 = 55
        Assert.Equal(55f, uv.ToPx(200f, 0f), 5);
    }

    [Fact]
    public void ToPx_PureValuesMatchLegacyBehaviour()
    {
        Assert.Equal(10f, UnitValue.Pixels(10).ToPx(200f, 0f));
        Assert.Equal(100f, UnitValue.Percentage(50).ToPx(200f, 0f));
        Assert.Equal(110f, UnitValue.Percentage(50, 10).ToPx(200f, 0f));
        Assert.Equal(42f, UnitValue.Auto.ToPx(200f, 42f));
        Assert.Equal(42f, UnitValue.Stretch(1).ToPx(200f, 42f));
    }

    [Fact]
    public void Floor_ReturnsPxPlusPercentOnly()
    {
        var v = UnitValue.Stretch(1) + UnitValue.Pixels(10);
        Assert.Equal(10f, v.Floor(200f));

        var pct = UnitValue.Percentage(25, 5);
        Assert.Equal(55f, pct.Floor(200f));
    }

    [Fact]
    public void Operators_ComposeComponentsWise()
    {
        var composite = UnitValue.Stretch(1) + UnitValue.Pixels(10);
        Assert.Equal(10f, composite.Px);
        Assert.Equal(1f, composite.Grow);
        Assert.True(composite.HasGrow);
        Assert.False(composite.IsStretch); // not pure stretch
        Assert.False(composite.IsFixed);

        var pctMinusPx = UnitValue.Percentage(50) - UnitValue.Pixels(8);
        Assert.Equal(-8f, pctMinusPx.Px);
        Assert.Equal(50f, pctMinusPx.Pct);

        var doubled = UnitValue.Pixels(10) * 2f;
        Assert.Equal(20f, doubled.Px);

        var halved = UnitValue.Stretch(4) / 2f;
        Assert.Equal(2f, halved.Grow);
    }

    [Fact]
    public void Predicates_DistinguishPureFromComposite()
    {
        Assert.True(UnitValue.Auto.IsAuto);
        Assert.True(UnitValue.Auto.HasAuto);
        Assert.True(UnitValue.Stretch(2).IsStretch);
        Assert.True(UnitValue.Stretch(2).HasGrow);
        Assert.True(UnitValue.Pixels(5).IsPixels);
        Assert.True(UnitValue.Pixels(5).IsFixed);

        var composite = UnitValue.Stretch(1) + UnitValue.Pixels(10);
        Assert.False(composite.IsStretch);
        Assert.True(composite.HasGrow);
        Assert.False(composite.IsFixed);

        // Lerping into Auto from a fixed value yields a partial AutoFactor, not a pure Auto.
        var partial = UnitValue.Lerp(UnitValue.Pixels(0), UnitValue.Auto, 0.5f);
        Assert.False(partial.IsAuto);
        Assert.True(partial.HasAuto);
        Assert.Equal(0.5f, partial.AutoFactor);
    }
}
