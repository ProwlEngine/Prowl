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
/// Cartesian bar chart. The x axis is a sequence of categorical bands, one per point index, and every
/// visible series gets its own bar side by side inside each band. Bars grow from the zero baseline, so
/// negative values hang below it.
/// </summary>
public sealed class BarChart<T> : CartesianCore<BarChart<T>, T>
{
    internal BarChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private float _barWidth = 0.8f;
    private float _barGap = 0.1f;

    private const float CornerRadius = 2f;
    private const float MinRoundedHeight = 4f;

    protected override bool BandedX => true;
    protected override bool TickPerBand => true;

    /// <summary>Fraction of a band, in (0, 1], filled by the whole group of bars. Default 0.8.</summary>
    public BarChart<T> BarWidth(float width) { _barWidth = Math.Clamp(width, 0.001f, 1f); return this; }

    /// <summary>Fraction of a single bar's slot, in [0, 1), left empty as spacing between the bars of
    /// different series within the same band. Default 0.1.</summary>
    public BarChart<T> BarGap(float gap) { _barGap = Math.Clamp(gap, 0f, 0.999f); return this; }

    private static List<CartesianSeries<T>> VisibleSeries(IReadOnlyList<CartesianSeries<T>> series)
    {
        var visible = new List<CartesianSeries<T>>(series.Count);
        foreach (CartesianSeries<T> s in series)
            if (s.EffectiveVisible && s.Points.Count > 0) visible.Add(s);
        return visible;
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext ctx)
    {
        if (ctx.MaxN <= 0) return;

        List<CartesianSeries<T>> visible = VisibleSeries(ctx.Series);
        if (visible.Count == 0) return;

        float baseline = Math.Clamp(ctx.YPos(0d), ctx.PlotT, ctx.PlotB);
        var slots = new BandSlots(ctx.BandWidth, _barWidth, _barGap, visible.Count);
        float barWidth = slots.MarkWidth;

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> s = visible[k];
            Color32 fill = ToC32(s.Color ?? System.Drawing.Color.Gray);
            bool stroke = s.StrokeColor.HasValue;
            Color32 strokeCol = ToC32(s.StrokeColor ?? System.Drawing.Color.Gray);
            float strokeWidth = s.StrokeWidth ?? 1f;

            for (int i = 0; i < s.Points.Count && i < ctx.MaxN; i++)
            {
                double value = s.Points[i].Y;
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;

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

    /// <summary>Highlights the sampled band as a whole and marks the top of each series' bar inside it,
    /// since a bar's value is read off its end rather than off a point on a line.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        List<CartesianSeries<T>> visible = VisibleSeries(ctx.Series);
        if (visible.Count == 0) return;

        float bandLeft = ctx.BandLeft(ctx.Index);
        SampleBand(paper, in ctx, bandLeft, ctx.BandWidth);

        var slots = new BandSlots(ctx.BandWidth, _barWidth, _barGap, visible.Count);
        var rows = new List<(Color Color, string Text)>();

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> s = visible[k];
            if (ctx.Index >= s.Points.Count) continue;

            double value = s.Points[ctx.Index].Y;
            if (double.IsNaN(value) || double.IsInfinity(value)) continue;

            Color color = s.Color ?? System.Drawing.Color.Gray;
            float cx = slots.Center(bandLeft, k);

            SampleDot(paper, k.ToString(), cx, Math.Clamp(ctx.YPos(value), ctx.PlotT, ctx.PlotB), color);
            rows.Add((color, $"{s.Label}: {FormatValue(value)}"));
        }

        SamplePopup(paper, in ctx, bandLeft + ctx.BandWidth, SampleHeader(ctx.Index), rows);
    }
}
