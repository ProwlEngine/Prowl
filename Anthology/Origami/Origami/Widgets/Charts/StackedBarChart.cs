// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Quill;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Cartesian stacked bar chart. The x axis is a sequence of categorical bands, one per point index.
/// Series sharing a stack group accumulate on top of each other within a band, while separate stack
/// groups sit side by side. Positive and negative values accumulate away from the zero baseline
/// independently, so negatives stack downwards.
/// </summary>
public sealed class StackedBarChart<T> : CartesianCore<StackedBarChart<T>, T>
{
    internal StackedBarChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private readonly List<string> _stacks = new();
    private float _barWidth = 0.8f;

    private const float CornerRadius = 2f;
    private const float MinRoundedHeight = 4f;
    private const string DefaultStack = "";

    protected override bool BandedX => true;
    protected override bool TickPerBand => true;

    /// <summary>Fraction of a band, in (0, 1], filled by the whole group of stacked bars. Default 0.8.</summary>
    public StackedBarChart<T> BarWidth(float width) { _barWidth = Math.Clamp(width, 0.001f, 1f); return this; }

    /// <summary>Assigns the most recently added series to the named stack group. Stack keys are recorded
    /// in call order and matched to series in the order the series were added, so this must be called
    /// immediately after the <c>.Series(...)</c> it applies to, and once per series if it is used at all.
    /// Series with no key belong to a single shared default group.</summary>
    public StackedBarChart<T> Stack(string groupKey) { _stacks.Add(groupKey ?? DefaultStack); return this; }

    private string StackOf(int seriesIndex) => seriesIndex < _stacks.Count ? _stacks[seriesIndex] : DefaultStack;

    private List<List<CartesianSeries<T>>> BuildGroups(IReadOnlyList<CartesianSeries<T>> series)
    {
        var keys = new List<string>();
        var groups = new List<List<CartesianSeries<T>>>();

        for (int i = 0; i < series.Count; i++)
        {
            CartesianSeries<T> s = series[i];
            if (!s.EffectiveVisible || s.Points.Count == 0) continue;

            string key = StackOf(i);
            int g = keys.IndexOf(key);
            if (g < 0)
            {
                keys.Add(key);
                groups.Add(new List<CartesianSeries<T>>());
                g = groups.Count - 1;
            }
            groups[g].Add(s);
        }

        return groups;
    }

    private static double StackSegment(double value, ref double posOffset, ref double negOffset, out double to)
    {
        double from;
        if (value >= 0d) { from = posOffset; to = posOffset + value; posOffset = to; }
        else { from = negOffset; to = negOffset + value; negOffset = to; }
        return from;
    }

    protected override void OnBeforeShow()
    {
        if (HasExplicitYRange) return;

        List<List<CartesianSeries<T>>> groups = BuildGroups(SeriesList);
        if (groups.Count == 0) return;

        int maxN = 0;
        foreach (List<CartesianSeries<T>> members in groups)
            foreach (CartesianSeries<T> s in members)
                maxN = Math.Max(maxN, s.Points.Count);

        bool any = false;
        double highest = 0d, lowest = 0d;

        foreach (List<CartesianSeries<T>> members in groups)
        {
            for (int i = 0; i < maxN; i++)
            {
                double posOffset = 0d, negOffset = 0d;

                foreach (CartesianSeries<T> s in members)
                {
                    if (!TryValue(s, i, out double v)) continue;

                    StackSegment(v, ref posOffset, ref negOffset, out _);
                    any = true;
                }

                if (posOffset > highest) highest = posOffset;
                if (negOffset < lowest) lowest = negOffset;
            }
        }

        if (!any) return;

        DeriveYRange(Math.Min(0d, lowest), Math.Max(0d, highest));
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext ctx)
    {
        if (ctx.MaxN <= 0) return;

        List<List<CartesianSeries<T>>> groups = BuildGroups(ctx.Series);
        if (groups.Count == 0) return;

        float groupWidth = ctx.BandWidth * _barWidth;
        float groupInset = (ctx.BandWidth - groupWidth) * 0.5f;
        float slotWidth = groupWidth / groups.Count;
        float barWidth = MathF.Max(1f, slotWidth);

        for (int g = 0; g < groups.Count; g++)
        {
            List<CartesianSeries<T>> members = groups[g];

            for (int i = 0; i < ctx.MaxN; i++)
            {
                float left = ctx.BandLeft(i) + groupInset + slotWidth * g;

                int lastPos = -1, lastNeg = -1;
                for (int k = 0; k < members.Count; k++)
                {
                    if (!TryValue(members[k], i, out double v)) continue;
                    if (v >= 0d) lastPos = k; else lastNeg = k;
                }

                double posOffset = 0d, negOffset = 0d;

                for (int k = 0; k < members.Count; k++)
                {
                    CartesianSeries<T> s = members[k];
                    if (!TryValue(s, i, out double v)) continue;

                    double from = StackSegment(v, ref posOffset, ref negOffset, out double to);

                    float y0 = ctx.YPos(from);
                    float y1 = ctx.YPos(to);
                    float top = MathF.Min(y0, y1);
                    float height = MathF.Max(1f, MathF.Abs(y1 - y0));

                    bool outerTop = v >= 0d && k == lastPos;
                    bool outerBottom = v < 0d && k == lastNeg;
                    float radius = height > MinRoundedHeight ? CornerRadius : 0f;

                    canvas.BeginPath();
                    if (outerTop && radius > 0f)
                        canvas.RoundedRect(left, top, barWidth, height, radius, radius, 0f, 0f);
                    else if (outerBottom && radius > 0f)
                        canvas.RoundedRect(left, top, barWidth, height, 0f, 0f, radius, radius);
                    else
                        canvas.Rect(left, top, barWidth, height);

                    canvas.SetFillColor(ToC32(s.Color ?? System.Drawing.Color.Gray));
                    canvas.Fill();
                }
            }
        }
    }

    /// <summary>Highlights the sampled band. The readout lists every segment in the band plus each
    /// multi-segment stack's total, which is the value the bar's overall height actually shows.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        List<List<CartesianSeries<T>>> groups = BuildGroups(ctx.Series);
        if (groups.Count == 0) return;

        float bandLeft = ctx.BandLeft(ctx.Index);
        SampleBand(paper, in ctx, bandLeft, ctx.BandWidth);

        var rows = new List<(Color Color, string Text)>();

        for (int g = 0; g < groups.Count; g++)
        {
            List<CartesianSeries<T>> members = groups[g];

            double posOffset = 0d, negOffset = 0d;
            int segments = 0;

            foreach (CartesianSeries<T> s in members)
            {
                if (!TryValue(s, ctx.Index, out double v)) continue;

                StackSegment(v, ref posOffset, ref negOffset, out _);
                segments++;
                rows.Add((s.Color ?? System.Drawing.Color.Gray, $"{s.Label}: {FormatValue(v)}"));
            }

            if (segments == 0) continue;

            if (segments > 1)
                rows.Add((SampleColor, $"Total: {FormatValue(posOffset + negOffset)}"));
        }

        SamplePopup(paper, in ctx, bandLeft + ctx.BandWidth, SampleHeader(ctx.Index), rows);
    }

    private static bool TryValue(CartesianSeries<T> s, int index, out double value)
    {
        value = 0d;
        if (index >= s.Points.Count) return false;
        double y = s.Points[index].Y;
        if (double.IsNaN(y) || double.IsInfinity(y)) return false;
        value = y;
        return true;
    }
}
