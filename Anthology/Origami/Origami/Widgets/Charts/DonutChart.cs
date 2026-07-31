// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>
/// Circular donut chart. Behaves exactly like a pie, except the slices are cut off short of the centre
/// by <see cref="InnerRadius"/>, leaving a hole the chart draws nothing in.
/// </summary>
public sealed class DonutChart<T> : CircularCore<DonutChart<T>, T>
{
    internal DonutChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private float _startAngle = -90f;
    private bool _clockwise = true;
    private float _innerRadius = 0.6f;
    private HashSet<int>? _exploded;

    private const float ExplodeFraction = 0.08f;
    private const float HighlightWidth = 1.5f;

    /// <summary>Angle, in degrees clockwise from three o'clock, the first slice starts at. Defaults to
    /// -90, which puts it at twelve o'clock.</summary>
    public DonutChart<T> StartAngle(float degrees) { _startAngle = degrees; return this; }

    /// <summary>Direction the slices are laid out in around the circle. Defaults to clockwise.</summary>
    public DonutChart<T> Clockwise(bool clockwise = true) { _clockwise = clockwise; return this; }

    /// <summary>Radius of the hole as a fraction of the chart's radius, clamped into (0, 0.95].
    /// Defaults to 0.6.</summary>
    public DonutChart<T> InnerRadius(float fraction) { _innerRadius = Math.Clamp(fraction, 0.001f, 0.95f); return this; }

    /// <summary>Pulls the slice of the item at <paramref name="index"/> in the source data set out of the
    /// ring along its middle. Call once per slice to explode several of them.</summary>
    public DonutChart<T> Explode(int index) { (_exploded ??= new HashSet<int>()).Add(index); return this; }

    private bool IsExploded(CircularSlice<T> slice) => _exploded != null && _exploded.Contains(slice.Index);

    private float SweepOf(CircularSlice<T> slice) => (float)(slice.Fraction * 360d) * DegToRad * (_clockwise ? 1f : -1f);

    private float OuterRadius(in CircularContext ctx)
        => _exploded != null && _exploded.Count > 0 ? ctx.Radius * (1f - ExplodeFraction) : ctx.Radius;

    private Float2 CenterOf(in CircularContext ctx, CircularSlice<T> slice, float midAngle)
    {
        if (!IsExploded(slice)) return new Float2(ctx.CenterX, ctx.CenterY);

        float offset = OuterRadius(in ctx) * ExplodeFraction;
        return new Float2(ctx.CenterX + MathF.Cos(midAngle) * offset, ctx.CenterY + MathF.Sin(midAngle) * offset);
    }

    private bool SliceArc(in CircularContext ctx, int ordinal, out float a0, out float a1)
    {
        a0 = 0f;
        a1 = 0f;

        float cursor = _startAngle * DegToRad;

        for (int i = 0; i < ctx.Slices.Count; i++)
        {
            CircularSlice<T> slice = ctx.Slices[i];
            if (!slice.Visible || slice.Fraction <= 0d) continue;

            float sweep = SweepOf(slice);

            if (i == ordinal)
            {
                a0 = MathF.Min(cursor, cursor + sweep);
                a1 = MathF.Max(cursor, cursor + sweep);
                return true;
            }

            cursor += sweep;
        }

        return false;
    }

    protected override void PaintMarks(Canvas canvas, in CircularContext ctx)
    {
        float cursor = _startAngle * DegToRad;
        float outerR = OuterRadius(in ctx);
        float innerR = outerR * _innerRadius;
        Color32 highlight = ToC32(_theme.Ink.C100);

        for (int i = 0; i < ctx.Slices.Count; i++)
        {
            CircularSlice<T> slice = ctx.Slices[i];
            if (!slice.Visible || slice.Fraction <= 0d) continue;

            float sweep = SweepOf(slice);
            float a0 = MathF.Min(cursor, cursor + sweep);
            float a1 = MathF.Max(cursor, cursor + sweep);
            cursor += sweep;

            Float2 center = CenterOf(in ctx, slice, (a0 + a1) * 0.5f);

            PaintWedge(canvas, center.X, center.Y, innerR, outerR, a0, a1, ToC32(slice.Color));

            if (ctx.HoverIndex == i)
                StrokeWedge(canvas, center.X, center.Y, innerR, outerR, a0, a1, highlight);
        }
    }

    protected override int HitTest(in CircularContext ctx, Float2 pointer)
    {
        float outerR = OuterRadius(in ctx);
        float innerR = outerR * _innerRadius;

        for (int i = 0; i < ctx.Slices.Count; i++)
        {
            CircularSlice<T> slice = ctx.Slices[i];
            if (!slice.Visible || slice.Fraction <= 0d) continue;
            if (!SliceArc(in ctx, i, out float a0, out float a1)) continue;

            Float2 center = CenterOf(in ctx, slice, (a0 + a1) * 0.5f);
            float dx = pointer.X - center.X, dy = pointer.Y - center.Y;
            float radius = MathF.Sqrt(dx * dx + dy * dy);

            if (radius < innerR || radius > outerR) continue;
            if (InSweep(MathF.Atan2(dy, dx), a0, a1 - a0)) return i;
        }

        return -1;
    }

    protected override Float2 LabelAnchor(in CircularContext ctx, int ordinal)
    {
        if (!SliceArc(in ctx, ordinal, out float a0, out float a1))
            return new Float2(ctx.CenterX, ctx.CenterY);

        float mid = (a0 + a1) * 0.5f;
        float radius = OuterRadius(in ctx) * (1f + _innerRadius) * 0.5f;
        Float2 center = CenterOf(in ctx, ctx.Slices[ordinal], mid);

        return new Float2(center.X + MathF.Cos(mid) * radius, center.Y + MathF.Sin(mid) * radius);
    }

    private static bool InSweep(float angle, float a0, float span)
    {
        const float TwoPi = MathF.PI * 2f;

        float delta = angle - a0;
        delta -= MathF.Floor(delta / TwoPi) * TwoPi;

        return delta <= span;
    }

    private static void StrokeWedge(Canvas canvas, float cx, float cy, float innerR, float outerR,
        float a0, float a1, Color32 stroke)
    {
        if (outerR <= 0f || MathF.Abs(a1 - a0) < 1e-5f) return;

        int segments = Math.Clamp((int)MathF.Ceiling(MathF.Abs(a1 - a0) * MathF.Max(outerR, 1f) / 4f), 3, 240);

        canvas.BeginPath();
        for (int i = 0; i <= segments; i++)
        {
            float a = a0 + (a1 - a0) * i / segments;
            float x = cx + MathF.Cos(a) * outerR, y = cy + MathF.Sin(a) * outerR;
            if (i == 0) canvas.MoveTo(x, y); else canvas.LineTo(x, y);
        }
        for (int i = segments; i >= 0; i--)
        {
            float a = a0 + (a1 - a0) * i / segments;
            canvas.LineTo(cx + MathF.Cos(a) * innerR, cy + MathF.Sin(a) * innerR);
        }

        canvas.ClosePath();
        canvas.SetStrokeColor(stroke);
        canvas.SetStrokeWidth(HighlightWidth);
        canvas.Stroke();
    }
}
