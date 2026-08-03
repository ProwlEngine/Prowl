// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Shared implementation for the distribution chart types (Histogram). Unlike the rest of
/// the Cartesian family these do not plot their input values: they plot geometry derived from them,
/// so every group's raw values are collected before layout, handed to <see cref="DeriveGroup"/>, and
/// replaced in place by whatever that produces. Everything else - axes, grid, ticks, legend, sampler,
/// zoom and pan - is inherited from <see cref="CartesianCore{TSelf, T}"/> unchanged.
/// </summary>
public abstract class DistributionCore<TSelf, T> : CartesianCore<TSelf, T>
    where TSelf : DistributionCore<TSelf, T>
{
    protected DistributionCore(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    private Func<T, double>? _value;

    private readonly Dictionary<CartesianSeries<T>, Color> _fills = new();
    private Color? _pendingFill;

    private const int MaxBins = 512;

    private TSelf Self => (TSelf)this;

    /// <summary>Value selector for the data set passed at construction. The projected values become one
    /// unlabelled group, plotted alongside any further groups added with <c>.Series(...)</c>.</summary>
    public TSelf Value(Func<T, double> selector) { _value = selector; return Self; }

    /// <summary>Interior colour of the most recently added group's marks, overriding <c>.Color(...)</c>
    /// for the filled area only. Before any group has been added this applies to the group produced by
    /// <see cref="Value"/> instead.</summary>
    public TSelf Fill(Color color)
    {
        if (SeriesList.Count > 0) _fills[SeriesList[^1]] = color;
        else _pendingFill = color;
        return Self;
    }

    /// <summary>Interior colour of <paramref name="group"/>: the colour given to <see cref="Fill(Color)"/>
    /// when there was one, otherwise the group's own accent colour.</summary>
    protected Color FillColorOf(CartesianSeries<T> group)
        => _fills.TryGetValue(group, out Color fill) ? fill : group.Color ?? System.Drawing.Color.Gray;

    /// <summary>Called once per frame with every group's raw values, in <c>SeriesList</c> order, before
    /// any group is derived. Chart types whose groups must share geometry compute it here, so that for
    /// instance one set of bin edges spans the union of all groups and their bars line up.</summary>
    protected virtual void OnDeriveBegin(IReadOnlyList<IReadOnlyList<double>> groups) { }

    /// <summary>Derive one group's plotted values from its raw values, appending them to
    /// <paramref name="derived"/>. Entry i becomes the point at x = i, so a banded chart type gets one
    /// band per appended value. <paramref name="values"/> holds only the finite raw values of
    /// <paramref name="group"/>, in input order.</summary>
    protected abstract void DeriveGroup(CartesianSeries<T> group, IReadOnlyList<double> values, List<double> derived);

    protected override void OnBeforeShow()
    {
        ProjectValueGroup();

        IReadOnlyList<CartesianSeries<T>> groups = SeriesList;
        if (groups.Count == 0) return;

        var raw = new double[groups.Count][];
        for (int i = 0; i < groups.Count; i++)
            raw[i] = FiniteValues(groups[i]);

        OnDeriveBegin(raw);

        var derived = new List<double>();
        for (int i = 0; i < groups.Count; i++)
        {
            derived.Clear();
            DeriveGroup(groups[i], raw[i], derived);

            List<(double X, double Y, T? Payload)> points = groups[i].Points;
            points.Clear();
            for (int k = 0; k < derived.Count; k++)
                points.Add((k, derived[k], default));
        }
    }

    private void ProjectValueGroup()
    {
        if (_value == null || _data == null) return;

        var values = new double[_data.Count];
        for (int i = 0; i < _data.Count; i++)
            values[i] = _value(_data[i]);

        Series("", System.Drawing.Color.Gray, values);

        CartesianSeries<T> group = SeriesList[^1];
        group.Color = null;
        if (_pendingFill.HasValue) _fills[group] = _pendingFill.Value;
    }

    private static double[] FiniteValues(CartesianSeries<T> group)
    {
        var values = new List<double>(group.Points.Count);
        foreach ((double _, double y, T? _) in group.Points)
            if (IsFinite(y)) values.Add(y);
        return values.ToArray();
    }

    /// <summary>True when <paramref name="v"/> is a real number a mark can be placed at.</summary>
    protected static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>A uniform run of bins: <see cref="Count"/> bins of <see cref="Width"/> starting at
    /// <see cref="Min"/>, so bin i covers [Edge(i), Edge(i + 1)).</summary>
    protected readonly struct BinEdges
    {
        public readonly double Min;
        public readonly double Width;
        public readonly int Count;

        internal BinEdges(double min, double width, int count)
        {
            Min = min;
            Width = width;
            Count = count;
        }

        /// <summary>Upper edge of the last bin.</summary>
        public double Max => Min + Width * Count;

        /// <summary>Lower edge of bin <paramref name="index"/>. Passing <see cref="Count"/> gives
        /// <see cref="Max"/>.</summary>
        public double Edge(int index) => Min + Width * index;

        /// <summary>Midpoint of bin <paramref name="index"/>.</summary>
        public double Center(int index) => Min + Width * (index + 0.5d);

        /// <summary>Index of the bin holding <paramref name="value"/>, or -1 when it falls outside the
        /// run. The last bin is closed at its upper edge so the largest value still lands in a bin.</summary>
        public int IndexOf(double value)
        {
            if (Count <= 0 || Width <= 0d || !IsFinite(value)) return -1;
            if (value < Min || value > Max) return -1;
            return Math.Clamp((int)((value - Min) / Width), 0, Count - 1);
        }
    }

    /// <summary>Bin edges spanning the union of every set in <paramref name="groups"/>, so that groups
    /// binned against the result line up. <paramref name="binWidth"/> takes precedence when positive and
    /// the run is then aligned to a multiple of it; otherwise the range is split into
    /// <paramref name="binCount"/> equal bins.</summary>
    protected static BinEdges ComputeBinEdges(IReadOnlyList<IReadOnlyList<double>> groups, int binCount, double binWidth = 0d)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (IReadOnlyList<double> group in groups)
        {
            foreach (double v in group)
            {
                if (!IsFinite(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        if (min > max) return new BinEdges(0d, 1d, 0);

        if (binWidth > 0d && IsFinite(binWidth))
        {
            double start = Math.Floor(min / binWidth) * binWidth;
            return new BinEdges(start, binWidth, Math.Clamp((int)Math.Ceiling((max - start) / binWidth), 1, MaxBins));
        }

        double span = max - min;
        if (span <= 0d)
        {
            span = Math.Abs(max) > 0d ? Math.Abs(max) : 1d;
            min -= span * 0.5d;
            span *= 2d;
        }

        int count = Math.Clamp(binCount, 1, MaxBins);
        return new BinEdges(min, span / count, count);
    }

    /// <summary>Number of values falling in each bin of <paramref name="edges"/>. Values outside the run
    /// and non-finite values are dropped.</summary>
    protected static int[] ComputeBinCounts(IReadOnlyList<double> values, in BinEdges edges)
    {
        var counts = new int[Math.Max(0, edges.Count)];
        foreach (double v in values)
        {
            int i = edges.IndexOf(v);
            if (i >= 0) counts[i]++;
        }
        return counts;
    }

}
