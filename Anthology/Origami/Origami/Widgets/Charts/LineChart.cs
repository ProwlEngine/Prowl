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
/// Cartesian line chart. Plots one or more <see cref="CartesianSeries"/> as a stroked polyline
/// Can be smoothed and filled.
/// </summary>
public sealed class LineChart<T> : CartesianCore<LineChart<T>, T>
{
    internal LineChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private CartesianInterpolation _interpolation = CartesianInterpolation.Linear;

    public LineChart<T> Smooth() => Interpolation(CartesianInterpolation.Smooth);

    public LineChart<T> Interpolation(CartesianInterpolation interpolation) { _interpolation = interpolation; return this; }

    private const int SmoothStepsPerSegment = 12;


    protected override void PaintMarks(Canvas canvas, in PlotContext ctx)
    {
        foreach (var s in ctx.Series)
        {
            if (!s.EffectiveVisible || s.Points.Count == 0) continue;

            List<Float2> pts = BuildPathPoints(s, in ctx);
            if (pts.Count == 0) continue;

            Color strokeColor = s.StrokeColor ?? s.Color ?? System.Drawing.Color.Gray;
            float strokeWidth = s.StrokeWidth ?? 1.5f;

            if (pts.Count == 1)
            {
                canvas.BeginPath();
                canvas.Circle(pts[0].X, pts[0].Y, MathF.Max(2f, strokeWidth * 1.25f));
                canvas.SetFillColor(ToC32(strokeColor));
                canvas.Fill();
                continue;
            }

            if (s.Fill)
                PaintFill(canvas, in ctx, s, pts, strokeColor);

            PaintStroke(canvas, pts, strokeColor, strokeWidth, s.Dash);
        }
    }


    /// <summary>Crosshair through the sampled x with a dot where every visible series' line crosses it,
    /// and a readout of each series' value there.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        CartesianSeries<T>? longest = LongestVisible(ctx.Series);
        if (longest == null || ctx.Index >= longest.Points.Count) return;

        float lx = ctx.XPos(longest.Points[ctx.Index].X);
        SampleLine(paper, in ctx, lx);

        var rows = new List<(Color Color, string Text)>();

        for (int i = 0; i < ctx.Series.Count; i++)
        {
            CartesianSeries<T> s = ctx.Series[i];
            if (!s.EffectiveVisible || ctx.Index >= s.Points.Count) continue;

            double value = s.Points[ctx.Index].Y;
            Color color = s.Color ?? System.Drawing.Color.Gray;

            SampleDot(paper, i.ToString(), lx, Math.Clamp(ctx.YPos(value), ctx.PlotT, ctx.PlotB), color);
            rows.Add((color, $"{s.Label}: {FormatValue(value)}"));
        }

        SamplePopup(paper, in ctx, lx, SampleHeader(ctx.Index), rows);
    }


    private List<Float2> BuildPathPoints(CartesianSeries<T> s, in PlotContext ctx)
    {
        var raw = new List<Float2>(s.Points.Count);
        foreach ((double x, double y, T? _) in s.Points)
            raw.Add(new Float2(ctx.XPos(x), ctx.YPos(y)));

        if (_interpolation != CartesianInterpolation.Smooth || raw.Count < 3)
            return raw;

        var smooth = new List<Float2>((raw.Count - 1) * SmoothStepsPerSegment + 1) { raw[0] };
        for (int i = 0; i < raw.Count - 1; i++)
        {
            Float2 p0 = raw[Math.Max(0, i - 1)];
            Float2 p1 = raw[i];
            Float2 p2 = raw[i + 1];
            Float2 p3 = raw[Math.Min(raw.Count - 1, i + 2)];

            Float2 c1 = new Float2(p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f);
            Float2 c2 = new Float2(p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f);

            for (int t = 1; t <= SmoothStepsPerSegment; t++)
                smooth.Add(CubicBezier(p1, c1, c2, p2, t / (float)SmoothStepsPerSegment));
        }
        return smooth;
    }


    private static Float2 CubicBezier(Float2 p0, Float2 c1, Float2 c2, Float2 p1, float t)
    {
        float mt = 1f - t;
        float a = mt * mt * mt, b = 3f * mt * mt * t, c = 3f * mt * t * t, d = t * t * t;
        return new Float2(
            a * p0.X + b * c1.X + c * c2.X + d * p1.X,
            a * p0.Y + b * c1.Y + c * c2.Y + d * p1.Y);
    }


    private static void PaintFill(Canvas canvas, in PlotContext ctx, CartesianSeries<T> s, List<Float2> pts, Color strokeColor)
    {
        float baseline = Math.Clamp(ctx.YPos(0d), ctx.PlotT, ctx.PlotB);
        Color32 fillCol = ToC32(s.Color ?? strokeColor, 0.18f);

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Float2 a = pts[i], b = pts[i + 1];
            canvas.BeginPath();
            canvas.MoveTo(a.X, baseline);
            canvas.LineTo(a.X, a.Y);
            canvas.LineTo(b.X, b.Y);
            canvas.LineTo(b.X, baseline);
            canvas.ClosePath();
            canvas.SetFillColor(fillCol);
            canvas.Fill();
        }
    }

    private static void PaintStroke(Canvas canvas, List<Float2> pts, Color strokeColor, float strokeWidth, CartesianDash dash)
    {
        Color32 col = ToC32(strokeColor);

        switch (dash)
        {
            case CartesianDash.Dashed:
                StrokeDashed(canvas, pts, col, strokeWidth, MathF.Max(4f, strokeWidth * 3f), MathF.Max(3f, strokeWidth * 2f));
                break;

            case CartesianDash.Dotted:
                StrokeDotted(canvas, pts, col, strokeWidth);
                break;

            default:
                canvas.BeginPath();
                canvas.MoveTo(pts[0].X, pts[0].Y);
                for (int i = 1; i < pts.Count; i++)
                    canvas.LineTo(pts[i].X, pts[i].Y);
                canvas.SetStrokeColor(col);
                canvas.SetStrokeWidth(strokeWidth);
                canvas.Stroke();
                break;
        }
    }

    /// <summary>Walks the polyline emitting only the "on" portions of a dash/gap pattern, keeping the
    /// pattern phase continuous across segment boundaries so it doesn't reset (and look uneven) at
    /// every point.</summary>
    private static void StrokeDashed(Canvas canvas, List<Float2> pts, Color32 col, float width, float dashLen, float gapLen)
    {
        canvas.SetStrokeColor(col);
        canvas.SetStrokeWidth(width);
        canvas.SetStrokeCap(EndCapStyle.Butt);

        float period = dashLen + gapLen;
        float dist = 0f;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Float2 a = pts[i], b = pts[i + 1];
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            if (segLen <= 0f) continue;

            float segPos = 0f;
            while (segPos < segLen)
            {
                float patternPos = dist % period;
                bool on = patternPos < dashLen;
                float remainInState = on ? dashLen - patternPos : period - patternPos;
                float step = MathF.Min(remainInState, segLen - segPos);

                if (on)
                {
                    float t0 = segPos / segLen;
                    float t1 = (segPos + step) / segLen;
                    canvas.BeginPath();
                    canvas.MoveTo(a.X + dx * t0, a.Y + dy * t0);
                    canvas.LineTo(a.X + dx * t1, a.Y + dy * t1);
                    canvas.Stroke();
                }

                segPos += step;
                dist += step;
            }
        }
    }

    private static void StrokeDotted(Canvas canvas, List<Float2> pts, Color32 col, float width)
    {
        float spacing = MathF.Max(4f, width * 2.5f);
        float radius = MathF.Max(1f, width * 0.55f);
        float dist = 0f;
        canvas.SetFillColor(col);

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Float2 a = pts[i], b = pts[i + 1];
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            if (segLen <= 0f) continue;

            while (dist <= segLen)
            {
                float t = dist / segLen;
                canvas.BeginPath();
                canvas.Circle(a.X + dx * t, a.Y + dy * t, radius);
                canvas.Fill();
                dist += spacing;
            }
            dist -= segLen;
        }
    }
}
