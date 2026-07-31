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
/// Cartesian bubble chart. Like a scatter chart, but each marker's diameter comes from a selector
/// run against the source item behind the point, so a third value can be encoded as bubble area.
/// Bubbles are filled at partial alpha with a solid outline so overlaps stay readable.
/// </summary>
public sealed class BubbleChart<T> : CartesianCore<BubbleChart<T>, T>
{
    internal BubbleChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private const float DefaultDiameter = 12f;

    private Func<T, float>? _sizeSelector;
    private MarkerShape _markerShape = MarkerShape.Circle;

    /// <summary>Per-item marker diameter, in pixels. Points whose source item is unavailable (series
    /// added as pre-sampled values rather than through the <c>.X</c>/<c>.Y</c> selectors) fall back to
    /// a fixed diameter.</summary>
    public BubbleChart<T> MarkerSize(Func<T, float> selector) { _sizeSelector = selector; return this; }

    /// <summary>Shape drawn at each point. Defaults to <see cref="MarkerShape.Circle"/>.</summary>
    public BubbleChart<T> Marker(MarkerShape shape) { _markerShape = shape; return this; }

    protected override bool SampleNearest2D => true;

    protected override bool DefaultPanY => true;

    /// <summary>Crosshairs through the sampled bubble on both axes with a ring around it, and a readout
    /// of its position plus the size value driving its diameter.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        CartesianSeries<T>? longest = LongestVisible(ctx.Series);
        if (longest == null || ctx.Index >= longest.Points.Count) return;

        (double ax, double ay, T? _) = longest.Points[ctx.Index];
        float lx = ctx.XPos(ax);
        float ly = Math.Clamp(ctx.YPos(ay), ctx.PlotT, ctx.PlotB);

        SampleLine(paper, in ctx, lx);
        SampleLineH(paper, in ctx, ly);

        var rows = new List<(Color Color, string Text)>();

        for (int i = 0; i < ctx.Series.Count; i++)
        {
            CartesianSeries<T> s = ctx.Series[i];
            if (!s.EffectiveVisible || ctx.Index >= s.Points.Count) continue;

            (double x, double y, T? payload) = s.Points[ctx.Index];
            if (double.IsNaN(x) || double.IsInfinity(x)) continue;
            if (double.IsNaN(y) || double.IsInfinity(y)) continue;

            Color color = s.Color ?? System.Drawing.Color.Gray;
            float diameter = DiameterOf(payload);

            SampleRing(paper, i.ToString(), ctx.XPos(x), Math.Clamp(ctx.YPos(y), ctx.PlotT, ctx.PlotB), diameter, color);

            string text = $"{s.Label}: ({FormatValue(x)}, {FormatValue(y)})";
            if (_sizeSelector != null && payload is T item)
                text += $", size {FormatValue(_sizeSelector(item))}";

            rows.Add((color, text));
        }

        SamplePopup(paper, in ctx, lx, SampleHeader(ctx.Index), rows);
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext ctx)
    {
        foreach (var s in ctx.Series)
        {
            if (!s.EffectiveVisible || s.Points.Count == 0) continue;

            Color baseColor = s.Color ?? System.Drawing.Color.Gray;
            Color32 fillCol = ToC32(baseColor, 0.45f);
            Color32 outlineCol = ToC32(s.StrokeColor ?? baseColor);

            int count = s.Points.Count;
            var diameters = new float[count];
            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                diameters[i] = DiameterOf(s.Points[i].Payload);
                order[i] = i;
            }

            Array.Sort(order, (a, b) => diameters[b].CompareTo(diameters[a]));

            foreach (int i in order)
            {
                (double x, double y, T? _) = s.Points[i];
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (double.IsNaN(y) || double.IsInfinity(y)) continue;

                float cx = ctx.XPos(x);
                float cy = ctx.YPos(y);
                float size = diameters[i];

                if (_markerShape == MarkerShape.Cross)
                {
                    canvas.BeginPath();
                    PaintMarker(canvas, MarkerShape.Cross, cx, cy, size);
                    canvas.SetStrokeColor(outlineCol);
                    canvas.SetStrokeWidth(1f);
                    canvas.Stroke();
                    continue;
                }

                canvas.BeginPath();
                PaintMarker(canvas, _markerShape, cx, cy, size);
                canvas.SetFillColor(fillCol);
                canvas.Fill();

                canvas.BeginPath();
                PaintMarker(canvas, _markerShape, cx, cy, size);
                canvas.SetStrokeColor(outlineCol);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();
            }
        }
    }

    private float DiameterOf(T? payload)
    {
        if (_sizeSelector == null || payload is not T item)
            return DefaultDiameter;

        float size = _sizeSelector(item);
        if (float.IsNaN(size) || float.IsInfinity(size)) return DefaultDiameter;
        return MathF.Max(1f, size);
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
