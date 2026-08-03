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
/// Bar module for a <see cref="CartesianChart{T}"/>. Every visible series gets its own bar side by side,
/// grouped around each index. Bars grow from the zero baseline, so negative values hang below it. Added
/// with <c>.AddBarChart()</c>.
///
/// Unlike a standalone banded chart, a bar here is centred on the same continuous x every other module on
/// the chart plots against (<see cref="PlotContext{T}.UnitWidth"/>) rather than owning a
/// <c>[i, i + 1)</c> band of its own - so a bar for index i lines up directly under a Line or Scatter
/// module's point at that same index instead of sitting offset in an adjacent band.
/// </summary>
public sealed class BarModule<T> : CartesianModuleBase<BarModule<T>, T>
{
    internal BarModule(CartesianChart<T> chart) : base(chart) { }

    private float _barWidth = 0.8f;
    private float _barGap = 0.1f;

    private const float CornerRadius = 2f;
    private const float MinRoundedHeight = 4f;

    /// <summary>Fraction of one x unit, in (0, 1], filled by the whole group of bars at an index. Default 0.8.</summary>
    public BarModule<T> BarWidth(float width) { _barWidth = Math.Clamp(width, 0.001f, 1f); return this; }

    /// <summary>Fraction of a single bar's slot, in [0, 1), left empty as spacing between the bars of
    /// different series at the same index. Default 0.1.</summary>
    public BarModule<T> BarGap(float gap) { _barGap = Math.Clamp(gap, 0f, 0.999f); return this; }

    private static List<CartesianSeries<T>> VisibleSeries(IReadOnlyList<CartesianSeries<T>> series)
    {
        var visible = new List<CartesianSeries<T>>(series.Count);
        foreach (CartesianSeries<T> s in series)
            if (s.EffectiveVisible && s.Points.Count > 0) visible.Add(s);
        return visible;
    }

    /// <summary>Left edge in pixels of the unit-wide band centred on <paramref name="index"/>.</summary>
    private static float UnitBandLeft(in PlotContext<T> ctx, double index) => ctx.XPos(index) - ctx.UnitWidth * 0.5f;

    protected override void PaintMarks(Canvas canvas, in PlotContext<T> ctx)
    {
        if (ctx.MaxN <= 0) return;

        List<CartesianSeries<T>> visible = VisibleSeries(ctx.Series);
        if (visible.Count == 0) return;

        float baseline = Math.Clamp(ctx.YPos(0d), ctx.PlotT, ctx.PlotB);
        var slots = new BandSlots(ctx.UnitWidth, _barWidth, _barGap, visible.Count);
        float barWidth = slots.MarkWidth;

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> s = visible[k];
            Color32 fill = ToC32(s.Color ?? System.Drawing.Color.Gray);
            bool stroke = s.StrokeColor.HasValue;
            Color32 strokeCol = ToC32(s.StrokeColor ?? System.Drawing.Color.Gray);
            float strokeWidth = s.StrokeWidth ?? 1f;

            for (int i = 0; i < s.Points.Count; i++)
            {
                (double x, double value, T? _) = s.Points[i];
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;

                float valueY = ctx.YPos(value);
                float top = MathF.Min(baseline, valueY);
                float height = MathF.Max(1f, MathF.Abs(valueY - baseline));
                float left = slots.Left(UnitBandLeft(in ctx, x), k);

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

    /// <summary>Highlights the sampled index's unit band as a whole.</summary>
    protected override void AppendSample(Paper paper, in SampleContext<T> ctx, List<(Color Color, string Text)> rows)
    {
        List<CartesianSeries<T>> visible = VisibleSeries(ctx.Series);
        if (visible.Count == 0) return;

        float bandLeft = UnitBandLeft(in ctx.Plot, ctx.Index);
        SampleBand(paper, in ctx, bandLeft, ctx.UnitWidth);

        for (int k = 0; k < visible.Count; k++)
        {
            CartesianSeries<T> s = visible[k];
            if (ctx.Index >= s.Points.Count) continue;

            double value = s.Points[ctx.Index].Y;
            if (double.IsNaN(value) || double.IsInfinity(value)) continue;

            Color color = s.Color ?? System.Drawing.Color.Gray;

            rows.Add((color, $"{s.Label}: {FormatValue(value)}"));
        }
    }
}
