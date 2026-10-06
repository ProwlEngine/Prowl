// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Uniform bins spanning every group given to <see cref="Create"/>. Feed <see cref="Counts"/> to a
/// bar module and label x ticks with <see cref="Edge"/>.</summary>
public readonly struct HistogramBins
{
    public readonly double Min;
    public readonly double Width;
    public readonly int Count;

    private const int MaxBins = 512;

    private HistogramBins(double min, double width, int count)
    {
        Min = min;
        Width = width;
        Count = count;
    }

    public static HistogramBins Create(int count, params IReadOnlyList<double>[] groups)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (IReadOnlyList<double> group in groups)
            foreach (double v in group)
                if (double.IsFinite(v)) { min = Math.Min(min, v); max = Math.Max(max, v); }

        if (min > max) return new HistogramBins(0d, 1d, 0);

        double span = max - min;
        if (span <= 0d)
        {
            span = Math.Abs(max) > 0d ? Math.Abs(max) : 1d;
            min -= span * 0.5d;
            span *= 2d;
        }

        count = Math.Clamp(count, 1, MaxBins);
        return new HistogramBins(min, span / count, count);
    }

    /// <summary>Lower edge of bin <paramref name="index"/>; <see cref="Count"/> gives the upper end.</summary>
    public double Edge(int index) => Min + Width * index;

    public double[] Counts(IReadOnlyList<double> values)
    {
        var counts = new double[Count];
        if (Count == 0) return counts;

        double max = Edge(Count);
        foreach (double v in values)
            if (double.IsFinite(v) && v >= Min && v <= max)
                counts[Math.Clamp((int)((v - Min) / Width), 0, Count - 1)]++;
        return counts;
    }
}
