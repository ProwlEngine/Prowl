// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>One group header row of a <see cref="PerformanceCore{TSelf, T}"/> chart: a thread or a track
/// within one, drawn as a full-width row the user clicks to collapse. <c>Row</c> is its index into the
/// same uniform row grid the spans are laid out on, so a header and the lanes under it share one pitch.</summary>
public sealed class PerformanceLane
{
    public string Label = "";

    /// <summary>Storage key the collapsed flag is kept under. Unique across the chart, unlike
    /// <see cref="Label"/>, which repeats whenever two threads name a track the same.</summary>
    public string Key = "";

    /// <summary>0 for a thread header, 1 for a track header nested under one.</summary>
    public int Depth;

    public int Row;
    public bool Collapsed;
}

/// <summary>
/// Shared implementation for the performance chart types (Timeline). A lane-based sibling of the flame
/// graph: instead of stacking spans by depth it groups them into threads and tracks, packs the ones that
/// overlap in time into extra sub-rows, and lays every span out against one shared time axis. The forest
/// <see cref="HierarchicalCore{TSelf, T}"/> draws is built here rather than by walking a child selector,
/// so a node's <c>Start</c> is its start time relative to the earliest one, its <c>Value</c> is its
/// duration and its <c>Depth</c> is the row it occupies; everything else - zoom and pan, the cells, the
/// hover tooltip and the Highlight dim pass - is inherited unchanged.
/// </summary>
public abstract class PerformanceCore<TSelf, T> : HierarchicalCore<TSelf, T>
    where TSelf : PerformanceCore<TSelf, T>
{
    protected PerformanceCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private Func<T, double>? _start;
    private Func<T, double>? _end;
    private Func<T, double>? _duration;

    private Func<T, string>? _thread;
    private Func<T, string>? _track;
    private Func<T, string>? _category;

    private Action<T, bool, bool>? _onSelect;
    private Action<string>? _onCollapse;
    private Action<string>? _onExpand;

    private readonly List<PerformanceLane> _lanes = new();
    private readonly List<string> _categoryNames = new();
    private readonly List<Color> _categoryColors = new();
    private readonly List<double> _categoryTotals = new();

    private ElementHandle _stateEl;
    private double _minTime;
    private int _rowCount;

    private const string CollapseKeyPrefix = "perf_collapsed_";
    private const int RulerTicks = 6;
    private const int MaxRulerTicks = 64;
    private const float RulerTickLabelWidth = 64f;

    private TSelf Self => (TSelf)this;

    // --- Data ---

    /// <summary>Start-time selector for every item. Times carry whatever unit the caller works in; the
    /// axis and every readout run through <c>ValueFormatter</c>.</summary>
    public TSelf Start(Func<T, double> selector) { _start = selector; return Self; }

    /// <summary>End-time selector. Takes precedence over <see cref="Duration"/> when both are set; an end
    /// before the start is clamped to a zero-length span.</summary>
    public TSelf End(Func<T, double> selector) { _end = selector; return Self; }

    /// <summary>Duration selector, used to derive an end time when <see cref="End"/> is not set.</summary>
    public TSelf Duration(Func<T, double> selector) { _duration = selector; return Self; }

    /// <summary>Thread selector: the outer grouping, drawn as a collapsible header above its tracks.
    /// Without it every item lands in one unnamed group and no thread header is drawn.</summary>
    public TSelf Thread(Func<T, string> selector) { _thread = selector; return Self; }

    /// <summary>Track selector: the lane an item is placed in within its thread, drawn as its own
    /// collapsible header. Items that overlap in time inside one track stack into extra sub-rows.</summary>
    public TSelf Track(Func<T, string> selector) { _track = selector; return Self; }

    /// <summary>Category selector: what the chart colours and legends by. Falling back, in order, to the
    /// track then the thread when it is not set, so a chart always has something to key colour off.</summary>
    public TSelf Category(Func<T, string> selector) { _category = selector; return Self; }

    // --- Events ---

    /// <summary>Called with the clicked span and the modifier keys held at the time, so a host can build
    /// add-to-selection and toggle-selection on top of a plain click.</summary>
    public TSelf OnSelectModified(Action<T, bool, bool> handler) { _onSelect = handler; return Self; }

    /// <summary>Called with a group's label when the user collapses it. The chart keeps the collapsed
    /// state itself; this only reports the change.</summary>
    public TSelf Collapse(Action<string> handler) { _onCollapse = handler; return Self; }

    /// <summary>Called with a group's label when the user expands it again.</summary>
    public TSelf Expand(Action<string> handler) { _onExpand = handler; return Self; }

    // --- Read-only state for chart types ---

    /// <summary>The group header rows for the current data, in row order.</summary>
    protected IReadOnlyList<PerformanceLane> Lanes => _lanes;

    /// <summary>Earliest start time in the data set, and the origin every node's <c>Start</c> is relative
    /// to. Added back on before a time is formatted for the axis.</summary>
    protected double MinTime => _minTime;

    /// <summary>Number of rows the lanes occupy, headers included.</summary>
    protected int RowCount => _rowCount;

    /// <summary>Spans are placed on a fixed row grid rather than by the data, so a vertical view would
    /// only slide rows out of sight. Vertical overflow is clipped by the plot box instead.</summary>
    protected override bool DefaultPanY => false;

    protected override void OnBeforeShow()
    {
        base.OnBeforeShow();
        OnNodeClick(RaiseSelectModified);
    }

    private void RaiseSelectModified(T item)
    {
        if (_onSelect == null) return;

        bool shift = _paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift);
        bool ctrl = _paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl);
        _onSelect(item, shift, ctrl);
    }

    // --- Lane resolution ---

    private sealed class Span
    {
        public T Payload = default!;
        public double Start;
        public double End;
        public int Category;
    }

    private sealed class TrackGroup
    {
        public string Label = "";
        public string Key = "";
        public readonly List<Span> Spans = new();
    }

    private sealed class ThreadGroup
    {
        public string Label = "";
        public string Key = "";
        public readonly List<TrackGroup> Tracks = new();
        public readonly Dictionary<string, TrackGroup> ByName = new();
    }

    /// <summary>Resolves the caller's items into rows: read the selectors, bucket by thread and track,
    /// collapse whatever the user has collapsed, then pack the rest into sub-rows so no two spans on one
    /// row overlap in time. Node values are laid out against the time axis rather than the flattened value
    /// axis the tree walk uses, which is what makes a span's position mean when it happened.</summary>
    protected override List<HierarchicalNode<T>> BuildTree(ElementHandle el, out List<HierarchicalNode<T>> flat,
        out double total, out int maxDepth)
    {
        _stateEl = el;
        _lanes.Clear();
        _categoryNames.Clear();
        _categoryColors.Clear();
        _categoryTotals.Clear();

        flat = new List<HierarchicalNode<T>>();
        total = 0d;
        maxDepth = 0;
        _rowCount = 0;
        _minTime = 0d;

        var roots = new List<HierarchicalNode<T>>();
        if (_data == null || _data.Count == 0) return roots;

        var threads = new List<ThreadGroup>();
        var threadsByName = new Dictionary<string, ThreadGroup>();
        var categoryIndex = new Dictionary<string, int>();

        double min = double.MaxValue, max = double.MinValue;

        foreach (T item in _data)
        {
            if (item is null) continue;

            double start = _start != null ? _start(item) : 0d;
            if (!IsFinite(start)) continue;

            double end = start;
            if (_end != null) end = _end(item);
            else if (_duration != null) end = start + _duration(item);
            if (!IsFinite(end) || end < start) end = start;

            string categoryName = _category != null ? _category(item) ?? ""
                : _track != null ? _track(item) ?? ""
                : _thread != null ? _thread(item) ?? ""
                : "";

            if (!categoryIndex.TryGetValue(categoryName, out int category))
            {
                category = _categoryNames.Count;
                categoryIndex[categoryName] = category;
                _categoryNames.Add(categoryName);
                _categoryColors.Add(NodeColor(item, category, category, 0));
                _categoryTotals.Add(0d);
            }

            _categoryTotals[category] += end - start;

            if (start < min) min = start;
            if (end > max) max = end;

            string threadName = _thread != null ? _thread(item) ?? "" : "";
            if (!threadsByName.TryGetValue(threadName, out ThreadGroup? thread))
            {
                thread = new ThreadGroup { Label = threadName, Key = "t:" + threadName };
                threadsByName[threadName] = thread;
                threads.Add(thread);
            }

            string trackName = _track != null ? _track(item) ?? "" : "";
            if (!thread.ByName.TryGetValue(trackName, out TrackGroup? track))
            {
                track = new TrackGroup { Label = trackName, Key = thread.Key + "/k:" + trackName };
                thread.ByName[trackName] = track;
                thread.Tracks.Add(track);
            }

            track.Spans.Add(new Span { Payload = item, Start = start, End = end, Category = category });
        }

        if (min > max) return roots;

        _minTime = min;
        total = max - min;
        if (total <= 0d) total = 1d;

        int row = 0;
        foreach (ThreadGroup thread in threads)
        {
            if (_thread != null)
            {
                bool collapsed = IsCollapsed(thread.Key);
                _lanes.Add(new PerformanceLane { Label = thread.Label, Key = thread.Key, Depth = 0, Row = row, Collapsed = collapsed });
                row++;

                if (collapsed)
                {
                    foreach (TrackGroup track in thread.Tracks)
                        EmitRow(track.Spans, row, total, flat);
                    row++;
                    continue;
                }
            }

            foreach (TrackGroup track in thread.Tracks)
            {
                if (_track != null)
                {
                    bool collapsed = IsCollapsed(track.Key);
                    _lanes.Add(new PerformanceLane
                    {
                        Label = track.Label,
                        Key = track.Key,
                        Depth = _thread != null ? 1 : 0,
                        Row = row,
                        Collapsed = collapsed,
                    });
                    row++;

                    if (collapsed)
                    {
                        EmitRow(track.Spans, row, total, flat);
                        row++;
                        continue;
                    }
                }

                row += Pack(track.Spans, row, total, flat);
            }
        }

        _rowCount = row;
        maxDepth = Math.Max(0, row - 1);

        roots.AddRange(flat);
        return roots;
    }

    /// <summary>Places every span of one track on a single row, which is what a collapsed group draws as:
    /// one summary lane where overlapping spans simply sit on top of each other.</summary>
    private void EmitRow(List<Span> spans, int row, double total, List<HierarchicalNode<T>> flat)
    {
        foreach (Span span in spans)
            AddNode(span, row, total, flat);
    }

    /// <summary>Greedily packs one track's spans into as few rows as their overlaps allow: a span reuses
    /// the first row whose last span has already ended, and opens a new one otherwise. Returns how many
    /// rows were used, never fewer than one so an empty track still leaves its lane visible.</summary>
    private int Pack(List<Span> spans, int baseRow, double total, List<HierarchicalNode<T>> flat)
    {
        var ordered = new List<Span>(spans);
        ordered.Sort((a, b) => a.Start.CompareTo(b.Start));

        var rowEnds = new List<double>();

        foreach (Span span in ordered)
        {
            int row = -1;
            for (int i = 0; i < rowEnds.Count; i++)
            {
                if (rowEnds[i] > span.Start) continue;
                row = i;
                break;
            }

            if (row < 0)
            {
                row = rowEnds.Count;
                rowEnds.Add(span.End);
            }
            else
            {
                rowEnds[row] = span.End;
            }

            AddNode(span, baseRow + row, total, flat);
        }

        return Math.Max(1, rowEnds.Count);
    }

    private void AddNode(Span span, int row, double total, List<HierarchicalNode<T>> flat)
    {
        if (IsCategoryHidden(span.Category)) return;

        double value = span.End - span.Start;

        var node = new HierarchicalNode<T>
        {
            Payload = span.Payload,
            Label = NodeLabel(span.Payload),
            Value = value,
            Color = _categoryColors[span.Category],
            Depth = row,
            Index = flat.Count,
            Start = span.Start - _minTime,
            Fraction = total > 0d ? value / total : 0d,
            Dimmed = IsDimmed(span.Payload),
            Selected = IsSelectedItem(span.Payload),
        };

        flat.Add(node);
    }

    /// <summary>A span always occupies its row, so unlike the tree walk's nodes a zero-length one is still
    /// drawn - an instantaneous event is a mark, not an absence.</summary>
    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    // --- Collapse state ---

    private bool IsCollapsed(string key)
        => _stateEl.IsValid && _paper.GetElementStorage(_stateEl, CollapseKeyPrefix + key, false);

    private void ToggleCollapsed(PerformanceLane lane)
    {
        if (!_stateEl.IsValid) return;

        bool collapsed = !_paper.GetElementStorage(_stateEl, CollapseKeyPrefix + lane.Key, false);
        _paper.SetElementStorage(_stateEl, CollapseKeyPrefix + lane.Key, collapsed);

        if (collapsed) _onCollapse?.Invoke(lane.Label);
        else _onExpand?.Invoke(lane.Label);
    }

    // --- Legend ---

    private bool IsCategoryHidden(int category)
        => LegendInteractiveActive && _stateEl.IsValid && _paper.GetElementStorage(_stateEl, HiddenKeyPrefix + category, false);

    /// <summary>One row per category rather than per node: a timeline's spans repeat the same handful of
    /// kinds thousands of times over, so the categories are the only list short enough to read and the
    /// only one worth toggling.</summary>
    protected override IReadOnlyList<LegendEntry> BuildLegend(List<HierarchicalNode<T>> roots)
    {
        if (_categoryNames.Count == 0) return Array.Empty<LegendEntry>();
        if (_categoryNames.Count == 1 && _categoryNames[0].Length == 0) return Array.Empty<LegendEntry>();

        var entries = new List<LegendEntry>(_categoryNames.Count);
        for (int i = 0; i < _categoryNames.Count; i++)
            entries.Add(new LegendEntry(
                _categoryNames[i].Length > 0 ? _categoryNames[i] : "Other",
                _categoryColors[i], i, FormatValue(_categoryTotals[i]), IsCategoryHidden(i)));

        return entries;
    }

    // --- Chrome ---

    /// <summary>The time axis across the top of the plot: nice-numbered ticks over the visible window,
    /// each with its own label, closed off by a divider the lanes hang under.</summary>
    protected void DrawTimeRuler(Paper paper, in HierarchicalContext ctx, float height)
    {
        paper.Box(_id + "_ruler_div")
            .PositionType(PositionType.SelfDirected)
            .Position(ctx.PlotL, ctx.PlotT + height - 1f)
            .Size(ctx.Width, 1f)
            .BackgroundColor(_theme.BorderSoft)
            .IsNotInteractable();

        double visible = ctx.Total * ctx.ViewW;
        double step = NiceStep(visible / RulerTicks);
        if (step <= 0d) return;

        double from = _minTime + ctx.ViewX * ctx.Total;
        double to = from + visible;

        float labelWidth = Math.Clamp(ctx.Width * (float)(step / visible) - 6f, 8f, RulerTickLabelWidth);

        int i = 0;
        for (double t = Math.Ceiling(from / step) * step; t <= to && i < MaxRulerTicks; t += step, i++)
        {
            float x = ctx.MapX((t - _minTime) / ctx.Total);

            paper.Box($"{_id}_rtick_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(x, ctx.PlotT)
                .Size(1f, height)
                .BackgroundColor(_theme.Ink.C300)
                .IsNotInteractable();

            using (paper.Box($"{_id}_rlbl_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(x + 3f, ctx.PlotT)
                .Size(labelWidth, height - 1f)
                .IsNotInteractable()
                .Enter())
            {
                Origami.Label(paper, $"{_id}_rlbl_txt_{i}", FormatValue(t))
                    .XS()
                    .TextColor(_theme.Ink.C300)
                    .Ellipsis()
                    .AlignCenter()
                    .AlignLeft()
                    .Width(labelWidth)
                    .Height(height - 1f)
                    .Show();
            }
        }
    }

    /// <summary>The group headers, each a full-width row carrying its chevron and label. Clicking one
    /// collapses or expands that thread or track.</summary>
    protected void DrawLaneHeaders(Paper paper, in HierarchicalContext ctx, float laneTop, float rowHeight)
    {
        foreach (PerformanceLane lane in _lanes)
        {
            float y = laneTop + lane.Row * rowHeight;
            if (y >= ctx.PlotB || y + rowHeight <= laneTop) continue;

            float indent = lane.Depth * rowHeight;
            float width = ctx.Width - indent;
            if (width < rowHeight) continue;

            PerformanceLane captured = lane;

            using (paper.Row($"{_id}_lane_{lane.Key}")
                .PositionType(PositionType.SelfDirected)
                .Position(ctx.PlotL + indent, y)
                .Size(width, rowHeight)
                .BackgroundColor(_theme.Glass)
                .Rounded(2f)
                .RowBetween(2f)
                .Cursor(PaperCursor.Pointer)
                .Hovered.BackgroundColor(_theme.Hover).End()
                .OnClick(_ => ToggleCollapsed(captured))
                .Enter())
            {
                paper.Box($"{_id}_lane_ico_{lane.Key}")
                    .Width(rowHeight).Height(rowHeight)
                    .IsNotInteractable()
                    .Icon(paper, lane.Collapsed ? _theme.Icons.ChevronRight : _theme.Icons.ChevronDown,
                        _theme.Ink.C300, size: 10f);

                Origami.Label(paper, $"{_id}_lane_txt_{lane.Key}",
                        lane.Label.Length > 0 ? lane.Label : "Track")
                    .XS()
                    .TextColor(_theme.Ink.C500)
                    .Ellipsis()
                    .AlignCenter()
                    .AlignLeft()
                    .Width(MathF.Max(1f, width - rowHeight - 4f))
                    .Height(rowHeight)
                    .Show();
            }
        }
    }

    /// <summary>Rounds a raw tick spacing up to the nearest 1, 2 or 5 times a power of ten, so the ruler
    /// lands on times a reader can do arithmetic with.</summary>
    private static double NiceStep(double raw)
    {
        if (raw <= 0d || double.IsNaN(raw) || double.IsInfinity(raw)) return 0d;

        double exp = Math.Floor(Math.Log10(raw));
        double f = raw / Math.Pow(10d, exp);
        double nf = f < 1.5d ? 1d : f < 3d ? 2d : f < 7d ? 5d : 10d;
        return nf * Math.Pow(10d, exp);
    }
}
