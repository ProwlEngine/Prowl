// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

internal sealed class CartesianSeries<T>
{
    public string Label = "";
    public Color Color;
    public bool HasColor;
    public float? StrokeWidth;
    public bool Fill;
    public bool Dashed;
    public bool Hidden;
    public readonly List<(double X, double Y, T? Payload)> Points = new();
}

internal readonly struct PlotContext
{
    public readonly float L, T, R, B;
    public readonly double XMin, XMax, YMin, YMax;

    public PlotContext(float l, float t, float r, float b, double xMin, double xMax, double yMin, double yMax)
    {
        L = l; T = t; R = r; B = b;
        XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax;
    }

    public float XPos(double x) => XMax > XMin ? L + (float)((x - XMin) / (XMax - XMin)) * (R - L) : (L + R) * 0.5f;
    public float YPos(double y) => YMax > YMin ? B - (float)((y - YMin) / (YMax - YMin)) * (B - T) : (T + B) * 0.5f;
    public float UnitWidth => XMax > XMin ? (float)((R - L) / (XMax - XMin)) : 0f;
    public float Baseline => Math.Clamp(YPos(0d), T, B);
}

/// <summary>Cartesian chart whose marks come from modules added with <c>.AddLineChart()</c>,
/// <c>.AddBarChart()</c> and <c>.AddScatterPlot()</c>, all sharing one set of axes.</summary>
public sealed class CartesianChart<T> : ChartCore<CartesianChart<T>, T>
{
    private readonly List<CartesianModule<T>> _modules = new();
    private readonly List<CartesianSeries<T>> _series = new();

    private Func<T, double>? _xSelector;
    private bool _hasYRange;
    private double _yRangeMin, _yRangeMax;
    private int _yTicks = 4;
    private int _xTicks = 6;
    private Func<int, string>? _xTickFormatter;
    private string _xLabel = "";
    private string _yLabel = "";
    private bool _axes = true;
    private bool _gridX, _gridY;
    private Color? _gridLineColor;
    private bool _sampleable;
    private Color? _sampleLineColor;
    private bool _zoomable, _pannable;

    private const string PlotRectKey = "cartesian_plot";
    private const string SamplePosKey = "cartesian_sample_pos";
    private const string SampleOnKey = "cartesian_sample_on";
    private const float TickLength = 4f;
    private const float LabelGap = 2f;

    private struct PlotRect
    {
        public float L, T, R, B;
    }

    internal CartesianChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    public LineModule<T> AddLineChart() => Add(new LineModule<T>(this));
    public BarModule<T> AddBarChart() => Add(new BarModule<T>(this));
    public ScatterModule<T> AddScatterPlot() => Add(new ScatterModule<T>(this));

    private TModule Add<TModule>(TModule module) where TModule : CartesianModule<T>
    {
        _modules.Add(module);
        return module;
    }

    /// <summary>X selector over the data set, shared by every module's <c>.Y(...)</c>.</summary>
    public CartesianChart<T> X(Func<T, double> selector) { _xSelector = selector; return this; }

    public CartesianChart<T> YRange(double min, double max) { _hasYRange = true; _yRangeMin = Math.Min(min, max); _yRangeMax = Math.Max(min, max); return this; }
    public CartesianChart<T> YTicks(int count) { _yTicks = Math.Max(2, count); return this; }
    public CartesianChart<T> XTicks(int count) { _xTicks = Math.Max(2, count); return this; }

    /// <summary>Label for the x value rounded to an integer, also used as the sampler header.</summary>
    public CartesianChart<T> XTickFormatter(Func<int, string> formatter) { _xTickFormatter = formatter; return this; }

    public CartesianChart<T> XLabel(string text) { _xLabel = text ?? ""; return this; }
    public CartesianChart<T> YLabel(string text) { _yLabel = text ?? ""; return this; }
    public CartesianChart<T> Axes(bool show = true) { _axes = show; return this; }

    /// <summary>Grid lines at the x and/or y ticks.</summary>
    public CartesianChart<T> Grid(bool x = true, bool y = true) { _gridX = x; _gridY = y; return this; }

    public CartesianChart<T> GridLineColor(Color color) { _gridLineColor = color; return this; }

    /// <summary>Left-drag over the plot to show a crosshair and readout of the nearest point.</summary>
    public CartesianChart<T> Sampleable(bool enable = true) { _sampleable = enable; return this; }

    public CartesianChart<T> SampleLineColor(Color color) { _sampleLineColor = color; return this; }

    /// <summary>Scroll-wheel zoom about the pointer, after clicking the plot. Y joins in with a scatter module.</summary>
    public CartesianChart<T> Zoomable(bool enable = true) { _zoomable = enable; return this; }

    /// <summary>Middle-drag panning of a zoomed view.</summary>
    public CartesianChart<T> Pannable(bool enable = true) { _pannable = enable; return this; }

    internal Color SampleColor => _sampleLineColor ?? _theme.Ink.C500;
    internal string Format(double v) => FormatValue(v);

    private string XTickLabel(double x)
    {
        int key = (int)Math.Round(x);
        return _xTickFormatter != null ? _xTickFormatter(key) ?? "" : key.ToString();
    }

    protected override IReadOnlyList<LegendEntry> BuildLegendEntries()
    {
        _series.Clear();
        foreach (CartesianModule<T> m in _modules)
            m.Resolve(_data, _xSelector, _series);

        var entries = new List<LegendEntry>(_series.Count);
        for (int i = 0; i < _series.Count; i++)
        {
            CartesianSeries<T> s = _series[i];
            if (!s.HasColor) s.Color = RampColor(i);
            s.Hidden = IsLegendHidden(i);

            string? value = LegendShowValueEnabled && s.Points.Count > 0 ? FormatValue(s.Points[^1].Y) : null;
            entries.Add(new LegendEntry(s.Label, s.Color, i, value, s.Hidden));
        }
        return entries;
    }

    protected override void DrawPlot()
    {
        bool any = false;
        foreach (CartesianSeries<T> s in _series) any |= s.Points.Count > 0;
        if (!any)
        {
            DrawEmpty();
            return;
        }

        bool nearest2D = false;
        foreach (CartesianModule<T> m in _modules) nearest2D |= m.Nearest2D;

        BaseRange(out double xMin, out double xMax, out double yMin, out double yMax);

        ViewRect view = View;
        double xSpan = xMax - xMin, ySpan = yMax - yMin;
        xMin += view.X * xSpan;
        xMax = xMin + view.W * xSpan;
        yMin += view.Y * ySpan;
        yMax = yMin + view.H * ySpan;

        double[] xTicks = Ticks(xMin, xMax, _xTicks, 1d);
        double[] yTicks = Ticks(yMin, yMax, _yTicks, 0d);

        ElementBuilder plotBox = _paper.Box(_id + "_chart_plot").Clip();
        if (_sampleable) plotBox.Cursor(PaperCursor.Crosshair);

        using (plotBox.Enter())
        {
            ElementHandle plotEl = _paper.CurrentParent;
            WireView(plotBox, plotEl, _zoomable, _pannable, nearest2D);

            CartesianSeries<T>? hit = null;
            int index = -1;

            if (_sampleable)
            {
                plotBox.OnHeld(e =>
                {
                    _paper.SetElementStorage(plotEl, SamplePosKey, e.RelativePosition);
                    _paper.SetElementStorage(plotEl, SampleOnKey, true);
                });
                plotBox.OnRelease(_ => _paper.SetElementStorage(plotEl, SampleOnKey, false));

                PlotRect r = _paper.GetElementStorage(plotEl, PlotRectKey, default(PlotRect));
                if (r.R > r.L && _paper.GetElementStorage(plotEl, SampleOnKey, false))
                {
                    var local = new PlotContext(r.L, r.T, r.R, r.B, xMin, xMax, yMin, yMax);
                    Float2 pointer = _paper.GetElementStorage(plotEl, SamplePosKey, new Float2(0f, 0f));
                    (hit, index) = FindSample(in local, pointer, nearest2D);
                    if (hit != null) SamplePopup(in local, hit, index, nearest2D);
                }
            }

            _paper.Draw((canvas, rect) => Paint(canvas, rect, plotEl, xMin, xMax, yMin, yMax, xTicks, yTicks, hit, index, nearest2D));
        }
    }

    private void BaseRange(out double xMin, out double xMax, out double yMin, out double yMax)
    {
        xMin = yMin = double.MaxValue;
        xMax = yMax = double.MinValue;

        foreach (CartesianSeries<T> s in _series)
        {
            if (s.Hidden) continue;
            foreach ((double x, double y, T? _) in s.Points)
            {
                if (double.IsFinite(x)) { xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x); }
                if (double.IsFinite(y)) { yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y); }
            }
        }

        float pad = 0f;
        foreach (CartesianModule<T> m in _modules) pad = MathF.Max(pad, m.XPad);

        if (xMin > xMax) { xMin = 0d; xMax = 1d; }
        xMin -= pad;
        xMax += pad;
        if (xMax <= xMin) xMax = xMin + 1d;

        if (_hasYRange)
        {
            yMin = _yRangeMin;
            yMax = _yRangeMax;
        }
        else
        {
            if (yMin > yMax) { yMin = 0d; yMax = 1d; }
            yMin = Math.Min(yMin, 0d);
            yMax = Math.Max(yMax, 0d);

            double step = NiceStep((yMax - yMin) / (_yTicks - 1));
            if (step > 0d)
            {
                yMin = Math.Floor(yMin / step) * step;
                yMax = Math.Ceiling(yMax / step) * step;
            }
        }

        if (yMax <= yMin) yMax = yMin + 1d;
    }

    private static double NiceStep(double raw)
    {
        if (!(raw > 0d) || !double.IsFinite(raw)) return 0d;
        double exp = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        double f = raw / exp;
        return (f < 1.5d ? 1d : f < 3d ? 2d : f < 7d ? 5d : 10d) * exp;
    }

    private static double[] Ticks(double min, double max, int count, double minStep)
    {
        double step = Math.Max(minStep, NiceStep((max - min) / (count - 1)));
        if (!(step > 0d)) return Array.Empty<double>();

        double first = Math.Ceiling(min / step - 1e-9) * step;
        int n = Math.Clamp((int)Math.Floor((max - first) / step + 1e-9) + 1, 0, 1000);

        var ticks = new double[n];
        for (int i = 0; i < n; i++) ticks[i] = first + i * step;
        return ticks;
    }

    private (CartesianSeries<T>? Series, int Index) FindSample(in PlotContext ctx, Float2 pointer, bool nearest2D)
    {
        float px = Math.Clamp(pointer.X, ctx.L, ctx.R);
        float py = Math.Clamp(pointer.Y, ctx.T, ctx.B);

        CartesianSeries<T>? best = null;
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        foreach (CartesianModule<T> m in _modules)
        {
            if (m.Nearest2D != nearest2D) continue;

            foreach (CartesianSeries<T> s in m._series)
            {
                if (s.Hidden) continue;

                for (int i = 0; i < s.Points.Count; i++)
                {
                    (double x, double y, T? _) = s.Points[i];
                    if (!double.IsFinite(x)) continue;

                    float dx = ctx.XPos(x) - px;
                    float d = dx * dx;
                    if (nearest2D)
                    {
                        if (!double.IsFinite(y)) continue;
                        float dy = ctx.YPos(y) - py;
                        d += dy * dy;
                    }

                    if (d < bestDist) { bestDist = d; best = s; bestIndex = i; }
                }
            }
        }

        return (best, bestIndex);
    }

    private void SamplePopup(in PlotContext ctx, CartesianSeries<T> hit, int index, bool nearest2D)
    {
        var rows = new List<(Color Color, string Text)>();
        foreach (CartesianModule<T> m in _modules)
            m.AppendRows(nearest2D ? hit : null, index, rows);

        string header = nearest2D ? hit.Label : _xTickFormatter != null ? _xTickFormatter(index) ?? "" : index.ToString();
        Popup(ctx.XPos(hit.Points[index].X), ctx.T, ctx.L, ctx.R, header, rows);
    }

    private void Paint(Canvas canvas, Rect rect, ElementHandle plotEl, double xMin, double xMax, double yMin, double yMax,
        double[] xTicks, double[] yTicks, CartesianSeries<T>? hit, int index, bool nearest2D)
    {
        float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;

        var xLabels = new string[xTicks.Length];
        for (int i = 0; i < xTicks.Length; i++) xLabels[i] = XTickLabel(xTicks[i]);

        var yLabels = new string[yTicks.Length];
        for (int i = 0; i < yTicks.Length; i++) yLabels[i] = FormatValue(yTicks[i]);

        float lineH = MathF.Max(MeasureText(canvas, "0").Y, TextSize);
        float left = 0f, top = lineH * 0.5f, right = 0f, bottom = 0f;

        if (_axes)
        {
            foreach (string label in yLabels) left = MathF.Max(left, MeasureText(canvas, label).X);
            left += TickLength + LabelGap * 2f;
            right = lineH;
            bottom = TickLength + LabelGap + lineH;
            if (_yLabel.Length > 0) top = lineH + LabelGap * 2f;
            if (_xLabel.Length > 0) bottom += lineH + LabelGap;
        }

        if (w - left - right < 4f || h - top - bottom < 4f) return;

        _paper.SetElementStorage(plotEl, PlotRectKey, new PlotRect { L = left, T = top, R = w - right, B = h - bottom });
        var ctx = new PlotContext(ox + left, oy + top, ox + w - right, oy + h - bottom, xMin, xMax, yMin, yMax);

        Color32 grid = _gridLineColor.HasValue ? ToC32(_gridLineColor.Value) : ToC32(_theme.BorderSoft, 0.6f);
        Color32 tick = ToC32(_theme.Ink.C500);

        canvas.SetStrokeWidth(1f);
        canvas.BeginPath();
        if (_gridX)
            foreach (double v in xTicks) { float x = ctx.XPos(v); canvas.MoveTo(x, ctx.T); canvas.LineTo(x, ctx.B); }
        if (_gridY)
            foreach (double v in yTicks) { float y = ctx.YPos(v); canvas.MoveTo(ctx.L, y); canvas.LineTo(ctx.R, y); }
        canvas.SetStrokeColor(grid);
        canvas.Stroke();

        canvas.BeginPath();
        canvas.MoveTo(ctx.L, ctx.T);
        canvas.LineTo(ctx.L, ctx.B);
        canvas.LineTo(ctx.R, ctx.B);
        canvas.SetStrokeColor(ToC32(_theme.Ink.C300));
        canvas.Stroke();

        if (_axes)
        {
            canvas.BeginPath();
            foreach (double v in yTicks) { float y = ctx.YPos(v); canvas.MoveTo(ctx.L - TickLength, y); canvas.LineTo(ctx.L, y); }
            foreach (double v in xTicks) { float x = ctx.XPos(v); canvas.MoveTo(x, ctx.B); canvas.LineTo(x, ctx.B + TickLength); }
            canvas.SetStrokeColor(tick);
            canvas.Stroke();

            for (int i = 0; i < yTicks.Length; i++)
                DrawText(canvas, yLabels[i], ctx.L - TickLength - LabelGap, ctx.YPos(yTicks[i]), 1f, 0.5f);
            for (int i = 0; i < xTicks.Length; i++)
                DrawText(canvas, xLabels[i], ctx.XPos(xTicks[i]), ctx.B + TickLength + LabelGap, 0.5f, 0f);

            DrawText(canvas, _yLabel, ox, oy, 0f, 0f);
            DrawText(canvas, _xLabel, (ctx.L + ctx.R) * 0.5f, oy + h, 0.5f, 1f);
        }

        canvas.SaveState();
        canvas.IntersectScissor(ctx.L, ctx.T, ctx.R - ctx.L, ctx.B - ctx.T);

        foreach (CartesianModule<T> m in _modules)
            m.Paint(canvas, in ctx);

        if (hit != null && index < hit.Points.Count)
        {
            if (!nearest2D)
            {
                float x = ctx.XPos(hit.Points[index].X);
                canvas.BeginPath();
                canvas.MoveTo(x, ctx.T);
                canvas.LineTo(x, ctx.B);
                canvas.SetStrokeColor(ToC32(SampleColor));
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();
            }

            foreach (CartesianModule<T> m in _modules)
                m.PaintSample(canvas, in ctx, nearest2D ? hit : null, index);
        }

        canvas.RestoreState();
    }
}
