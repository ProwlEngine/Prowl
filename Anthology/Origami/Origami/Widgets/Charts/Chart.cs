// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

public static partial class Origami
{
    /// <summary>Entry points for every chart family. Each method builds a stateless-per-frame builder;
    /// pass the full current data set on every call and finish with <c>.Show()</c>.</summary>
    public static class Chart
    {
        public static LineChart<T> Line<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static AreaChart<T> Area<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static StepChart<T> Step<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static BarChart<T> Bar<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static StackedBarChart<T> StackedBar<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static ScatterChart<T> Scatter<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static BubbleChart<T> Bubble<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static HistogramChart<T> Histogram<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static BoxPlotChart<T> BoxPlot<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static CandlestickChart<T> Candlestick<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static OHLCChart<T> OHLC<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static PieChart<T> Pie<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static DonutChart<T> Donut<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static RadarChart<T> Radar<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static TreemapChart<T> Treemap<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static SunburstChart<T> Sunburst<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);

        public static FlameGraphChart<T> FlameGraph<T>(Paper paper, string id, IReadOnlyList<T>? data = null)
            => new(paper, id, Current, data);
    }
}
