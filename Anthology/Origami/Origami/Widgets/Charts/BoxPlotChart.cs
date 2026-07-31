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
/// beyond them. The whole plot is a single band and the groups sit side by side in slots within it, so
/// the x axis carries no per-group tick and group identity is read off the legend instead.
/// </summary>
public sealed class BoxPlotChart<T> : DistributionCore<BoxPlotChart<T>, T>
{
    internal BoxPlotChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private readonly Dictionary<CartesianSeries<T>, DistributionSummary> _summaries = new();

    private bool _showMean;
    private bool _showMedian = true;
    private bool _showOutliers = true;

    private const float GroupWidthFactor = 0.8f;
    private const float BoxGap = 0.2f;
    private const float CapWidthFactor = 0.5f;
    private const float MeanMarkerSize = 7f;
    private const float OutlierSize = 5f;
    private const float RangePadding = 0.05f;

    protected override bool BandedX => true;
    protected override bool TickPerBand => true;

    /// <summary>Draw a marker at each group's mean alongside its median. Off by default.</summary>
    public BoxPlotChart<T> ShowMean(bool show = true) { _showMean = show; return this; }

    /// <summary>Draw the median line across each group's body. On by default.</summary>
    public BoxPlotChart<T> ShowMedian(bool show = true) { _showMedian = show; return this; }

    /// <summary>Draw a dot for every value beyond a group's 1.5*IQR fences. On by default.</summary>
    public BoxPlotChart<T> ShowOutliers(bool show = true) { _showOutliers = show; return this; }

    protected override void OnDeriveBegin(IReadOnlyList<IReadOnlyList<double>> groups) => _summaries.Clear();

    protected override void DeriveGroup(CartesianSeries<T> group, IReadOnlyList<double> values, List<double> derived)
    {
        DistributionSummary summary = ComputeSummary(values);
        _summaries[group] = summary;

        if (summary.Count == 0) return;

        derived.Add(summary.Median);
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

        float bandLeft = ctx.BandLeft(0);
        var slots = new BandSlots(ctx.BandWidth, GroupWidthFactor, BoxGap, visible.Count);

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> group = visible[k];
            DistributionSummary summary = _summaries[group];

            Color fillColor = FillColorOf(group);
            Color strokeColor = group.StrokeColor ?? group.Color ?? System.Drawing.Color.Gray;
            Color32 fill = ToC32(fillColor);
            Color32 stroke = ToC32(strokeColor);
            float strokeWidth = group.StrokeWidth ?? 1f;

            float left = slots.Left(bandLeft, k);
            float width = slots.MarkWidth;
            float cx = slots.Center(bandLeft, k);
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

    /// <summary>Highlights the single band, rings the body of the group the pointer is over, and reads
    /// out that group's five-number summary, since a box encodes values its series' y never carries.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        List<CartesianSeries<T>> visible = VisibleGroups(ctx.Series);
        if (visible.Count == 0) return;

        float bandLeft = ctx.BandLeft(ctx.Index);
        SampleBand(paper, in ctx, bandLeft, ctx.BandWidth);

        var slots = new BandSlots(ctx.BandWidth, GroupWidthFactor, BoxGap, visible.Count);
        int slot = slots.SlotAt(bandLeft, ctx.Pointer.X, visible.Count);
        CartesianSeries<T> group = visible[slot];
        DistributionSummary summary = _summaries[group];

        float width = slots.MarkWidth;
        float cx = slots.Center(bandLeft, slot);
        float top = Math.Clamp(ctx.YPos(summary.Q3), ctx.PlotT, ctx.PlotB);
        float bottom = Math.Clamp(ctx.YPos(summary.Q1), ctx.PlotT, ctx.PlotB);

        Color color = group.Color ?? System.Drawing.Color.Gray;

        SampleRing(paper, slot.ToString(), cx, (top + bottom) * 0.5f, MathF.Max(width, bottom - top), color);

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
        SamplePopup(paper, in ctx, slots.Left(bandLeft, slot) + width, header, rows);
    }
}
