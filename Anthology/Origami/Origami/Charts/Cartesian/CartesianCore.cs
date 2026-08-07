// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>
/// Dash pattern for a Cartesian series' stroke. Set via <c>.Dashed()</c>/<c>.Dotted()</c>.
/// </summary>
public enum CartesianDash
{
    Solid,
    Dashed,
    Dotted,
}

/// <summary>
/// One series plotted on a <see cref="CartesianCore{TSelf, T}"/> chart.
/// </summary>
public sealed class CartesianSeries<T>
{
    public string Label = "";
    public Color? Color;
    public Color? StrokeColor;
    public float? StrokeWidth;
    public bool Fill;
    public CartesianDash Dash = CartesianDash.Solid;
    public bool Visible = true;
    public bool LegendHidden;
    public readonly List<(double X, double Y, T? Payload)> Points = new();
    internal object? Owner;

    public bool EffectiveVisible => Visible && !LegendHidden;
}


internal readonly struct AxisTick
{
    public readonly double Position;
    public readonly double Value;
    public readonly string Label;

    public AxisTick(double position, double value, string label)
    {
        Position = position;
        Value = value;
        Label = label;
    }
}


public readonly struct PlotContext<T>
{
    public readonly float PlotL, PlotT, PlotR, PlotB;
    public readonly double XMin, XMax;
    public readonly double YMin, YMax;
    public readonly IReadOnlyList<CartesianSeries<T>> Series;

    public readonly int MaxN;

    internal readonly IReadOnlyList<AxisTick> XTicks;
    internal readonly IReadOnlyList<AxisTick> YTicks;

    private readonly float _bandFirst;
    private readonly float _bandSpan;

    internal PlotContext(float plotL, float plotT, float plotR, float plotB,
        double xMin, double xMax, double yMin, double yMax,
        IReadOnlyList<CartesianSeries<T>> series, int maxN,
        IReadOnlyList<AxisTick> xTicks, IReadOnlyList<AxisTick> yTicks,
        double bandFirst, double bandSpan)
    {
        PlotL = plotL; PlotT = plotT; PlotR = plotR; PlotB = plotB;
        XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax;
        Series = series; MaxN = maxN;
        XTicks = xTicks; YTicks = yTicks;
        _bandFirst = bandSpan > 0d ? (float)bandFirst : 0f;
        _bandSpan = bandSpan > 0d ? (float)bandSpan : maxN;
    }

    /// <summary>Returns this context with <see cref="Series"/> replaced, keeping every other field (axis
    /// range, ticks, band window) the same. This is how a <see cref="CartesianChart{T}"/> hands each of its
    /// modules a view scoped to just that module's own series while sharing one axis/tick pass.</summary>
    internal PlotContext<T> WithSeries(IReadOnlyList<CartesianSeries<T>> series)
        => new(PlotL, PlotT, PlotR, PlotB, XMin, XMax, YMin, YMax, series, MaxN, XTicks, YTicks, _bandFirst, _bandSpan);

    public float BandWidth => _bandSpan <= 0f ? 0f : (PlotR - PlotL) / _bandSpan;

    /// <summary>Left edge in pixels of the band owned by point <paramref name="index"/>.</summary>
    public float BandLeft(int index) => PlotL + BandWidth * (index - _bandFirst);

    /// <summary>Centre in pixels of the band owned by point <paramref name="index"/>.</summary>
    public float BandCenter(int index) => PlotL + BandWidth * (index + 0.5f - _bandFirst);

    /// <summary>
    /// Width in pixels of one x unit in the continuous coordinate space.
    /// </summary>
    public float UnitWidth
    {
        get
        {
            double span = XMax - XMin;
            return span <= 0d ? 0f : (float)((PlotR - PlotL) / span);
        }
    }

    public float XPos(double x)
    {
        double span = XMax - XMin;
        if (span <= 0d) return (PlotL + PlotR) * 0.5f;
        return AxisPos(x, XMin, span, PlotL, PlotR);
    }

    public float YPos(double y)
    {
        double span = YMax - YMin;
        if (span <= 0d) return (PlotT + PlotB) * 0.5f;
        return AxisPos(y, YMin, span, PlotB, PlotT);
    }

    private static float AxisPos(double v, double min, double span, float pxA, float pxB)
        => pxA + (float)((v - min) / span) * (pxB - pxA);
}


/// <summary>The current sample selection handed to a sampler-drawing step, along with the same
/// geometry <c>PaintMarks</c> gets. Unlike the paint pass, the pixel coordinates here are relative to the
/// plot element's own top-left corner, because a sampler is built out of self-directed child nodes of that
/// element rather than painted onto the canvas.</summary>
public readonly struct SampleContext<T>
{
    /// <summary>Plot geometry and resolved series, in plot-element-local pixels.</summary>
    public readonly PlotContext<T> Plot;

    /// <summary>Index of the sampled point within each series.</summary>
    public readonly int Index;

    /// <summary>Pointer position that produced <see cref="Index"/>, in the same space as
    /// <see cref="Plot"/>.</summary>
    public readonly Float2 Pointer;

    internal SampleContext(in PlotContext<T> plot, int index, Float2 pointer)
    {
        Plot = plot;
        Index = index;
        Pointer = pointer;
    }

    internal SampleContext<T> WithSeries(IReadOnlyList<CartesianSeries<T>> series) => new(Plot.WithSeries(series), Index, Pointer);

    public IReadOnlyList<CartesianSeries<T>> Series => Plot.Series;
    public int MaxN => Plot.MaxN;
    public float PlotL => Plot.PlotL;
    public float PlotT => Plot.PlotT;
    public float PlotR => Plot.PlotR;
    public float PlotB => Plot.PlotB;

    public float XPos(double x) => Plot.XPos(x);
    public float YPos(double y) => Plot.YPos(y);
    public float BandWidth => Plot.BandWidth;
    public float UnitWidth => Plot.UnitWidth;
    public float BandLeft(int index) => Plot.BandLeft(index);
    public float BandCenter(int index) => Plot.BandCenter(index);
}

/// <summary>Divides one categorical band into a side-by-side slot per visible series, which is how
/// every banded type that draws more than one mark per band places them. <paramref name="widthFraction"/>
/// is the share of the band the whole group occupies, and <paramref name="gapFraction"/> the share of
/// each slot left empty as spacing between neighbouring marks.</summary>
public readonly struct BandSlots
{
    public readonly float GroupInset;
    public readonly float SlotWidth;
    public readonly float MarkInset;
    public readonly float MarkWidth;

    public BandSlots(float bandWidth, float widthFraction, float gapFraction, int seriesCount)
    {
        float groupWidth = bandWidth * widthFraction;
        GroupInset = (bandWidth - groupWidth) * 0.5f;
        SlotWidth = groupWidth / Math.Max(1, seriesCount);
        MarkInset = SlotWidth * gapFraction * 0.5f;
        MarkWidth = MathF.Max(1f, SlotWidth - MarkInset * 2f);
    }

    /// <summary>Left edge in pixels of the mark drawn in <paramref name="seriesSlot"/>, given the left
    /// edge in pixels of the band as a whole.</summary>
    public float Left(float bandLeft, int seriesSlot) => bandLeft + GroupInset + SlotWidth * seriesSlot + MarkInset;

    /// <summary>Centre in pixels of the mark drawn in <paramref name="seriesSlot"/>.</summary>
    public float Center(float bandLeft, int seriesSlot) => Left(bandLeft, seriesSlot) + MarkWidth * 0.5f;

    /// <summary>Inverse of <see cref="Left"/>: the slot the pixel <paramref name="x"/> falls in,
    /// clamped to the occupied range. Samplers use this to resolve which series the pointer is over
    /// within a band.</summary>
    public int SlotAt(float bandLeft, float x, int seriesCount)
    {
        if (seriesCount <= 1 || SlotWidth <= 0f) return 0;
        return Math.Clamp((int)((x - bandLeft - GroupInset) / SlotWidth), 0, seriesCount - 1);
    }
}

/// <summary>
/// Shared implementation for every Cartesian chart type (Line, Bar, Scatter, Bubble), and indirectly,
/// via <see cref="DistributionCore{TSelf, T}"/>, for Histogram. Owns the axes, gutters, ticks, grid,
/// sampler, zoom/pan and mark-painting plumbing; everything about the outer box, title, legend and data
/// visibility is <see cref="ChartCore{TSelf, T}"/>'s.
///
/// A subtype normally supplies its own <see cref="PaintMarks"/>/<see cref="DrawSampler"/> directly (Line,
/// Bar, Scatter, Bubble used standalone). <see cref="CartesianChart{T}"/> is the one exception: it supplies
/// no geometry of its own and instead fans both hooks out across a list of <see cref="CartesianModuleBase{T}"/>
/// instances, which is what lets several chart types share one set of axes via <c>.AddLineChart()</c>,
/// <c>.AddBarChart()</c>, etc.
/// </summary>
public abstract class CartesianCore<TSelf, T> : ChartCore<TSelf, T> where TSelf : CartesianCore<TSelf, T>
{
    private Color? _backgroundColor;

    private List<CartesianSeries<T>>? _resolvedCache;

    private readonly List<CartesianSeries<T>> _series = new();
    private CartesianSeries<T>? _lastSeries;
    private Func<T, double>? _xSelector;
    private Func<T, double>? _ySelector;
    private string _pointSeriesName = "";

    private bool _hasYRange;
    private bool _derivedYRange;
    private double _yRangeMin, _yRangeMax;
    private double _minSpan;
    private bool _includeZero = true;
    private AxisScale _scale = AxisScale.Linear;
    private int _yTicks = 4;
    private Func<double, string>? _valueFormatter;
    private Func<int, string>? _xTickFormatter;
    private int _xTicks = -1;
    private string _xLabel = "";
    private string _yLabel = "";
    private bool _axes = true;

    private enum GridMode { None, FixedCount, TickRatio }
    private GridMode _gridMode = GridMode.None;
    private int _gridCountX = -1, _gridCountY = 4;
    private int _gridRatioX = -1, _gridRatioY = 1;
    private Color? _gridLineColor;

    private bool _sampleable;
    private Color? _sampleLineColor;

    private bool _autoFit;

    private bool _zoomable;
    private bool _pannable;

    protected CartesianCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data = null)
        : base(paper, id, theme, data) { }

    private TSelf Self => (TSelf)this;

    // ── Chrome ──────────────────────────────────────────────────

    public TSelf BackgroundColor(Color color) { _backgroundColor = color; return Self; }

    protected override void DecorateContainer(ElementBuilder container)
    {
        if (_backgroundColor.HasValue) container.BackgroundColor(_backgroundColor.Value);
    }

    // ── Data ────────────────────────────────────────────────────

    /// <summary>Add a series of pre-sampled values. Index in <paramref name="values"/> maps to the x axis.</summary>
    public TSelf Series(string label, Color color, IReadOnlyList<double> values)
    {
        var s = new CartesianSeries<T> { Label = label ?? "", Color = color };
        if (values != null)
            for (int i = 0; i < values.Count; i++)
                s.Points.Add((i, values[i], default));
        _series.Add(s);
        _lastSeries = s;
        return Self;
    }

    /// <summary>Name used for the implicit series produced by <see cref="X"/>/<see cref="Y"/> selectors
    /// over the chart's data set.</summary>
    public TSelf Name(string text) { _pointSeriesName = text ?? ""; return Self; }

    /// <summary>X selector for the data set passed at construction. Used together with <see cref="Y"/>
    /// instead of <see cref="Series(string, Color, IReadOnlyList{double})"/>.</summary>
    public TSelf X(Func<T, double> selector) { _xSelector = selector; return Self; }

    /// <summary>Y selector for the data set passed at construction. Used together with <see cref="X"/>
    /// instead of <see cref="Series(string, Color, IReadOnlyList{double})"/>.</summary>
    public TSelf Y(Func<T, double> selector) { _ySelector = selector; return Self; }

    /// <summary>The x selector set via <see cref="X"/>, if any. Exposed so <see cref="CartesianChart{T}"/>
    /// can share it with every module plugged into it - modules only ever supply their own <c>Y</c>.</summary>
    protected Func<T, double>? XSelector => _xSelector;

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

    /// <summary>Recompute the y range from the currently visible series every frame, ignoring any
    /// explicit <see cref="YRange"/>. Useful alongside <see cref="LegendInteractive"/> so hiding a
    /// series refits the axis to what's left visible.</summary>
    public TSelf AutoFit() { _autoFit = true; return Self; }

    // ── Y axis / range ──────────────────────────────────────────

    public TSelf YRange(double min, double max) { _hasYRange = true; _yRangeMin = Math.Min(min, max); _yRangeMax = Math.Max(min, max); return Self; }

    /// <summary>True once a caller has set an explicit <see cref="YRange"/>, so an
    /// <see cref="OnBeforeShow"/> override knows not to overwrite it.</summary>
    protected bool HasExplicitYRange => _hasYRange;

    /// <summary>Sets the y range from a chart type's own geometry rather than from a caller, for
    /// <see cref="OnBeforeShow"/> overrides. Unlike <see cref="YRange"/> this does not mark the range
    /// as caller-supplied, so <see cref="HasExplicitYRange"/> stays false and the range is recomputed
    /// on the next frame instead of freezing at whatever the first frame's data implied. A caller's
    /// explicit <see cref="YRange"/> always wins.</summary>
    protected void DeriveYRange(double min, double max)
    {
        if (_hasYRange) return;
        _derivedYRange = true;
        _yRangeMin = Math.Min(min, max);
        _yRangeMax = Math.Max(min, max);
    }

    /// <summary>The series added via <see cref="Series(string, Color, IReadOnlyList{double})"/>, in call
    /// order. Exposed for <see cref="OnBeforeShow"/> overrides whose marks span a value the per-point y
    /// does not reach, such as a distribution chart type deriving points from a group's raw values rather
    /// than plotting them directly. Does not include the implicit series built from the <see cref="X"/>/
    /// <see cref="Y"/> selectors, which is resolved later.</summary>
    protected IReadOnlyList<CartesianSeries<T>> SeriesList => _series;

    /// <summary>Additional series to fold into every axis/tick/legend/sampler pass alongside
    /// <see cref="SeriesList"/> and the implicit <see cref="X"/>/<see cref="Y"/> series, without those
    /// series living in this type's own <see cref="Series(string, Color, IReadOnlyList{double})"/> list.
    /// <see cref="CartesianChart{T}"/> is the only override: it has no marks of its own, so every series
    /// on it comes from its modules instead.</summary>
    protected virtual List<CartesianSeries<T>>? ExternalSeries => null;

    public TSelf MinSpan(double span) { _minSpan = Math.Max(0d, span); return Self; }
    public TSelf IncludeZero(bool include = true) { _includeZero = include; return Self; }
    public TSelf Scale(AxisScale scale) { _scale = scale; return Self; }
    public TSelf YTicks(int count) { _yTicks = Math.Max(2, count); return Self; }

    public TSelf ValueFormatter(Func<double, string> formatter) { _valueFormatter = formatter; return Self; }
    public TSelf XTickFormatter(Func<int, string> formatter) { _xTickFormatter = formatter; return Self; }
    public TSelf XTicks(int count) { _xTicks = Math.Max(1, count); return Self; }
    public TSelf XLabel(string text) { _xLabel = text ?? ""; return Self; }
    public TSelf YLabel(string text) { _yLabel = text ?? ""; return Self; }
    public TSelf Axes(bool show = true) { _axes = show; return Self; }

    // ── Grid ────────────────────────────────────────────────────

    public TSelf GridLines(int countY)
    {
        _gridMode = GridMode.FixedCount;
        _gridCountY = Math.Max(1, countY);
        _gridCountX = -1;
        return Self;
    }

    public TSelf GridLines(int countX, int countY)
    {
        _gridMode = GridMode.FixedCount;
        _gridCountX = Math.Max(1, countX);
        _gridCountY = Math.Max(1, countY);
        return Self;
    }

    public TSelf GridTickLines(int ratioY)
    {
        _gridMode = GridMode.TickRatio;
        _gridRatioY = Math.Max(1, ratioY);
        _gridRatioX = -1;
        return Self;
    }

    public TSelf GridTickLines(int ratioX, int ratioY)
    {
        _gridMode = GridMode.TickRatio;
        _gridRatioX = Math.Max(1, ratioX);
        _gridRatioY = Math.Max(1, ratioY);
        return Self;
    }

    public TSelf GridLineColor(Color color) { _gridLineColor = color; return Self; }

    // ── Sample / crosshair ─────────────────────────────────────

    public TSelf Sampleable(bool enable = true) { _sampleable = enable; return Self; }
    public TSelf SampleLineColor(Color color) { _sampleLineColor = color; return Self; }

    /// <summary>Enable scroll-wheel zoom over the plot, anchored on the pointer so the value under the
    /// cursor stays put. Zooming narrows the plotted range; it can never widen it past the full data
    /// range. Applies to the same axes as <see cref="Pannable(bool, bool)"/>.</summary>
    public TSelf Zoomable(bool enable = true) { _zoomable = enable; return Self; }

    /// <summary>Enable middle-mouse drag panning over the plot, on this chart type's default axes.
    /// Left drag stays bound to the sampler.</summary>
    public TSelf Pannable(bool enable = true) { _pannable = enable; return Self; }

    /// <summary>Whether the x axis takes part in zoom and pan when no explicit axis mask was given.
    /// True everywhere: x is the sequence axis on every Cartesian type.</summary>
    protected virtual bool DefaultPanX => true;

    /// <summary>Whether the y axis takes part in zoom and pan when no explicit axis mask was given.
    /// False except on chart types whose points are scattered freely in both axes.</summary>
    protected virtual bool DefaultPanY => false;

    private bool ViewAxisX => DefaultPanX;
    private bool ViewAxisY => DefaultPanY;

    // ── Tick layout ─────────────────────────────────────────────

    /// <summary>X-axis ticks for the current data, each with its position mapped into [0, 1]
    /// across the plotted x range and its label from <see cref="XTickFormatter"/>. Tick count
    /// comes from <see cref="XTicks"/> if set, otherwise derived from the data point count.</summary>
    internal IReadOnlyList<AxisTick> GetXTicks()
    {
        List<CartesianSeries<T>> resolved = ResolveSeries();
        ComputeXRange(resolved, out double xMin, out double xMax);
        return ComputeXTicks(resolved, xMin, xMax, 0d, 0d);
    }

    /// <summary>Y-axis ticks for the current data, each with its position mapped into [0, 1]
    /// across the plotted y range and its label from <see cref="ValueFormatter"/>. Ticks account
    /// for <see cref="Scale"/> (log vs. linear), <see cref="YTicks"/>, and any explicit
    /// <see cref="YRange"/>.</summary>
    internal IReadOnlyList<AxisTick> GetYTicks()
    {
        List<CartesianSeries<T>> resolved = ResolveSeries();
        ComputeYRange(resolved, out double yMin, out double yMax, out double tickSpacing);
        return ComputeYTicks(resolved, yMin, yMax, tickSpacing);
    }

    private List<AxisTick> ComputeXTicks(IReadOnlyList<CartesianSeries<T>> series, double xMin, double xMax,
        double bandFirst, double bandSpan)
    {
        var result = new List<AxisTick>();

        double span = xMax - xMin; if (span <= 0d) span = 1d;

        CartesianSeries<T>? longest = null;
        int maxN = 0;
        foreach (CartesianSeries<T> s in series)
        {
            if (!s.EffectiveVisible) continue;
            if (s.Points.Count > maxN) { maxN = s.Points.Count; longest = s; }
        }

        if (longest == null || maxN == 0) return result;

        if (!BandedX)
        {
            int n = _xTicks > 0 ? _xTicks : Math.Min(6, Math.Max(2, maxN));
            for (int i = 0; i < n; i++)
            {
                double pos = n <= 1 ? 0d : i / (double)(n - 1);
                double xVal = xMin + pos * span;
                int key = (int)Math.Round(xVal);
                string tickLabel = _xTickFormatter != null ? (_xTickFormatter(key) ?? "") : key.ToString();
                result.Add(new AxisTick(pos, xVal, tickLabel));
            }
            return result;
        }

        if (bandSpan <= 0d) { bandFirst = 0d; bandSpan = maxN; }

        int lo = Math.Clamp((int)Math.Floor(bandFirst), 0, maxN - 1);
        int hi = Math.Clamp((int)Math.Ceiling(bandFirst + bandSpan) - 1, lo, maxN - 1);

        int avail = hi - lo + 1;
        int count = _xTicks > 0 ? Math.Min(_xTicks, avail)
            : TickPerBand ? avail
            : Math.Min(6, Math.Max(2, avail));
        count = Math.Max(1, count);

        int lastIdx = -1;
        for (int i = 0; i < count; i++)
        {
            int idx = count <= 1 ? lo : lo + (int)Math.Round(i * (avail - 1) / (double)(count - 1));
            idx = Math.Clamp(idx, lo, hi);
            if (idx == lastIdx) continue;
            lastIdx = idx;

            double xVal = longest.Points[idx].X;
            double pos = ((idx + 0.5d) - bandFirst) / bandSpan;
            if (pos < -1e-6d || pos > 1d + 1e-6d) continue;

            string label = _xTickFormatter != null ? (_xTickFormatter(idx) ?? "") : DefaultXTickLabel(idx);
            result.Add(new AxisTick(pos, xVal, label));
        }
        return result;
    }

    private List<AxisTick> ComputeYTicks(IReadOnlyList<CartesianSeries<T>> series, double yMin, double yMax, double tickSpacing)
    {
        List<double> tickVals = _scale == AxisScale.Log
            ? LogTicks(yMin, yMax)
            : BuildLinearTicks(yMin, yMax, tickSpacing);

        double span = yMax - yMin; if (span <= 0d) span = 1d;

        var result = new List<AxisTick>(tickVals.Count);
        foreach (double v in tickVals)
        {
            double pos = (v - yMin) / span;
            result.Add(new AxisTick(pos, v, Format(v)));
        }
        return result;
    }

    // ── Mark painting hook ──────────────────────────────────────

    /// <summary>Paint this chart type's marks (line stroke, bar rects, scatter dots, ...) into the
    /// already-clipped plot area described by <paramref name="ctx"/>.</summary>
    protected abstract void PaintMarks(Canvas canvas, in PlotContext<T> ctx);

    /// <summary>When true the x axis is treated as a sequence of equal-width categorical bands rather
    /// than a continuous value range: point i owns the band <c>[BandLeft(i), BandLeft(i) + BandWidth)</c>
    /// and x-axis ticks are placed at band centres instead of at their data x. Bar and Histogram override
    /// this; point/line types leave it false. <see cref="CartesianChart{T}"/> also leaves this false even
    /// when it holds a bar module: combo charts centre bars on the shared continuous x instead (see
    /// <see cref="PlotContext{T}.UnitWidth"/>), so every module - banded or not - reads the same axis.</summary>
    protected virtual bool BandedX => false;

    /// <summary>When true, a <see cref="BandedX"/> chart with no explicit <see cref="XTicks"/> gets one
    /// x tick per band rather than a capped, evenly spread subset. Set on the categorical types whose
    /// bands each carry a name worth reading (Bar, Histogram); left false on types whose bands are a
    /// long numeric sequence where one label per band would be unreadable. Ignored when
    /// <see cref="BandedX"/> is false.</summary>
    protected virtual bool TickPerBand => false;

    /// <summary>Label for band <paramref name="index"/> when the caller set no <see cref="XTickFormatter"/>.
    /// Chart types whose bands are not point indices - a box plot band is a whole group - override this so
    /// the axis names the band without every caller having to supply a formatter.</summary>
    protected virtual string DefaultXTickLabel(int index) => index.ToString();

    /// <summary>When true the sampler picks the point closest to the pointer in both axes rather than
    /// the one closest in x alone. Scatter and Bubble override this because their points are scattered
    /// freely rather than laid out along a shared x sequence; ignored when <see cref="BandedX"/> is set.</summary>
    protected virtual bool SampleNearest2D => false;

    /// <summary>Build this chart type's sample readout for the point selected in
    /// <paramref name="ctx"/>. Called only while a sample is active, from inside the plot element, so
    /// everything laid out here must be self-directed. Selection itself is the core's job; this only
    /// draws it. Use <see cref="SampleLine"/>, <see cref="SampleBand"/>, <see cref="SampleDot"/> and
    /// <see cref="SamplePopup"/> to build the readout out of Paper nodes.</summary>
    protected abstract void DrawSampler(Paper paper, in SampleContext<T> ctx);

    private static float SwatchSize = 12f;

    private const string SampleIdxKey = "cartesian_sample";
    private const string SamplePosKey = "cartesian_sample_pos";
    private const string ViewRectKey = "cartesian_view";

    private const float MinViewSpan = 0.005f;
    private const float ZoomRate = 0.14f;

    /// <summary>The visible fraction of the base data range on each axis, as an offset plus a span in
    /// [0, 1]. Identity is (0, 0, 1, 1), the full range. Normalized rather than absolute so it survives
    /// the data set changing between frames.</summary>
    private struct ViewRect
    {
        public float X, Y, W, H;
    }

    /// <summary>The plotted axis window once the view rect has narrowed the base range, plus the band
    /// sub-range it maps to for <see cref="BandedX"/> chart types.</summary>
    private readonly struct PlotWindow
    {
        public readonly double XMin, XMax, YMin, YMax;
        public readonly double BandFirst, BandSpan;

        public PlotWindow(double xMin, double xMax, double yMin, double yMax, double bandFirst, double bandSpan)
        {
            XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax;
            BandFirst = bandFirst; BandSpan = bandSpan;
        }
    }


    /// <summary>This chart's series, resolved and legend-filtered, built once per <c>Show()</c> by
    /// <see cref="BuildLegendEntries"/> and consumed by <see cref="DrawPlot"/>.</summary>
    protected override IReadOnlyList<LegendEntry> BuildLegendEntries()
    {
        List<CartesianSeries<T>> resolved = ResolveSeries();
        for (int i = 0; i < resolved.Count; i++)
            resolved[i].LegendHidden = IsLegendHidden(i);

        _resolvedCache = resolved;

        var entries = new List<LegendEntry>(resolved.Count);
        for (int i = 0; i < resolved.Count; i++)
        {
            CartesianSeries<T> s = resolved[i];
            string? valueText = LegendShowValueEnabled && s.Points.Count > 0 ? Format(s.Points[^1].Y) : null;
            entries.Add(new LegendEntry(s.Label, s.Color ?? System.Drawing.Color.Gray, i, valueText, s.LegendHidden));
        }
        return entries;
    }

    protected override void DrawPlot()
    {
        List<CartesianSeries<T>> series = _resolvedCache ?? ResolveSeries();
        DrawChartBox(series, ContainerEl);
    }

    private void DrawChartBox(List<CartesianSeries<T>> series, ElementHandle el)
    {
        if (series.Count == 0 || !series.Any(x => x.Points.Count != 0))
        {
            using (_paper.Row(_id + "_chart_empty_wrap").Enter())
                Origami.Label(_paper, _id + "_chart_empty", EmptyLabelText).LG().Show();

            return;
        }

        int maxN = 0;
        CartesianSeries<T>? longest = null;
        foreach (CartesianSeries<T> s in series)
        {
            if (!s.EffectiveVisible) continue;
            maxN = Math.Max(maxN, s.Points.Count);
            if (longest == null || s.Points.Count > longest.Points.Count) longest = s;
        }

        ComputeYRange(series, out double yMin, out double yMax, out double tickSpacing);
        ComputeXRange(series, out double xMin, out double xMax);

        PlotWindow window = ApplyView(ReadView(el), maxN, ref xMin, ref xMax, ref yMin, ref yMax, ref tickSpacing);

        List<AxisTick> yTicks = ComputeYTicks(series, yMin, yMax, tickSpacing);
        List<AxisTick> xTicks = ComputeXTicks(series, xMin, xMax, window.BandFirst, window.BandSpan);

        using (_paper.Column(_id + "_chart_wrap").Enter())
        {
            if (_axes && !string.IsNullOrEmpty(_yLabel))
                Origami.Label(_paper, _id + "_chart_y_label", _yLabel)
                    .XS()
                    .AlignCenter()
                    .AlignLeft()
                    .Height(SwatchSize + 6f)
                    .Show();

            using (_paper.Row(_id + "_chart_row").Enter())
            {
                if (_axes)
                {
                    using (_paper.Column(_id + "_chart_y_gtr_c").Width(UnitValue.Auto).Enter())
                    {
                        using (_paper.Column(_id + "_chart_y_gtr").ChildLeft().Width(UnitValue.Auto).Enter())
                        {
                            for (int i = yTicks.Count - 1; i >= 0; i--)
                            {
                                if (i < yTicks.Count - 1)
                                {
                                    float pHeight = (float)(yTicks[i + 1].Position - yTicks[i].Position);
                                    _paper.Box($"{_id}_ytick_{i}_sp").Height(UnitValue.Percentage(pHeight * 100, -1));
                                }

                                using (_paper.Row($"{_id}_ytick_{i}").Height(1f).Width(UnitValue.Auto).ChildLeft().Enter())
                                {
                                    if (!string.IsNullOrEmpty(yTicks[i].Label))
                                        Origami.Label(_paper, $"{_id}_ytick_label_{i}", yTicks[i].Label).XS().Height(1).AlignCenter().Show();

                                    _paper.Box($"{_id}_ytick_tick_{i}").Height(1f).Width(4f).BackgroundColor(_theme.Ink.C500);
                                }

                            }

                            if (yTicks.Count > 0 && yTicks[0].Position > 0d)
                                _paper.Box(_id + "_ytick_trail_sp").Height(UnitValue.Percentage((float)yTicks[0].Position * 100));
                        }

                        float spacerHeight = !string.IsNullOrEmpty(_xLabel) ? 16 : 0;
                        spacerHeight += xTicks.Count > 0 ? 4 : 0;
                        spacerHeight += xTicks.Any(x => !string.IsNullOrEmpty(x.Label)) ? 14 : 0;

                        _paper.Box(_id + "_chart_y_gtr_sp").Height(spacerHeight);
                    }
                }

                using (_paper.Column(_id + "_chart_plot_col").Enter())
                {
                    DrawPlotBox(series, window, xTicks, yTicks, maxN, el);

                    if (_axes)
                    {
                        using (_paper.Row(_id + "_chart_x_gtr").ChildBottom().Height(UnitValue.Auto).Enter())
                        {
                            if (xTicks.Count > 0 && xTicks[0].Position > 0d)
                                _paper.Box(_id + "_xtick_lead_sp").Width(UnitValue.Percentage((float)xTicks[0].Position * 100));

                            for (int i = 0; i < xTicks.Count; i++)
                            {
                                using (_paper.Column($"{_id}_xtick_{i}").Height(UnitValue.Auto).Width(1f).ChildBottom().ChildRight().Enter())
                                {
                                    _paper.Box($"{_id}_xtick_tick_{i}").Height(4f).Width(1f).BackgroundColor(_theme.Ink.C500);

                                    if (!string.IsNullOrEmpty(xTicks[i].Label))
                                        Origami.Label(_paper, $"{_id}_xtick_label_{i}", xTicks[i].Label).XS().Width(1).Height(14).AlignCenter().Show();
                                }

                                if (i < xTicks.Count - 1)
                                {
                                    float pWidth = (float)(xTicks[i + 1].Position - xTicks[i].Position);
                                    _paper.Box($"{_id}_xtick_{i}_sp").Width(UnitValue.Percentage(pWidth * 100, -1));
                                }
                            }
                        }
                    }

                    if (_axes && !string.IsNullOrEmpty(_xLabel))
                        using (_paper.Row(_id + "_chart_x_label_center").Height(UnitValue.Auto).ChildLeft().ChildRight().Enter())
                            Origami.Label(_paper, _id + "_chart_x_label", _xLabel)
                                .XS()
                                .AlignCenter()
                                .Height(15)
                                .Show();
                }
            }
        }
    }

    private void DrawPlotBox(List<CartesianSeries<T>> series, PlotWindow window,
        List<AxisTick> xTicks, List<AxisTick> yTicks, int maxN, ElementHandle el)
    {
        double xMin = window.XMin, xMax = window.XMax;
        double yMin = window.YMin, yMax = window.YMax;
        double bandFirst = window.BandFirst, bandSpan = window.BandSpan;

        ElementBuilder plotBox = _paper.Box(_id + "_chart_plot").Clip();
        if (_sampleable)
            plotBox.Cursor(PaperCursor.Crosshair);

        using (plotBox.Enter())
        {
            ElementHandle chartEl = _paper.CurrentParent;

            if (_sampleable)
            {
                plotBox.OnHeld(e => UpdateSample(chartEl, series, window, e));
                plotBox.OnRelease(_ => ClearSample(chartEl));
            }

            if (_zoomable)
                plotBox.OnScroll(e => ApplyZoom(el, e));

            _paper.Draw((canvas, rect) =>
            {
                float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
                float w = (float)rect.Size.X, h = (float)rect.Size.Y;

                var ctx = new PlotContext<T>(ox, oy, ox + w, oy + h, xMin, xMax, yMin, yMax, series, maxN, xTicks, yTicks,
                    bandFirst, bandSpan);
                PaintChart(canvas, rect, in ctx, maxN);

                var info = new PlotInfo { PlotL = 0f, PlotT = 0f, PlotR = w, PlotB = h, MaxN = maxN };
                _paper.SetElementStorage(chartEl, "cartesian_plot", info);
            });

            PlotInfo lastInfo = _paper.GetElementStorage<PlotInfo>(chartEl, "cartesian_plot", default);
            bool havePlotRect = lastInfo.PlotR > lastInfo.PlotL;

            if (_pannable && havePlotRect)
                ApplyPan(el, lastInfo);

            if (_sampleable && havePlotRect)
            {
                int sampleIdx = _paper.GetElementStorage(chartEl, SampleIdxKey, -1);
                if (sampleIdx >= 0 && sampleIdx < maxN)
                {
                    var plot = new PlotContext<T>(lastInfo.PlotL, lastInfo.PlotT, lastInfo.PlotR, lastInfo.PlotB,
                        xMin, xMax, yMin, yMax, series, maxN, xTicks, yTicks, bandFirst, bandSpan);
                    Float2 pointer = _paper.GetElementStorage(chartEl, SamplePosKey, new Float2(0f, 0f));

                    DrawSampler(_paper, new SampleContext<T>(in plot, sampleIdx, pointer));
                }
            }
        }
    }

    /// <summary>Narrows the base axis range to the visible slice described by <paramref name="view"/>,
    /// leaving the range that <see cref="AutoFit"/>, <see cref="YRange"/>, <see cref="DeriveYRange"/>,
    /// <see cref="IncludeZero"/> and <see cref="MinSpan"/> produced untouched underneath it. Ticks are
    /// computed from the result, so they follow the viewport rather than the full data range.</summary>
    private PlotWindow ApplyView(ViewRect view, int maxN, ref double xMin, ref double xMax,
        ref double yMin, ref double yMax, ref double tickSpacing)
    {
        double baseXMin = xMin, xSpan = xMax - xMin;
        xMin = baseXMin + view.X * xSpan;
        xMax = baseXMin + (view.X + view.W) * xSpan;
        if (xMax <= xMin) xMax = xMin + 1d;

        double baseYMin = yMin, ySpan = yMax - yMin;
        yMin = baseYMin + view.Y * ySpan;
        yMax = baseYMin + (view.Y + view.H) * ySpan;
        if (yMax <= yMin) yMax = yMin + 1d;

        if (view.H < 1f && _scale != AxisScale.Log)
            tickSpacing = (yMax - yMin) / Math.Max(1, _yTicks - 1);

        return new PlotWindow(xMin, xMax, yMin, yMax, view.X * maxN, view.W * maxN);
    }

    private static ViewRect ClampView(ViewRect view)
    {
        view.W = Math.Clamp(view.W <= 0f ? 1f : view.W, MinViewSpan, 1f);
        view.H = Math.Clamp(view.H <= 0f ? 1f : view.H, MinViewSpan, 1f);
        view.X = Math.Clamp(view.X, 0f, 1f - view.W);
        view.Y = Math.Clamp(view.Y, 0f, 1f - view.H);
        return view;
    }

    private ViewRect ReadView(ElementHandle el) => ClampView(_paper.GetElementStorage(el, ViewRectKey, default(ViewRect)));

    private void WriteView(ElementHandle el, ViewRect view) => _paper.SetElementStorage(el, ViewRectKey, ClampView(view));

    /// <summary>Scroll-wheel zoom about the pointer. The normalized coordinate under the cursor is held
    /// fixed while the visible span shrinks or grows, so the value the pointer is over stays put.</summary>
    private void ApplyZoom(ElementHandle el, ScrollEvent e)
    {
        bool zoomX = ViewAxisX, zoomY = ViewAxisY;
        if (!zoomX && !zoomY) return;

        double w = e.ElementRect.Size.X, h = e.ElementRect.Size.Y;
        if (w <= 0d || h <= 0d) return;

        ViewRect view = ReadView(el);
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
            float fy = (float)Math.Clamp((e.ElementRect.Min.Y + h - e.PointerPosition.Y) / h, 0d, 1d);
            float nh = Math.Clamp(view.H * factor, MinViewSpan, 1f);
            view.Y += fy * (view.H - nh);
            view.H = nh;
        }

        WriteView(el, view);
        e.StopPropagation();
    }

    /// <summary>Middle-mouse drag pan. Paper's own drag events are left-button only, and the left button
    /// already drives the sampler, so this polls the middle button against the hovered plot instead.</summary>
    private void ApplyPan(ElementHandle el, PlotInfo info)
    {
        bool panX = ViewAxisX, panY = ViewAxisY;
        if (!panX && !panY) return;
        if (!_paper.IsPointerDown(PaperMouseBtn.Middle) || !_paper.IsParentHovered) return;

        float w = info.PlotR - info.PlotL, h = info.PlotB - info.PlotT;
        if (w <= 0f || h <= 0f) return;

        Float2 delta = _paper.PointerDelta;
        if (delta.X == 0f && delta.Y == 0f) return;

        ViewRect view = ReadView(el);
        if (panX) view.X -= (float)delta.X / w * view.W;
        if (panY) view.Y += (float)delta.Y / h * view.H;
        WriteView(el, view);
    }


    private List<CartesianSeries<T>> ResolveSeries()
    {
        var list = new List<CartesianSeries<T>>(_series);

        if (_xSelector != null && _ySelector != null && _data != null)
        {
            var s = new CartesianSeries<T> { Label = _pointSeriesName };
            if (_lastSeries == null) _lastSeries = s;
            foreach (T? item in _data)
                s.Points.Add((_xSelector(item), _ySelector(item), item));
            list.Add(s);
        }

        List<CartesianSeries<T>>? external = ExternalSeries;
        if (external != null) list.AddRange(external);

        Color defaultColor = Ramp.C500;
        foreach (CartesianSeries<T> s in list)
            if (s.Color == null) s.Color = defaultColor;

        return list;
    }


    private void UpdateSample(ElementHandle el, List<CartesianSeries<T>> series, PlotWindow window, ClickEvent e)
    {
        if (e.RelativePosition.X < 0f || e.RelativePosition.X > e.ElementRect.Size.X ||
            e.RelativePosition.Y < 0f || e.RelativePosition.Y > e.ElementRect.Size.Y)
        {
            ClearSample(el);
            return;
        }

        PlotInfo info = _paper.GetElementStorage<PlotInfo>(el, "cartesian_plot", default);
        if (info.MaxN <= 0 || info.PlotR <= info.PlotL)
        {
            ClearSample(el);
            return;
        }

        Float2 pointer = e.RelativePosition;

        int idx = NearestIndexAt(series, in info, pointer, window);
        if (idx < 0) { ClearSample(el); return; }

        _paper.SetElementStorage(el, SampleIdxKey, idx);
        _paper.SetElementStorage(el, SamplePosKey, pointer);
    }

    private void ClearSample(ElementHandle el) => _paper.SetElementStorage(el, SampleIdxKey, -1);

    /// <summary>Index of the point the pointer is sampling. Banded charts take the band the pointer
    /// falls in; everything else takes the point nearest in pixel space, which unlike a
    /// position-along-the-axis guess also holds for series whose x values are unevenly spaced.</summary>
    private int NearestIndexAt(List<CartesianSeries<T>> series, in PlotInfo info, Float2 pointer, PlotWindow window)
    {
        if (info.MaxN <= 0 || info.PlotR <= info.PlotL) return -1;

        float px = Math.Clamp(pointer.X, info.PlotL, info.PlotR);
        float py = Math.Clamp(pointer.Y, info.PlotT, info.PlotB);

        if (BandedX)
        {
            float frac = (px - info.PlotL) / (info.PlotR - info.PlotL);
            double bandSpan = window.BandSpan > 0d ? window.BandSpan : info.MaxN;
            return Math.Clamp((int)(window.BandFirst + frac * bandSpan), 0, info.MaxN - 1);
        }

        CartesianSeries<T>? longest = LongestVisible(series);
        if (longest == null || longest.Points.Count == 0) return -1;

        double xMin = window.XMin;
        double xSpan = window.XMax - window.XMin; if (xSpan <= 0d) xSpan = 1d;

        double yMin = window.YMin;
        double ySpan = window.YMax - window.YMin; if (ySpan <= 0d) ySpan = 1d;

        bool nearest2D = SampleNearest2D;
        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < longest.Points.Count; i++)
        {
            (double x, double y, T? _) = longest.Points[i];
            if (double.IsNaN(x) || double.IsInfinity(x)) continue;

            float dx = AxisPos(x, xMin, xSpan, info.PlotL, info.PlotR) - px;
            float dist = MathF.Abs(dx);

            if (nearest2D)
            {
                if (double.IsNaN(y) || double.IsInfinity(y)) continue;
                float dy = AxisPos(y, yMin, ySpan, info.PlotB, info.PlotT) - py;
                dist = MathF.Sqrt(dx * dx + dy * dy);
            }

            if (dist < bestDist) { bestDist = dist; best = i; }
        }

        return best;
    }

    /// <summary>The visible series carrying the most points. This is the series whose x values define
    /// the sampled index, so a sampler anchors its crosshair to it.</summary>
    protected static CartesianSeries<T>? LongestVisible(IReadOnlyList<CartesianSeries<T>> series)
    {
        CartesianSeries<T>? longest = null;
        foreach (CartesianSeries<T> s in series)
            if (s.EffectiveVisible && (longest == null || s.Points.Count > longest.Points.Count))
                longest = s;
        return longest;
    }

    private struct PlotInfo
    {
        public float PlotL, PlotT, PlotR, PlotB;
        public int MaxN;
    }

    /// <summary>Maps a data value in [min, min+span] to a pixel coordinate in [pxA, pxB]. Passing
    /// (plotB, plotT) instead of (plotT, plotB) flips the mapping, which is how this doubles as
    /// both the X and Y axis mapping.</summary>
    private static float AxisPos(double v, double min, double span, float pxA, float pxB)
        => pxA + (float)((v - min) / span) * (pxB - pxA);

    private void ComputeXRange(IReadOnlyList<CartesianSeries<T>> series, out double xMin, out double xMax)
    {
        xMin = double.MaxValue;
        xMax = double.MinValue;
        foreach (CartesianSeries<T> s in series)
        {
            if (!s.EffectiveVisible) continue;
            foreach ((double x, double _, T? _) in s.Points)
            {
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
            }
        }
        if (xMin > xMax) { xMin = 0d; xMax = 1d; }
        if (xMax <= xMin) xMax = xMin + 1d;
    }

    private void ComputeYRange(IReadOnlyList<CartesianSeries<T>> series, out double yMin, out double yMax, out double tickSpacing)
    {
        bool useExplicitRange = (_hasYRange || _derivedYRange) && !_autoFit;

        if (useExplicitRange)
        {
            yMin = _yRangeMin;
            yMax = _yRangeMax;
        }
        else
        {
            yMin = double.MaxValue;
            yMax = double.MinValue;
            foreach (CartesianSeries<T> s in series)
            {
                if (!s.EffectiveVisible) continue;
                foreach ((double _, double y, T? _) in s.Points)
                {
                    if (double.IsNaN(y) || double.IsInfinity(y)) continue;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }
            if (yMin > yMax) { yMin = 0d; yMax = 1d; }
            if (_includeZero && _scale == AxisScale.Linear) { yMin = Math.Min(yMin, 0d); yMax = Math.Max(yMax, 0d); }
            if (_minSpan > 0d && yMax - yMin < _minSpan) yMax = yMin + _minSpan;
        }

        if (_scale == AxisScale.Log)
        {
            yMin = Math.Max(yMin, 1e-6);
            yMax = Math.Max(yMax, yMin * 10d);
            tickSpacing = 0d;
            return;
        }

        if (yMax <= yMin) yMax = yMin + 1d;

        int ticks = _yTicks;
        if (!useExplicitRange)
        {
            double niceRange = NiceNum(yMax - yMin, false);
            tickSpacing = NiceNum(niceRange / Math.Max(1, ticks - 1), true);
            if (tickSpacing > 0d)
            {
                yMin = Math.Floor(yMin / tickSpacing) * tickSpacing;
                yMax = Math.Ceiling(yMax / tickSpacing) * tickSpacing;
            }
            if (yMax <= yMin) yMax = yMin + 1d;
        }
        else
        {
            tickSpacing = (yMax - yMin) / Math.Max(1, ticks - 1);
        }
    }

    private static double NiceNum(double range, bool round)
    {
        if (range <= 0d || double.IsNaN(range) || double.IsInfinity(range)) return 0d;
        double exp = Math.Floor(Math.Log10(range));
        double f = range / Math.Pow(10d, exp);
        double nf;
        if (round)
            nf = f < 1.5d ? 1d : f < 3d ? 2d : f < 7d ? 5d : 10d;
        else
            nf = f <= 1d ? 1d : f <= 2d ? 2d : f <= 5d ? 5d : 10d;
        return nf * Math.Pow(10d, exp);
    }

    private static List<double> LogTicks(double yMin, double yMax)
    {
        var ticks = new List<double>();
        int startExp = (int)Math.Floor(Math.Log10(yMin));
        int endExp = (int)Math.Ceiling(Math.Log10(yMax));
        for (int e = startExp; e <= endExp; e++)
        {
            double v = Math.Pow(10d, e);
            if (v >= yMin * 0.999 && v <= yMax * 1.001) ticks.Add(v);
        }
        return ticks;
    }

    private string Format(double v) => _valueFormatter != null ? _valueFormatter(v) : v.ToString("0.###");

    private void PaintChart(Canvas canvas, Rect rect, in PlotContext<T> ctx, int maxN)
    {
        float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
        float width = (float)rect.Size.X, height = (float)rect.Size.Y;
        if (width < 4f || height < 4f) return;

        DrawGrid(canvas, ox, oy, ox + width, oy + height, width, height, in ctx, maxN);
        PaintMarks(canvas, in ctx);
    }

    private static List<double> BuildLinearTicks(double yMin, double yMax, double tickSpacing)
    {
        var tickVals = new List<double>();
        if (tickSpacing > 0d)
        {
            int guard = 0;
            for (double v = yMin; v <= yMax + tickSpacing * 1e-6 && guard < 1000; v += tickSpacing, guard++)
                tickVals.Add(v);
        }
        return tickVals;
    }

    private void DrawGrid(Canvas canvas, float plotL, float plotT, float plotR, float plotB,
        float plotW, float plotH, in PlotContext<T> ctx, int maxN)
    {
        if (_gridMode != GridMode.None)
        {
            List<float> xLines, yLines;

            if (_gridMode == GridMode.FixedCount)
            {
                float spacingY = MathF.Max(1f, plotH / _gridCountY);
                float spacingX = MathF.Max(1f, _gridCountX > 0 ? plotW / _gridCountX : spacingY);

                xLines = new List<float>();
                int guard = 0;
                for (float x = plotL; x <= plotR + 0.5f && guard < 1000; x += spacingX, guard++)
                    xLines.Add(x);

                yLines = new List<float>();
                guard = 0;
                for (float y = plotT; y <= plotB + 0.5f && guard < 1000; y += spacingY, guard++)
                    yLines.Add(y);
            }
            else
            {
                int ratioY = Math.Max(1, _gridRatioY);
                int ratioX = _gridRatioX > 0 ? _gridRatioX : ratioY;

                yLines = GridLinePositionsPx(ctx.YTicks, ratioY, plotB, plotT);
                xLines = GridLinePositionsPx(ctx.XTicks, ratioX, plotL, plotR);
            }

            Color gridLineColor = _gridLineColor ?? _theme.BorderSoft;
            Color32 gridCol = _gridLineColor.HasValue ? ToC32(gridLineColor) : ToC32(gridLineColor, 0.6f);

            canvas.SetStrokeColor(gridCol);
            canvas.SetStrokeWidth(1f);

            foreach (float x in xLines)
            {
                canvas.BeginPath();
                canvas.MoveTo(x, plotT);
                canvas.LineTo(x, plotB);
                canvas.Stroke();
            }

            foreach (float y in yLines)
            {
                canvas.BeginPath();
                canvas.MoveTo(plotL, y);
                canvas.LineTo(plotR, y);
                canvas.Stroke();
            }
        }

        Color32 axisCol = ToC32(_theme.Ink.C300);
        canvas.SetStrokeColor(axisCol);
        canvas.SetStrokeWidth(1f);

        canvas.BeginPath();
        canvas.MoveTo(plotL, plotT);
        canvas.LineTo(plotL, plotB);
        canvas.Stroke();

        canvas.BeginPath();
        canvas.MoveTo(plotL, plotB);
        canvas.LineTo(plotR, plotB);
        canvas.Stroke();
    }

    private static List<float> GridLinePositionsPx(IReadOnlyList<AxisTick> ticks, int ratio, float pxA, float pxB)
    {
        var positions = new List<float>();
        if (ticks.Count == 0) return positions;

        if (ticks.Count == 1)
        {
            positions.Add(pxA + (float)ticks[0].Position * (pxB - pxA));
            return positions;
        }

        double baseStep = ticks[1].Position - ticks[0].Position;
        if (baseStep <= 1e-9)
        {
            foreach (AxisTick t in ticks)
                positions.Add(pxA + (float)t.Position * (pxB - pxA));
            return positions;
        }

        int n = Math.Max(1, ratio);
        double subStep = baseStep / n;

        double start = ticks[0].Position;
        int backGuard = 0;
        while (start - subStep > -1e-9 && backGuard < 1000) { start -= subStep; backGuard++; }

        int guard = 0;
        for (double v = start; v <= 1.0 + 1e-9 && guard < 1000; v += subStep, guard++)
        {
            if (v >= -1e-9)
                positions.Add(pxA + (float)v * (pxB - pxA));
        }
        return positions;
    }

    // ── Sampler building blocks ─────────────────────────────────

    /// <summary>Colour of the sampler's crosshair and band highlight.</summary>
    protected Color SampleColor => _sampleLineColor ?? _theme.Ink.C500;

    /// <summary>Formats a value the way this chart's axis labels and legend do, honouring
    /// <see cref="ValueFormatter"/>.</summary>
    protected string FormatValue(double v) => Format(v);

    /// <summary>Label for the sampled index, from <see cref="XTickFormatter"/> if one is set. This is
    /// what a sampler popup uses as its header.</summary>
    protected string SampleHeader(int index) => _xTickFormatter != null ? (_xTickFormatter(index) ?? "") : $"Index {index}";

    /// <summary>Vertical crosshair spanning the plot at pixel <paramref name="x"/>.</summary>
    protected void SampleLine(Paper paper, in SampleContext<T> ctx, float x)
    {
        paper.Box(_id + "_sampler_vline")
            .PositionType(PositionType.SelfDirected)
            .Position(x - 0.5f, ctx.PlotT)
            .Size(1f, ctx.PlotB - ctx.PlotT)
            .BackgroundColor(SampleColor);
    }

    /// <summary>Horizontal crosshair spanning the plot at pixel <paramref name="y"/>.</summary>
    protected void SampleLineH(Paper paper, in SampleContext<T> ctx, float y)
    {
        paper.Box(_id + "_sampler_hline")
            .PositionType(PositionType.SelfDirected)
            .Position(ctx.PlotL, y - 0.5f)
            .Size(ctx.PlotR - ctx.PlotL, 1f)
            .BackgroundColor(SampleColor);
    }

    /// <summary>Translucent full-height highlight over a horizontal slice of the plot. Banded chart
    /// types use this in place of a crosshair, since the sample covers a whole band rather than a
    /// single x.</summary>
    protected void SampleBand(Paper paper, in SampleContext<T> ctx, float left, float width)
    {
        if (width <= 0f) return;

        Color c = SampleColor;

        paper.Box(_id + "_sampler_band")
            .PositionType(PositionType.SelfDirected)
            .Position(left, ctx.PlotT)
            .Size(width, ctx.PlotB - ctx.PlotT)
            .BackgroundColor(System.Drawing.Color.FromArgb(c.A / 5, c));
    }

    /// <summary>Marker dot centred on a sampled mark. <paramref name="key"/> must be unique among the
    /// dots one sampler pass emits.</summary>
    protected void SampleDot(Paper paper, string key, float x, float y, Color color, float diameter = 6f)
    {
        float r = diameter * 0.5f;

        paper.Box($"{_id}_sampler_dot_{key}")
            .PositionType(PositionType.SelfDirected)
            .Position(x - r, y - r)
            .Size(diameter)
            .Rounded(r)
            .BackgroundColor(color);
    }

    /// <summary>Hollow ring around a sampled mark, for chart types whose marks are already filled
    /// shapes big enough that a dot would disappear inside them.</summary>
    protected void SampleRing(Paper paper, string key, float x, float y, float diameter, Color color)
    {
        float r = MathF.Max(3f, diameter * 0.5f) + 2f;

        paper.Box($"{_id}_sampler_ring_{key}")
            .PositionType(PositionType.SelfDirected)
            .Position(x - r, y - r)
            .Size(r * 2f)
            .Rounded(r)
            .BorderColor(color).BorderWidth(1.5f);
    }

    /// <summary>Readout panel listing the sampled values, anchored beside <paramref name="anchorX"/>.
    /// It flips to the other side of the anchor when it would overhang the plot's right edge, using the
    /// width it laid out at on the previous frame.</summary>
    protected void SamplePopup(Paper paper, in SampleContext<T> ctx, float anchorX, string header, IReadOnlyList<(Color Color, string Text)> rows)
    {
        if (header.Length == 0 && rows.Count == 0) return;

        string widthKey = _id + "_sampler_popup_w";
        float lastWidth = paper.GetRootStorage<float>(widthKey);

        float x = anchorX + SamplePopupGap;
        if (lastWidth > 0f && x + lastWidth > ctx.PlotR)
            x = MathF.Max(ctx.PlotL, anchorX - SamplePopupGap - lastWidth);

        ElementBuilder popup = paper.Column(_id + "_sampler_popup")
            .PositionType(PositionType.SelfDirected)
            .Position(x, ctx.PlotT)
            .Size(UnitValue.Auto)
            .BackgroundColor(_theme.Popover)
            .BorderColor(_theme.BorderStrong).BorderWidth(1f)
            .Rounded(6f)
            .Padding(6f)
            .ColBetween(6f)
            .ChildBottom()
            .Layer(Layer.Topmost + 1000)
            .OnPostLayout((_, rect) => paper.SetRootStorage(widthKey, (float)rect.Size.X));

        using (popup.Enter())
        {
            if (header.Length > 0)
                Origami.Label(paper, $"{_id}_sampler_hdr", header)
                    .XS()
                    .AlignCenter()
                    .AlignLeft()
                    .Height(SwatchSize)
                    .Show();

            for (int i = 0; i < rows.Count; i++)
            {
                (Color color, string text) = rows[i];

                using (paper.Row($"{_id}_sampler_row_{i}").Height(SwatchSize).Width(UnitValue.Auto).RowBetween(2f).Enter())
                {
                    paper.Box($"{_id}_sampler_sw_{i}").Size(SwatchSize).BackgroundColor(color).Rounded(2f);

                    Origami.Label(paper, $"{_id}_sampler_txt_{i}", text)
                        .XS()
                        .AlignCenter()
                        .AlignLeft()
                        .Height(SwatchSize)
                        .Show();
                }
            }
        }
    }

    private const float SamplePopupGap = 8f;
}
