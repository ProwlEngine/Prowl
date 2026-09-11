using Prowl.Scaffold;

namespace Scaffold.Tests;
public class SpacingTests
{
    [Fact]
    public void MarginGrowSharesSurplusWithSizeAndRedistributesAtMaximum()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row });
        var a = t.Create(Style.Default with { Width = Length.Stretch(), Height = 20, MaxWidth = 100, Margin = new LengthEdges(Length.Stretch(), 0, Length.Stretch(), 0) }, r);
        t.Layout(r, new(400, 100));
        Assert.Equal(new Rect(150, 0, 100, 20), t.GetRect(a));
    }

    [Fact]
    public void CrossMarginGrowSharesWidthBeforeMeasuringTextHeight()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Style.Default with { Width = Length.Stretch(), Margin = new LengthEdges(Length.Stretch(), 0, 0, 0) }, r);
        t.SetMeasure(a, (_, size, _) => new(size.Width, 1000 / Math.Max(1, size.Width)));
        t.Layout(r, new(200, 100));
        Assert.Equal(new Rect(100, 0, 100, 10), t.GetRect(a));
    }

    [Fact]
    public void PercentageBoundsAndPaddingRecomputeOnResize()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Style.Default with { Width = 100, Height = 50, Padding = new LengthEdges(Length.Percentage(10), 0, 0, 0) }, r);
        var child = t.Create(Style.Default with { Width = Length.Stretch(), Height = 10 }, a);
        var bounded = t.Create(Style.Default with { Width = 500, Height = 10, MaxWidth = Length.Percentage(50) }, r);
        t.Layout(r, new(200, 100));
        Assert.Equal(20, t.GetWorldRect(child).X, 3);
        Assert.Equal(100, t.GetRect(bounded).Width);
        t.Layout(r, new(300, 100));
        Assert.Equal(30, t.GetWorldRect(child).X, 3);
        Assert.Equal(70, t.GetRect(child).Width, 3);
        Assert.Equal(150, t.GetRect(bounded).Width);
    }

    [Fact]
    public void FailedResizedLayoutCannotPoisonArrangementCache()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Padding = new LengthEdges(Length.Percentage(10)) });
        var child = t.Create(Style.Default, r);
        bool fail = false;
        t.SetMeasure(child, (_, _, _) => fail ? throw new InvalidOperationException() : new Size(20, 20));
        t.Layout(r, new(100, 100));
        fail = true;
        Assert.Throws<InvalidOperationException>(() => t.Layout(r, new(200, 200)));
        fail = false;
        t.Layout(r, new(200, 200));
        Assert.Equal(20, t.GetRect(child).X, 3);
        Assert.Equal(20, t.GetRect(child).Y, 3);
        Assert.Equal(20, t.GetRect(child).Width, 3);
    }

    /// <summary>
    /// A percentage bound is the whole bound, not an addition to it. Splitting pixels and percent
    /// across parallel fields used to make this a silent no-op, because the unlimited default
    /// swallowed the percentage.
    /// </summary>
    [Fact]
    public void APercentageBoundReplacesTheUnlimitedDefault()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var capped = t.Create(Style.Default with { Width = 500, Height = 10, MaxWidth = Length.Percentage(50) }, r);
        var floored = t.Create(Style.Default with { Width = 10, Height = 10, MinWidth = Length.Percentage(25) }, r);
        var composite = t.Create(Style.Default with { Width = 500, Height = 10, MaxWidth = Length.Percentage(50, offset: 20) }, r);
        t.Layout(r, new(400, 100));
        Assert.Equal(200, t.GetRect(capped).Width);
        Assert.Equal(100, t.GetRect(floored).Width);
        Assert.Equal(220, t.GetRect(composite).Width);
    }

    [Fact]
    public void StretchingChildDoesNotInflateAnIntrinsicCrossAxis()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var row = t.Create(Style.Default with { Layout = LayoutMode.Row, Width = Length.Stretch(), Height = Length.Auto }, r);
        var content = t.Create(Style.Default with { Width = 80, Height = 20 }, row);
        var spacer = t.Create(Style.Default with { Width = Length.Stretch(), Height = Length.Stretch() }, row);
        t.Layout(r, new(400, 300));
        // The spacer contributes nothing to the row's intrinsic height, then fills the resolved 20.
        Assert.Equal(20, t.GetRect(row).Height);
        Assert.Equal(20, t.GetRect(spacer).Height);
        Assert.Equal(new Rect(0, 0, 80, 20), t.GetRect(content));
    }

    [Fact]
    public void StretchingChildStillFillsADefiniteCrossAxis()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var row = t.Create(Style.Default with { Layout = LayoutMode.Row, Width = Length.Stretch(), Height = 60 }, r);
        var spacer = t.Create(Style.Default with { Width = Length.Stretch(), Height = Length.Stretch() }, row);
        t.Layout(r, new(400, 300));
        Assert.Equal(60, t.GetRect(spacer).Height);
    }

    [Fact]
    public void IntrinsicOverlayIsNotInflatedByAStretchingChild()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var overlay = t.Create(Style.Default with { Layout = LayoutMode.Overlay, Width = Length.Auto, Height = Length.Auto }, r);
        t.Create(Style.Default with { Width = 90, Height = 24 }, overlay);
        t.Create(Style.Default with { Width = Length.Stretch(), Height = Length.Stretch() }, overlay);
        t.Layout(r, new(400, 300));
        Assert.Equal(new Rect(0, 0, 90, 24), t.GetRect(overlay));
    }
}
