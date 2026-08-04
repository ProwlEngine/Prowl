// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.PaperUI;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Entry points for every chart family. Each method builds a stateless-per-frame builder;
/// pass the full current data set on every call and finish with <c>.Show()</c>.</summary>
public static class Chart
{
    /// <summary>A Cartesian chart whose marks come from the modules plugged into it -
    /// <c>.AddLineChart()</c>, <c>.AddBarChart()</c>, <c>.AddScatterPlot()</c>, <c>.AddBubbleChart()</c>
    /// - so a line can be overlaid on a bar, a scatter share one plot with a bubble, and so on. Every
    /// module reads the same x, set once with <c>.X(...)</c>.</summary>
    public static CartesianChart<T> CreateCartesian<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
        => new(paper, id, Origami.Current, data);

    public static HistogramChart<T> Histogram<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
        => new(paper, id, Origami.Current, data);

    public static PieChart<T> Pie<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
        => new(paper, id, Origami.Current, data);

    public static DonutChart<T> Donut<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
        => new(paper, id, Origami.Current, data);

    public static RadarChart<T> Radar<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
        => new(paper, id, Origami.Current, data);

    public static FlameGraphChart<T> FlameGraph<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
        => new(paper, id, Origami.Current, data);
}
