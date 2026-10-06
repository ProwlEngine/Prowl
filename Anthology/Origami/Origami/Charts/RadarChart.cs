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

/// <summary>Radar chart. Each data item is a spoke named by <see cref="Name"/>; each <see cref="Series"/>
/// is a closed polygon whose value i sits on spoke i.</summary>
public sealed class RadarChart<T> : ChartCore<RadarChart<T>, T>
{
    private sealed class RadarSeries
    {
        public string Label = "";
        public Color Color;
        public IReadOnlyList<double> Values = Array.Empty<double>();
        public bool Fill = true;
        public float StrokeWidth = 2f;
        public bool Hidden;
    }

    private readonly List<RadarSeries> _series = new();
    private RadarSeries? _last;

    private Func<T, string>? _nameSelector;
    private int _ticks = 4;
    private bool _hasRange;
    private double _rangeMin, _rangeMax;
    private bool _labels = true;
    private bool _tooltip = true;

    private const string PlotSizeKey = "radar_size";
    private const float RadiusInset = 18f;
    private const float FillAlpha = 0.25f;
    private const float VertexRadius = 3f;
    private const float LabelRadiusScale = 1.06f;
    private const float HitRadiusScale = 1.1f;

    internal RadarChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    public RadarChart<T> Name(Func<T, string> selector) { _nameSelector = selector; return this; }

    /// <summary>Add a polygon; value i sits on the spoke of data item i.</summary>
    public RadarChart<T> Series(string label, Color color, IReadOnlyList<double> values)
    {
        _last = new RadarSeries { Label = label ?? "", Color = color, Values = values ?? Array.Empty<double>() };
        _series.Add(_last);
        return this;
    }

    public RadarChart<T> Color(Color color) { if (_last != null) _last.Color = color; return this; }
    public RadarChart<T> Fill(bool fill = true) { if (_last != null) _last.Fill = fill; return this; }
    public RadarChart<T> StrokeWidth(float width) { if (_last != null) _last.StrokeWidth = MathF.Max(0.1f, width); return this; }

    /// <summary>Number of grid rings and their value labels. Default 4.</summary>
    public RadarChart<T> YTicks(int count) { _ticks = Math.Max(1, count); return this; }

    /// <summary>Fixed value range. Unset, it spans zero to the largest visible value.</summary>
    public RadarChart<T> Range(double min, double max) { _hasRange = true; _rangeMin = Math.Min(min, max); _rangeMax = Math.Max(min, max); return this; }

    public RadarChart<T> Labels(bool show = true) { _labels = show; return this; }
    public RadarChart<T> Tooltip(bool show = true) { _tooltip = show; return this; }

    protected override IReadOnlyList<LegendEntry> BuildLegendEntries()
    {
        var entries = new List<LegendEntry>(_series.Count);
        for (int i = 0; i < _series.Count; i++)
        {
            RadarSeries s = _series[i];
            s.Hidden = IsLegendHidden(i);
            entries.Add(new LegendEntry(s.Label.Length > 0 ? s.Label : "Series " + i, s.Color, i, null, s.Hidden));
        }
        return entries;
    }

    private int Spokes => _data?.Count ?? 0;

    private string SpokeName(int i) => _nameSelector?.Invoke(_data![i]) ?? "";

    private static float SpokeAngle(int i, int spokes) => -MathF.PI * 0.5f + MathF.Tau * i / spokes;

    private static float Radius(float w, float h) => MathF.Max(1f, 0.5f * MathF.Min(w, h) - RadiusInset);

    private void ValueRange(out double min, out double max)
    {
        if (_hasRange)
        {
            min = _rangeMin;
            max = _rangeMax;
        }
        else
        {
            min = 0d;
            max = double.MinValue;
            foreach (RadarSeries s in _series)
            {
                if (s.Hidden) continue;
                foreach (double v in s.Values)
                    if (IsFinite(v)) { min = Math.Min(min, v); max = Math.Max(max, v); }
            }
            if (max == double.MinValue) max = 1d;
        }

        if (max <= min) max = min + 1d;
    }

    protected override void DrawPlot()
    {
        if (Spokes < 3 || _series.Count == 0)
        {
            DrawEmpty();
            return;
        }

        ValueRange(out double min, out double max);

        ElementBuilder plotBox = _paper.Box(_id + "_chart_plot").Clip();

        using (plotBox.Enter())
        {
            ElementHandle plotEl = _paper.CurrentParent;
            int hover = -1;

            if (_tooltip)
            {
                TrackPointer(plotBox, plotEl);

                Float2 size = _paper.GetElementStorage(plotEl, PlotSizeKey, new Float2(0f, 0f));
                if (size.X > 0f && TryGetPointer(plotEl, out Float2 pointer))
                {
                    hover = HitTest(size.X * 0.5f, size.Y * 0.5f, Radius(size.X, size.Y), pointer);
                    if (hover >= 0)
                    {
                        var rows = new List<(Color Color, string Text)>();
                        foreach (RadarSeries s in _series)
                            if (!s.Hidden && hover < s.Values.Count && IsFinite(s.Values[hover]))
                                rows.Add((s.Color, $"{s.Label}: {FormatValue(s.Values[hover])}"));

                        Popup(pointer.X, Math.Clamp(pointer.Y + 8f, 0f, size.Y), 0f, size.X, SpokeName(hover), rows);
                    }
                }
            }

            _paper.Draw((canvas, rect) => Paint(canvas, rect, plotEl, hover, min, max));
        }
    }

    private int HitTest(float cx, float cy, float radius, Float2 pointer)
    {
        float dx = pointer.X - cx, dy = pointer.Y - cy;
        float reach = radius * HitRadiusScale;
        if (dx * dx + dy * dy > reach * reach) return -1;

        int spokes = Spokes;
        float turn = (MathF.Atan2(dy, dx) + MathF.PI * 0.5f) / MathF.Tau;
        turn -= MathF.Floor(turn);
        return (int)MathF.Round(turn * spokes) % spokes;
    }

    private void Paint(Canvas canvas, Rect rect, ElementHandle plotEl, int hover, double min, double max)
    {
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        if (w < 4f || h < 4f) return;

        _paper.SetElementStorage(plotEl, PlotSizeKey, new Float2(w, h));

        int spokes = Spokes;
        float cx = (float)rect.Min.X + w * 0.5f, cy = (float)rect.Min.Y + h * 0.5f;
        float radius = Radius(w, h);
        var pts = new Float2[spokes];

        canvas.BeginPath();
        for (int t = 1; t <= _ticks; t++)
        {
            float r = radius * t / _ticks;
            for (int i = 0; i < spokes; i++)
            {
                float a = SpokeAngle(i, spokes);
                float x = cx + MathF.Cos(a) * r, y = cy + MathF.Sin(a) * r;
                if (i == 0) canvas.MoveTo(x, y); else canvas.LineTo(x, y);
            }
            canvas.ClosePath();
        }
        for (int i = 0; i < spokes; i++)
        {
            if (i == hover) continue;
            float a = SpokeAngle(i, spokes);
            canvas.MoveTo(cx, cy);
            canvas.LineTo(cx + MathF.Cos(a) * radius, cy + MathF.Sin(a) * radius);
        }
        canvas.SetStrokeColor(ToC32(_theme.BorderSoft, 0.6f));
        canvas.SetStrokeWidth(1f);
        canvas.Stroke();

        if (hover >= 0)
        {
            float a = SpokeAngle(hover, spokes);
            canvas.BeginPath();
            canvas.MoveTo(cx, cy);
            canvas.LineTo(cx + MathF.Cos(a) * radius, cy + MathF.Sin(a) * radius);
            canvas.SetStrokeColor(ToC32(_theme.Ink.C500));
            canvas.SetStrokeWidth(1.5f);
            canvas.Stroke();
        }

        foreach (RadarSeries s in _series)
        {
            if (s.Hidden) continue;

            for (int i = 0; i < spokes; i++)
            {
                double v = i < s.Values.Count && IsFinite(s.Values[i]) ? s.Values[i] : min;
                float r = (float)Math.Clamp((v - min) / (max - min), 0d, 1d) * radius;
                float a = SpokeAngle(i, spokes);
                pts[i] = new Float2(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r);
            }

            canvas.BeginPath();
            canvas.MoveTo(pts[0].X, pts[0].Y);
            for (int i = 1; i < spokes; i++) canvas.LineTo(pts[i].X, pts[i].Y);
            canvas.ClosePath();

            if (s.Fill)
            {
                canvas.SetFillColor(ToC32(s.Color, FillAlpha));
                canvas.FillComplexAA();
            }

            canvas.SetStrokeColor(ToC32(s.Color));
            canvas.SetStrokeWidth(s.StrokeWidth);
            canvas.Stroke();

            canvas.BeginPath();
            foreach (Float2 p in pts) canvas.Circle(p.X, p.Y, VertexRadius);
            canvas.SetFillColor(ToC32(s.Color));
            canvas.Fill();
        }

        for (int t = 1; t <= _ticks; t++)
            DrawText(canvas, FormatValue(min + (max - min) * t / _ticks), cx + 4f, cy - radius * t / _ticks, 0f, 0.5f);

        if (!_labels) return;

        for (int i = 0; i < spokes; i++)
        {
            float a = SpokeAngle(i, spokes);
            float r = radius * LabelRadiusScale;
            DrawText(canvas, SpokeName(i), cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r,
                0.5f - 0.5f * MathF.Cos(a), 0.5f - 0.5f * MathF.Sin(a));
        }
    }
}
