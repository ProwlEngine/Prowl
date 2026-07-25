// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// One line in a <see cref="ChartBuilder"/>. Fully data-agnostic: the widget reads only a
/// <see cref="Label"/>, a <see cref="Color"/>, a flat list of <see cref="Values"/> (one sample per
/// x index) and whether to <see cref="Fill"/> the area beneath the line. Overlay several series on a
/// single chart by adding more than one; they share the chart's x and y axes.
/// </summary>
public sealed class ChartSeries
{
    /// <summary>Series name, shown in the legend.</summary>
    public string Label = "";
    /// <summary>Line/area colour. When left at its default (alpha 0) the chart substitutes the
    /// builder's variant colour.</summary>
    public Color Color;
    /// <summary>Y samples in x order. Index maps to the x axis; the value maps to the y axis.</summary>
    public IReadOnlyList<double> Values = Array.Empty<double>();
    /// <summary>Fill the area under this series with a translucent wash beneath the stroked line.</summary>
    public bool Fill;

    public ChartSeries() { }

    public ChartSeries(string label, Color color, IReadOnlyList<double> values)
    {
        Label = label;
        Color = color;
        Values = values ?? Array.Empty<double>();
    }
}

/// <summary>
/// Fluent builder for a generic, data-agnostic multi-series line chart. Plots one or more
/// <see cref="ChartSeries"/> against a shared, auto-ranged (or explicit) y axis and an index-based
/// x axis, with optional grid, ticks, axis titles and a per-series legend. The widget owns its own
/// layout: it reserves gutters for the axis chrome and clips every draw to its box so it never
/// overflows its container. References no host-specific or render-specific types.
///
/// Construct via <see cref="Origami.Chart"/>; chain modifiers; call <see cref="Show"/> to render.
/// </summary>
public sealed class ChartBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private UnitValue _width = UnitValue.Stretch();
    private float _height = 220f;
    private readonly List<ChartSeries> _series = new();

    private bool _hasYRange;
    private double _yMin, _yMax;
    private double _minSpan;
    private bool _includeZero = true;
    private Func<double, string>? _valueFormatter;
    private Func<int, string>? _xTickFormatter;
    private string _xLabel = "";
    private string _yLabel = "";
    private int _yTicks = 4;
    private bool _axes = true;
    private bool _legend = true;
    private bool _legendShowValue = true;
    private float _legendFontSize = 11f;
    private float _axisFontSize = 11f;
    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private Color? _backgroundColor;
    private float _yLabelMinWidth = 0f;
    private float _yLabelMaxWidth = float.MaxValue;
    private float _padding = 0f;

    private bool _sampleable;
    private Color? _sampleLineColor;

    private enum ChartGridMode { None, FixedCount, TickRatio }
    private ChartGridMode _gridMode = ChartGridMode.None;
    private int _gridCountX = -1;
    private int _gridCountY = 4;
    private float _gridRatioX = float.NaN;
    private float _gridRatioY = 1f;
    private Color? _gridLineColor;

    internal ChartBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>Fix the overall widget size to an explicit pixel width and height.</summary>
    public ChartBuilder Size(float width, float height) { _width = MathF.Max(32f, width); _height = MathF.Max(32f, height); return this; }
    /// <summary>Fix the widget width in pixels. Default inherits the parent's width (<see cref="UnitValue.Stretch"/>).</summary>
    public ChartBuilder Width(float width) { _width = MathF.Max(32f, width); return this; }
    /// <summary>Set the widget width as a layout unit (e.g. <see cref="UnitValue.Stretch"/> to inherit the parent's width, or <see cref="UnitValue.Percentage"/>). Default is Stretch.</summary>
    public ChartBuilder Width(UnitValue width) { _width = width; return this; }
    /// <summary>Overall widget height in pixels.</summary>
    public ChartBuilder Height(float height) { _height = MathF.Max(32f, height); return this; }

    /// <summary>Add a series to overlay on the chart. Call repeatedly for multiple lines.</summary>
    public ChartBuilder Series(ChartSeries s) { if (s != null) _series.Add(s); return this; }

    /// <summary>Add a series from its parts. Call repeatedly for multiple lines.</summary>
    public ChartBuilder Series(string label, Color color, IReadOnlyList<double> values, bool fill = false)
    {
        _series.Add(new ChartSeries(label, color, values) { Fill = fill });
        return this;
    }

    /// <summary>Pin the y axis to an explicit range. Overrides auto-ranging, IncludeZero and MinSpan.</summary>
    public ChartBuilder YRange(double min, double max) { _hasYRange = true; _yMin = Math.Min(min, max); _yMax = Math.Max(min, max); return this; }

    /// <summary>Force the y axis to span at least this many units, so a near-flat series is not drawn
    /// as a smear across the middle. Default 0 (off). Ignored when an explicit <see cref="YRange"/> is set.</summary>
    public ChartBuilder MinSpan(double span) { _minSpan = Math.Max(0d, span); return this; }

    /// <summary>Force the auto y range to include 0. Default true, which suits counters. Ignored when
    /// an explicit <see cref="YRange"/> is set.</summary>
    public ChartBuilder IncludeZero(bool include = true) { _includeZero = include; return this; }

    /// <summary>Format a y value for tick labels and the legend's latest-value readout.
    /// Default is a plain numeric string.</summary>
    public ChartBuilder ValueFormatter(Func<double, string> formatter) { _valueFormatter = formatter; return this; }

    /// <summary>Title drawn centred under the x axis.</summary>
    public ChartBuilder XLabel(string text) { _xLabel = text ?? ""; return this; }

    /// <summary>Title drawn at the top-left of the y axis.</summary>
    public ChartBuilder YLabel(string text) { _yLabel = text ?? ""; return this; }

    /// <summary>Map a sample index to an x tick label. Default none (tick marks without labels).</summary>
    public ChartBuilder XTickFormatter(Func<int, string> formatter) { _xTickFormatter = formatter; return this; }

    /// <summary>Approximate number of y tick divisions. Default 4. Clamped to at least 2.</summary>
    public ChartBuilder YTicks(int count) { _yTicks = Math.Max(2, count); return this; }

    /// <summary>Clamp the reserved width, in pixels, for y-axis tick label text. The column never
    /// shrinks below <paramref name="min"/> even for short labels, and never grows past
    /// <paramref name="max"/>; a label wider than <paramref name="max"/> is clipped to the column.
    /// Default is unclamped (grows to fit the widest label).</summary>
    public ChartBuilder YLabelWidth(float min, float max)
    {
        _yLabelMinWidth = MathF.Max(0f, min);
        _yLabelMaxWidth = MathF.Max(_yLabelMinWidth, max);
        return this;
    }

    /// <summary>Draw axis lines, tick marks and tick labels. When false, render a bare sparkline.
    /// Default on.</summary>
    public ChartBuilder Axes(bool show = true) { _axes = show; return this; }

    /// <summary>Draw a legend row (swatch + label + latest value) per series. Default on.</summary>
    public ChartBuilder Legend(bool show = true) { _legend = show; return this; }

    /// <summary>Show the latest value readout next to each legend label. Default on.</summary>
    public ChartBuilder LegendShowValue(bool show = true) { _legendShowValue = show; return this; }

    /// <summary>Font size, in pixels, of the legend row text (swatch labels and latest-value readout).
    /// Default 11.</summary>
    public ChartBuilder LegendFontSize(float size) { _legendFontSize = MathF.Max(1f, size); return this; }

    /// <summary>Font size, in pixels, of the axis chrome text (y/x tick labels and axis titles).
    /// Default 11.</summary>
    public ChartBuilder AxisFontSize(float size) { _axisFontSize = MathF.Max(1f, size); return this; }

    /// <summary>Background colour of the chart panel. Default is none (transparent), matching bare widgets.</summary>
    public ChartBuilder BackgroundColor(Color color) { _backgroundColor = color; return this; }

    /// <summary>Inset the chart contents by this many pixels on every side of the box. Default 0, so
    /// every label/tick sits directly against the border.</summary>
    public ChartBuilder Padding(float padding) { _padding = MathF.Max(0f, padding); return this; }

    /// <summary>Let the user click or drag inside the plot area to sample a point. Draws a vertical
    /// line at the sampled x index and a tooltip-style popup listing every series' value there.
    /// Default off.</summary>
    public ChartBuilder Sampleable(bool enable = true) { _sampleable = enable; return this; }

    /// <summary>Colour of the sample line drawn by <see cref="Sampleable"/>. Default reads the theme's
    /// ink colour, so most callers never need to set this.</summary>
    public ChartBuilder SampleLineColor(Color color) { _sampleLineColor = color; return this; }

    /// <summary>Colour of the grid lines drawn by <see cref="GridLines(int)"/> / <see cref="GridTickLines(float)"/>.
    /// Default reads the theme's soft border colour, so most callers never need to set this.</summary>
    public ChartBuilder GridLineColor(Color color) { _gridLineColor = color; return this; }

    /// <summary>Draw a fixed grid with <paramref name="countY"/> horizontal divisions; the vertical
    /// division count is derived automatically so grid cells come out square in pixel space.</summary>
    public ChartBuilder GridLines(int countY)
    {
        _gridMode = ChartGridMode.FixedCount;
        _gridCountY = Math.Max(1, countY);
        _gridCountX = -1;
        return this;
    }

    /// <summary>Draw a fixed grid with an explicit division count on each axis.</summary>
    public ChartBuilder GridLines(int countX, int countY)
    {
        _gridMode = ChartGridMode.FixedCount;
        _gridCountX = Math.Max(1, countX);
        _gridCountY = Math.Max(1, countY);
        return this;
    }

    /// <summary>Draw a grid that follows the y-axis ticks, with <paramref name="ratioY"/> grid lines
    /// per y-tick (2 = twice as many lines, 0.5 = half as many). The x division count is derived
    /// automatically so grid cells come out square in pixel space.</summary>
    public ChartBuilder GridTickLines(float ratioY)
    {
        _gridMode = ChartGridMode.TickRatio;
        _gridRatioY = MathF.Max(0.01f, ratioY);
        _gridRatioX = float.NaN;
        return this;
    }

    /// <summary>Draw a grid that follows both axes' ticks, with an explicit lines-per-tick ratio on
    /// each axis (2 = twice as many lines, 0.5 = half as many).</summary>
    public ChartBuilder GridTickLines(float ratioX, float ratioY)
    {
        _gridMode = ChartGridMode.TickRatio;
        _gridRatioX = MathF.Max(0.01f, ratioX);
        _gridRatioY = MathF.Max(0.01f, ratioY);
        return this;
    }

    /// <summary>Default single-series colour used when a series has no explicit colour of its own.</summary>
    public ChartBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public ChartBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ChartBuilder Success() => Variant(OrigamiVariant.Success);
    public ChartBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ChartBuilder Danger() => Variant(OrigamiVariant.Danger);
    public ChartBuilder Info() => Variant(OrigamiVariant.Info);

    private const float TickLen = 4f;
    private const float TickLabelPad = 4f;
    private const float EdgePad = 6f;
    private const float SwatchSize = 9f;
    private const float TextClipMargin = 4f;

    private const string PlotInfoKey = "chart_plot";
    private const string SampleIdxKey = "chart_sample";

    /// <summary>Render the chart.</summary>
    public void Show()
    {
        Color defaultColor = _theme.Get(_variant).C500;
        var resolved = new List<ChartSeries>(_series.Count);
        foreach (var s in _series)
        {
            if (s == null) continue;
            var use = s;
            if (s.Color.A == 0)
                use = new ChartSeries(s.Label, defaultColor, s.Values) { Fill = s.Fill };
            resolved.Add(use);
        }

        Color gridLineColor = _gridLineColor ?? _theme.BorderSoft;

        var snap = new Snapshot
        {
            Series = resolved,
            Font = _theme.Font,
            Axes = _axes,
            Legend = _legend,
            LegendShowValue = _legendShowValue,
            LegendFs = _legendFontSize,
            AxisFs = _axisFontSize,
            YTicks = Math.Max(2, _yTicks),
            XLabel = _xLabel,
            YLabel = _yLabel,
            HasYRange = _hasYRange,
            YMinFixed = _yMin,
            YMaxFixed = _yMax,
            MinSpan = _minSpan,
            IncludeZero = _includeZero,
            Formatter = _valueFormatter,
            XTickFormatter = _xTickFormatter,
            GridCol = ToC32(_theme.BorderSoft, 0.6f),
            AxisCol = ToC32(_theme.Ink.C300, 0.9f),
            TickTextCol = ToC32(_theme.Ink.C400),
            LabelTextCol = ToC32(_theme.Ink.C500),
            YLabelMinWidth = _yLabelMinWidth,
            YLabelMaxWidth = _yLabelMaxWidth,
            Padding = _padding,
            Sampleable = _sampleable,
            SampleLineCol = ToC32(_sampleLineColor ?? _theme.Ink.C500),
            PopoverCol = ToC32(_theme.Popover),
            BorderStrongCol = ToC32(_theme.BorderStrong),
            Grid = _gridMode,
            GridCountX = _gridCountX,
            GridCountY = _gridCountY,
            GridRatioX = _gridRatioX,
            GridRatioY = _gridRatioY,
            GridLineCol = _gridLineColor.HasValue ? ToC32(gridLineColor) : ToC32(gridLineColor, 0.6f),
        };

        var box = _paper.Box(_id)
            .Width(_width).Height(_height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(_theme.BorderSoft).BorderWidth(1f)
            .Clip();
        if (_sampleable) box.Cursor(PaperCursor.Crosshair); else box.IsNotInteractable();
        if (_backgroundColor.HasValue) box.BackgroundColor(_backgroundColor.Value);

        using (box.Enter())
        {
            ElementHandle el = _paper.CurrentParent;

            if (_sampleable)
            {
                box.OnHeld(e => UpdateSample(el, e));
                box.OnRelease(e => ClearSample(el));
            }

            _paper.Draw((canvas, rect) =>
            {
                PlotInfo info = Paint(canvas, rect, in snap);
                if (_sampleable)
                {
                    _paper.SetElementStorage(el, PlotInfoKey, info);

                    // Drawn here, inside the box's own (clipped) draw commands, so the sample line
                    // and per-series dots are scissored to the chart's bounds like the rest of the
                    // plot. Only the tooltip popup below needs to escape that clip.
                    int sampleIdx = _paper.GetElementStorage(el, SampleIdxKey, -1);
                    if (sampleIdx >= 0 && sampleIdx < info.MaxN)
                        PaintSampleMarkers(canvas, in snap, rect, in info, sampleIdx);
                }
            });

            // The sample popup renders on its own zero-size, Topmost-layer child rather than as
            // part of the box's own draw commands above: the box has Clip() enabled, so anything
            // painted inside its own render commands is scissored to its bounds. A higher-layer
            // element's rendering is deferred past the normal pass (see Paper's layer breakout),
            // so it paints without inheriting that scissor - letting the popup overflow onto
            // neighbouring charts/panels instead of being cut off at this chart's edge. Positioned
            // at (0,0) self-directed within `box` with no padding of its own, its LayoutRect origin
            // lands exactly on the box's, so PlotInfo's box-relative coordinates still apply as-is.
            if (_sampleable)
            {
                var overlay = _paper.Box(_id + "_sample")
                    .PositionType(PositionType.SelfDirected)
                    .Position(0, 0)
                    .Width(0).Height(0)
                    .IsNotInteractable()
                    .Layer(Layer.Topmost);

                using (overlay.Enter())
                {
                    _paper.Draw((canvas, r) =>
                    {
                        int sampleIdx = _paper.GetElementStorage(el, SampleIdxKey, -1);
                        PlotInfo info = _paper.GetElementStorage<PlotInfo>(el, PlotInfoKey, default);
                        if (sampleIdx >= 0 && sampleIdx < info.MaxN)
                            PaintSamplePopup(canvas, in snap, r.Min.X, r.Min.Y, in info, sampleIdx, _legendFontSize, _paper);
                    });
                }
            }
        }
    }

    private void UpdateSample(ElementHandle el, ElementEvent e)
    {
        // OnHeld keeps firing for the pressed element even once the pointer strays outside its
        // bounds (e.g. dragging past the chart's edge) - hide the sampler in that case instead of
        // clamping the reading to the nearest edge, so it disappears the moment the mouse leaves.
        if (e.RelativePosition.X < 0f || e.RelativePosition.X > e.ElementRect.Size.X ||
            e.RelativePosition.Y < 0f || e.RelativePosition.Y > e.ElementRect.Size.Y)
        {
            ClearSample(el);
            return;
        }

        PlotInfo info = _paper.GetElementStorage<PlotInfo>(el, PlotInfoKey, default);
        if (info.MaxN <= 0 || info.PlotR <= info.PlotL)
        {
            ClearSample(el);
            return;
        }

        float rel = (float)e.RelativePosition.X;
        float clamped = Math.Clamp(rel, info.PlotL, info.PlotR);
        float frac = (clamped - info.PlotL) / (info.PlotR - info.PlotL);
        int idx = info.MaxN <= 1 ? 0 : (int)Math.Round(frac * (info.MaxN - 1));
        idx = Math.Clamp(idx, 0, info.MaxN - 1);
        _paper.SetElementStorage(el, SampleIdxKey, idx);
    }

    private void ClearSample(ElementHandle el) => _paper.SetElementStorage(el, SampleIdxKey, -1);

    private struct PlotInfo
    {
        public float PlotL, PlotT, PlotR, PlotB;
        public int MaxN;
    }

    private struct Snapshot
    {
        public List<ChartSeries> Series;
        public float Width, Height;
        public FontFile? Font;
        public bool Axes, Legend, LegendShowValue, HasYRange, IncludeZero;
        public float LegendFs, AxisFs;
        public int YTicks;
        public string XLabel, YLabel;
        public double YMinFixed, YMaxFixed, MinSpan;
        public Func<double, string>? Formatter;
        public Func<int, string>? XTickFormatter;
        public Color32 GridCol, AxisCol, TickTextCol, LabelTextCol;
        public float YLabelMinWidth, YLabelMaxWidth;
        public float Padding;
        public bool Sampleable;
        public Color32 SampleLineCol, PopoverCol, BorderStrongCol;
        public ChartGridMode Grid;
        public int GridCountX, GridCountY;
        public float GridRatioX, GridRatioY;
        public Color32 GridLineCol;
    }

    private static PlotInfo Paint(Canvas canvas, Rect rect, in Snapshot sIn)
    {
        Snapshot s = sIn;
        float pad = Math.Min(s.Padding, MathF.Min((float)rect.Size.X, (float)rect.Size.Y) * 0.5f);
        s.Width = (float)rect.Size.X - pad * 2f;
        s.Height = (float)rect.Size.Y - pad * 2f;

        float ox = (float)rect.Min.X + pad, oy = (float)rect.Min.Y + pad;
        float legendFs = s.LegendFs;
        float axisFs = s.AxisFs;
        float legendLineH = 13f;
        float axisLineH = 13f;
        if (s.Font != null)
        {
            legendLineH = MathF.Max(9f, (float)canvas.MeasureText("0", legendFs, s.Font).Y);
            axisLineH = MathF.Max(9f, (float)canvas.MeasureText("0", axisFs, s.Font).Y);
        }

        canvas.SaveState();
        canvas.IntersectScissor(ox, oy, s.Width, s.Height);

        ComputeYRange(in s, out double yMin, out double yMax, out double tickSpacing);

        var tickVals = new List<double>();
        if (tickSpacing > 0d)
        {
            int guard = 0;
            for (double v = yMin; v <= yMax + tickSpacing * 1e-6 && guard < 1000; v += tickSpacing, guard++)
                tickVals.Add(v);
        }

        float legendH = (s.Legend && s.Series.Count > 0 && s.Font != null) ? legendLineH + 8f : 0f;
        float yLabelH = (s.Axes && s.YLabel.Length > 0 && s.Font != null) ? axisLineH + 2f : 0f;
        float topRegion = legendH + yLabelH;

        bool hasXTickText = s.Axes && s.Font != null && s.XTickFormatter != null;
        bool hasXLabelText = s.Axes && s.Font != null && s.XLabel.Length > 0;

        float leftGutter = 0f, bottomGutter = 0f, rightPad = 0f, maxTickW = 0f;
        if (s.Axes)
        {
            if (s.Font != null)
                foreach (var v in tickVals)
                {
                    var tw = canvas.MeasureText(Format(in s, v), axisFs, s.Font);
                    maxTickW = MathF.Max(maxTickW, (float)tw.X);
                }
            maxTickW = Math.Clamp(maxTickW, s.YLabelMinWidth, s.YLabelMaxWidth);
            leftGutter = 4f + maxTickW + TickLabelPad + TickLen;

            bottomGutter = TickLen;
            if (hasXTickText) bottomGutter += TickLabelPad + axisLineH;
            if (hasXLabelText) bottomGutter += axisLineH + 2f;
            if (hasXTickText || hasXLabelText) bottomGutter += EdgePad;

            rightPad = EdgePad;
        }

        float plotL = ox + leftGutter;
        float plotT = oy + topRegion + (s.Axes ? 2f : 0f);
        float plotR = ox + s.Width - rightPad;
        float plotB = oy + s.Height - bottomGutter;
        float plotW = plotR - plotL;
        float plotH = plotB - plotT;

        if (plotW < 4f || plotH < 4f) { canvas.RestoreState(); return default; }

        double ySpan = yMax - yMin;
        if (ySpan <= 0d) ySpan = 1d;

        int maxN = 0;
        foreach (var series in s.Series)
            if (series.Values != null) maxN = Math.Max(maxN, series.Values.Count);

        if (s.Grid != ChartGridMode.None)
            DrawGrid(canvas, in s, plotL, plotT, plotR, plotB, plotW, plotH, ySpan, tickSpacing, maxN);

        if (s.Axes)
        {
            foreach (var v in tickVals)
            {
                float ty = plotB - (float)((v - yMin) / ySpan) * plotH;
                if (ty < plotT - 0.5f || ty > plotB + 0.5f) continue;

                if (s.Grid == ChartGridMode.None)
                {
                    canvas.BeginPath();
                    canvas.MoveTo(plotL, ty);
                    canvas.LineTo(plotR, ty);
                    canvas.SetStrokeColor(s.GridCol);
                    canvas.SetStrokeWidth(1f);
                    canvas.Stroke();
                }

                canvas.BeginPath();
                canvas.MoveTo(plotL - TickLen, ty);
                canvas.LineTo(plotL, ty);
                canvas.SetStrokeColor(s.AxisCol);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();

                if (s.Font != null)
                {
                    string txt = Format(in s, v);
                    var tw = canvas.MeasureText(txt, axisFs, s.Font);
                    float tx = plotL - TickLen - TickLabelPad - (float)tw.X;
                    float tty = ty - (float)tw.Y * 0.5f;
                    canvas.SaveState();
                    canvas.IntersectScissor(ox, tty - TextClipMargin, 4f + maxTickW, (float)tw.Y + TextClipMargin * 2f);
                    canvas.DrawText(txt, tx, tty, s.TickTextCol, axisFs, s.Font);
                    canvas.RestoreState();
                }
            }
        }

        if (s.Axes)
        {
            int xTicks = Math.Min(6, Math.Max(2, maxN));
            if (maxN >= 1)
                for (int t = 0; t < xTicks; t++)
                {
                    double frac = xTicks == 1 ? 0d : t / (double)(xTicks - 1);
                    int idx = (int)Math.Round(frac * (maxN - 1));
                    float xx = plotL + (float)frac * plotW;

                    canvas.BeginPath();
                    canvas.MoveTo(xx, plotB);
                    canvas.LineTo(xx, plotB + TickLen);
                    canvas.SetStrokeColor(s.AxisCol);
                    canvas.SetStrokeWidth(1f);
                    canvas.Stroke();

                    if (s.Font != null && s.XTickFormatter != null)
                    {
                        string txt = s.XTickFormatter(idx) ?? "";
                        if (txt.Length > 0)
                        {
                            var tw = canvas.MeasureText(txt, axisFs, s.Font);
                            float tx = Math.Clamp(xx - (float)tw.X * 0.5f, plotL, plotR - (float)tw.X);
                            canvas.DrawText(txt, tx, plotB + TickLen + TickLabelPad, s.TickTextCol, axisFs, s.Font);
                        }
                    }
                }

            canvas.BeginPath();
            canvas.MoveTo(plotL, plotT);
            canvas.LineTo(plotL, plotB);
            canvas.LineTo(plotR, plotB);
            canvas.SetStrokeColor(s.AxisCol);
            canvas.SetStrokeWidth(1f);
            canvas.Stroke();

            if (s.Font != null && s.YLabel.Length > 0)
                canvas.DrawText(s.YLabel, ox + 4f, oy + legendH, s.LabelTextCol, axisFs, s.Font);

            if (hasXLabelText)
            {
                var tw = canvas.MeasureText(s.XLabel, axisFs, s.Font!);
                float tx = plotL + (plotW - (float)tw.X) * 0.5f;
                canvas.DrawText(s.XLabel, tx, oy + s.Height - axisLineH - EdgePad, s.LabelTextCol, axisFs, s.Font!);
            }
        }

        canvas.SaveState();
        canvas.IntersectScissor(plotL, plotT, plotW, plotH);
        foreach (var series in s.Series)
            PaintSeries(canvas, series, plotL, plotT, plotW, plotH, plotB, yMin, ySpan, maxN);
        canvas.RestoreState();

        if (legendH > 0f)
            PaintLegend(canvas, in s, ox, oy, legendFs, legendLineH);

        canvas.RestoreState();

        return new PlotInfo
        {
            PlotL = plotL - (float)rect.Min.X,
            PlotT = plotT - (float)rect.Min.Y,
            PlotR = plotR - (float)rect.Min.X,
            PlotB = plotB - (float)rect.Min.Y,
            MaxN = maxN,
        };
    }

    private static void PaintSeries(Canvas canvas, ChartSeries series, float plotL, float plotT, float plotW, float plotH, float plotB,
        double yMin, double ySpan, int maxN)
    {
        var vals = series.Values;
        if (vals == null || vals.Count == 0) return;

        int n = vals.Count;
        Color32 lineCol = series.Color;
        Color32 fillCol = ToC32(series.Color, 0.18f);

        if (n == 1)
        {
            float px = XPos(plotL, plotW, 0, maxN);
            float py = plotB - (float)((vals[0] - yMin) / ySpan) * plotH;
            py = Math.Clamp(py, plotT, plotB);
            canvas.BeginPath();
            canvas.Circle(px, py, 2.5f);
            canvas.SetFillColor(lineCol);
            canvas.Fill();
            return;
        }

        // The area fill is emitted as one convex trapezoid per segment (baseline, A, B, baseline)
        // rather than a single many-point closed polygon: a concave polygon spanning all points
        // triangulates into crossing slivers that bleed through the curve.
        if (series.Fill)
        {
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = XPos(plotL, plotW, i, maxN);
                float x1 = XPos(plotL, plotW, i + 1, maxN);
                float y0 = plotB - (float)((vals[i] - yMin) / ySpan) * plotH;
                float y1 = plotB - (float)((vals[i + 1] - yMin) / ySpan) * plotH;

                canvas.BeginPath();
                canvas.MoveTo(x0, plotB);
                canvas.LineTo(x0, y0);
                canvas.LineTo(x1, y1);
                canvas.LineTo(x1, plotB);
                canvas.ClosePath();
                canvas.SetFillColor(fillCol);
                canvas.Fill();
            }
        }

        canvas.BeginPath();
        for (int i = 0; i < n; i++)
        {
            float x = XPos(plotL, plotW, i, maxN);
            float y = plotB - (float)((vals[i] - yMin) / ySpan) * plotH;
            if (i == 0) canvas.MoveTo(x, y);
            else canvas.LineTo(x, y);
        }
        canvas.SetStrokeColor(lineCol);
        canvas.SetStrokeWidth(1.5f);
        canvas.Stroke();
    }

    private static void PaintLegend(Canvas canvas, in Snapshot s, float ox, float oy, float fs, float lineH)
    {
        if (s.Font == null) return;
        float x = ox + 4f;
        float y = oy + 4f;
        float rowTop = y + (lineH - SwatchSize) * 0.5f;

        foreach (var series in s.Series)
        {
            string latest = (s.LegendShowValue && series.Values != null && series.Values.Count > 0)
                ? Format(in s, series.Values[series.Values.Count - 1]) : "";
            string txt = latest.Length > 0 ? series.Label + "  " + latest : series.Label;

            var tw = canvas.MeasureText(txt.Length == 0 ? " " : txt, fs, s.Font);
            float entryW = SwatchSize + 5f + (float)tw.X + 14f;
            if (x + entryW > ox + s.Width - 2f && x > ox + 4f) break;

            canvas.BeginPath();
            canvas.RoundedRect(x, rowTop, SwatchSize, SwatchSize, 2f, 2f, 2f, 2f);
            canvas.SetFillColor((Color32)series.Color);
            canvas.Fill();

            canvas.DrawText(txt, x + SwatchSize + 5f, y + (lineH - (float)tw.Y) * 0.5f, s.LabelTextCol, fs, s.Font);
            x += entryW;
        }
    }

    private static void DrawGrid(Canvas canvas, in Snapshot s, float plotL, float plotT, float plotR, float plotB,
        float plotW, float plotH, double ySpan, double tickSpacing, int maxN)
    {
        float spacingY, spacingX;

        if (s.Grid == ChartGridMode.FixedCount)
        {
            spacingY = plotH / s.GridCountY;
            spacingX = s.GridCountX > 0 ? plotW / s.GridCountX : spacingY;
        }
        else
        {
            float yTickPx = tickSpacing > 0d ? (float)(tickSpacing / ySpan) * plotH : plotH;
            spacingY = yTickPx / s.GridRatioY;

            if (!float.IsNaN(s.GridRatioX))
            {
                int xTicks = Math.Min(6, Math.Max(2, maxN));
                float xTickPx = plotW / Math.Max(1, xTicks - 1);
                spacingX = xTickPx / s.GridRatioX;
            }
            else
            {
                spacingX = spacingY;
            }
        }

        spacingX = MathF.Max(1f, spacingX);
        spacingY = MathF.Max(1f, spacingY);

        canvas.SetStrokeColor(s.GridLineCol);
        canvas.SetStrokeWidth(1f);

        int guard = 0;
        for (float x = plotL; x <= plotR + 0.5f && guard < 1000; x += spacingX, guard++)
        {
            canvas.BeginPath();
            canvas.MoveTo(x, plotT);
            canvas.LineTo(x, plotB);
            canvas.Stroke();
        }

        guard = 0;
        for (float y = plotT; y <= plotB + 0.5f && guard < 1000; y += spacingY, guard++)
        {
            canvas.BeginPath();
            canvas.MoveTo(plotL, y);
            canvas.LineTo(plotR, y);
            canvas.Stroke();
        }
    }

    /// <summary>Draws the vertical sample line and per-series crosshair dots for
    /// <see cref="ChartBuilder.Sampleable"/>. Called from within the box's own (clipped) draw
    /// commands (see <see cref="Show"/>), so these markers are scissored to the chart's bounds like
    /// the rest of the plot - only the tooltip popup (<see cref="PaintSamplePopup"/>) escapes that
    /// clip. <paramref name="rect"/> is the box's own layout rect, matching what <see cref="Paint"/>
    /// was given, so it can recover absolute plot coordinates from <paramref name="info"/>'s
    /// box-relative plot bounds.</summary>
    private static void PaintSampleMarkers(Canvas canvas, in Snapshot s, Rect rect, in PlotInfo info, int sampleIdx)
    {
        if (info.MaxN <= 0) return;

        float plotL = (float)rect.Min.X + info.PlotL;
        float plotT = (float)rect.Min.Y + info.PlotT;
        float plotR = (float)rect.Min.X + info.PlotR;
        float plotB = (float)rect.Min.Y + info.PlotB;
        float plotW = plotR - plotL;
        int maxN = info.MaxN;

        ComputeYRange(in s, out double yMin, out double yMax, out _);
        double ySpan = yMax - yMin;
        if (ySpan <= 0d) ySpan = 1d;

        float lx = XPos(plotL, plotW, sampleIdx, maxN);

        canvas.BeginPath();
        canvas.MoveTo(lx, plotT);
        canvas.LineTo(lx, plotB);
        canvas.SetStrokeColor(s.SampleLineCol);
        canvas.SetStrokeWidth(1f);
        canvas.Stroke();

        foreach (var series in s.Series)
        {
            if (series.Values == null || sampleIdx >= series.Values.Count) continue;
            float py = plotB - (float)((series.Values[sampleIdx] - yMin) / ySpan) * (plotB - plotT);
            py = Math.Clamp(py, plotT, plotB);
            canvas.BeginPath();
            canvas.Circle(lx, py, 3f);
            canvas.SetFillColor((Color32)series.Color);
            canvas.Fill();
        }
    }

    /// <summary>Draws the tooltip-style value popup for <see cref="ChartBuilder.Sampleable"/>. Called
    /// from the box's Topmost-layer overlay child (see <see cref="Show"/>), not the box's own clipped
    /// draw commands, so the popup can overflow this chart's bounds instead of being clipped at its
    /// edge. <paramref name="boxOx"/>/<paramref name="boxOy"/> are the overlay's own layout origin,
    /// which lands exactly on the box's origin (see Show) - added back to <paramref name="info"/>'s
    /// box-relative plot bounds to recover the same absolute plot coordinates <see cref="Paint"/> used.</summary>
    private static void PaintSamplePopup(Canvas canvas, in Snapshot s, float boxOx, float boxOy, in PlotInfo info, int sampleIdx, float fs, Paper paper)
    {
        if (s.Font == null || info.MaxN <= 0) return;

        float plotL = boxOx + info.PlotL;
        float plotT = boxOy + info.PlotT;
        float plotR = boxOx + info.PlotR;
        float plotW = plotR - plotL;
        int maxN = info.MaxN;

        float lineH = MathF.Max(9f, (float)canvas.MeasureText("0", fs, s.Font).Y);

        float lx = XPos(plotL, plotW, sampleIdx, maxN);

        string header = s.XTickFormatter != null ? (s.XTickFormatter(sampleIdx) ?? "") : $"Index {sampleIdx}";

        const float padX = 9f, padY = 6f, swatchGap = 6f, rowGap = 2f, anchorGap = 8f;
        float rowW = 0f;
        var headerSize = header.Length > 0 ? canvas.MeasureText(header, fs, s.Font) : default;
        if (header.Length > 0) rowW = MathF.Max(rowW, (float)headerSize.X);

        var rows = new List<(Color32 Color, string Text)>();
        foreach (var series in s.Series)
        {
            if (series.Values == null || sampleIdx >= series.Values.Count) continue;
            string text = $"{series.Label}: {Format(in s, series.Values[sampleIdx])}";
            rows.Add((series.Color, text));
            var tw = canvas.MeasureText(text, fs, s.Font);
            rowW = MathF.Max(rowW, SwatchSize + swatchGap + (float)tw.X);
        }

        if (header.Length == 0 && rows.Count == 0) return;

        float popW = rowW + padX * 2f;
        int rowCount = rows.Count + (header.Length > 0 ? 1 : 0);
        float popH = padY * 2f + rowCount * lineH + Math.Max(0, rowCount - 1) * rowGap;

        // Anchor the popup's top-left corner to the top of the sample line, nudged right so the
        // popup doesn't sit directly over the line it's reporting on. Flip to the line's left if
        // that would run the popup off the right edge of the screen; either way, clamp fully
        // on-screen last (the popup floats above everything, so this chart's own bounds no longer
        // constrain it, but the screen still should).
        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;

        float popX = lx + anchorGap;
        if (popX + popW > screenW - 2f)
            popX = lx - anchorGap - popW;
        popX = Math.Clamp(popX, 2f, MathF.Max(2f, screenW - popW - 2f));

        float popY = Math.Clamp(plotT, 2f, MathF.Max(2f, screenH - popH - 2f));

        canvas.BeginPath();
        canvas.RoundedRect(popX, popY, popW, popH, 6f, 6f, 6f, 6f);
        canvas.SetFillColor(s.PopoverCol);
        canvas.Fill();
        canvas.SetStrokeColor(s.BorderStrongCol);
        canvas.SetStrokeWidth(1f);
        canvas.Stroke();

        float rowY = popY + padY;
        if (header.Length > 0)
        {
            canvas.DrawText(header, popX + padX, rowY, s.LabelTextCol, fs, s.Font);
            rowY += lineH + rowGap;
        }

        foreach (var (color, text) in rows)
        {
            float swatchY = rowY + (lineH - SwatchSize) * 0.5f;
            canvas.BeginPath();
            canvas.RoundedRect(popX + padX, swatchY, SwatchSize, SwatchSize, 2f, 2f, 2f, 2f);
            canvas.SetFillColor(color);
            canvas.Fill();

            canvas.DrawText(text, popX + padX + SwatchSize + swatchGap, rowY, s.LabelTextCol, fs, s.Font);
            rowY += lineH + rowGap;
        }
    }

    private static float XPos(float plotL, float plotW, int idx, int maxN)
    {
        if (maxN <= 1) return plotL + plotW * 0.5f;
        return plotL + (float)(idx / (double)(maxN - 1)) * plotW;
    }

    private static void ComputeYRange(in Snapshot s, out double yMin, out double yMax, out double tickSpacing)
    {
        if (s.HasYRange)
        {
            yMin = s.YMinFixed;
            yMax = s.YMaxFixed;
        }
        else
        {
            yMin = double.MaxValue;
            yMax = double.MinValue;
            foreach (var series in s.Series)
            {
                if (series.Values == null) continue;
                foreach (var v in series.Values)
                {
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (v < yMin) yMin = v;
                    if (v > yMax) yMax = v;
                }
            }
            if (yMin > yMax) { yMin = 0d; yMax = 1d; }
            if (s.IncludeZero) { yMin = Math.Min(yMin, 0d); yMax = Math.Max(yMax, 0d); }
            if (s.MinSpan > 0d && yMax - yMin < s.MinSpan) yMax = yMin + s.MinSpan;
        }

        if (yMax <= yMin) yMax = yMin + 1d;

        if (!s.HasYRange)
        {
            double niceRange = NiceNum(yMax - yMin, false);
            tickSpacing = NiceNum(niceRange / Math.Max(1, s.YTicks - 1), true);
            if (tickSpacing > 0d)
            {
                yMin = Math.Floor(yMin / tickSpacing) * tickSpacing;
                yMax = Math.Ceiling(yMax / tickSpacing) * tickSpacing;
            }
            if (yMax <= yMin) yMax = yMin + 1d;
        }
        else
        {
            tickSpacing = (yMax - yMin) / Math.Max(1, s.YTicks - 1);
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

    private static string Format(in Snapshot s, double v) => s.Formatter != null ? s.Formatter(v) : v.ToString("0.###");

    private static Color32 ToC32(Color c) => new Color32(c.R, c.G, c.B, c.A);

    private static Color32 ToC32(Color c, float alpha) => new Color32(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));
}
