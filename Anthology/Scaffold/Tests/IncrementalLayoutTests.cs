using Prowl.Scaffold;

namespace Scaffold.Tests;
public class IncrementalLayoutTests
{
    [Fact]
    public void BulkExportMatchesIndividualQueriesIncludingHiddenAndDetachedSubtrees()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default with { Padding = new LengthEdges(7) });
        var a = tree.Create(Style.Default with { Width = 50, Height = 50, Padding = new LengthEdges(3), Reverse = true }, root);
        tree.Create(Style.Default with { Width = 10, Height = 10 }, a);
        var hidden = tree.Create(Style.Default with { Hidden = true }, a);
        tree.Create(Style.Default, hidden);
        tree.Layout(root, new(100, 100));
        var output = new LayoutResult[tree.Count + 2];
        int written = tree.CopyWorldRects(root, output);
        Assert.Equal(tree.Count, written);
        Assert.Equal(root, output[0].Node);
        foreach (var result in output.AsSpan(0, written))
        {
            Assert.Equal(tree.GetWorldRect(result.Node), result.WorldRect);
        }

        Assert.Equal(default, output[written]);
        written = tree.CopyWorldRects(a, output);
        Assert.Equal(tree.GetSubtreeCount(a), written);
        foreach (var result in output.AsSpan(0, written))
        {
            Assert.Equal(tree.GetWorldRect(result.Node), result.WorldRect);
        }

        written = tree.CopyWorldRects(hidden, output);
        foreach (var result in output.AsSpan(0, written))
        {
            Assert.Equal(default, result.WorldRect);
        }

        tree.Detach(a);
        tree.Layout(a, new(100, 100));
        written = tree.CopyWorldRects(a, output);
        foreach (var result in output.AsSpan(0, written))
        {
            Assert.Equal(tree.GetWorldRect(result.Node), result.WorldRect);
        }
    }

    [Fact]
    public void BulkExportRejectsShortBufferBeforeWriting()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        tree.Create(Style.Default, root);
        var sentinel = new LayoutResult(root, new Rect(1, 2, 3, 4));
        var output = new[]
        {
            sentinel
        };
        Assert.Throws<ArgumentException>(() => tree.CopyWorldRects(root, output));
        Assert.Equal(sentinel, output[0]);
        Assert.Throws<ArgumentException>(() => tree.CopyWorldRects(default, output));
    }

    [Fact]
    public void SubtreeInvalidationRefreshesExternalContentAndAutoAncestors()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var panel = tree.Create(Style.Default with { Width = 50 }, root);
        float height = 10;
        for (int i = 0; i < 5; i++)
        {
            var child = tree.Create(Style.Default, panel);
            tree.SetMeasure(child, (_, _, _) => new(20, height));
        }

        var sibling = tree.Create(Style.Default, root);
        int calls = 0;
        tree.SetMeasure(sibling, (_, _, _) =>
        {
            calls++;
            return new(20, 20);
        });
        tree.Layout(root, new(100, 100));
        int before = calls;
        height = 20;
        tree.MarkSubtreeDirty(panel);
        tree.Layout(root, new(100, 100));
        Assert.Equal(100, tree.GetRect(panel).Height);
        Assert.Equal(before, calls);
        Assert.Equal(100, tree.GetRect(sibling).Y);
    }

    [Fact]
    public void HiddenEditsAndNewChildrenRestoreAfterShow()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var panelStyle = Style.Default with
        {
            Width = 50,
            Height = 50
        };
        var panel = tree.Create(panelStyle, root);
        var child = tree.Create(Style.Default with { Width = 10, Height = 10 }, panel);
        tree.Layout(root, new(100, 100));
        tree.SetStyle(panel, panelStyle with { Hidden = true });
        tree.Layout(root, new(100, 100));
        var added = tree.Create(Style.Default with { Width = 20, Height = 20 }, panel);
        tree.SetStyle(child, tree.GetStyle(child)with { Height = 15 });
        tree.Layout(root, new(100, 100));
        Assert.False(tree.IsDirty(added));
        Assert.Equal(default, tree.GetRect(child));
        tree.SetStyle(panel, panelStyle);
        tree.Layout(root, new(100, 100));
        Assert.Equal(15, tree.GetRect(child).Height);
        Assert.Equal(15, tree.GetRect(added).Y);
    }

    [Fact]
    public void InvalidationCoalescingHandlesNewAndReparentedDirtyNodes()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var a = tree.Create(Style.Default, root);
        var b = tree.Create(Style.Default, root);
        tree.Layout(root, new(100, 100));
        var child = tree.Create(Style.Default with { Width = 10, Height = 10 }, a);
        Assert.True(tree.IsDirty(root));
        tree.Layout(root, new(100, 100));
        tree.MarkDirty(child);
        tree.PlaceAfter(child, b);
        tree.Layout(root, new(100, 100));
        Assert.Equal(0, tree.GetRect(a).Height);
        Assert.Equal(10, tree.GetRect(b).Height);
        Assert.False(tree.IsDirty(root));
    }

    [Fact]
    public void BulkApisAllocateNothingAndRejectReentrantInvalidation()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var c = tree.Create(Style.Default, root);
        var output = new LayoutResult[2];
        for (int i = 0; i < 100; i++)
        {
            tree.MarkSubtreeDirty(root);
            tree.Layout(root, new(100, 100));
            tree.CopyWorldRects(root, output);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            tree.MarkSubtreeDirty(root);
            tree.Layout(root, new(100, 100));
            tree.CopyWorldRects(root, output);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        tree.SetMeasure(c, (_, _, _) =>
        {
            tree.MarkSubtreeDirty(root);
            return default;
        });
        Assert.Throws<InvalidOperationException>(() => tree.Layout(root, new(100, 100)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IntrinsicMeasurementReceivesMinimumConstrainedSpace(bool width)
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var node = tree.Create(Style.Default with { MinWidth = width ? 200 : 0, MinHeight = width ? 0 : 200 }, root);
        tree.SetMeasure(node, (_, available, _) => width ? new(available.Width, 1000 / available.Width) : new(1000 / available.Height, available.Height));
        tree.Layout(root, new(100, 100));
        var rect = tree.GetRect(node);
        Assert.Equal(width ? 200 : 5, rect.Width);
        Assert.Equal(width ? 5 : 200, rect.Height);
    }

    [Fact]
    public void HiddenNodesAreCleanAfterLayout()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var hidden = tree.Create(Style.Default with { Hidden = true }, root);
        tree.Layout(root, new(100, 100));
        Assert.False(tree.IsDirty(hidden));
    }

    private sealed class Content
    {
        public float Height;
    }

    [Fact]
    public void ReregisteringAMeasureAlwaysInvalidatesEvenWhenNothingLooksChanged()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default);
        var node = tree.Create(Style.Default with { Width = Length.Auto, Height = Length.Auto }, root);
        var content = new Content { Height = 10 };
        MeasureContent measure = (_, _, context) => new Size(20, ((Content)context!).Height);
        tree.SetMeasure(node, measure, content);
        tree.Layout(root, new(400, 300));
        Assert.Equal(10, tree.GetRect(node).Height);
        // Same delegate, same context instance, mutated in place: the tree cannot see the change,
        // so re-registering has to be taken as the caller declaring one.
        content.Height = 50;
        tree.SetMeasure(node, measure, content);
        tree.Layout(root, new(400, 300));
        Assert.Equal(50, tree.GetRect(node).Height);
    }

    [Fact]
    public void ReregisteringAMeasureDirtiesOnlyItsOwnAncestorPath()
    {
        var tree = new LayoutTree();
        var root = tree.Create(Style.Default with { Layout = LayoutMode.Column });
        var leaves = new NodeId[4];
        var contents = new Content[4];
        MeasureContent measure = (_, _, context) => new Size(20, ((Content)context!).Height);
        for (int branch = 0; branch < leaves.Length; branch++)
        {
            var top = tree.Create(Style.Default with { Width = Length.Auto, Height = Length.Auto }, root);
            var middle = tree.Create(Style.Default with { Width = Length.Auto, Height = Length.Auto }, top);
            leaves[branch] = tree.Create(Style.Default with { Width = Length.Auto, Height = Length.Auto }, middle);
            contents[branch] = new Content { Height = 10 };
            tree.SetMeasure(leaves[branch], measure, contents[branch]);
        }

        tree.Layout(root, new(400, 300));
        contents[1].Height = 40;
        tree.SetMeasure(leaves[1], measure, contents[1]);
        var statistics = tree.Layout(root, new(400, 300));
        Assert.Equal(40, tree.GetRect(leaves[1]).Height);
        // Only the touched branch's three nodes plus the root are revisited; the other nine
        // reuse their arrangement and are never re-measured.
        Assert.True(statistics.ArrangedNodes <= 4, $"arranged {statistics.ArrangedNodes}");
        Assert.True(statistics.ArrangeCacheHits >= 3, $"hits {statistics.ArrangeCacheHits}");
        foreach (int branch in new[] { 0, 2, 3 })
        {
            Assert.Equal(10, tree.GetRect(leaves[branch]).Height);
        }
    }
}
