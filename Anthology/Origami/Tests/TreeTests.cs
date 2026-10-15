using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Xunit;

namespace Prowl.OrigamiUI.Tests;

public class TreeTests
{
    private const float TreeHeight = 300f;
    private const float PadTop = 6f; // Tree's default top inset

    [Fact]
    public void LargeList_BuildsOnlyRowsInView()
    {
        using var scene = new TreeScene(Flat(2000));
        var drawn = scene.Frame();

        Assert.Contains("n0", drawn.Keys);
        Assert.DoesNotContain("n1999", drawn.Keys);
        // The viewport plus a few rows of overscan, nowhere near the whole list.
        Assert.InRange(drawn.Count, (int)(TreeHeight / scene.RowHeight), (int)(TreeHeight / scene.RowHeight) + 8);
    }

    [Fact]
    public void Scrolled_RowsSitWhereAFullyBuiltTreeWouldPutThem()
    {
        using var scene = new TreeScene(Flat(2000));
        scene.Frame();

        float scroll = 1000 * scene.RowHeight + 7f;
        var drawn = scene.ScrollTo(scroll);

        Assert.Contains("n1000", drawn.Keys);
        Assert.DoesNotContain("n0", drawn.Keys);
        foreach (var (id, top) in drawn)
        {
            int index = int.Parse(id[1..]);
            Assert.Equal(PadTop + index * scene.RowHeight - scroll, top, 0.01f);
        }
    }

    [Fact]
    public void ScrollToEnd_ReachesLastRow()
    {
        using var scene = new TreeScene(Flat(2000));
        scene.Frame();

        // Clamped by the content height, which only comes out right if the spacers stand in exactly
        // for the rows they replace.
        var drawn = scene.ScrollTo(1_000_000f);

        Assert.Contains("n1999", drawn.Keys);
        float lastTop = drawn["n1999"];
        Assert.Equal(TreeHeight - scene.Padding - scene.RowHeight, lastTop, 0.01f);
    }

    [Fact]
    public void CollapsedParent_SkipsChildrenAndShiftsRowsBelow()
    {
        // p0 with 500 children, then 500 roots.
        var nodes = new List<TreeNode> { Node("p0", 0, hasChildren: true, expanded: false) };
        for (int i = 0; i < 500; i++) nodes.Add(Node($"c{i}", 1));
        for (int i = 0; i < 500; i++) nodes.Add(Node($"r{i}", 0));

        using var scene = new TreeScene(nodes);
        var drawn = scene.Frame();

        Assert.DoesNotContain("c0", drawn.Keys);
        Assert.Equal(PadTop + scene.RowHeight, drawn["r0"], 0.01f);
    }

    [Fact]
    public void ExpandedParent_ChildrenAreRowsOfTheirOwn()
    {
        var nodes = new List<TreeNode> { Node("p0", 0, hasChildren: true, expanded: true) };
        for (int i = 0; i < 1000; i++) nodes.Add(Node($"c{i}", 1));

        using var scene = new TreeScene(nodes);
        scene.Frame();
        float scroll = 600 * scene.RowHeight;
        var drawn = scene.ScrollTo(scroll);

        // The parent is scrolled away, but its children still virtualize individually.
        Assert.DoesNotContain("p0", drawn.Keys);
        Assert.Equal(PadTop + 600 * scene.RowHeight - scroll, drawn["c599"], 0.01f);
        Assert.True(drawn.Count < 40);
    }

    [Fact]
    public void ExpandAnimation_RevealsChildrenThenSettles()
    {
        var parent = Node("p0", 0, hasChildren: true, expanded: false);
        var nodes = new List<TreeNode> { parent };
        for (int i = 0; i < 3; i++) nodes.Add(Node($"c{i}", 1));
        nodes.Add(Node("r0", 0));

        using var scene = new TreeScene(nodes);
        var drawn = scene.Frame();
        Assert.DoesNotContain("c0", drawn.Keys);
        float rowH = scene.RowHeight;

        parent.OverrideExpanded = true;
        drawn = scene.Frame(0.05f); // one third of the 0.15s expand

        // Mid-animation the children are built (inside the clipped wrapper) and r0 sits below the
        // partially open block.
        Assert.Contains("c0", drawn.Keys);
        float r0Mid = drawn["r0"];
        Assert.InRange(r0Mid, PadTop + rowH + 0.5f, PadTop + 4 * rowH - 0.5f);

        for (int f = 0; f < 4; f++) drawn = scene.Frame(0.05f);

        Assert.Equal(PadTop + 4 * rowH, drawn["r0"], 0.01f);
        Assert.Equal(PadTop + 3 * rowH, drawn["c2"], 0.01f);
    }

    [Fact]
    public void ExpandStateSink_CoversRowsOutOfView()
    {
        var nodes = Flat(1000);
        nodes[900] = Node("n900", 0, hasChildren: true, expanded: true);
        nodes.Insert(901, Node("n900c", 1));

        using var scene = new TreeScene(nodes);
        scene.Frame();

        Assert.True(scene.ExpandState["n900"]);
        Assert.False(scene.ExpandState["n5"]);
    }

    [Fact]
    public void Events_ReportIndexInTheFullList()
    {
        using var scene = new TreeScene(Flat(2000));
        scene.Frame();
        float scroll = 1500 * scene.RowHeight;
        var drawn = scene.ScrollTo(scroll);

        float rowY = drawn["n1510"] + scene.RowHeight / 2f;
        int? clicked = scene.Click(new Float2(100, rowY));

        Assert.Equal(1510, clicked);
    }

    [Fact]
    public void RenamingRow_StaysBuiltWhenScrolledAway()
    {
        var nodes = Flat(2000);
        nodes[0].IsRenaming = true;

        using var scene = new TreeScene(nodes, t => t.OnRenamed((_, _) => { }));
        scene.Frame();
        float scroll = 1000 * scene.RowHeight;
        var drawn = scene.ScrollTo(scroll);

        // n0 is built out of view, and the spacer split around it keeps the visible rows in place.
        Assert.Equal(PadTop - scroll, drawn["n0"], 0.01f);
        Assert.DoesNotContain("n1", drawn.Keys);
        Assert.Equal(PadTop + 1000 * scene.RowHeight - scroll, drawn["n1000"], 0.01f);
    }

    [Fact]
    public void CollapseAnimation_ShrinksThenDropsChildren()
    {
        var parent = Node("p0", 0, hasChildren: true, expanded: true);
        var nodes = new List<TreeNode> { parent };
        for (int i = 0; i < 3; i++) nodes.Add(Node($"c{i}", 1));
        nodes.Add(Node("r0", 0));

        using var scene = new TreeScene(nodes);
        var drawn = scene.Frame();
        float rowH = scene.RowHeight;
        Assert.Equal(PadTop + 4 * rowH, drawn["r0"], 0.01f);

        parent.OverrideExpanded = false;
        drawn = scene.Frame(0.05f);

        // Mid-collapse the children are still built, in the shrinking wrapper.
        Assert.Contains("c0", drawn.Keys);
        Assert.InRange(drawn["r0"], PadTop + rowH + 0.5f, PadTop + 4 * rowH - 0.5f);

        drawn = scene.Settle();

        Assert.DoesNotContain("c0", drawn.Keys);
        Assert.Equal(PadTop + rowH, drawn["r0"], 0.01f);
    }

    [Fact]
    public void AnimatingParentAboveView_SpacerTracksItsHeight()
    {
        // p0 with 50 children, then 2000 roots; the view sits on the roots, well below p0.
        var parent = Node("p0", 0, hasChildren: true, expanded: true);
        var nodes = new List<TreeNode> { parent };
        for (int i = 0; i < 50; i++) nodes.Add(Node($"c{i}", 1));
        for (int i = 0; i < 2000; i++) nodes.Add(Node($"r{i}", 0));

        using var scene = new TreeScene(nodes);
        scene.Frame();
        float rowH = scene.RowHeight;
        float scroll = (51 + 1000) * rowH;
        scene.ScrollTo(scroll);

        parent.OverrideExpanded = false;
        var drawn = scene.Frame(0.05f); // a third of the 0.15s collapse

        // The animating block is out of view, so it isn't built, but the space it stands in for is
        // exactly its current (two-thirds open) height: everything below moves up ~17 rows.
        Assert.DoesNotContain("p0", drawn.Keys);
        Assert.DoesNotContain("c49", drawn.Keys);
        float anim = 1f - 0.05f / 0.15f;
        Assert.Equal(PadTop + (1 + anim * 50 + 1017) * rowH - scroll, drawn["r1017"], 0.5f);

        drawn = scene.Settle();

        // Fully collapsed: the whole 50 rows are gone, so r1050 now sits at the top of the view.
        Assert.Equal(PadTop + (1 + 1050) * rowH - scroll, drawn["r1050"], 0.01f);
    }

    [Fact]
    public void NestedAnimationUnderOpenParent_RowsBelowFollow()
    {
        // a (open) > b (collapsed) > g0..g3, then a's other child c, then root r0.
        var b = Node("b", 1, hasChildren: true, expanded: false);
        var nodes = new List<TreeNode> { Node("a", 0, hasChildren: true, expanded: true), b };
        for (int i = 0; i < 4; i++) nodes.Add(Node($"g{i}", 2));
        nodes.Add(Node("c", 1));
        nodes.Add(Node("r0", 0));

        using var scene = new TreeScene(nodes);
        var drawn = scene.Frame();
        float rowH = scene.RowHeight;
        Assert.Equal(PadTop + 2 * rowH, drawn["c"], 0.01f);

        b.OverrideExpanded = true;
        drawn = scene.Frame(0.05f);

        Assert.Contains("g0", drawn.Keys);
        Assert.InRange(drawn["c"], PadTop + 2 * rowH + 0.5f, PadTop + 6 * rowH - 0.5f);
        Assert.Equal(drawn["c"] + rowH, drawn["r0"], 0.01f);

        drawn = scene.Settle();

        Assert.Equal(PadTop + 5 * rowH, drawn["g3"], 0.01f);
        Assert.Equal(PadTop + 6 * rowH, drawn["c"], 0.01f);
        Assert.Equal(PadTop + 7 * rowH, drawn["r0"], 0.01f);
    }

    [Fact]
    public void ListShrinksWhileScrolledToEnd_ScrollClampsBack()
    {
        var nodes = Flat(2000);
        using var scene = new TreeScene(nodes);
        scene.Frame();
        scene.ScrollTo(1_000_000f);

        nodes.RemoveRange(10, nodes.Count - 10);
        var drawn = scene.Settle(10);

        for (int i = 0; i < 10; i++)
            Assert.Equal(PadTop + i * scene.RowHeight, drawn[$"n{i}"], 0.01f);
    }

    [Fact]
    public void ScrollingThrough_EveryRowInViewIsBuiltInPlace()
    {
        using var scene = new TreeScene(Flat(300));
        scene.Frame();
        float rowH = scene.RowHeight;

        // An odd step so the view lands at many different offsets within a row.
        for (float scroll = 0f; scroll < 250 * rowH; scroll += 61.7f)
        {
            var drawn = scene.ScrollTo(scroll);
            for (int k = 0; k < 300; k++)
            {
                float top = PadTop + k * rowH - scroll;
                if (top + rowH <= 0f || top >= TreeHeight) continue;

                Assert.True(drawn.ContainsKey($"n{k}"), $"n{k} missing at scroll {scroll}");
                Assert.Equal(top, drawn[$"n{k}"], 0.01f);
            }
        }
    }

    [Fact]
    public void CaretClick_OnScrolledRow_ExpandsIt()
    {
        var nodes = Flat(2000);
        nodes[1500] = Node("n1500", 0, hasChildren: true);
        nodes.InsertRange(1501, new[] { Node("k0", 1), Node("k1", 1), Node("k2", 1) });

        var changes = new List<(string id, bool expanded)>();
        using var scene = new TreeScene(nodes, t => t.OnExpandChanged((n, v) => changes.Add((n.Id, v))));
        scene.Frame();
        float rowH = scene.RowHeight;
        var drawn = scene.ScrollTo(1500 * rowH);
        Assert.DoesNotContain("k0", drawn.Keys);

        // The caret sits after the 6px base indent and is 12px wide.
        float rowTop = drawn["n1500"];
        int? selected = scene.Click(new Float2(12, rowTop + rowH / 2f));
        drawn = scene.Settle();

        Assert.Null(selected); // the caret doesn't select the row
        Assert.Equal(new[] { ("n1500", true) }, changes);
        Assert.Equal(rowTop + rowH, drawn["k0"], 0.01f);
        Assert.Equal(rowTop + 3 * rowH, drawn["k2"], 0.01f);
        Assert.Equal(rowTop + 4 * rowH, drawn["n1501"], 0.01f);
    }

    [Fact]
    public void NonExpandableNodes_DeeperFollowersAreSiblingRows()
    {
        // Neither x (no children) nor z (a leaf) can expand, so the deeper nodes after them are just
        // rows in sequence.
        var leaf = Node("z", 0, hasChildren: true);
        leaf.IsLeaf = true;
        var nodes = new List<TreeNode> { Node("x", 0), Node("y", 1), leaf, Node("w", 1), Node("v", 0) };

        using var scene = new TreeScene(nodes);
        var drawn = scene.Frame();

        string[] order = { "x", "y", "z", "w", "v" };
        for (int i = 0; i < order.Length; i++)
            Assert.Equal(PadTop + i * scene.RowHeight, drawn[order[i]], 0.01f);
    }

    private static List<TreeNode> Flat(int count)
    {
        var nodes = new List<TreeNode>(count);
        for (int i = 0; i < count; i++) nodes.Add(Node($"n{i}", 0));
        return nodes;
    }

    private static TreeNode Node(string id, int depth, bool hasChildren = false, bool expanded = false) => new()
    {
        Id = id,
        Label = id,
        Depth = depth,
        HasChildren = hasChildren,
        DefaultExpanded = expanded,
    };

    private sealed class TreeScene : IDisposable
    {
        private readonly IDisposable _themeScope;
        private readonly Paper _paper;
        private readonly List<TreeNode> _nodes;
        private readonly Action<TreeBuilder>? _configure;
        private readonly Dictionary<string, float> _drawn = new();
        private int? _clicked;

        public readonly Dictionary<string, bool> ExpandState = new();
        public float RowHeight { get; }
        public float Padding => 4f;

        public TreeScene(List<TreeNode> nodes, Action<TreeBuilder>? configure = null)
        {
            _configure = configure;
            using var stream = typeof(TreeTests).Assembly.GetManifestResourceStream("TestFont.ttf")!;
            var theme = OrigamiTheme.CreateDefaults();
            theme.Font = new FontFile(stream);
            _themeScope = Origami.PushTheme(theme);
            RowHeight = theme.Metrics.RowHeight;
            _paper = new Paper(new NullRenderer(), 800, 600, new FontAtlasSettings());
            _paper.PointerPos = new Float2(-100, -100);
            _nodes = nodes;
        }

        /// <summary>Run one frame; returns the top (y) of each row the tree built, by node id.</summary>
        public Dictionary<string, float> Frame(float dt = 0.125f)
        {
            _drawn.Clear();
            _paper.BeginFrame(dt);
            var tree = Origami.Tree(_paper, "tree", 400, TreeHeight);
            _configure?.Invoke(tree);
            tree.Nodes(_nodes)
                .MultiSelect()
                .OnSelectModified((e, _, _) => _clicked = e.Index)
                .ExpandStateSink(ExpandState)
                .CustomRowContent((p, node, _, _) =>
                {
                    string id = node.Id;
                    p.Box("probe").Width(1).Height(RowHeight).IsNotInteractable()
                        .OnPostLayout((_, rect) => _drawn[id] = (float)rect.Min.Y);
                })
                .Show();
            _paper.EndFrame();
            return new Dictionary<string, float>(_drawn);
        }

        /// <summary>Run enough frames for expand animations and smooth scrolling to finish.</summary>
        public Dictionary<string, float> Settle(int frames = 6)
        {
            Dictionary<string, float> drawn = null!;
            for (int i = 0; i < frames; i++) drawn = Frame();
            return drawn;
        }

        /// <summary>Scroll to <paramref name="y"/> and let the smooth scroll settle.</summary>
        public Dictionary<string, float> ScrollTo(float y)
        {
            Origami.ScrollTo("tree_scroll", new Float2(0, y));
            Dictionary<string, float> drawn = null!;
            for (int i = 0; i < 6; i++) drawn = Frame();
            return drawn;
        }

        public int? Click(Float2 pos)
        {
            _clicked = null;
            _paper.PointerPos = pos;
            Frame();
            _paper.SetPointerState(PaperMouseBtn.Left, (int)pos.X, (int)pos.Y, true, false);
            Frame();
            _paper.SetPointerState(PaperMouseBtn.Left, (int)pos.X, (int)pos.Y, false, false);
            Frame();
            return _clicked;
        }

        public void Dispose() => _themeScope.Dispose();
    }

    private sealed class NullRenderer : ICanvasRenderer
    {
        public void Dispose() { }
        public object CreateTexture(uint w, uint h) => new Int2((int)w, (int)h);
        public Int2 GetTextureSize(object texture) => (Int2)texture;
        public void SetTextureData(object texture, IntRect bounds, byte[] data) { }
        public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> calls) { }
    }
}
