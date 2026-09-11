using Prowl.Scaffold;

namespace Scaffold.Tests;
public class LayoutTests
{
    private static Style Box(float w, float h) => Style.Default with
    {
        Width = w,
        Height = h
    };
    private static void RectIs(LayoutTree t, NodeId n, float x, float y, float w, float h)
    {
        var r = t.GetWorldRect(n);
        Assert.Equal(x, r.X, 3);
        Assert.Equal(y, r.Y, 3);
        Assert.Equal(w, r.Width, 3);
        Assert.Equal(h, r.Height, 3);
    }

    [Fact]
    public void AutoOverlayUsesFinalContentWidthForMeasurement()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var overlay = t.Create(Style.Default with { Width = 100, Layout = LayoutMode.Overlay }, r);
        var c = t.Create(Style.Default with { Width = Length.Stretch() }, overlay);
        t.SetMeasure(c, (_, size, _) => new(size.Width, 1000 / Math.Max(1, size.Width)));
        t.Layout(r, new(300, 300));
        RectIs(t, overlay, 0, 0, 100, 10);
    }

    [Fact]
    public void DetachedSubtreeCanBeLaidOutIndependently()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Box(20, 20), r);
        var b = t.Create(Box(5, 5), a);
        t.Layout(r, new(100, 100));
        t.Detach(a);
        Assert.Equal(default, t.GetParent(a));
        t.Layout(a, new(50, 50));
        RectIs(t, b, 0, 0, 5, 5);
        t.Layout(r, new(100, 100));
        Assert.Equal(default, t.GetFirstChild(r));
    }

    [Fact]
    public void DeepIntrinsicChainsReuseMeasurements()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var last = r;
        for (int i = 0; i < 64; i++)
        {
            last = t.Create(Style.Default with { Width = 100 }, last);
        }

        float height = 10;
        t.SetMeasure(last, (_, _, _) => new(100, height));
        t.Layout(r, new(100, 100));
        Assert.Equal(10, t.GetRect(last).Height);
        height = 20;
        t.MarkDirty(last);
        var stats = t.Layout(r, new(100, 100));
        Assert.Equal(20, t.GetRect(last).Height);
        Assert.True(stats.MeasuredNodes < 1000, $"Unexpected repeated work: {stats.MeasuredNodes}");
    }

    [Fact]
    public void HiddenRootStaysZeroOnCleanLayoutAndRestoresChildren()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Padding = new LengthEdges(20) });
        var c = t.Create(Box(10, 10), r);
        t.Layout(r, new(100, 100));
        t.SetStyle(r, t.GetStyle(r)with { Hidden = true });
        t.Layout(r, new(100, 100));
        Assert.Equal(0, t.Layout(r, new(100, 100)).ArrangedNodes);
        Assert.Equal(default, t.GetWorldRect(r));
        Assert.Equal(default, t.GetWorldRect(c));
        t.SetStyle(r, t.GetStyle(r)with { Hidden = false });
        t.Layout(r, new(100, 100));
        RectIs(t, c, 20, 20, 10, 10);
    }

    [Fact]
    public void DepthLimitRejectsBeforeChangingTree()
    {
        var t = new LayoutTree();
        var root = t.Create(Box(1, 1));
        var last = root;
        for (int i = 1; i < 256; i++)
        {
            last = t.Create(Box(1, 1), last);
        }

        Assert.Throws<ArgumentException>(() => t.Create(Box(1, 1), last));
        Assert.Equal(256, t.Count);
        t.Layout(root, new(10, 10));
        t.Remove(root);
        Assert.Equal(0, t.Count);
    }

    [Theory]
    [InlineData(LayoutMode.Row)]
    [InlineData(LayoutMode.Column)]
    [InlineData(LayoutMode.Grid)]
    [InlineData(LayoutMode.Overlay)]
    public void FullDirtyLayoutAllocatesNothing(LayoutMode mode)
    {
        var t = new LayoutTree(101);
        var r = t.Create(Style.Default with { Layout = mode, GridColumns = 4 });
        var children = new NodeId[100];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = t.Create(Style.Default with { Width = Length.Stretch(), Height = Length.Stretch() }, r);
        }

        void Run()
        {
            foreach (var c in children)
            {
                t.MarkDirty(c);
            }

            t.Layout(r, new(1000, 1000));
        }

        for (int i = 0; i < 100; i++)
        {
            Run();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            Run();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void FlexWidthFeedsAutoHeightOfParentAndOnlyDirtyMeasurementsRepeat()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var row = t.Create(Style.Default with { Layout = LayoutMode.Row, Width = 100 }, r);
        var a = t.Create(Style.Default with { Width = Length.Stretch() }, row);
        var b = t.Create(Style.Default with { Width = Length.Stretch() }, row);
        int aCalls = 0;
        t.SetMeasure(a, (_, space, _) =>
        {
            aCalls++;
            return new(space.Width, 1000 / Math.Max(1, space.Width));
        });
        t.SetMeasure(b, (_, space, _) => new(space.Width, 1000 / Math.Max(1, space.Width)));
        t.Layout(r, new(300, 300));
        RectIs(t, row, 0, 0, 100, 20);
        int calls = aCalls;
        t.MarkDirty(b);
        t.Layout(r, new(300, 300));
        Assert.Equal(calls, aCalls);
        RectIs(t, row, 0, 0, 100, 20);
    }

    [Fact]
    public void OneLeafEditMeasuresOnlyThatLeafInWideFixedRow()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row });
        NodeId last = default;
        for (int i = 0; i < 1000; i++)
        {
            last = t.Create(Box(10, 10), r);
        }

        t.Layout(r, new(10000, 100));
        t.SetStyle(last, Box(10, 11));
        var stats = t.Layout(r, new(10000, 100));
        Assert.Equal(1, stats.MeasuredNodes);
        Assert.Equal(2, stats.ArrangedNodes);
    }

    [Fact]
    public void FixedRowAndCleanRoot()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row, Padding = new LengthEdges(10), Gap = 5 });
        var a = t.Create(Box(100, 40), r);
        var b = t.Create(Box(80, 20), r);
        t.Layout(r, new(400, 100));
        RectIs(t, a, 10, 10, 100, 40);
        RectIs(t, b, 115, 10, 80, 20);
        Assert.False(t.SetStyle(a, Box(100, 40)));
        Assert.Equal(new LayoutStatistics(0, 0, 0, 1), t.Layout(r, new(400, 100)));
    }

    [Fact]
    public void WeightedGrowRedistributesAtMaximum()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row });
        var a = t.Create(Box(0, 20)with { Width = Length.Stretch(), MaxWidth = 100 }, r);
        var b = t.Create(Box(0, 20)with { Width = Length.Stretch(2) }, r);
        t.Layout(r, new(600, 100));
        RectIs(t, a, 0, 0, 100, 20);
        RectIs(t, b, 100, 0, 500, 20);
    }

    [Fact]
    public void WeightedShrinkRedistributesAtMinimum()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row });
        var a = t.Create(Box(400, 20)with { Width = Length.Pixels(400).Shrinkable(), MinWidth = 250 }, r);
        var b = t.Create(Box(200, 20)with { Width = Length.Pixels(200).Shrinkable() }, r);
        t.Layout(r, new(300, 100));
        RectIs(t, a, 0, 0, 250, 20);
        RectIs(t, b, 250, 0, 50, 20);
    }

    [Fact]
    public void CompositePercentAndResize()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Box(0, 20)with { Width = Length.Percentage(50) - Length.Pixels(8) }, r);
        t.Layout(r, new(400, 100));
        RectIs(t, a, 0, 0, 192, 20);
        t.Layout(r, new(600, 100));
        RectIs(t, a, 0, 0, 292, 20);
    }

    [Fact]
    public void WrapAndReverse()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row, Wrap = true, Gap = 10, LineGap = 5, Reverse = true });
        var a = t.Create(Box(100, 20), r);
        var b = t.Create(Box(100, 30), r);
        var c = t.Create(Box(100, 40), r);
        t.Layout(r, new(210, 200));
        RectIs(t, c, 0, 0, 100, 40);
        RectIs(t, b, 110, 0, 100, 30);
        RectIs(t, a, 0, 45, 100, 20);
    }

    [Theory]
    [InlineData(Justify.Center, 100, 150)]
    [InlineData(Justify.End, 200, 250)]
    [InlineData(Justify.SpaceBetween, 0, 250)]
    [InlineData(Justify.SpaceAround, 50, 200)]
    [InlineData(Justify.SpaceEvenly, 66.666666f, 183.33333f)]
    public void Justification(Justify justify, float ax, float bx)
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row, Justify = justify, AlignItems = Align.Center });
        var a = t.Create(Box(50, 20), r);
        var b = t.Create(Box(50, 20), r);
        t.Layout(r, new(300, 100));
        RectIs(t, a, ax, 40, 50, 20);
        RectIs(t, b, bx, 40, 50, 20);
    }

    [Fact]
    public void GridAutoRowsAndAbsoluteAnchors()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Grid, GridColumns = 2, Gap = 10, LineGap = 5 });
        var a = t.Create(Box(1, 20), r);
        var b = t.Create(Box(1, 40), r);
        var c = t.Create(Box(1, 30), r);
        var d = t.Create(Box(1, 10)with { Position = Position.Absolute, Left = 10, Right = 20, Bottom = 5 }, r);
        t.Layout(r, new(210, 100));
        RectIs(t, a, 0, 0, 100, 20);
        RectIs(t, b, 110, 0, 100, 40);
        RectIs(t, c, 0, 45, 100, 30);
        RectIs(t, d, 10, 85, 180, 10);
    }

    [Fact]
    public void IntrinsicInvalidationAndWidthSensitiveContent()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Style.Default with { Width = Length.Stretch() }, r);
        int calls = 0;
        float area = 1000;
        t.SetMeasure(a, (_, available, _) =>
        {
            calls++;
            return new(available.Width, area / Math.Max(1, available.Width));
        });
        t.Layout(r, new(100, 200));
        RectIs(t, a, 0, 0, 100, 10);
        int before = calls;
        t.Layout(r, new(100, 200));
        Assert.Equal(before, calls);
        area = 2000;
        t.MarkDirty(a);
        t.Layout(r, new(100, 200));
        RectIs(t, a, 0, 0, 100, 20);
        t.Layout(r, new(50, 200));
        RectIs(t, a, 0, 0, 50, 40);
    }

    [Fact]
    public void MovingParentReusesDescendantsAndAutoAncestorResizes()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Row });
        var a = t.Create(Box(50, 20), r);
        var b = t.Create(Box(100, 100), r);
        var c = t.Create(Box(20, 20), b);
        t.Layout(r, new(400, 200));
        t.SetStyle(a, Box(80, 20));
        var stats = t.Layout(r, new(400, 200));
        RectIs(t, c, 80, 0, 20, 20);
        Assert.Equal(2, stats.ArrangedNodes);
        t.SetStyle(b, Style.Default);
        t.SetStyle(c, Box(70, 30));
        t.Layout(r, new(400, 200));
        RectIs(t, b, 80, 0, 70, 30);
    }

    [Fact]
    public void HiddenSubtreeRestoresAndDoesNotOccupyFlow()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Box(50, 50), r);
        var child = t.Create(Box(10, 10), a);
        var b = t.Create(Box(20, 20), r);
        t.Layout(r, new(100, 100));
        t.SetStyle(a, Box(50, 50)with { Hidden = true });
        t.Layout(r, new(100, 100));
        Assert.Equal(default, t.GetRect(child));
        RectIs(t, b, 0, 0, 20, 20);
        t.SetStyle(a, Box(50, 50));
        t.Layout(r, new(100, 100));
        RectIs(t, child, 0, 0, 10, 10);
        RectIs(t, b, 0, 50, 20, 20);
    }

    [Fact]
    public void ReparentRemovalAndHandleSafety()
    {
        var t = new LayoutTree(2);
        var r = t.Create(Style.Default);
        var a = t.Create(Box(10, 10), r);
        var b = t.Create(Box(20, 20), r);
        var c = t.Create(Box(5, 5), a);
        Assert.Throws<ArgumentException>(() => t.PlaceAfter(a, c));
        t.PlaceAfter(c, b);
        t.Remove(a);
        Assert.False(t.IsAlive(a));
        var replacement = t.Create(Box(30, 30), r);
        Assert.NotEqual(a, replacement);
        Assert.Throws<ArgumentException>(() => t.GetRect(a));
        Assert.Throws<ArgumentException>(() => new LayoutTree().GetRect(c));
        t.Layout(r, new(100, 100));
        RectIs(t, c, 0, 0, 5, 5);
        t.Remove(b);
        Assert.False(t.IsAlive(c));
        Assert.Equal(2, t.Count);
    }

    [Fact]
    public void OverlayAspectRatioAndSelfAlignment()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default with { Layout = LayoutMode.Overlay, AlignItems = Align.Center });
        var a = t.Create(Style.Default with { Width = 80, AspectRatio = 2 }, r);
        t.Layout(r, new(200, 100));
        RectIs(t, a, 60, 30, 80, 40);
    }

    [Fact]
    public void BadInputsAndCallbackMutationAreRejected()
    {
        var t = new LayoutTree();
        var r = t.Create(Style.Default);
        var a = t.Create(Style.Default, r);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.SetStyle(a, Box(float.NaN, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Layout(r, new(-1, 2)));
        t.SetMeasure(a, (_, _, _) =>
        {
            t.Remove(a);
            return default;
        });
        Assert.Throws<InvalidOperationException>(() => t.Layout(r, new(100, 100)));
        t.SetMeasure(a, null);
        t.Layout(r, new(100, 100));
    }

    [Fact]
    public void IncrementalMatchesFreshTreeAcrossRandomEdits()
    {
        var random = new Random(7341);
        var t = new LayoutTree();
        var styles = new Style[41];
        var parents = new int[41];
        var nodes = new NodeId[41];
        styles[0] = Style.Default with
        {
            Layout = LayoutMode.Row,
            Wrap = true
        };
        nodes[0] = t.Create(styles[0]);
        for (int i = 1; i < nodes.Length; i++)
        {
            parents[i] = i < 6 ? 0 : 1 + i % 5;
            styles[i] = i < 6 ? Style.Default with
            {
                Width = 100,
                Gap = 2
            }

            : Box(20 + i, 10 + i % 9);
            nodes[i] = t.Create(styles[i], nodes[parents[i]]);
        }

        for (int frame = 0; frame < 100; frame++)
        {
            int changed = random.Next(1, nodes.Length);
            styles[changed] = styles[changed] with
            {
                Width = random.Next(20, 160),
                Hidden = random.Next(7) == 0
            };
            t.SetStyle(nodes[changed], styles[changed]);
            var viewport = new Size(frame % 4 == 0 ? random.Next(200, 600) : 400, 400);
            t.Layout(nodes[0], viewport);
            var fresh = new LayoutTree();
            var other = new NodeId[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                other[i] = fresh.Create(styles[i], i == 0 ? default : other[parents[i]]);
            }

            fresh.Layout(other[0], viewport);
            for (int i = 0; i < nodes.Length; i++)
            {
                Assert.Equal(fresh.GetWorldRect(other[i]), t.GetWorldRect(nodes[i]));
            }
        }
    }
}
