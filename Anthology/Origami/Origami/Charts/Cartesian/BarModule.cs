// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Bars grouped side by side around each x, growing from zero.</summary>
public sealed class BarModule<T> : CartesianModule<BarModule<T>, T>
{
    private float _barWidth = 0.8f;
    private float _barGap = 0.1f;

    private const float CornerRadius = 2f;

    internal BarModule(CartesianChart<T> chart) : base(chart) { }

    internal override float XPad => 0.5f;

    /// <summary>Fraction of one x unit filled by the whole group. Default 0.8.</summary>
    public BarModule<T> BarWidth(float width) { _barWidth = Math.Clamp(width, 0.001f, 1f); return this; }

    /// <summary>Fraction of each bar's slot left as spacing. Default 0.1.</summary>
    public BarModule<T> BarGap(float gap) { _barGap = Math.Clamp(gap, 0f, 0.999f); return this; }

    internal override void Paint(Canvas canvas, in PlotContext ctx)
    {
        int visible = 0;
        foreach (CartesianSeries<T> s in _series) if (Visible(s)) visible++;
        if (visible == 0) return;

        float group = ctx.UnitWidth * _barWidth;
        float slot = group / visible;
        float inset = slot * _barGap * 0.5f;
        float width = MathF.Max(1f, slot - inset * 2f);
        float baseline = ctx.Baseline;

        int k = 0;
        foreach (CartesianSeries<T> s in _series)
        {
            if (!Visible(s)) continue;

            canvas.SetFillColor(C32(s.Color));

            foreach ((double x, double y, T? _) in s.Points)
            {
                if (!double.IsFinite(x) || !double.IsFinite(y)) continue;

                float valueY = ctx.YPos(y);
                float top = MathF.Min(baseline, valueY);
                float height = MathF.Max(1f, MathF.Abs(valueY - baseline));
                float left = ctx.XPos(x) - group * 0.5f + slot * k + inset;

                canvas.BeginPath();
                if (height > CornerRadius * 2f) canvas.RoundedRect(left, top, width, height, CornerRadius);
                else canvas.Rect(left, top, width, height);
                canvas.Fill();
            }
            k++;
        }
    }

    internal override void PaintSample(Canvas canvas, in PlotContext ctx, CartesianSeries<T>? only, int index)
    {
        foreach (CartesianSeries<T> s in _series)
        {
            if (!Visible(s) || index >= s.Points.Count) continue;

            float unit = ctx.UnitWidth;
            canvas.BeginPath();
            canvas.Rect(ctx.XPos(s.Points[index].X) - unit * 0.5f, ctx.T, unit, ctx.B - ctx.T);
            canvas.SetFillColor(C32(_chart.SampleColor, 0.2f));
            canvas.Fill();
            return;
        }
    }
}
