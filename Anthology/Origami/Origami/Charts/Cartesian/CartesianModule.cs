// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI.Charts;

/// <summary>One mark type plotted on a <see cref="CartesianChart{T}"/>.</summary>
public abstract class CartesianModule<T>
{
    internal readonly CartesianChart<T> _chart;
    internal readonly List<CartesianSeries<T>> _series = new();
    internal CartesianSeries<T>? _last;
    internal CartesianSeries<T>? _implicit;
    internal Func<T, double>? _ySelector;
    internal string _implicitName = "";

    private protected CartesianModule(CartesianChart<T> chart) => _chart = chart;

    internal virtual float XPad => 0f;
    internal virtual bool Nearest2D => false;

    internal void Resolve(IReadOnlyList<T>? data, Func<T, double>? xSelector, List<CartesianSeries<T>> all)
    {
        if (_implicit != null)
        {
            _implicit.Label = _implicitName;
            _implicit.Points.Clear();
            if (data != null && xSelector != null && _ySelector != null)
                foreach (T item in data)
                    _implicit.Points.Add((xSelector(item), _ySelector(item), item));
        }

        all.AddRange(_series);
    }

    internal abstract void Paint(Canvas canvas, in PlotContext ctx);

    internal abstract void PaintSample(Canvas canvas, in PlotContext ctx, CartesianSeries<T>? only, int index);

    internal void AppendRows(CartesianSeries<T>? only, int index, List<(Color Color, string Text)> rows)
    {
        foreach (CartesianSeries<T> s in _series)
        {
            if (s.Hidden || (only != null && s != only) || index >= s.Points.Count) continue;

            (double x, double y, T? payload) = s.Points[index];
            if (double.IsFinite(y)) rows.Add((s.Color, RowText(s, x, y, payload)));
        }
    }

    private protected virtual string RowText(CartesianSeries<T> s, double x, double y, T? payload)
        => $"{s.Label}: {_chart.Format(y)}";

    private protected static Color32 C32(Color c, float alpha = 1f)
        => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * alpha, 0f, 255f));

    private protected static bool Visible(CartesianSeries<T> s) => !s.Hidden && s.Points.Count > 0;
}

/// <summary>Fluent series API shared by every module.</summary>
public abstract class CartesianModule<TSelf, T> : CartesianModule<T> where TSelf : CartesianModule<TSelf, T>
{
    private protected CartesianModule(CartesianChart<T> chart) : base(chart) { }

    private protected TSelf Self => (TSelf)this;

    /// <summary>Back to the owning chart.</summary>
    public CartesianChart<T> Cartesian => _chart;

    /// <summary>Add a series whose index in <paramref name="values"/> is its x.</summary>
    public TSelf Series(string label, Color color, IReadOnlyList<double> values)
    {
        var s = new CartesianSeries<T> { Label = label ?? "", Color = color, HasColor = true };
        if (values != null)
            for (int i = 0; i < values.Count; i++)
                s.Points.Add((i, values[i], default));

        _series.Add(s);
        _last = s;
        return Self;
    }

    /// <summary>Y selector over the chart's data set, paired with the chart's <c>.X(...)</c>.</summary>
    public TSelf Y(Func<T, double> selector)
    {
        _ySelector = selector;
        if (_implicit == null)
        {
            _implicit = new CartesianSeries<T>();
            _series.Add(_implicit);
        }
        _last = _implicit;
        return Self;
    }

    /// <summary>Name of the series built from <see cref="Y"/>.</summary>
    public TSelf Name(string text) { _implicitName = text ?? ""; return Self; }

    /// <summary>Colour of the most recently added series.</summary>
    public TSelf Color(Color color)
    {
        if (_last != null) { _last.Color = color; _last.HasColor = true; }
        return Self;
    }
}
