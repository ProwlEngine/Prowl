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
/// Financial candlestick chart. Each data item owns one categorical band and is drawn as a
/// high/low wick with an open/close body, coloured by <see cref="UpColor"/> when close is at or
/// above open and by <see cref="DownColor"/> otherwise.
/// </summary>
public sealed class CandlestickChart<T> : CartesianCore<CandlestickChart<T>, T>
{
    internal CandlestickChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private Func<T, double>? _open;
    private Func<T, double>? _high;
    private Func<T, double>? _low;
    private Func<T, double>? _close;

    private Color? _upColor;
    private Color? _downColor;

    private const float BodyWidthFactor = 0.7f;
    private const float RangePadding = 0.05f;

    /// <summary>Open price selector for the data set passed at construction.</summary>
    public CandlestickChart<T> Open(Func<T, double> selector) { _open = selector; return this; }

    /// <summary>High price selector for the data set passed at construction.</summary>
    public CandlestickChart<T> High(Func<T, double> selector) { _high = selector; return this; }

    /// <summary>Low price selector for the data set passed at construction.</summary>
    public CandlestickChart<T> Low(Func<T, double> selector) { _low = selector; return this; }

    /// <summary>Close price selector for the data set passed at construction. This doubles as the
    /// chart's y selector, so it is what the base class' series, legend and sample readout report.</summary>
    public CandlestickChart<T> Close(Func<T, double> selector)
    {
        _close = selector;
        Y(selector);
        return this;
    }

    /// <summary>Colour of bars whose close is at or above their open. Defaults to the theme's success ramp.</summary>
    public CandlestickChart<T> UpColor(Color color) { _upColor = color; return this; }

    /// <summary>Colour of bars whose close is below their open. Defaults to the theme's danger ramp.</summary>
    public CandlestickChart<T> DownColor(Color color) { _downColor = color; return this; }

    protected override bool BandedX => true;

    protected override void OnBeforeShow()
    {
        IncludeZero(false);

        if (HasExplicitYRange || _data == null || _high == null || _low == null) return;

        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (T? item in _data)
        {
            if (item is not T value) continue;

            double lo = _low(value);
            double hi = _high(value);
            if (IsFinite(lo) && lo < min) min = lo;
            if (IsFinite(hi) && hi > max) max = hi;
        }

        if (min > max) return;

        double span = max - min;
        if (span <= 0d) span = Math.Abs(max) > 0d ? Math.Abs(max) : 1d;
        double pad = span * RangePadding;

        DeriveYRange(min - pad, max + pad);
    }

    protected override void PaintMarks(Canvas canvas, in PlotContext ctx)
    {
        if (_open == null || _high == null || _low == null || _close == null) return;

        Color up = _upColor ?? _theme.Get(OrigamiVariant.Success).C500;
        Color down = _downColor ?? _theme.Get(OrigamiVariant.Danger).C500;

        float bodyWidth = MathF.Max(1f, ctx.BandWidth * BodyWidthFactor);

        foreach (CartesianSeries<T> s in ctx.Series)
        {
            if (!s.EffectiveVisible) continue;

            for (int i = 0; i < s.Points.Count; i++)
            {
                if (s.Points[i].Payload is not T item) continue;

                double open = _open(item);
                double high = _high(item);
                double low = _low(item);
                double close = _close(item);
                if (!IsFinite(open) || !IsFinite(high) || !IsFinite(low) || !IsFinite(close)) continue;

                Color32 col = ToC32(close >= open ? up : down);
                float cx = ctx.BandCenter(i);

                canvas.BeginPath();
                canvas.MoveTo(cx, ctx.YPos(high));
                canvas.LineTo(cx, ctx.YPos(low));
                canvas.SetStrokeColor(col);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();

                float top = ctx.YPos(Math.Max(open, close));
                float bottom = ctx.YPos(Math.Min(open, close));
                float bodyHeight = MathF.Max(1f, bottom - top);

                canvas.BeginPath();
                canvas.Rect(cx - bodyWidth * 0.5f, top, bodyWidth, bodyHeight);
                canvas.SetFillColor(col);
                canvas.Fill();
                canvas.SetStrokeColor(col);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();
            }
        }
    }

    /// <summary>Highlights the sampled candle's band, marks its close, and reads out all four prices,
    /// since a candle encodes three values the base series' y never carries.</summary>
    protected override void DrawSampler(Paper paper, in SampleContext ctx)
    {
        if (_open == null || _high == null || _low == null || _close == null) return;

        CartesianSeries<T>? series = LongestVisible(ctx.Series);
        if (series == null || ctx.Index >= series.Points.Count) return;
        if (series.Points[ctx.Index].Payload is not T item) return;

        double open = _open(item);
        double high = _high(item);
        double low = _low(item);
        double close = _close(item);
        if (!IsFinite(open) || !IsFinite(high) || !IsFinite(low) || !IsFinite(close)) return;

        float bandLeft = ctx.BandLeft(ctx.Index);
        float cx = ctx.BandCenter(ctx.Index);

        SampleBand(paper, in ctx, bandLeft, ctx.BandWidth);
        SampleLine(paper, in ctx, cx);

        Color col = close >= open
            ? _upColor ?? _theme.Get(OrigamiVariant.Success).C500
            : _downColor ?? _theme.Get(OrigamiVariant.Danger).C500;

        SampleDot(paper, "close", cx, Math.Clamp(ctx.YPos(close), ctx.PlotT, ctx.PlotB), col);

        var rows = new List<(Color Color, string Text)>
        {
            (col, $"Open: {FormatValue(open)}"),
            (col, $"High: {FormatValue(high)}"),
            (col, $"Low: {FormatValue(low)}"),
            (col, $"Close: {FormatValue(close)}"),
        };

        SamplePopup(paper, in ctx, bandLeft + ctx.BandWidth, SampleHeader(ctx.Index), rows);
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
