// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Box-and-whisker chart. Every group is summarised into a five-number summary and drawn as a Q1..Q3
/// body with whiskers reaching the most extreme values inside the 1.5*IQR fences, plus a dot per value
/// beyond them. Each group owns one x band and its box is centred in it, so the axis carries a tick per
/// group labelled with that group's name and hiding a group leaves its band empty rather than reflowing
/// the rest.
/// </summary>
public sealed class BoxPlotChart<T> : DistributionCore<BoxPlotChart<T>, T>
{
    internal BoxPlotChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private readonly Dictionary<CartesianSeries<T>, DistributionSummary> _summaries = new();
    private readonly List<string> _bandLabels = new();

    private bool _showMean;
    private bool _showMedian = true;
    private bool _showOutliers = true;
    private float _boxWidth = 0.5f;
    private float _maxBoxWidth = 56f;

    private const float CapWidthFactor = 0.5f;
    private const float MeanMarkerSize = 7f;
    private const float OutlierSize = 5f;
    private const float RangePadding = 0.05f;

    protected override bool BandedX => true;
    protected override bool TickPerBand => true;

    protected override string DefaultXTickLabel(int index)
        => index >= 0 && index < _bandLabels.Count ? _bandLabels[index] : "";

    /// <summary>Draw a marker at each group's mean alongside its median. Off by default.</summary>
    public BoxPlotChart<T> ShowMean(bool show = true) { _showMean = show; return this; }

    /// <summary>Draw the median line across each group's body. On by default.</summary>
    public BoxPlotChart<T> ShowMedian(bool show = true) { _showMedian = show; return this; }

    /// <summary>Draw a dot for every value beyond a group's 1.5*IQR fences. On by default.</summary>
    public BoxPlotChart<T> ShowOutliers(bool show = true) { _showOutliers = show; return this; }

    /// <summary>Fraction of a band, in (0, 1], filled by that band's box. Default 0.5.</summary>
    public BoxPlotChart<T> BoxWidth(float width) { _boxWidth = Math.Clamp(width, 0.001f, 1f); return this; }

    /// <summary>Upper bound in pixels on a box's width, so boxes stay readable on a wide chart with few
    /// groups instead of growing with the band. Default 56; pass zero to let the band decide alone.</summary>
    public BoxPlotChart<T> MaxBoxWidth(float width) { _maxBoxWidth = MathF.Max(0f, width); return this; }

    protected override void OnDeriveBegin(IReadOnlyList<IReadOnlyList<double>> groups)
    {
        _summaries.Clear();
        _bandLabels.Clear();
    }

    /// <summary>Pads with blanks so that the group's median lands at x = its own ordinal, which gives every
    /// group a band of its own rather than stacking them all into band zero.</summary>
    protected override void DeriveGroup(CartesianSeries<T> group, IReadOnlyList<double> values, List<double> derived)
    {
        DistributionSummary summary = ComputeSummary(values);
        _summaries[group] = summary;

        int band = BandOf(group);
        while (_bandLabels.Count <= band) _bandLabels.Add("");
        _bandLabels[band] = group.Label;

        if (summary.Count == 0) return;

        for (int i = 0; i < band; i++) derived.Add(double.NaN);
        derived.Add(summary.Median);
    }

    private int BandOf(CartesianSeries<T> group)
    {
        for (int i = 0; i < SeriesList.Count; i++)
            if (ReferenceEquals(SeriesList[i], group)) return i;
        return 0;
    }

    private void BoxBounds(float bandLeft, float bandWidth, out float left, out float width, out float cx)
    {
        width = bandWidth * _boxWidth;
        if (_maxBoxWidth > 0f) width = MathF.Min(width, _maxBoxWidth);
        width = MathF.Max(1f, width);

        cx = bandLeft + bandWidth * 0.5f;
        left = cx - width * 0.5f;
    }

    protected override void OnDeriveEnd()
    {
        IncludeZero(false);

        if (HasExplicitYRange) return;

        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (DistributionSummary summary in _summaries.Values)
        {
            if (summary.Count == 0) continue;

            Extend(ref min, ref max, summary.Q1);
            Extend(ref min, ref max, summary.Q3);
            Extend(ref min, ref max, summary.LowerWhisker);
            Extend(ref min, ref max, summary.UpperWhisker);

            if (_showMean) Extend(ref min, ref max, summary.Mean);

            if (_showOutliers)
                foreach (double v in summary.Outliers)
                    Extend(ref min, ref max, v);
        }

        if (min > max) return;

        double span = max - min;
        if (span <= 0d) span = Math.Abs(max) > 0d ? Math.Abs(max) : 1d;
        double pad = span * RangePadding;

        DeriveYRange(min - pad, max + pad);
    }

    private static void Extend(ref double min, ref double max, double v)
    {
        if (!IsFinite(v)) return;
        if (v < min) min = v;
        if (v > max) max = v;
    }

    private List<CartesianSeries<T>> VisibleGroups(IReadOnlyList<CartesianSeries<T>> series)
    {
        var visible = new List<CartesianSeries<T>>(series.Count);
        foreach (CartesianSeries<T> s in series)
            if (s.EffectiveVisible && _summaries.TryGetValue(s, out DistributionSummary summary) && summary.Count > 0)
                visible.Add(s);
        return visible;
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext ctx)
    {
        if (ctx.MaxN <= 0) return;

        List<CartesianSeries<T>> visible = VisibleGroups(ctx.Series);
        if (visible.Count == 0) return;

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> group = visible[k];
            DistributionSummary summary = _summaries[group];

            Color fillColor = FillColorOf(group);
            Color strokeColor = group.StrokeColor ?? group.Color ?? System.Drawing.Color.Gray;
            Color32 fill = ToC32(fillColor);
            Color32 stroke = ToC32(strokeColor);
            float strokeWidth = group.StrokeWidth ?? 1f;

            BoxBounds(ctx.BandLeft(BandOf(group)), ctx.BandWidth, out float left, out float width, out float cx);
            float capHalf = MathF.Max(1f, width * CapWidthFactor) * 0.5f;

            float top = ctx.YPos(summary.Q3);
            float bottom = ctx.YPos(summary.Q1);
            float height = MathF.Max(1f, bottom - top);

            canvas.SetStrokeColor(stroke);
            canvas.SetStrokeWidth(strokeWidth);

            canvas.BeginPath();
            canvas.MoveTo(cx, top);
            canvas.LineTo(cx, ctx.YPos(summary.UpperWhisker));
            canvas.MoveTo(cx - capHalf, ctx.YPos(summary.UpperWhisker));
            canvas.LineTo(cx + capHalf, ctx.YPos(summary.UpperWhisker));
            canvas.MoveTo(cx, bottom);
            canvas.LineTo(cx, ctx.YPos(summary.LowerWhisker));
            canvas.MoveTo(cx - capHalf, ctx.YPos(summary.LowerWhisker));
            canvas.LineTo(cx + capHalf, ctx.YPos(summary.LowerWhisker));
            canvas.Stroke();

            canvas.BeginPath();
            canvas.Rect(left, top, width, height);
            canvas.SetFillColor(fill);
            canvas.Fill();
            canvas.SetStrokeColor(stroke);
            canvas.SetStrokeWidth(strokeWidth);
            canvas.Stroke();

            if (_showMedian)
            {
                float medianY = ctx.YPos(summary.Median);

                canvas.BeginPath();
                canvas.MoveTo(left, medianY);
                canvas.LineTo(left + width, medianY);
                canvas.SetStrokeColor(stroke);
                canvas.SetStrokeWidth(MathF.Max(1f, strokeWidth * 2f));
                canvas.Stroke();
            }

            if (_showMean)
            {
                float meanY = ctx.YPos(summary.Mean);
                float r = MeanMarkerSize * 0.5f;

                canvas.BeginPath();
                canvas.MoveTo(cx, meanY - r);
                canvas.LineTo(cx + r, meanY);
                canvas.LineTo(cx, meanY + r);
                canvas.LineTo(cx - r, meanY);
                canvas.ClosePath();
                canvas.SetFillColor(stroke);
                canvas.Fill();
            }

            if (!_showOutliers) continue;

            foreach (double v in summary.Outliers)
            {
                if (!IsFinite(v)) continue;

                canvas.BeginPath();
                canvas.Circle(cx, ctx.YPos(v), OutlierSize * 0.5f);
                canvas.SetFillColor(fill);
                canvas.Fill();
                canvas.SetStrokeColor(stroke);
                canvas.SetStrokeWidth(strokeWidth);
                canvas.Stroke();
            }
        }
    }

    /// <summary>Highlights the sampled group's band, rings its body, and reads out its five-number
    /// summary, since a box encodes values its series' y never carries.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        CartesianSeries<T>? group = null;
        foreach (CartesianSeries<T> s in VisibleGroups(ctx.Series))
            if (BandOf(s) == ctx.Index) { group = s; break; }

        if (group == null) return;

        DistributionSummary summary = _summaries[group];

        float bandLeft = ctx.BandLeft(ctx.Index);
        SampleBand(paper, in ctx, bandLeft, ctx.BandWidth);

        BoxBounds(bandLeft, ctx.BandWidth, out float left, out float width, out float cx);
        float top = Math.Clamp(ctx.YPos(summary.Q3), ctx.PlotT, ctx.PlotB);
        float bottom = Math.Clamp(ctx.YPos(summary.Q1), ctx.PlotT, ctx.PlotB);

        Color color = group.Color ?? System.Drawing.Color.Gray;

        SampleRing(paper, ctx.Index.ToString(), cx, (top + bottom) * 0.5f, MathF.Max(width, bottom - top), color);

        var rows = new List<(Color Color, string Text)>
        {
            (color, $"Max: {FormatValue(summary.Max)}"),
            (color, $"Q3: {FormatValue(summary.Q3)}"),
            (color, $"Median: {FormatValue(summary.Median)}"),
            (color, $"Q1: {FormatValue(summary.Q1)}"),
            (color, $"Min: {FormatValue(summary.Min)}"),
        };

        if (_showMean)
            rows.Add((color, $"Mean: {FormatValue(summary.Mean)}"));

        string header = group.Label.Length > 0 ? group.Label : SampleHeader(ctx.Index);
        SamplePopup(paper, in ctx, left + width, header, rows);
    }
}
