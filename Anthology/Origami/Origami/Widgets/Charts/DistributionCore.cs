// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Shared implementation for the distribution chart types (Histogram, BoxPlot). Unlike the rest of
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
    private const double FenceFactor = 1.5d;

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

    /// <summary>Called once per frame after every group has been derived. Chart types whose marks span
    /// past the derived point y - box plot whiskers and outliers - widen the axis here with
    /// <c>DeriveYRange</c>, guarded by <c>HasExplicitYRange</c>.</summary>
    protected virtual void OnDeriveEnd() { }

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

        OnDeriveEnd();
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

    /// <summary>A group's five-number summary plus the extras a box plot draws: its mean, the
    /// 1.5*IQR fences, the whisker ends and the outliers past them.</summary>
    protected readonly struct DistributionSummary
    {
        /// <summary>Number of finite values the summary was built from. Zero means every other field is
        /// meaningless and nothing should be drawn.</summary>
        public readonly int Count;

        public readonly double Min;
        public readonly double Q1;
        public readonly double Median;
        public readonly double Q3;
        public readonly double Max;
        public readonly double Mean;

        /// <summary>Lower and upper 1.5*IQR fences. Values beyond them are the outliers.</summary>
        public readonly double LowerFence;
        public readonly double UpperFence;

        /// <summary>The most extreme values still inside the fences: where the whiskers end.</summary>
        public readonly double LowerWhisker;
        public readonly double UpperWhisker;

        public readonly IReadOnlyList<double> Outliers;

        internal DistributionSummary(int count, double min, double q1, double median, double q3, double max,
            double mean, double lowerFence, double upperFence, double lowerWhisker, double upperWhisker,
            IReadOnlyList<double> outliers)
        {
            Count = count;
            Min = min; Q1 = q1; Median = median; Q3 = q3; Max = max;
            Mean = mean;
            LowerFence = lowerFence; UpperFence = upperFence;
            LowerWhisker = lowerWhisker; UpperWhisker = upperWhisker;
            Outliers = outliers;
        }

        public double Iqr => Q3 - Q1;
    }

    /// <summary>Summarises a group's values. Quartiles use the linear-interpolation convention: the
    /// values are sorted and quantile p is read at the fractional position <c>p * (n - 1)</c>,
    /// interpolating between its neighbours. This is the inclusive convention, matching NumPy's default
    /// and Excel's PERCENTILE.INC, so Q2 is the ordinary median.</summary>
    protected static DistributionSummary ComputeSummary(IReadOnlyList<double> values)
    {
        var sorted = new List<double>(values.Count);
        double sum = 0d;

        foreach (double v in values)
        {
            if (!IsFinite(v)) continue;
            sorted.Add(v);
            sum += v;
        }

        if (sorted.Count == 0)
            return new DistributionSummary(0, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, Array.Empty<double>());

        sorted.Sort();

        double min = sorted[0];
        double max = sorted[^1];
        double q1 = Quantile(sorted, 0.25d);
        double median = Quantile(sorted, 0.5d);
        double q3 = Quantile(sorted, 0.75d);

        double fence = (q3 - q1) * FenceFactor;
        double lowerFence = q1 - fence;
        double upperFence = q3 + fence;

        double lowerWhisker = max;
        double upperWhisker = min;
        List<double>? outliers = null;

        foreach (double v in sorted)
        {
            if (v < lowerFence || v > upperFence)
            {
                (outliers ??= new List<double>()).Add(v);
                continue;
            }

            if (v < lowerWhisker) lowerWhisker = v;
            if (v > upperWhisker) upperWhisker = v;
        }

        if (upperWhisker < lowerWhisker) { lowerWhisker = median; upperWhisker = median; }

        return new DistributionSummary(sorted.Count, min, q1, median, q3, max, sum / sorted.Count,
            lowerFence, upperFence, lowerWhisker, upperWhisker,
            (IReadOnlyList<double>?)outliers ?? Array.Empty<double>());
    }

    private static double Quantile(List<double> sorted, double p)
    {
        int n = sorted.Count;
        if (n == 1) return sorted[0];

        double pos = p * (n - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, n - 1);
        double frac = pos - lo;

        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }
}
