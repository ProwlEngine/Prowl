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
