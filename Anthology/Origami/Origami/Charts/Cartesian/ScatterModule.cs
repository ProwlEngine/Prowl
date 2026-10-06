// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI.Charts;

public enum MarkerShape
{
    Circle,
    Square,
    Triangle,
    Diamond,
    Cross,
}

/// <summary>One marker per point. With <see cref="Size"/> set it draws translucent bubbles.</summary>
public sealed class ScatterModule<T> : CartesianModule<ScatterModule<T>, T>
{
    private float _markerSize = 6f;
    private MarkerShape _shape = MarkerShape.Circle;
    private Func<T, float>? _size;

    internal ScatterModule(CartesianChart<T> chart) : base(chart) { }

    internal override bool Nearest2D => true;

    /// <summary>Marker diameter in pixels. Default 6.</summary>
    public ScatterModule<T> MarkerSize(float size) { _markerSize = MathF.Max(1f, size); return this; }

    public ScatterModule<T> Marker(MarkerShape shape) { _shape = shape; return this; }

    /// <summary>Per-item marker diameter in pixels, drawn as translucent bubbles.</summary>
    public ScatterModule<T> Size(Func<T, float> selector) { _size = selector; return this; }

    private float DiameterOf(T? payload)
    {
        if (_size == null || payload is not T item) return _markerSize;
        float d = _size(item);
        return float.IsFinite(d) ? MathF.Max(1f, d) : _markerSize;
    }

    internal override void Paint(Canvas canvas, in PlotContext ctx)
    {
        foreach (CartesianSeries<T> s in _series)
        {
            if (!Visible(s)) continue;

            int n = s.Points.Count;
            var keys = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = -DiameterOf(s.Points[i].Payload); order[i] = i; }
            if (_size != null) Array.Sort(keys, order);

            Color32 fill = C32(s.Color, _size != null ? 0.45f : 1f);
            Color32 outline = C32(s.Color);

            for (int k = 0; k < n; k++)
            {
                (double x, double y, T? _) = s.Points[order[k]];
                if (!double.IsFinite(x) || !double.IsFinite(y)) continue;

                canvas.BeginPath();
                MarkerPath(canvas, ctx.XPos(x), ctx.YPos(y), -keys[k]);

                if (_shape != MarkerShape.Cross)
                {
                    canvas.SetFillColor(fill);
                    canvas.Fill();
                    if (_size == null) continue;
                }

                canvas.SetStrokeColor(outline);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();
            }
        }
    }

    internal override void PaintSample(Canvas canvas, in PlotContext ctx, CartesianSeries<T>? only, int index)
    {
        if (only == null || !_series.Contains(only) || index >= only.Points.Count) return;

        (double x, double y, T? payload) = only.Points[index];
        canvas.BeginPath();
        canvas.Circle(ctx.XPos(x), ctx.YPos(y), MathF.Max(3f, DiameterOf(payload) * 0.5f) + 2f);
        canvas.SetStrokeColor(C32(only.Color));
        canvas.SetStrokeWidth(1.5f);
        canvas.Stroke();
    }

    private protected override string RowText(CartesianSeries<T> s, double x, double y, T? payload)
    {
        string text = $"({_chart.Format(x)}, {_chart.Format(y)})";
        if (_size != null && payload is T item) text += $", size {_chart.Format(_size(item))}";
        return text;
    }

    private void MarkerPath(Canvas canvas, float cx, float cy, float size)
    {
        float r = size * 0.5f;

        switch (_shape)
        {
            case MarkerShape.Square:
                canvas.Rect(cx - r, cy - r, size, size);
                break;

            case MarkerShape.Triangle:
                canvas.MoveTo(cx, cy - r);
                canvas.LineTo(cx + r, cy + r);
                canvas.LineTo(cx - r, cy + r);
                canvas.ClosePath();
                break;

            case MarkerShape.Diamond:
                canvas.MoveTo(cx, cy - r);
                canvas.LineTo(cx + r, cy);
                canvas.LineTo(cx, cy + r);
                canvas.LineTo(cx - r, cy);
                canvas.ClosePath();
                break;

            case MarkerShape.Cross:
                canvas.MoveTo(cx - r, cy - r);
                canvas.LineTo(cx + r, cy + r);
                canvas.MoveTo(cx - r, cy + r);
                canvas.LineTo(cx + r, cy - r);
                break;

            default:
                canvas.Circle(cx, cy, r);
                break;
        }
    }
}
