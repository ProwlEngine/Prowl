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
