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
/// Histogram of one or more groups of values. Every group is binned against one shared run of bin
/// edges spanning them all, so a band is a value range rather than a category and each group's bar
/// for that range sits beside the others inside it. Bars grow from the zero baseline.
/// </summary>
public sealed class HistogramChart<T> : DistributionCore<HistogramChart<T>, T>
{
    internal HistogramChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private int _binCount = 10;
    private float _binWidth;
    private bool _normalize;

    private BinEdges _edges;

    private const float BandFill = 0.9f;
    private const float SlotGap = 0.1f;
    private const float CornerRadius = 2f;
    private const float MinRoundedHeight = 4f;

    protected override bool BandedX => true;
    protected override bool TickPerBand => true;

    /// <summary>Number of equal bins the value range is split into. Default 10, ignored once
    /// <see cref="BinWidth"/> is set.</summary>
    public HistogramChart<T> BinCount(int count) { _binCount = Math.Max(1, count); return this; }

    /// <summary>Width of one bin in value units, which fixes the bin size and lets the count follow the
    /// data range. Takes precedence over <see cref="BinCount"/>; pass zero to go back to it.</summary>
    public HistogramChart<T> BinWidth(float width) { _binWidth = MathF.Max(0f, width); return this; }

    /// <summary>Plot each bin as its fraction of its own group's total rather than as a raw count, so
    /// groups of different sizes stay comparable. Default false.</summary>
    public HistogramChart<T> Normalize(bool normalize = true) { _normalize = normalize; return this; }

    protected override void OnDeriveBegin(IReadOnlyList<IReadOnlyList<double>> groups)
        => _edges = ComputeBinEdges(groups, _binCount, _binWidth);

    protected override void DeriveGroup(CartesianSeries<T> group, IReadOnlyList<double> values, List<double> derived)
    {
        int[] counts = ComputeBinCounts(values, in _edges);

        double total = 0d;
        foreach (int c in counts) total += c;

        double scale = _normalize && total > 0d ? 1d / total : 1d;
        foreach (int c in counts) derived.Add(c * scale);
    }

    private static List<CartesianSeries<T>> VisibleGroups(IReadOnlyList<CartesianSeries<T>> series)
    {
        var visible = new List<CartesianSeries<T>>(series.Count);
        foreach (CartesianSeries<T> s in series)
            if (s.EffectiveVisible && s.Points.Count > 0) visible.Add(s);
        return visible;
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext<T> ctx)
    {
        if (ctx.MaxN <= 0) return;

        List<CartesianSeries<T>> visible = VisibleGroups(ctx.Series);
        if (visible.Count == 0) return;

        float baseline = Math.Clamp(ctx.YPos(0d), ctx.PlotT, ctx.PlotB);
        var slots = new BandSlots(ctx.BandWidth, BandFill, SlotGap, visible.Count);
        float barWidth = slots.MarkWidth;

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> s = visible[k];
            Color32 fill = ToC32(FillColorOf(s));
            bool stroke = s.StrokeColor.HasValue;
            Color32 strokeCol = ToC32(s.StrokeColor ?? System.Drawing.Color.Gray);
            float strokeWidth = s.StrokeWidth ?? 1f;

            for (int i = 0; i < s.Points.Count && i < ctx.MaxN; i++)
            {
                double value = s.Points[i].Y;
                if (!IsFinite(value)) continue;

                float valueY = ctx.YPos(value);
                float top = MathF.Min(baseline, valueY);
                float height = MathF.Max(1f, MathF.Abs(valueY - baseline));
                float left = slots.Left(ctx.BandLeft(i), k);

                canvas.BeginPath();
                if (height > MinRoundedHeight)
                    canvas.RoundedRect(left, top, barWidth, height, CornerRadius);
                else
                    canvas.Rect(left, top, barWidth, height);

                canvas.SetFillColor(fill);
                canvas.Fill();

                if (stroke)
                {
                    canvas.SetStrokeColor(strokeCol);
                    canvas.SetStrokeWidth(strokeWidth);
                    canvas.Stroke();
                }
            }
        }
    }

    /// <summary>Highlights the sampled bin. The popup header names the bin's value range rather than
    /// its index, since a histogram band is a range of values.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext<T> ctx)
    {
        List<CartesianSeries<T>> visible = VisibleGroups(ctx.Series);
        if (visible.Count == 0) return;

        float bandLeft = ctx.BandLeft(ctx.Index);
        SampleBand(paper, in ctx, bandLeft, ctx.BandWidth);

        var rows = new List<(Color Color, string Text)>();

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> s = visible[k];
            if (ctx.Index >= s.Points.Count) continue;

            double value = s.Points[ctx.Index].Y;
            if (!IsFinite(value)) continue;

            Color color = FillColorOf(s);

            rows.Add((color, s.Label.Length > 0 ? $"{s.Label}: {FormatValue(value)}" : FormatValue(value)));
        }

        SamplePopup(paper, in ctx, bandLeft + ctx.BandWidth, BinRangeHeader(ctx.Index), rows);
    }

    private string BinRangeHeader(int index)
    {
        if (index < 0 || index >= _edges.Count) return "";
        return $"{FormatValue(_edges.Edge(index))} - {FormatValue(_edges.Edge(index + 1))}";
    }
}
