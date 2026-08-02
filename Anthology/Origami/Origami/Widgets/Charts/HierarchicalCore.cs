// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>One node of a <see cref="HierarchicalCore{TSelf, T}"/> chart's forest: a single item of the
/// source tree resolved into the label, aggregated value and colour the chart draws it with.
/// <c>Index</c> is a stable pre-order ordinal over the whole forest assigned in final draw order, so a
/// legend toggle or a hover index keeps pointing at the node that was actually drawn even after
/// <c>SortBy</c> and duplicate merging have reordered the siblings. <c>Start</c> and <c>Value</c>
/// together are the node's span in value units along the forest's flattened order, which is what every
/// type turns into pixels or degrees.</summary>
public sealed class HierarchicalNode<T>
{
    public T? Payload;
    public string Label = "";
    public double Value;
    public Color Color;
    public int Depth;
    public int Index;
    public HierarchicalNode<T>? Parent;
    public readonly List<HierarchicalNode<T>> Children = new();
    public double Start;
    public double Fraction;
    public bool Dimmed;
    public bool Selected;
    public bool LegendHidden;

    public bool Visible => !LegendHidden && Value > 0d;
}

/// <summary>One row of a hierarchical chart's legend. <c>Key</c> is the element-storage key the hide
/// toggle writes, and is always a node's <see cref="HierarchicalNode{T}.Index"/>.</summary>
public readonly struct HierarchicalLegendEntry
{
    public readonly string Label;
    public readonly Color Color;
    public readonly double Value;
    public readonly bool HasValue;
    public readonly int Key;
    public readonly bool Hidden;

    public HierarchicalLegendEntry(string label, Color color, double value, bool hasValue, int key, bool hidden)
    {
        Label = label ?? "";
        Color = color;
        Value = value;
        HasValue = hasValue;
        Key = key;
        Hidden = hidden;
    }
}

/// <summary>
/// Shared implementation for every hierarchical chart type (Treemap, Sunburst, FlameGraph). Resolves the
/// caller's item tree into a forest of aggregated, coloured, ordered nodes, and owns the title, legend,
/// cell chrome, hover tooltip and zoom/pan view state around the geometry each type lays out for itself.
/// </summary>
public abstract class HierarchicalCore<TSelf, T> where TSelf : HierarchicalCore<TSelf, T>
{
    protected readonly Paper _paper;
    protected readonly string _id;
    protected readonly OrigamiTheme _theme;
    protected readonly IReadOnlyList<T>? _data;

    private string _title = "";

    private UnitValue _width = UnitValue.Stretch();
    private float _height = 220f;
    private float _padding = 0f;

    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private Color? _backgroundColor;
    private string _emptyLabel = "No data";

    private Func<T, string>? _nameSelector;
    private Func<T, double>? _valueSelector;
    private Func<T, IEnumerable<T>>? _childrenSelector;
    private Func<T, int, Color>? _colorFunction;

    private bool _legend = true;
    private float? _legendFontSize;
    private bool _legendInteractive;

    private bool _tooltip = true;
    private Func<double, string>? _valueFormatter;

    private bool _zoomable;
    private bool _pannable;

    private bool _labels = true;
    private Func<T, IComparable>? _sortKey;
    private bool _sortDescending;
    private Func<T, bool>? _highlight;
    private bool _showValues;
    private bool _showPercent;
    private bool _mergeDuplicates;
    private bool _invert;

    private Action<T>? _onNodeClick;
    private T? _selected;
    private bool _hasSelection;

    private ElementHandle _containerEl;
    private ElementHandle _plotEl;

    protected HierarchicalCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data = null)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _data = data;
    }

    private TSelf Self => (TSelf)this;

    // --- Chrome ---

    public TSelf Title(string text) { _title = text ?? ""; return Self; }

    public TSelf Width(float width) { _width = MathF.Max(32f, width); return Self; }
    public TSelf Width(UnitValue width) { _width = width; return Self; }
    public TSelf Height(float height) { _height = MathF.Max(32f, height); return Self; }
    public TSelf Size(float width, float height) { _width = MathF.Max(32f, width); _height = MathF.Max(32f, height); return Self; }

    /// <summary>Does double duty: the container's own padding, so a hierarchical chart lines up with the
    /// other families at the same setting, and the gap in pixels left between sibling cells, ring
    /// segments and bars. One knob because both are the same visual breathing room.</summary>
    public TSelf Padding(float padding) { _padding = MathF.Max(0f, padding); return Self; }

    public TSelf Variant(OrigamiVariant v) { _variant = v; return Self; }
    public TSelf Primary() => Variant(OrigamiVariant.Primary);
    public TSelf Success() => Variant(OrigamiVariant.Success);
    public TSelf Warning() => Variant(OrigamiVariant.Warning);
    public TSelf Danger() => Variant(OrigamiVariant.Danger);
    public TSelf Info() => Variant(OrigamiVariant.Info);

    public TSelf BackgroundColor(Color color) { _backgroundColor = color; return Self; }
    public TSelf EmptyLabel(string text) { _emptyLabel = text ?? "No data"; return Self; }

    // --- Data ---

    /// <summary>Label selector for every item in the tree. Names the node in the legend, in its own cell
    /// and in the tooltip header.</summary>
    public TSelf Name(Func<T, string> selector) { _nameSelector = selector; return Self; }

    /// <summary>Value selector for every item in the tree. A finite, positive read is taken as the node's
    /// own weight; anything else makes the node weigh whatever its children add up to, which is how an
    /// interior item with no value of its own still gets a span.</summary>
    public TSelf Value(Func<T, double> selector) { _valueSelector = selector; return Self; }

    /// <summary>Child selector, walked from every item in the data set passed at construction. Without it
    /// the data set is drawn as a flat single level.</summary>
    public TSelf Children(Func<T, IEnumerable<T>> selector) { _childrenSelector = selector; return Self; }

    /// <summary>Colour selector, taking the item and its depth in the tree. Overrides the colour the chart
    /// would otherwise cycle out of the active variant's ramp.</summary>
    public TSelf ColorFunction(Func<T, int, Color> selector) { _colorFunction = selector; return Self; }

    // --- Legend ---

    public TSelf Legend(bool show = true) { _legend = show; return Self; }

    /// <summary>Font size, in pixels, of the legend's labels. Unset uses the same XS size the rest of the
    /// chart chrome does.</summary>
    public TSelf LegendFontSize(float size) { _legendFontSize = MathF.Max(1f, size); return Self; }

    /// <summary>Let a click on a legend swatch hide and show that node's whole subtree, which also drops
    /// it out of the forest total. Also gated globally by <see cref="Origami.LegendSelectionEnabled"/>.</summary>
    public TSelf LegendInteractive(bool interactive) { _legendInteractive = interactive; return Self; }

    private bool LegendInteractiveActive => _legendInteractive && Origami.LegendSelectionEnabled;

    // --- Tooltip / formatting ---

    /// <summary>Show a readout beside the pointer for the hovered node. On by default: a nested cell
    /// carries no axis to read its value off.</summary>
    public TSelf Tooltip(bool show = true) { _tooltip = show; return Self; }

    public TSelf ValueFormatter(Func<double, string> formatter) { _valueFormatter = formatter; return Self; }

    // --- View ---

    /// <summary>Enable scroll-wheel zoom over the plot, anchored on the pointer so the node under the
    /// cursor stays put. Applies to whichever axes this chart type pans on.</summary>
    public TSelf Zoomable(bool enable = true) { _zoomable = enable; return Self; }

    /// <summary>Enable middle-mouse drag panning over the plot, on this chart type's default axes. The
    /// left button is left free for node clicks.</summary>
    public TSelf Pannable(bool enable = true) { _pannable = enable; return Self; }

    // --- Presentation ---

    public TSelf Labels(bool show = true) { _labels = show; return Self; }

    /// <summary>Order siblings at every level by <paramref name="key"/> rather than by tree order.
    /// Sorting is stable and runs before node indices are handed out, so a legend toggle keeps referring
    /// to the node that ends up drawn in that position.</summary>
    public TSelf SortBy(Func<T, IComparable> key, bool descending = false) { _sortKey = key; _sortDescending = descending; return Self; }

    /// <summary>Dim every node whose item fails <paramref name="predicate"/>, leaving the matches at full
    /// opacity. The dimmed nodes still occupy their span; this is emphasis, not a filter.</summary>
    public TSelf Highlight(Func<T, bool> predicate) { _highlight = predicate; return Self; }

    public TSelf ShowValues(bool show = true) { _showValues = show; return Self; }
    public TSelf ShowPercent(bool show = true) { _showPercent = show; return Self; }

    /// <summary>Fold siblings that share a label into one node before layout, summing their values and
    /// concatenating their children, recursively. This is what turns a call tree sampled many times over
    /// into one readable flame or ring.</summary>
    public TSelf MergeDuplicates(bool merge = true) { _mergeDuplicates = merge; return Self; }

    /// <summary>Draw the tree outward-in rather than inward-out. Only the radial type reads this; the
    /// rectangular types have no equivalent reversal.</summary>
    public TSelf Invert(bool invert = true) { _invert = invert; return Self; }

    // --- Events ---

    /// <summary>Called with the source item when a node's cell is clicked.</summary>
    public TSelf OnNodeClick(Action<T> handler) { _onNodeClick = handler; return Self; }

    /// <summary>Mark one item as selected, drawn with the same ring hover uses. Independent of the
    /// pointer, so a host can drive the emphasis from its own selection state.</summary>
    public TSelf Selected(T? node) { _selected = node; _hasSelection = node is not null; return Self; }

    // --- Read-only state for chart types ---

    /// <summary>The active variant's colour ramp, which is where node colours and a chart type's own
    /// accent colour come from.</summary>
    protected OrigamiRamp Ramp => _theme.Get(_variant);

    /// <summary>Whether the caller wants per-node labels. A type that measures its own cells before
    /// emitting them reads this to decide whether to reserve room for text.</summary>
    protected bool LabelsEnabled => _labels;

    /// <summary>Pixels of gap left between sibling cells, ring segments and bars. Same value as
    /// <see cref="Padding"/>.</summary>
    protected float CellGap => _padding;

    /// <summary>Whether <see cref="Invert"/> was set. Only the radial type acts on it.</summary>
    protected bool Inverted => _invert;

    /// <summary>Ordinal into the flattened forest of the node under the pointer, or -1. Written by
    /// <see cref="NodeCell"/> and by <see cref="HitTest"/>, and read back on the following frame.</summary>
    protected int HoverIndex => _plotEl.IsValid ? _paper.GetElementStorage(_plotEl, HoverIdxKey, -1) : -1;

    /// <summary>Records the node under the pointer and where the pointer was, in plot-local pixels. Chart
    /// types that resolve hits themselves rather than through <see cref="NodeCell"/> call this.</summary>
    protected void SetHover(int nodeIndex, Float2 pointerLocal)
    {
        if (!_plotEl.IsValid) return;
        _paper.SetElementStorage(_plotEl, HoverIdxKey, nodeIndex);
        _paper.SetElementStorage(_plotEl, HoverPosKey, pointerLocal);
    }

    /// <summary>Colour of the node at <paramref name="ordinal"/> among its siblings when no
    /// <see cref="ColorFunction"/> is set: the active variant's ramp walked in the same high-contrast
    /// order the circular family uses, then stepped one stop lighter per level of depth so a child reads
    /// as a lighter shade of its parent's band. The step wraps rather than saturating, so a deep tree
    /// keeps producing distinguishable levels.</summary>
    protected Color DefaultNodeColor(int ordinal, int depth)
    {
        int stop = (ordinal % 7) switch
        {
            0 => 5,
            1 => 3,
            2 => 7,
            3 => 4,
            4 => 6,
            5 => 2,
            _ => 1,
        };

        stop = ((stop - 1 + Math.Max(0, depth)) % 7) + 1;

        OrigamiRamp ramp = Ramp;
        return stop switch
        {
            1 => ramp.C100,
            2 => ramp.C200,
            3 => ramp.C300,
            4 => ramp.C400,
            5 => ramp.C500,
            6 => ramp.C600,
            _ => ramp.C700,
        };
    }

    /// <summary>Formats a value the way this chart's legend, cells and tooltip do, honouring
    /// <see cref="ValueFormatter"/>.</summary>
    protected string FormatValue(double v) => _valueFormatter != null ? _valueFormatter(v) : v.ToString("0.###");

    /// <summary>The text a node's cell carries: its label, plus its value under
    /// <see cref="ShowValues"/> and its share of the forest under <see cref="ShowPercent"/>.</summary>
    protected string CellText(HierarchicalNode<T> node)
    {
        string text = node.Label;
        if (_showValues) text += " " + FormatValue(node.Value);
        if (_showPercent) text += " (" + (node.Fraction * 100d).ToString("0.#") + "%)";
        return text;
    }

    // --- Geometry context ---

    /// <summary>Geometry and resolved forest handed to <see cref="BuildMarks"/>. Coordinates are
    /// plot-local pixels with the plot's top-left corner at the origin, because every mark in this family
    /// is a self-directed child of the plot element rather than a canvas primitive.</summary>
    protected readonly struct HierarchicalContext
    {
        public readonly float PlotL, PlotT, PlotR, PlotB;

        /// <summary>The forest's top-level nodes, in draw order.</summary>
        public readonly IReadOnlyList<HierarchicalNode<T>> Roots;

        /// <summary>Every node in pre-order, so <c>Flat[i].Index == i</c>.</summary>
        public readonly IReadOnlyList<HierarchicalNode<T>> Flat;

        /// <summary>Sum of the visible roots' values, and the denominator behind every
        /// <see cref="HierarchicalNode{T}.Fraction"/>.</summary>
        public readonly double Total;

        /// <summary>Deepest level present in the forest, counting roots as zero.</summary>
        public readonly int MaxDepth;

        /// <summary>Index into <see cref="Flat"/> of the node under the pointer, or -1.</summary>
        public readonly int HoverIndex;

        /// <summary>The visible window as an offset plus a span in [0, 1] on each axis, identity being
        /// (0, 0, 1, 1). <see cref="MapX"/> and <see cref="MapY"/> already fold this in; a type only reads
        /// it directly when it needs the zoom factor itself, such as to scale a radius.</summary>
        public readonly float ViewX, ViewY, ViewW, ViewH;

        internal HierarchicalContext(float plotL, float plotT, float plotR, float plotB,
            IReadOnlyList<HierarchicalNode<T>> roots, IReadOnlyList<HierarchicalNode<T>> flat,
            double total, int maxDepth, int hoverIndex,
            float viewX, float viewY, float viewW, float viewH)
        {
            PlotL = plotL; PlotT = plotT; PlotR = plotR; PlotB = plotB;
            Roots = roots;
            Flat = flat;
            Total = total;
            MaxDepth = maxDepth;
            HoverIndex = hoverIndex;
            ViewX = viewX; ViewY = viewY; ViewW = viewW; ViewH = viewH;
        }

        public float Width => PlotR - PlotL;
        public float Height => PlotB - PlotT;

        /// <summary>Maps a fraction of the plot's width, in [0, 1], to a pixel x through the current
        /// view window.</summary>
        public float MapX(double fraction)
            => ViewW <= 0f ? PlotL : PlotL + (float)((fraction - ViewX) / ViewW) * Width;

        /// <summary>Maps a fraction of the plot's height, in [0, 1], to a pixel y through the current
        /// view window.</summary>
        public float MapY(double fraction)
            => ViewH <= 0f ? PlotT : PlotT + (float)((fraction - ViewY) / ViewH) * Height;
    }

    // --- Type hooks ---

    /// <summary>Lay this chart type's geometry out inside the already-clipped plot area described by
    /// <paramref name="ctx"/>. Called from inside the plot element, so everything emitted here must be
    /// self-directed; <see cref="NodeCell"/> is the intended way to emit a node.</summary>
    protected abstract void BuildMarks(Paper paper, in HierarchicalContext ctx);

    /// <summary>Index into <see cref="HierarchicalContext.Flat"/> of the node at
    /// <paramref name="pointer"/>, or -1 for none. Only a type that paints its nodes onto the canvas
    /// needs this; the types built out of <see cref="NodeCell"/> get their hover from the cells' own
    /// pointer events and leave this alone.</summary>
    protected virtual int HitTest(in HierarchicalContext ctx, Float2 pointer) => -1;

    /// <summary>Whether the x axis takes part in zoom and pan. True everywhere: every hierarchical type
    /// lays its spans out horizontally.</summary>
    protected virtual bool DefaultPanX => true;

    /// <summary>Whether the y axis takes part in zoom and pan. False on the types whose vertical extent is
    /// fixed by depth rather than by the data, where a vertical view would only slide rows out of sight.</summary>
    protected virtual bool DefaultPanY => true;

    /// <summary>Called once at the top of <see cref="Show"/>, before any data is resolved.</summary>
    protected virtual void OnBeforeShow() { }

    // --- Tree resolution ---

    /// <summary>Turns the caller's items and selectors into the drawn forest: aggregate, merge, sort,
    /// prune, index, apply legend hiding, then lay out each node's span. The order matters - indices are
    /// handed out only once the draw order is final, and spans are assigned only once hiding has settled
    /// which nodes still weigh anything.</summary>
    private List<HierarchicalNode<T>> BuildTree(ElementHandle el, out List<HierarchicalNode<T>> flat,
        out double total, out int maxDepth)
    {
        flat = new List<HierarchicalNode<T>>();
        total = 0d;
        maxDepth = 0;

        var roots = new List<HierarchicalNode<T>>();
        if (_data == null || _data.Count == 0) return roots;

        foreach (T item in _data)
        {
            HierarchicalNode<T>? node = BuildNode(item, 0, null);
            if (node != null) roots.Add(node);
        }

        if (_mergeDuplicates) roots = MergeSiblings(roots, null);

        SortLevel(roots);
        roots = Prune(roots);

        AssignIndices(roots, flat);

        bool anyHidden = false;
        if (LegendInteractiveActive)
        {
            foreach (HierarchicalNode<T> node in LegendNodes(roots))
            {
                if (!_paper.GetElementStorage(el, HiddenKeyPrefix + node.Index, false)) continue;
                HideSubtree(node);
                anyHidden = true;
            }
        }

        if (anyHidden)
            foreach (HierarchicalNode<T> root in roots)
                ReaggregateVisible(root);

        foreach (HierarchicalNode<T> root in roots)
            total += root.Value;

        double cursor = 0d;
        foreach (HierarchicalNode<T> root in roots)
        {
            AssignSpans(root, cursor, total);
            cursor += root.Value;
        }

        foreach (HierarchicalNode<T> node in flat)
            maxDepth = Math.Max(maxDepth, node.Depth);

        return roots;
    }

    /// <summary>Resolves one item and everything under it. The aggregate is the item's own value when it
    /// reads as finite and positive, and the sum of its children otherwise, which is how a container item
    /// that carries no number of its own still gets a span. A node's aggregate is never less than the sum
    /// of its children, so a child's span always lies inside its parent's rather than overflowing past it.
    /// Recursion stops at <see cref="MaxTreeDepth"/> so a self-referencing child selector cannot hang the
    /// frame.</summary>
    private HierarchicalNode<T>? BuildNode(T item, int depth, HierarchicalNode<T>? parent)
    {
        if (item is null || depth > MaxTreeDepth) return null;

        var node = new HierarchicalNode<T>
        {
            Payload = item,
            Label = _nameSelector != null ? (_nameSelector(item) ?? "") : "",
            Depth = depth,
            Parent = parent,
        };

        if (_childrenSelector != null)
        {
            IEnumerable<T>? kids = _childrenSelector(item);
            if (kids != null)
                foreach (T kid in kids)
                {
                    HierarchicalNode<T>? child = BuildNode(kid, depth + 1, node);
                    if (child != null) node.Children.Add(child);
                }
        }

        double own = _valueSelector != null ? _valueSelector(item) : 0d;
        if (double.IsNaN(own) || double.IsInfinity(own)) own = 0d;

        double childSum = 0d;
        foreach (HierarchicalNode<T> child in node.Children) childSum += child.Value;

        node.Value = own > 0d ? Math.Max(own, childSum) : childSum;

        return node;
    }

    /// <summary>Folds same-labelled siblings into one node, recursively. Values add up and children are
    /// concatenated then merged in turn; the surviving node keeps the first duplicate's payload so a
    /// colour function or click handler still gets a real item back.</summary>
    private List<HierarchicalNode<T>> MergeSiblings(List<HierarchicalNode<T>> siblings, HierarchicalNode<T>? parent)
    {
        var merged = new List<HierarchicalNode<T>>(siblings.Count);
        var byLabel = new Dictionary<string, HierarchicalNode<T>>(siblings.Count);

        foreach (HierarchicalNode<T> node in siblings)
        {
            if (byLabel.TryGetValue(node.Label, out HierarchicalNode<T>? target))
            {
                target.Value += node.Value;
                target.Children.AddRange(node.Children);
                continue;
            }

            node.Parent = parent;
            byLabel[node.Label] = node;
            merged.Add(node);
        }

        foreach (HierarchicalNode<T> node in merged)
        {
            List<HierarchicalNode<T>> kids = MergeSiblings(new List<HierarchicalNode<T>>(node.Children), node);
            node.Children.Clear();
            node.Children.AddRange(kids);
        }

        return merged;
    }

    /// <summary>Applies <see cref="SortBy"/> to one level and every level under it.</summary>
    private void SortLevel(List<HierarchicalNode<T>> siblings)
    {
        if (_sortKey != null && siblings.Count > 1)
        {
            List<HierarchicalNode<T>> ordered = _sortDescending
                ? siblings.OrderByDescending(n => n.Payload is null ? null : _sortKey(n.Payload)).ToList()
                : siblings.OrderBy(n => n.Payload is null ? null : _sortKey(n.Payload)).ToList();

            siblings.Clear();
            siblings.AddRange(ordered);
        }

        foreach (HierarchicalNode<T> node in siblings)
            SortLevel(node.Children);
    }

    /// <summary>Drops the nodes that aggregated to nothing. A node with no span has nothing to draw and
    /// nothing to hover, so carrying it through layout would only cost cells of zero size.</summary>
    private static List<HierarchicalNode<T>> Prune(List<HierarchicalNode<T>> siblings)
    {
        var kept = new List<HierarchicalNode<T>>(siblings.Count);
        foreach (HierarchicalNode<T> node in siblings)
        {
            if (node.Value <= 0d) continue;

            List<HierarchicalNode<T>> kids = Prune(node.Children);
            node.Children.Clear();
            node.Children.AddRange(kids);
            kept.Add(node);
        }
        return kept;
    }

    /// <summary>Numbers the forest in pre-order and resolves each node's colour and emphasis flags. This
    /// runs after sorting and merging so an index is a position in the drawn output, which is what makes
    /// it safe to key legend state and hover state on.</summary>
    private void AssignIndices(List<HierarchicalNode<T>> siblings, List<HierarchicalNode<T>> flat)
    {
        for (int ordinal = 0; ordinal < siblings.Count; ordinal++)
        {
            HierarchicalNode<T> node = siblings[ordinal];
            node.Index = flat.Count;
            flat.Add(node);

            node.Color = _colorFunction != null && node.Payload is not null
                ? _colorFunction(node.Payload, node.Depth)
                : DefaultNodeColor(ordinal, node.Depth);

            node.Dimmed = _highlight != null && !(node.Payload is not null && _highlight(node.Payload));
            node.Selected = _hasSelection && node.Payload is not null && EqualityComparer<T>.Default.Equals(node.Payload, _selected);

            AssignIndices(node.Children, flat);
        }
    }

    /// <summary>The nodes the legend lists: the roots when there is more than one, otherwise the single
    /// root's own children, because a legend of one entry covering the whole chart says nothing.</summary>
    private static IReadOnlyList<HierarchicalNode<T>> LegendNodes(List<HierarchicalNode<T>> roots)
        => roots.Count == 1 ? roots[0].Children : roots;

    private static void HideSubtree(HierarchicalNode<T> node)
    {
        node.LegendHidden = true;
        foreach (HierarchicalNode<T> child in node.Children)
            HideSubtree(child);
    }

    /// <summary>Re-adds the forest up from its surviving leaves once the legend has hidden something, so a
    /// hidden branch stops counting toward its ancestors and toward the total. Only runs while something
    /// is actually hidden, leaving the plain case with the values the selectors produced.</summary>
    private static double ReaggregateVisible(HierarchicalNode<T> node)
    {
        if (node.LegendHidden)
        {
            node.Value = 0d;
            foreach (HierarchicalNode<T> child in node.Children) ReaggregateVisible(child);
            return 0d;
        }

        if (node.Children.Count == 0) return node.Value;

        double sum = 0d;
        foreach (HierarchicalNode<T> child in node.Children) sum += ReaggregateVisible(child);
        node.Value = sum;
        return sum;
    }

    /// <summary>Lays a node and its children out along the forest's flattened value axis: a node owns
    /// <c>[Start, Start + Value)</c>, and its children are laid consecutively from the node's own
    /// <c>Start</c> so a child always sits inside its parent's span.</summary>
    private static void AssignSpans(HierarchicalNode<T> node, double start, double total)
    {
        node.Start = start;
        node.Fraction = total > 0d ? node.Value / total : 0d;

        double cursor = start;
        foreach (HierarchicalNode<T> child in node.Children)
        {
            AssignSpans(child, cursor, total);
            cursor += child.Value;
        }
    }

    // --- Show ---

    public void Show()
    {
        OnBeforeShow();

        ElementBuilder container = _paper.Column(_id)
            .Size(_width, _height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(_theme.BorderSoft).BorderWidth(1f)
            .Padding(_padding);

        if (_backgroundColor.HasValue)
            container.BackgroundColor(_backgroundColor.Value);

        using (container.Enter())
        {
            _containerEl = _paper.CurrentParent;

            using (_paper.Row(_id + "_chart_header").Height(20).ChildRight().Enter())
                Origami.Label(_paper, _id + "_chart_header", _title).MD().Height(15).AlignCenter().Show();

            _paper.Box(_id + "_chart_header_div").Height(1).BackgroundColor(_theme.BorderStrong);

            using (_paper.Row(_id + "_chart_col_split").Enter())
            {
                List<HierarchicalNode<T>> roots = BuildTree(_containerEl, out List<HierarchicalNode<T>> flat,
                    out double total, out int maxDepth);

                IReadOnlyList<HierarchicalLegendEntry> entries = BuildLegend(roots);

                if (_legend && entries.Count > 0)
                    DrawLegend(entries);

                _paper.Box(_id + "_chart_split_div").Width(1).BackgroundColor(_theme.BorderStrong);

                DrawChartBox(roots, flat, total, maxDepth);
            }
        }
    }

    /// <summary>The legend rows for the current forest, one per node the legend lists.</summary>
    protected virtual IReadOnlyList<HierarchicalLegendEntry> BuildLegend(List<HierarchicalNode<T>> roots)
    {
        IReadOnlyList<HierarchicalNode<T>> nodes = LegendNodes(roots);

        var entries = new List<HierarchicalLegendEntry>(nodes.Count);
        foreach (HierarchicalNode<T> node in nodes)
            entries.Add(new HierarchicalLegendEntry(
                node.Label.Length > 0 ? node.Label : "Node " + node.Index,
                node.Color, node.Value, true, node.Index, node.LegendHidden));

        return entries;
    }

    private void DrawLegend(IReadOnlyList<HierarchicalLegendEntry> entries)
    {
        ElementBuilder legendCol = _paper.Column(_id + "_legend")
            .Width(125f)
            .PaddingTop(_padding)
            .ColBetween(_padding);

        using (legendCol.Enter())
        {
            for (int i = 0; i < entries.Count; i++)
            {
                HierarchicalLegendEntry entry = entries[i];
                int key = entry.Key;

                string text = entry.HasValue ? entry.Label + "  " + FormatValue(entry.Value) : entry.Label;
                Color swatchColor = entry.Hidden ? Color.FromArgb(entry.Color.A / 3, entry.Color) : entry.Color;

                using (_paper.Row($"{_id}_legend_row_{i}").Height(SwatchSize).RowBetween(2f).Enter())
                {
                    ElementBuilder swatch = _paper.Box($"{_id}_legend_sw_{i}").Size(SwatchSize).BackgroundColor(swatchColor).Rounded(2f);

                    if (LegendInteractiveActive)
                        swatch.OnClick(_ => Toggle(key));

                    LabelBuilder label = Origami.Label(_paper, $"{_id}_legend_txt_{i}", text);
                    if (_legendFontSize.HasValue) label.FontSize(_legendFontSize.Value); else label.XS();

                    label.AlignCenter().AlignLeft().Height(SwatchSize).Show();
                }
            }
        }
    }

    private void Toggle(int key)
    {
        if (!_containerEl.IsValid) return;
        bool hidden = !_paper.GetElementStorage(_containerEl, HiddenKeyPrefix + key, false);
        _paper.SetElementStorage(_containerEl, HiddenKeyPrefix + key, hidden);
    }

    /// <summary>The plot element itself: a clipped box that owns the view state, the pointer wiring and
    /// the type's marks. Its size comes from the previous frame's layout rather than from a paint
    /// callback, because the rectangular types build their cells out of Paper nodes and so must know the
    /// box before it is drawn.</summary>
    private void DrawChartBox(List<HierarchicalNode<T>> roots, List<HierarchicalNode<T>> flat,
        double total, int maxDepth)
    {
        if (flat.Count == 0 || total <= 0d)
        {
            using (_paper.Row(_id + "_chart_empty_wrap").Enter())
                Origami.Label(_paper, _id + "_chart_empty", _emptyLabel).LG().Show();

            return;
        }

        ElementBuilder plotBox = _paper.Box(_id + "_chart_plot").Clip();

        using (plotBox.Enter())
        {
            ElementHandle plotEl = _paper.CurrentParent;
            _plotEl = plotEl;

            plotBox.OnPostLayout((_, rect) =>
                _paper.SetElementStorage(plotEl, PlotKey, new PlotInfo { Width = (float)rect.Size.X, Height = (float)rect.Size.Y }));

            plotBox.OnHover(e =>
            {
                _paper.SetElementStorage(plotEl, HoverPosKey, e.RelativePosition);
                _paper.SetElementStorage(plotEl, HoverOnKey, true);
            });

            plotBox.OnLeave(_ =>
            {
                _paper.SetElementStorage(plotEl, HoverOnKey, false);
                _paper.SetElementStorage(plotEl, HoverIdxKey, -1);
            });

            if (_zoomable)
                plotBox.OnScroll(ApplyZoom);

            PlotInfo info = _paper.GetElementStorage<PlotInfo>(plotEl, PlotKey, default);
            if (info.Width <= 0f || info.Height <= 0f) return;

            ViewRect view = ReadView();

            bool hovering = _paper.GetElementStorage(plotEl, HoverOnKey, false);
            Float2 pointer = _paper.GetElementStorage(plotEl, HoverPosKey, new Float2(0f, 0f));

            int hover = -1;
            if (hovering)
            {
                var probe = new HierarchicalContext(0f, 0f, info.Width, info.Height, roots, flat, total, maxDepth, -1,
                    view.X, view.Y, view.W, view.H);

                hover = HitTest(in probe, pointer);
                if (hover < 0) hover = _paper.GetElementStorage(plotEl, HoverIdxKey, -1);
            }

            var ctx = new HierarchicalContext(0f, 0f, info.Width, info.Height, roots, flat, total, maxDepth, hover,
                view.X, view.Y, view.W, view.H);

            BuildMarks(_paper, in ctx);

            if (_pannable)
                ApplyPan(info);

            if (_tooltip && hover >= 0 && hover < flat.Count)
                DrawTooltip(in ctx, pointer, flat[hover]);
        }
    }

    /// <summary>One node's cell: a self-directed box carrying the node's fill, its hover and selection
    /// ring, its pointer wiring and, when there is room for it, its label. Every rectangular chart type in
    /// this family is built out of these rather than out of canvas geometry, so the cells take part in
    /// Paper's own hit testing and clipping.</summary>
    protected void NodeCell(Paper paper, in HierarchicalContext ctx, HierarchicalNode<T> node,
        float x, float y, float w, float h)
    {
        if (w < 1f || h < 1f) return;

        Color fill = node.Dimmed ? Color.FromArgb((int)Math.Clamp(node.Color.A * DimAlpha, 0f, 255f), node.Color) : node.Color;
        bool emphasised = node.Selected || ctx.HoverIndex == node.Index;

        ElementBuilder cell = paper.Box($"{_id}_cell_{node.Index}")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Size(w, h)
            .BackgroundColor(fill)
            .Rounded(2f)
            .Clip();

        if (emphasised)
            cell.BorderColor(_theme.Ink.C700).BorderWidth(1.5f);

        int index = node.Index;
        float cellX = x, cellY = y;

        cell.OnHover(e => SetHover(index, new Float2(cellX + e.RelativePosition.X, cellY + e.RelativePosition.Y)));
        cell.OnLeave(_ => { if (HoverIndex == index) SetHover(-1, new Float2(cellX, cellY)); });

        if (_onNodeClick != null)
        {
            Action<T> click = _onNodeClick;
            T? payload = node.Payload;
            if (payload is not null)
                cell.OnClick(_ => click(payload));
        }

        using (cell.Enter())
        {
            if (!_labels || w < MinLabelWidth || h < MinLabelHeight) return;

            string text = CellText(node);
            if (text.Length == 0) return;

            Origami.Label(paper, $"{_id}_cell_txt_{node.Index}", text)
                .XS()
                .Ellipsis()
                .AlignCenter()
                .AlignLeft()
                .TextColor(LabelInk(fill))
                .Padding(3f, 0f)
                .Width(w)
                .Height(h)
                .Show();
        }
    }

    private void DrawTooltip(in HierarchicalContext ctx, Float2 anchor, HierarchicalNode<T> node)
    {
        var rows = new List<(Color Color, string Text)> { (node.Color, FormatValue(node.Value)) };
        if (_showPercent)
            rows.Add((node.Color, (node.Fraction * 100d).ToString("0.#") + "%"));

        HierarchicalTooltip(_paper, in ctx, anchor, node.Label.Length > 0 ? node.Label : "Node " + node.Index, rows);
    }

    /// <summary>Readout panel anchored beside <paramref name="anchor"/>, in the same plot-local pixels the
    /// marks are laid out in. It flips to the other side of the anchor when it would overhang the plot's
    /// right edge, using the width it laid out at on the previous frame.</summary>
    protected void HierarchicalTooltip(Paper paper, in HierarchicalContext ctx, Float2 anchor, string header,
        IReadOnlyList<(Color Color, string Text)> rows)
    {
        if (header.Length == 0 && rows.Count == 0) return;

        string widthKey = _id + "_tooltip_w";
        float lastWidth = paper.GetRootStorage<float>(widthKey);

        float x = anchor.X + TooltipGap;
        if (lastWidth > 0f && x + lastWidth > ctx.PlotR)
            x = MathF.Max(ctx.PlotL, anchor.X - TooltipGap - lastWidth);

        float y = Math.Clamp(anchor.Y + TooltipGap, ctx.PlotT, ctx.PlotB);

        ElementBuilder popup = paper.Column(_id + "_tooltip")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Size(UnitValue.Auto)
            .BackgroundColor(_theme.Popover)
            .BorderColor(_theme.BorderStrong).BorderWidth(1f)
            .Rounded(6f)
            .Padding(6f)
            .ColBetween(6f)
            .Layer(Layer.Topmost + 1000)
            .OnPostLayout((_, rect) => paper.SetRootStorage(widthKey, (float)rect.Size.X));

        using (popup.Enter())
        {
            if (header.Length > 0)
                Origami.Label(paper, $"{_id}_tooltip_hdr", header)
                    .XS()
                    .AlignCenter()
                    .AlignLeft()
                    .Height(SwatchSize)
                    .Show();

            for (int i = 0; i < rows.Count; i++)
            {
                (Color color, string text) = rows[i];

                using (paper.Row($"{_id}_tooltip_row_{i}").Height(SwatchSize).Width(UnitValue.Auto).RowBetween(2f).Enter())
                {
                    paper.Box($"{_id}_tooltip_sw_{i}").Size(SwatchSize).BackgroundColor(color).Rounded(2f);

                    Origami.Label(paper, $"{_id}_tooltip_txt_{i}", text)
                        .XS()
                        .AlignCenter()
                        .AlignLeft()
                        .Height(SwatchSize)
                        .Show();
                }
            }
        }
    }

    // --- View ---

    /// <summary>The visible fraction of the plot on each axis, as an offset plus a span in [0, 1].
    /// Identity is (0, 0, 1, 1), the whole plot. Normalized rather than absolute so it survives the tree
    /// changing between frames.</summary>
    private struct ViewRect
    {
        public float X, Y, W, H;
    }

    private static ViewRect ClampView(ViewRect view)
    {
        view.W = Math.Clamp(view.W <= 0f ? 1f : view.W, MinViewSpan, 1f);
        view.H = Math.Clamp(view.H <= 0f ? 1f : view.H, MinViewSpan, 1f);
        view.X = Math.Clamp(view.X, 0f, 1f - view.W);
        view.Y = Math.Clamp(view.Y, 0f, 1f - view.H);
        return view;
    }

    private ViewRect ReadView()
        => _containerEl.IsValid
            ? ClampView(_paper.GetElementStorage(_containerEl, ViewRectKey, default(ViewRect)))
            : ClampView(default);

    private void WriteView(ViewRect view)
    {
        if (!_containerEl.IsValid) return;
        _paper.SetElementStorage(_containerEl, ViewRectKey, ClampView(view));
    }

    /// <summary>Scroll-wheel zoom about the pointer. The normalized coordinate under the cursor is held
    /// fixed while the visible span shrinks or grows, so the node the pointer is over stays put.</summary>
    private void ApplyZoom(ScrollEvent e)
    {
        bool zoomX = DefaultPanX, zoomY = DefaultPanY;
        if (!zoomX && !zoomY) return;

        double w = e.ElementRect.Size.X, h = e.ElementRect.Size.Y;
        if (w <= 0d || h <= 0d) return;

        ViewRect view = ReadView();
        float factor = MathF.Exp(-e.Delta * ZoomRate);

        if (zoomX)
        {
            float fx = (float)Math.Clamp((e.PointerPosition.X - e.ElementRect.Min.X) / w, 0d, 1d);
            float nw = Math.Clamp(view.W * factor, MinViewSpan, 1f);
            view.X += fx * (view.W - nw);
            view.W = nw;
        }

        if (zoomY)
        {
            float fy = (float)Math.Clamp((e.PointerPosition.Y - e.ElementRect.Min.Y) / h, 0d, 1d);
            float nh = Math.Clamp(view.H * factor, MinViewSpan, 1f);
            view.Y += fy * (view.H - nh);
            view.H = nh;
        }

        WriteView(view);
        e.StopPropagation();
    }

    /// <summary>Middle-mouse drag pan. Paper's own drag events are left-button only, and the left button
    /// already routes node clicks, so this polls the middle button against the hovered plot instead.</summary>
    private void ApplyPan(PlotInfo info)
    {
        bool panX = DefaultPanX, panY = DefaultPanY;
        if (!panX && !panY) return;
        if (!_paper.IsPointerDown(PaperMouseBtn.Middle) || !_paper.IsParentHovered) return;
        if (info.Width <= 0f || info.Height <= 0f) return;

        Float2 delta = _paper.PointerDelta;
        if (delta.X == 0f && delta.Y == 0f) return;

        ViewRect view = ReadView();
        if (panX) view.X -= (float)delta.X / info.Width * view.W;
        if (panY) view.Y -= (float)delta.Y / info.Height * view.H;
        WriteView(view);
    }

    // --- Canvas helpers ---

    protected const float DegToRad = MathF.PI / 180f;

    protected static Color32 ToC32(Color c) => new Color32(c.R, c.G, c.B, c.A);
    protected static Color32 ToC32(Color c, float alpha) => new Color32(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    /// <summary>Ink colour that stays legible on <paramref name="fill"/>. Cell fills walk a whole ramp, so
    /// a fixed text colour would disappear at one end of it.</summary>
    private Color LabelInk(Color fill)
    {
        float luminance = (0.2126f * fill.R + 0.7152f * fill.G + 0.0722f * fill.B) / 255f;
        return luminance > 0.55f ? _theme.Ink.C100 : _theme.Ink.C700;
    }

    private struct PlotInfo
    {
        public float Width, Height;
    }

    private const string HiddenKeyPrefix = "hier_hidden_";
    private const string PlotKey = "hier_plot";
    private const string HoverPosKey = "hier_hover_pos";
    private const string HoverOnKey = "hier_hover_on";
    private const string HoverIdxKey = "hier_hover_idx";
    private const string ViewRectKey = "hier_view";

    private const int MaxTreeDepth = 32;

    private const float MinViewSpan = 0.005f;
    private const float ZoomRate = 0.14f;

    private const float SwatchSize = 12f;
    private const float TooltipGap = 8f;
    private const float MinLabelWidth = 60f;
    private const float MinLabelHeight = 12f;
    private const float DimAlpha = 0.35f;
}
