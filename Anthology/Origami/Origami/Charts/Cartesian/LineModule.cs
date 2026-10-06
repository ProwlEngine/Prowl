// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Stroked polyline per series, optionally smoothed, filled or dashed.</summary>
public sealed class LineModule<T> : CartesianModule<LineModule<T>, T>
{
    private bool _smooth;
    private readonly List<Float2> _pts = new();

    private const int SmoothSteps = 12;

    internal LineModule(CartesianChart<T> chart) : base(chart) { }

    public LineModule<T> Smooth(bool smooth = true) { _smooth = smooth; return this; }

    /// <summary>Fill under the most recently added series.</summary>
    public LineModule<T> Fill(bool fill = true) { if (_last != null) _last.Fill = fill; return this; }

    /// <summary>Dash the most recently added series.</summary>
    public LineModule<T> Dashed(bool dashed = true) { if (_last != null) _last.Dashed = dashed; return this; }

    /// <summary>Stroke width of the most recently added series. Defaults to 1.5.</summary>
    public LineModule<T> StrokeWidth(float width) { if (_last != null) _last.StrokeWidth = MathF.Max(0.1f, width); return this; }

    internal override void Paint(Canvas canvas, in PlotContext ctx)
    {
        foreach (CartesianSeries<T> s in _series)
        {
            if (!Visible(s)) continue;

            BuildPath(s, in ctx);
            if (_pts.Count == 0) continue;

            Color32 col = C32(s.Color);
            float width = s.StrokeWidth ?? 1.5f;

            if (_pts.Count == 1)
            {
                canvas.BeginPath();
                canvas.Circle(_pts[0].X, _pts[0].Y, MathF.Max(2f, width * 1.25f));
                canvas.SetFillColor(col);
                canvas.Fill();
                continue;
            }

            if (s.Fill)
            {
                float baseline = ctx.Baseline;
                canvas.BeginPath();
                canvas.MoveTo(_pts[0].X, baseline);
                foreach (Float2 p in _pts) canvas.LineTo(p.X, p.Y);
                canvas.LineTo(_pts[^1].X, baseline);
                canvas.ClosePath();
                canvas.SetFillColor(C32(s.Color, 0.18f));
                canvas.FillComplexAA();
            }

            canvas.SaveState();
            canvas.BeginPath();
            if (s.Dashed)
            {
                canvas.SetStrokeCap(EndCapStyle.Butt);
                AddDashes(canvas, MathF.Max(4f, width * 3f), MathF.Max(3f, width * 2f));
            }
            else
            {
                canvas.MoveTo(_pts[0].X, _pts[0].Y);
                for (int i = 1; i < _pts.Count; i++) canvas.LineTo(_pts[i].X, _pts[i].Y);
            }
            canvas.SetStrokeColor(col);
            canvas.SetStrokeWidth(width);
            canvas.Stroke();
            canvas.RestoreState();
        }
    }

    internal override void PaintSample(Canvas canvas, in PlotContext ctx, CartesianSeries<T>? only, int index)
    {
        foreach (CartesianSeries<T> s in _series)
        {
            if (s.Hidden || index >= s.Points.Count) continue;

            (double x, double y, T? _) = s.Points[index];
            if (!double.IsFinite(x) || !double.IsFinite(y)) continue;

            canvas.BeginPath();
            canvas.Circle(ctx.XPos(x), Math.Clamp(ctx.YPos(y), ctx.T, ctx.B), 3f);
            canvas.SetFillColor(C32(s.Color));
            canvas.Fill();
        }
    }

    private void BuildPath(CartesianSeries<T> s, in PlotContext ctx)
    {
        _pts.Clear();
        foreach ((double x, double y, T? _) in s.Points)
            if (double.IsFinite(x) && double.IsFinite(y))
                _pts.Add(new Float2(ctx.XPos(x), ctx.YPos(y)));

        if (!_smooth || _pts.Count < 3) return;

        int n = _pts.Count;
        Float2[] raw = _pts.ToArray();
        _pts.Clear();
        _pts.Add(raw[0]);

        for (int i = 0; i < n - 1; i++)
        {
            Float2 p0 = raw[Math.Max(0, i - 1)], p1 = raw[i], p2 = raw[i + 1], p3 = raw[Math.Min(n - 1, i + 2)];
            Float2 c1 = new(p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f);
            Float2 c2 = new(p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f);

            for (int k = 1; k <= SmoothSteps; k++)
            {
                float t = k / (float)SmoothSteps, mt = 1f - t;
                float a = mt * mt * mt, b = 3f * mt * mt * t, c = 3f * mt * t * t, d = t * t * t;
                _pts.Add(new Float2(a * p1.X + b * c1.X + c * c2.X + d * p2.X, a * p1.Y + b * c1.Y + c * c2.Y + d * p2.Y));
            }
        }
    }

    private void AddDashes(Canvas canvas, float dashLen, float gapLen)
    {
        float period = dashLen + gapLen;
        float dist = 0f;

        for (int i = 0; i < _pts.Count - 1; i++)
        {
            Float2 a = _pts[i], b = _pts[i + 1];
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            if (segLen <= 0f) continue;

            float pos = 0f;
            while (pos < segLen)
            {
                float phase = dist % period;
                bool on = phase < dashLen;
                float step = MathF.Min(on ? dashLen - phase : period - phase, segLen - pos);

                if (on)
                {
                    float t0 = pos / segLen, t1 = (pos + step) / segLen;
                    canvas.MoveTo(a.X + dx * t0, a.Y + dy * t0);
                    canvas.LineTo(a.X + dx * t1, a.Y + dy * t1);
                }

                pos += step;
                dist += step;
            }
        }
    }
}
