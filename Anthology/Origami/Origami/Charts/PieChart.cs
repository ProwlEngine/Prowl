// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Pie chart, or a donut once <see cref="InnerRadius"/> is above zero.</summary>
public sealed class PieChart<T> : ChartCore<PieChart<T>, T>
{
    private sealed class Slice
    {
        public string Label = "";
        public double Value;
        public Color Color;
        public int Index;
        public bool Hidden;
        public double Fraction;
        public float A0, A1;
    }

    private readonly List<Slice> _slices = new();

    private Func<T, string>? _nameSelector;
    private Func<T, double>? _valueSelector;
    private Func<T, int, Color>? _colorFunction;
    private Func<T, IComparable>? _sortKey;
    private bool _sortDescending;
    private bool _tooltip = true;
    private bool _labels = true;
    private bool _showPercent;
    private bool _showValues;
    private bool _clockwise = true;
    private float _innerRadius;

    private const string PlotSizeKey = "pie_size";
    private const float RadiusInset = 8f;
    private const float HighlightWidth = 1.5f;
    private const double MinLabelFraction = 0.02d;

    internal PieChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    public PieChart<T> Name(Func<T, string> selector) { _nameSelector = selector; return this; }

    /// <summary>Slice size. Non-finite values count as zero.</summary>
    public PieChart<T> Value(Func<T, double> selector) { _valueSelector = selector; return this; }

    /// <summary>Slice colour from the item and its index in the data set.</summary>
    public PieChart<T> ColorFunction(Func<T, int, Color> selector) { _colorFunction = selector; return this; }

    public PieChart<T> SortBy(Func<T, IComparable> key, bool descending = false) { _sortKey = key; _sortDescending = descending; return this; }
    public PieChart<T> Tooltip(bool show = true) { _tooltip = show; return this; }
    public PieChart<T> Labels(bool show = true) { _labels = show; return this; }
    public PieChart<T> ShowPercent(bool show = true) { _showPercent = show; return this; }
    public PieChart<T> ShowValues(bool show = true) { _showValues = show; return this; }
    public PieChart<T> Clockwise(bool clockwise = true) { _clockwise = clockwise; return this; }

    /// <summary>Hole radius as a fraction of the outer radius, in [0, 0.95]. Default 0.</summary>
    public PieChart<T> InnerRadius(float fraction) { _innerRadius = Math.Clamp(fraction, 0f, 0.95f); return this; }

    protected override IReadOnlyList<LegendEntry> BuildLegendEntries()
    {
        _slices.Clear();
        if (_data != null)
        {
            IEnumerable<int> order = Enumerable.Range(0, _data.Count);
            if (_sortKey != null)
                order = _sortDescending ? order.OrderByDescending(i => _sortKey(_data[i])) : order.OrderBy(i => _sortKey(_data[i]));

            foreach (int i in order)
            {
                T item = _data[i];
                double value = _valueSelector != null ? _valueSelector(item) : 0d;
                _slices.Add(new Slice
                {
                    Label = _nameSelector?.Invoke(item) ?? "",
                    Value = IsFinite(value) ? value : 0d,
                    Color = _colorFunction != null ? _colorFunction(item, i) : RampColor(_slices.Count),
                    Index = i,
                    Hidden = IsLegendHidden(i),
                });
            }
        }

        double magnitude = 0d;
        foreach (Slice s in _slices) if (!s.Hidden) magnitude += Math.Abs(s.Value);

        float cursor = -MathF.PI * 0.5f;
        foreach (Slice s in _slices)
        {
            s.Fraction = magnitude > 0d && !s.Hidden ? Math.Abs(s.Value) / magnitude : 0d;
            float sweep = (float)(s.Fraction * Math.Tau) * (_clockwise ? 1f : -1f);
            s.A0 = MathF.Min(cursor, cursor + sweep);
            s.A1 = MathF.Max(cursor, cursor + sweep);
            cursor += sweep;
        }

        var entries = new List<LegendEntry>(_slices.Count);
        foreach (Slice s in _slices)
            entries.Add(new LegendEntry(SliceName(s), s.Color, s.Index, LegendShowValueEnabled ? FormatValue(s.Value) : null, s.Hidden));
        return entries;
    }

    private static string SliceName(Slice s) => s.Label.Length > 0 ? s.Label : "Slice " + s.Index;

    protected override void DrawPlot()
    {
        bool any = false;
        foreach (Slice s in _slices) any |= s.Fraction > 0d;
        if (!any)
        {
            DrawEmpty();
            return;
        }

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
                        Slice s = _slices[hover];
                        var rows = new List<(Color Color, string Text)> { (s.Color, FormatValue(s.Value)) };
                        if (_showPercent) rows.Add((s.Color, Percent(s)));
                        Popup(pointer.X, Math.Clamp(pointer.Y + 8f, 0f, size.Y), 0f, size.X, SliceName(s), rows);
                    }
                }
            }

            _paper.Draw((canvas, rect) => Paint(canvas, rect, plotEl, hover));
        }
    }

    private static float Radius(float w, float h) => MathF.Max(1f, 0.5f * MathF.Min(w, h) - RadiusInset);

    private static string Percent(Slice s) => (s.Fraction * 100d).ToString("0.#") + "%";

    private int HitTest(float cx, float cy, float outer, Float2 pointer)
    {
        float dx = pointer.X - cx, dy = pointer.Y - cy;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        if (r > outer || r < outer * _innerRadius) return -1;

        float angle = MathF.Atan2(dy, dx);
        for (int i = 0; i < _slices.Count; i++)
        {
            Slice s = _slices[i];
            if (s.Fraction <= 0d) continue;

            float delta = angle - s.A0;
            delta -= MathF.Floor(delta / MathF.Tau) * MathF.Tau;
            if (delta <= s.A1 - s.A0) return i;
        }
        return -1;
    }

    private void Paint(Canvas canvas, Rect rect, ElementHandle plotEl, int hover)
    {
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        if (w < 4f || h < 4f) return;

        _paper.SetElementStorage(plotEl, PlotSizeKey, new Float2(w, h));

        float cx = (float)rect.Min.X + w * 0.5f, cy = (float)rect.Min.Y + h * 0.5f;
        float outer = Radius(w, h);
        float inner = outer * _innerRadius;

        for (int i = 0; i < _slices.Count; i++)
        {
            Slice s = _slices[i];
            if (s.Fraction <= 0d) continue;

            WedgePath(canvas, cx, cy, inner, outer, s.A0, s.A1);
            canvas.SetFillColor(ToC32(s.Color));
            canvas.FillComplexAA();

            if (i == hover)
            {
                canvas.SetStrokeColor(ToC32(_theme.Ink.C100));
                canvas.SetStrokeWidth(HighlightWidth);
                canvas.Stroke();
            }
        }

        if (!_labels) return;

        float labelR = inner > 0f ? (inner + outer) * 0.5f : outer * 0.68f;
        foreach (Slice s in _slices)
        {
            if (s.Fraction < MinLabelFraction) continue;

            string text = s.Label;
            if (_showValues) text += " " + FormatValue(s.Value);
            if (_showPercent) text += " (" + Percent(s) + ")";

            float mid = (s.A0 + s.A1) * 0.5f;
            DrawText(canvas, text, cx + MathF.Cos(mid) * labelR, cy + MathF.Sin(mid) * labelR, 0.5f, 0.5f);
        }
    }

    private static void WedgePath(Canvas canvas, float cx, float cy, float inner, float outer, float a0, float a1)
    {
        int segments = Math.Clamp((int)MathF.Ceiling((a1 - a0) * MathF.Max(outer, 1f) / 4f), 3, 240);

        canvas.BeginPath();
        if (inner <= 0f) canvas.MoveTo(cx, cy);

        for (int i = 0; i <= segments; i++)
        {
            float a = a0 + (a1 - a0) * i / segments;
            float x = cx + MathF.Cos(a) * outer, y = cy + MathF.Sin(a) * outer;
            if (i == 0 && inner > 0f) canvas.MoveTo(x, y);
            else canvas.LineTo(x, y);
        }

        if (inner > 0f)
            for (int i = segments; i >= 0; i--)
            {
                float a = a0 + (a1 - a0) * i / segments;
                canvas.LineTo(cx + MathF.Cos(a) * inner, cy + MathF.Sin(a) * inner);
            }

        canvas.ClosePath();
    }
}
