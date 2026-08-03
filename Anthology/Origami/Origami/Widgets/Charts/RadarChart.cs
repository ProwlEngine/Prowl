// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Radar (spider) chart. The data set defines the spokes, one per item and labelled by <c>Name</c>, and
/// every series added with <see cref="Series"/> is drawn as one closed polygon whose value i sits on
/// spoke i. Without any series the items' own values form a single polygon.
/// </summary>
public sealed class RadarChart<T> : CircularCore<RadarChart<T>, T>
{
    internal RadarChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private sealed class RadarSeries
    {
        public string Label = "";
        public Color Tint;
        public IReadOnlyList<double> Values = Array.Empty<double>();
        public bool Fill = true;
        public float? StrokeWidth;
    }

    private readonly List<RadarSeries> _series = new();
    private RadarSeries? _lastSeries;

    private int _yTicks = 4;
    private bool _hasRange;
    private double _rangeMin;
    private double _rangeMax;
    private bool _spokeLabels = true;

    private const float DefaultStrokeWidth = 2f;
    private const float FillAlpha = 0.25f;
    private const float GridAlpha = 0.6f;
    private const float VertexRadius = 3f;
    private const float LabelRadiusScale = 1.06f;
    private const float HitRadiusScale = 1.1f;
    private const float TickBoxWidth = 48f;
    private const float TickBoxHeight = 14f;
    private const float SpokeBoxWidth = 90f;
    private const float SpokeBoxHeight = 14f;

    /// <summary>Add one polygon over the spokes. Value i in <paramref name="values"/> is plotted on the
    /// spoke made by data item i; missing values sit at the bottom of the range.</summary>
    public RadarChart<T> Series(string label, Color color, IReadOnlyList<double> values)
    {
        var s = new RadarSeries { Label = label ?? "", Tint = color, Values = values ?? Array.Empty<double>() };
        _series.Add(s);
        _lastSeries = s;
        return this;
    }

    /// <summary>Colour of the most recently added series.</summary>
    public RadarChart<T> Color(Color color) { if (_lastSeries != null) _lastSeries.Tint = color; return this; }

    /// <summary>Fill the most recently added series' polygon behind its stroke. On by default.</summary>
    public RadarChart<T> Fill(bool fill = true) { if (_lastSeries != null) _lastSeries.Fill = fill; return this; }

    /// <summary>Stroke width, in pixels, of the most recently added series' polygon. Defaults to 2.</summary>
    public RadarChart<T> StrokeWidth(float width) { if (_lastSeries != null) _lastSeries.StrokeWidth = MathF.Max(0.1f, width); return this; }

    /// <summary>Number of concentric grid rings, and of the tick labels drawn against them. Defaults to 4.</summary>
    public RadarChart<T> YTicks(int count) { _yTicks = Math.Max(1, count); return this; }

    /// <summary>Fix the value range every spoke is scaled against. Unset, the range spans zero to the
    /// largest value of the visible series.</summary>
    public RadarChart<T> Range(double min, double max)
    {
        _hasRange = true;
        _rangeMin = Math.Min(min, max);
        _rangeMax = Math.Max(min, max);
        return this;
    }

    /// <summary>Show the spoke labels around the outside of the chart. On by default.</summary>
    public new RadarChart<T> Labels(bool show = true) { _spokeLabels = show; base.Labels(show); return this; }

    protected override float RadiusInset => 18f;

    protected override bool RequiresPositiveTotal => false;

    protected override bool LabelsEnabledForType => false;

    protected override bool LegendListsSlices => _series.Count == 0;

    protected override IReadOnlyList<LegendEntry> BuildLegend(IReadOnlyList<CircularSlice<T>> slices)
    {
        if (_series.Count == 0) return base.BuildLegend(slices);

        var entries = new List<LegendEntry>(_series.Count);
        for (int i = 0; i < _series.Count; i++)
        {
            RadarSeries s = _series[i];
            entries.Add(new LegendEntry(
                s.Label.Length > 0 ? s.Label : "Series " + i, s.Tint, i, null, IsHidden(i)));
        }
        return entries;
    }

    // --- Geometry ---

    private static float SpokeAngle(int ordinal, int count)
        => (-90f + 360f * ordinal / count) * DegToRad;

    private bool SeriesVisible(int index) => !IsHidden(index);

    private double SeriesValue(int seriesIndex, int spoke, double fallback)
    {
        IReadOnlyList<double> values = _series[seriesIndex].Values;
        if (spoke >= values.Count) return fallback;

        double v = values[spoke];
        return double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
    }

    private void ValueRange(IReadOnlyList<CircularSlice<T>> slices, out double min, out double max)
    {
        if (_hasRange)
        {
            min = _rangeMin;
            max = _rangeMax;
        }
        else
        {
            double lo = 0d, hi = double.NegativeInfinity;
            bool any = false;

            if (_series.Count > 0)
            {
                for (int k = 0; k < _series.Count; k++)
                {
                    if (!SeriesVisible(k)) continue;

                    foreach (double v in _series[k].Values)
                    {
                        if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                        any = true;
                    }
                }
            }
            else
            {
                foreach (CircularSlice<T> slice in slices)
                {
                    if (!slice.Visible) continue;
                    lo = Math.Min(lo, slice.Value);
                    hi = Math.Max(hi, slice.Value);
                    any = true;
                }
            }

            if (!any) { lo = 0d; hi = 1d; }

            min = Math.Min(0d, lo);
            max = hi;
        }

        if (max <= min) max = min + 1d;
    }

    private static float ValueRadius(double value, double min, double max, float radius)
        => (float)Math.Clamp((value - min) / (max - min), 0d, 1d) * radius;

    private void PolygonPath(Canvas canvas, in CircularContext ctx, int spokes, Func<int, float> radiusOf)
    {
        canvas.BeginPath();
        for (int i = 0; i < spokes; i++)
        {
            Float2 p = ctx.PointAt(SpokeAngle(i, spokes), radiusOf(i));
            if (i == 0) canvas.MoveTo(p.X, p.Y); else canvas.LineTo(p.X, p.Y);
        }
        canvas.ClosePath();
    }

    protected override void PaintMarks(Canvas canvas, in CircularContext ctx)
    {
        int spokes = ctx.Slices.Count;
        if (spokes < 3) return;

        ValueRange(ctx.Slices, out double min, out double max);

        Color32 grid = ToC32(_theme.BorderSoft, GridAlpha);

        for (int t = 1; t <= _yTicks; t++)
        {
            float r = ctx.Radius * t / _yTicks;
            PolygonPath(canvas, in ctx, spokes, _ => r);
            canvas.SetStrokeColor(grid);
            canvas.SetStrokeWidth(1f);
            canvas.Stroke();
        }

        for (int i = 0; i < spokes; i++)
        {
            Float2 outer = ctx.PointAt(SpokeAngle(i, spokes), ctx.Radius);
            bool hovered = ctx.HoverIndex == i;

            canvas.BeginPath();
            canvas.MoveTo(ctx.CenterX, ctx.CenterY);
            canvas.LineTo(outer.X, outer.Y);
            canvas.SetStrokeColor(hovered ? ToC32(_theme.Ink.C500) : grid);
            canvas.SetStrokeWidth(hovered ? 1.5f : 1f);
            canvas.Stroke();
        }

        float radius = ctx.Radius;

        if (_series.Count == 0)
        {
            IReadOnlyList<CircularSlice<T>> slices = ctx.Slices;
            PaintPolygon(canvas, in ctx, spokes, Ramp.C500, true, DefaultStrokeWidth,
                i => ValueRadius(slices[i].Visible ? slices[i].Value : min, min, max, radius));
            return;
        }

        for (int k = 0; k < _series.Count; k++)
        {
            if (!SeriesVisible(k)) continue;

            RadarSeries s = _series[k];
            int index = k;
            PaintPolygon(canvas, in ctx, spokes, s.Tint, s.Fill, s.StrokeWidth ?? DefaultStrokeWidth,
                i => ValueRadius(SeriesValue(index, i, min), min, max, radius));
        }
    }

    private void PaintPolygon(Canvas canvas, in CircularContext ctx, int spokes, Color color, bool fill,
        float strokeWidth, Func<int, float> radiusOf)
    {
        if (fill)
        {
            PolygonPath(canvas, in ctx, spokes, radiusOf);
            canvas.SetFillColor(ToC32(color, FillAlpha));
            canvas.FillComplexAA();
        }

        PolygonPath(canvas, in ctx, spokes, radiusOf);
        canvas.SetStrokeColor(ToC32(color));
        canvas.SetStrokeWidth(strokeWidth);
        canvas.Stroke();

        for (int i = 0; i < spokes; i++)
        {
            Float2 p = ctx.PointAt(SpokeAngle(i, spokes), radiusOf(i));
            canvas.BeginPath();
            canvas.Circle(p.X, p.Y, VertexRadius);
            canvas.SetFillColor(ToC32(color));
            canvas.Fill();
        }
    }

    protected override int HitTest(in CircularContext ctx, Float2 pointer)
    {
        int spokes = ctx.Slices.Count;
        if (spokes < 3) return -1;

        float dx = pointer.X - ctx.CenterX;
        float dy = pointer.Y - ctx.CenterY;
        float reach = ctx.Radius * HitRadiusScale;
        if (dx * dx + dy * dy > reach * reach) return -1;

        float angle = MathF.Atan2(dy, dx) / DegToRad + 90f;
        while (angle < 0f) angle += 360f;

        float step = 360f / spokes;
        return (int)MathF.Round(angle / step) % spokes;
    }

    protected override Float2 LabelAnchor(in CircularContext ctx, int ordinal)
        => ctx.PointAt(SpokeAngle(ordinal, Math.Max(1, ctx.Slices.Count)), ctx.Radius * LabelRadiusScale);

    protected override void DrawOverlay(Paper paper, in CircularContext ctx)
    {
        int spokes = ctx.Slices.Count;
        if (spokes < 3) return;

        ValueRange(ctx.Slices, out double min, out double max);

        for (int t = 1; t <= _yTicks; t++)
        {
            float r = ctx.Radius * t / _yTicks;
            string text = FormatValue(min + (max - min) * t / _yTicks);

            using (paper.Box($"{_id}_radar_tick_{t}")
                .PositionType(PositionType.SelfDirected)
                .Position(ctx.CenterX + 4f, ctx.CenterY - r - TickBoxHeight * 0.5f)
                .Size(TickBoxWidth, TickBoxHeight)
                .Enter())
            {
                Origami.Label(paper, $"{_id}_radar_tick_txt_{t}", text)
                    .XS()
                    .AlignLeft()
                    .Width(TickBoxWidth)
                    .Height(TickBoxHeight)
                    .Show();
            }
        }

        if (!_spokeLabels) return;

        for (int i = 0; i < spokes; i++)
        {
            string label = ctx.Slices[i].Label;
            if (label.Length == 0) continue;

            Float2 anchor = LabelAnchor(in ctx, i);

            using (paper.Box($"{_id}_spoke_label_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(anchor.X - SpokeBoxWidth * 0.5f, anchor.Y - SpokeBoxHeight * 0.5f)
                .Size(SpokeBoxWidth, SpokeBoxHeight)
                .Enter())
            {
                Origami.Label(paper, $"{_id}_spoke_label_txt_{i}", label)
                    .XS()
                    .AlignCenter()
                    .Width(SpokeBoxWidth)
                    .Height(SpokeBoxHeight)
                    .Show();
            }
        }
    }
}
