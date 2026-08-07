// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Non-generic-in-TSelf surface a <see cref="CartesianChart{T}"/> talks to its modules through.
/// Internal: a module never needs to know this exists, it only ever sees the protected members
/// <see cref="CartesianModuleBase{TSelf, T}"/> exposes.</summary>
internal interface ICartesianModule<T>
{
    List<CartesianSeries<T>> ResolveOwnSeries();
    void PaintMarksInternal(Canvas canvas, in PlotContext<T> ctx);
    void AppendSampleInternal(Paper paper, in SampleContext<T> ctx, List<(Color Color, string Text)> rows);
    bool WantsSampleNearest2D { get; }
    bool WantsPanY { get; }
}

/// <summary>
/// One chart type plugged into a <see cref="CartesianChart{T}"/> via <c>.AddLineChart()</c>,
/// <c>.AddBarChart()</c>, <c>.AddScatterPlot()</c> or <c>.AddBubbleChart()</c>. Owns its own series and
/// their per-series styling (<see cref="Color"/>, <see cref="Stroke"/>, <see cref="Dashed"/>, ...) plus
/// whatever knobs are specific to its geometry (a line's <c>.Smooth()</c>, a bar's <c>.BarWidth()</c>, ...).
/// Everything shared across every module on the chart - the x axis, y range/ticks, grid, legend, sampler
/// crosshair, zoom/pan - belongs to the owning <see cref="Cartesian"/> chart instead, reachable by chaining
/// back through <see cref="Cartesian"/>.
/// </summary>
public abstract class CartesianModuleBase<TSelf, T> : ICartesianModule<T> where TSelf : CartesianModuleBase<TSelf, T>
{
    private readonly CartesianChart<T> _chart;
    private readonly List<CartesianSeries<T>> _series = new();
    private CartesianSeries<T>? _lastSeries;
    private Func<T, double>? _ySelector;
    private string _seriesName = "";

    protected CartesianModuleBase(CartesianChart<T> chart)
    {
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));
        chart.RegisterModule(this);
    }

    private TSelf Self => (TSelf)this;

    /// <summary>Back to the owning chart, to add another module or set a chart-level option (the shared
    /// <c>.X(...)</c>, y range/ticks, grid, sampler, zoom/pan, ...).</summary>
    public CartesianChart<T> Cartesian => _chart;

    // ── Data ────────────────────────────────────────────────────

    /// <summary>Add a series of pre-sampled values to this module. Index in <paramref name="values"/>
    /// maps to the x axis, shared with every other module on the chart.</summary>
    public TSelf Series(string label, Color color, IReadOnlyList<double> values)
    {
        var s = new CartesianSeries<T> { Label = label ?? "", Color = color, Owner = this };
        if (values != null)
            for (int i = 0; i < values.Count; i++)
                s.Points.Add((i, values[i], default));
        _series.Add(s);
        _lastSeries = s;
        return Self;
    }

    /// <summary>Y selector run against the chart's data set (passed to <c>Chart.CreateCartesian(...)</c>)
    /// and its shared x selector (<c>Cartesian.X(...)</c>) to build this module's implicit series. X is
    /// deliberately not settable per module - every module reads the same x, which is what lets the
    /// legend and sampler line up marks from different modules at the same index.</summary>
    public TSelf Y(Func<T, double> selector) { _ySelector = selector; return Self; }

    /// <summary>Name of the implicit series built from <see cref="Y"/>.</summary>
    public TSelf Name(string text) { _seriesName = text ?? ""; return Self; }

    /// <summary>Show/hide the most recently added series.</summary>
    public TSelf Visible(bool visible) { if (_lastSeries != null) _lastSeries.Visible = visible; return Self; }

    /// <summary>Accent colour of the most recently added series (swatch, stroke and fill base).</summary>
    public TSelf Color(Color color) { if (_lastSeries != null) _lastSeries.Color = color; return Self; }

    /// <summary>Stroke colour of the most recently added series, overriding <see cref="Color"/> for the
    /// drawn line/border only.</summary>
    public TSelf Stroke(Color color) { if (_lastSeries != null) _lastSeries.StrokeColor = color; return Self; }

    /// <summary>Stroke width, in pixels, of the most recently added series.</summary>
    public TSelf StrokeWidth(float width) { if (_lastSeries != null) _lastSeries.StrokeWidth = MathF.Max(0.1f, width); return Self; }

    /// <summary>Fill the area under/behind the most recently added series.</summary>
    public TSelf Fill(bool fill = true) { if (_lastSeries != null) _lastSeries.Fill = fill; return Self; }

    /// <summary>Draw the most recently added series' stroke as a dashed line.</summary>
    public TSelf Dashed() { if (_lastSeries != null) _lastSeries.Dash = CartesianDash.Dashed; return Self; }

    /// <summary>Draw the most recently added series' stroke as a dotted line.</summary>
    public TSelf Dotted() { if (_lastSeries != null) _lastSeries.Dash = CartesianDash.Dotted; return Self; }

    // ── Hooks a concrete module supplies ───────────────────────

    /// <summary>Paint this module's marks (line stroke, bar rects, scatter dots, ...) into the plot area
    /// described by <paramref name="ctx"/>, whose <see cref="PlotContext{T}.Series"/> is already scoped to
    /// just this module's own series - axis range and ticks still reflect every module on the chart.</summary>
    protected abstract void PaintMarks(Canvas canvas, in PlotContext<T> ctx);

    /// <summary>Contribute this module's own sampler dots/rings (via <see cref="SampleDot"/>/
    /// <see cref="SampleRing"/>) and readout rows for the sampled index in <paramref name="ctx"/>, whose
    /// series are scoped to this module. The shared crosshair and popup box are drawn once by the owning
    /// <see cref="CartesianChart{T}"/> after every module has appended its rows.</summary>
    protected abstract void AppendSample(Paper paper, in SampleContext<T> ctx, List<(Color Color, string Text)> rows);

    /// <summary>When true the sampler picks this module's point closest to the pointer in both axes
    /// rather than closest in x alone, if this module owns the chart's longest series. Scatter and Bubble
    /// set this because their points are scattered freely rather than laid out along a shared x sequence.</summary>
    protected virtual bool SampleNearest2D => false;

    /// <summary>When true, zoom/pan on the owning chart also affects the y axis by default. Scatter and
    /// Bubble set this since their points are scattered freely in both axes.</summary>
    protected virtual bool PanY => false;

    // ── Helpers forwarded from the owning chart ────────────────

    /// <summary>Formats a value the way the owning chart's axis labels and legend do.</summary>
    protected string FormatValue(double v) => _chart.FormatValueInternal(v);

    /// <summary>Label for the sampled index, from the chart's <c>.XTickFormatter(...)</c> if one is set.</summary>
    protected string SampleHeader(int index) => _chart.SampleHeaderInternal(index);

    /// <summary>Marker dot centred on a sampled mark. <paramref name="key"/> must be unique among the
    /// dots every module's sampler pass emits, so prefix it with something identifying this module.</summary>
    protected void SampleDot(Paper paper, string key, float x, float y, Color color, float diameter = 6f)
        => _chart.SampleDotInternal(paper, key, x, y, color, diameter);

    /// <summary>Hollow ring around a sampled mark, for marks already filled and big enough that a dot
    /// would disappear inside them.</summary>
    protected void SampleRing(Paper paper, string key, float x, float y, float diameter, Color color)
        => _chart.SampleRingInternal(paper, key, x, y, diameter, color);

    /// <summary>Translucent full-height highlight over a horizontal slice of the plot, for a module
    /// whose sample covers a whole band rather than a single x (Bar).</summary>
    protected void SampleBand(Paper paper, in SampleContext<T> ctx, float left, float width)
        => _chart.SampleBandInternal(paper, in ctx, left, width);

    protected static Color32 ToC32(Color c) => new(c.R, c.G, c.B, c.A);
    protected static Color32 ToC32(Color c, float alpha) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    protected static CartesianSeries<T>? LongestVisible(IReadOnlyList<CartesianSeries<T>> series)
    {
        CartesianSeries<T>? longest = null;
        foreach (CartesianSeries<T> s in series)
            if (s.EffectiveVisible && (longest == null || s.Points.Count > longest.Points.Count))
                longest = s;
        return longest;
    }


    private List<CartesianSeries<T>> ResolveOwnSeriesCore()
    {
        var list = new List<CartesianSeries<T>>(_series);

        Func<T, double>? xSelector = _chart.SharedXSelector;
        IReadOnlyList<T>? data = _chart.SharedData;
        if (_ySelector != null && xSelector != null && data != null)
        {
            var s = new CartesianSeries<T> { Label = _seriesName, Owner = this };
            foreach (T? item in data)
                s.Points.Add((xSelector(item), _ySelector(item), item));
            list.Add(s);
            if (_lastSeries == null) _lastSeries = s;
        }

        foreach (CartesianSeries<T> s in list) s.Owner = this;
        return list;
    }

    List<CartesianSeries<T>> ICartesianModule<T>.ResolveOwnSeries() => ResolveOwnSeriesCore();
    void ICartesianModule<T>.PaintMarksInternal(Canvas canvas, in PlotContext<T> ctx) => PaintMarks(canvas, in ctx);
    void ICartesianModule<T>.AppendSampleInternal(Paper paper, in SampleContext<T> ctx, List<(Color Color, string Text)> rows) => AppendSample(paper, in ctx, rows);
    bool ICartesianModule<T>.WantsSampleNearest2D => SampleNearest2D;
    bool ICartesianModule<T>.WantsPanY => PanY;
}

/// <summary>
/// A Cartesian chart whose marks come from modules: <c>.AddLineChart()</c>, <c>.AddBarChart()</c>, <c>.AddScatterPlot()</c>, <c>.AddBubbleChart()</c>. 
/// Built with <see cref="Chart.CreateCartesian{T}"/>.
///
/// X axis is shared, every module reads it back to place its own <c>.Y(...)</c>-selected series.
/// </summary>
public sealed class CartesianChart<T> : CartesianCore<CartesianChart<T>, T>
{
    internal CartesianChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private readonly List<ICartesianModule<T>> _modules = new();

    internal void RegisterModule(ICartesianModule<T> module) => _modules.Add(module);

    // ── Module factories ────────────────────────────────────────

    public LineModule<T> AddLineChart() => new(this);
    public BarModule<T> AddBarChart() => new(this);
    public ScatterModule<T> AddScatterPlot() => new(this);
    public BubbleModule<T> AddBubbleChart() => new(this);

    // ── Internal access for CartesianModuleBase<TSelf, T> ──────

    internal Func<T, double>? SharedXSelector => XSelector;
    internal IReadOnlyList<T>? SharedData => _data;

    internal string FormatValueInternal(double v) => FormatValue(v);
    internal string SampleHeaderInternal(int index) => SampleHeader(index);
    internal void SampleDotInternal(Paper paper, string key, float x, float y, Color color, float diameter) => SampleDot(paper, key, x, y, color, diameter);
    internal void SampleRingInternal(Paper paper, string key, float x, float y, float diameter, Color color) => SampleRing(paper, key, x, y, diameter, color);
    internal void SampleBandInternal(Paper paper, in SampleContext<T> ctx, float left, float width) => SampleBand(paper, in ctx, left, width);

    // ── Aggregating every module ────────────────────────────────

    protected override List<CartesianSeries<T>>? ExternalSeries
    {
        get
        {
            var all = new List<CartesianSeries<T>>();
            foreach (ICartesianModule<T> m in _modules)
                all.AddRange(m.ResolveOwnSeries());
            return all;
        }
    }

    protected override bool SampleNearest2D
    {
        get
        {
            foreach (ICartesianModule<T> m in _modules)
                if (m.WantsSampleNearest2D) return true;
            return false;
        }
    }

    protected override bool DefaultPanY
    {
        get
        {
            foreach (ICartesianModule<T> m in _modules)
                if (m.WantsPanY) return true;
            return false;
        }
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext<T> ctx)
    {
        foreach (ICartesianModule<T> m in _modules)
        {
            List<CartesianSeries<T>> own = FilterOwn(ctx.Series, m);
            if (own.Count == 0) continue;
            m.PaintMarksInternal(canvas, ctx.WithSeries(own));
        }
    }

    /// <summary>One shared crosshair for the whole chart, at the sampled index of whichever module's
    /// series is longest.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext<T> ctx)
    {
        if (ctx.MaxN <= 0) return;

        CartesianSeries<T>? longest = LongestVisible(ctx.Series);
        float lx = longest != null && ctx.Index < longest.Points.Count
            ? ctx.XPos(longest.Points[ctx.Index].X)
            : ctx.XPos(ctx.Index);

        SampleLine(paper, in ctx, lx);

        var rows = new List<(Color Color, string Text)>();
        foreach (ICartesianModule<T> m in _modules)
        {
            List<CartesianSeries<T>> own = FilterOwn(ctx.Series, m);
            if (own.Count == 0) continue;
            m.AppendSampleInternal(paper, ctx.WithSeries(own), rows);
        }

        SamplePopup(paper, in ctx, lx, SampleHeader(ctx.Index), rows);
    }

    private static List<CartesianSeries<T>> FilterOwn(IReadOnlyList<CartesianSeries<T>> all, ICartesianModule<T> owner)
    {
        var list = new List<CartesianSeries<T>>();
        foreach (CartesianSeries<T> s in all)
            if (ReferenceEquals(s.Owner, owner)) list.Add(s);
        return list;
    }
}
