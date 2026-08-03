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
/// Scatter module for a <see cref="CartesianChart{T}"/>. Plots one marker per point of every visible
/// series at its mapped (x, y) position, with no connecting stroke between points. Added with
/// <c>.AddScatterPlot()</c>.
/// </summary>
public sealed class ScatterModule<T> : CartesianModuleBase<ScatterModule<T>, T>
{
    internal ScatterModule(CartesianChart<T> chart) : base(chart) { }

    private float _markerSize = 6f;
    private MarkerShape _markerShape = MarkerShape.Circle;

    /// <summary>Marker diameter in pixels. Defaults to 6.</summary>
    public ScatterModule<T> MarkerSize(float size) { _markerSize = MathF.Max(1f, size); return this; }

    /// <summary>Shape drawn at each point. Defaults to <see cref="MarkerShape.Circle"/>.</summary>
    public ScatterModule<T> Marker(MarkerShape shape) { _markerShape = shape; return this; }

    protected override bool SampleNearest2D => true;

    protected override bool PanY => true;

    /// <summary>Ring around the sampled marker on both axes, and a readout of each visible series'
    /// point there. A scatter point carries an x of its own, so the readout reports the pair rather than
    /// a value against a shared x.</summary>
    protected override void AppendSample(Paper paper, in SampleContext<T> ctx, List<(Color Color, string Text)> rows)
    {
        for (int i = 0; i < ctx.Series.Count; i++)
        {
            CartesianSeries<T> s = ctx.Series[i];
            if (!s.EffectiveVisible || ctx.Index >= s.Points.Count) continue;

            (double x, double y, T? _) = s.Points[ctx.Index];
            if (double.IsNaN(x) || double.IsInfinity(x)) continue;
            if (double.IsNaN(y) || double.IsInfinity(y)) continue;

            Color color = s.Color ?? System.Drawing.Color.Gray;

            SampleRing(paper, $"scatter_{i}", ctx.XPos(x), Math.Clamp(ctx.YPos(y), ctx.PlotT, ctx.PlotB), _markerSize, color);
            rows.Add((color, $"{s.Label}: ({FormatValue(x)}, {FormatValue(y)})"));
        }
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext<T> ctx)
    {
        foreach (var s in ctx.Series)
        {
            if (!s.EffectiveVisible || s.Points.Count == 0) continue;

            Color fillColor = s.Color ?? System.Drawing.Color.Gray;
            Color32 fillCol = ToC32(fillColor);
            float strokeWidth = s.StrokeWidth ?? 1f;

            foreach ((double x, double y, T? _) in s.Points)
            {
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (double.IsNaN(y) || double.IsInfinity(y)) continue;

                float cx = ctx.XPos(x);
                float cy = ctx.YPos(y);

                if (_markerShape == MarkerShape.Cross)
                {
                    canvas.BeginPath();
                    PaintMarker(canvas, MarkerShape.Cross, cx, cy, _markerSize);
                    canvas.SetStrokeColor(ToC32(s.StrokeColor ?? fillColor));
                    canvas.SetStrokeWidth(strokeWidth);
                    canvas.Stroke();
                    continue;
                }

                canvas.BeginPath();
                PaintMarker(canvas, _markerShape, cx, cy, _markerSize);
                canvas.SetFillColor(fillCol);
                canvas.Fill();

                if (s.StrokeColor.HasValue)
                {
                    canvas.BeginPath();
                    PaintMarker(canvas, _markerShape, cx, cy, _markerSize);
                    canvas.SetStrokeColor(ToC32(s.StrokeColor.Value));
                    canvas.SetStrokeWidth(strokeWidth);
                    canvas.Stroke();
                }
            }
        }
    }

    private static void PaintMarker(Canvas canvas, MarkerShape shape, float cx, float cy, float size)
    {
        float r = size * 0.5f;

        switch (shape)
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
