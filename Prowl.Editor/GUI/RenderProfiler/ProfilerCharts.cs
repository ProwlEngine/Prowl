using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Shared layout for the profiler's counter tabs: renders a list of chart specs as titled
/// <see cref="Origami.Chart"/> cards, stacked one per row, inside a scroll view. Each spec may carry
/// several series that overlay on one chart. The chart widget owns its own axes, ticks, labels and
/// clipping.
/// </summary>
internal static class ProfilerCharts
{
    internal sealed class ChartSpec
    {
        public string Title = "";
        public string YLabel = "";
        public Func<double, string> Format = v => v.ToString("0.###");
        public double MinSpan = 10d;
        public List<(string Label, Color Color, double[] Values)> Series = new();
    }

    internal static readonly Color[] Palette =
    {
        EditorTheme.Blue400, EditorTheme.Green400, EditorTheme.Amber400,
        EditorTheme.Purple400, EditorTheme.Red400,
    };

    private const float ChartHeight = 150f;
    private const float TitleHeight = 16f;
    private const float RowGap = 8f;

    internal static void DrawGrid(Paper paper, List<ChartSpec> charts, FontFile font, float width, float height, string idPrefix)
    {
        using (paper.Column(idPrefix + "_root").Size(width, height).Enter())
        {
            if (charts.Count == 0)
            {
                EditorGUI.EmptyState(paper, idPrefix + "_empty", "No counter activity recorded", font);
                return;
            }

            float contentWidth = width - 12f;

            Origami.ScrollView(paper, idPrefix + "_scroll", width, height).Body(() =>
            {
                using (paper.Column(idPrefix + "_list").Width(contentWidth).Height(UnitValue.Auto).ColBetween(RowGap).Padding(8, 8, 8, 8).Enter())
                {
                    for (int i = 0; i < charts.Count; i++)
                        DrawCell(paper, charts[i], font, contentWidth, idPrefix + "_c" + i);
                }
            });
        }
    }

    private static void DrawCell(Paper paper, ChartSpec spec, FontFile font, float colWidth, string id)
    {
        using (paper.Column(id + "_cell").Width(colWidth).Height(UnitValue.Auto).ColBetween(4).Enter())
        {
            paper.Box(id + "_title").Width(UnitValue.Percentage(100)).Height(TitleHeight)
                .Text(spec.Title, font).FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink500)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            ChartBuilder chart = Origami.Chart(paper, id + "_chart")
                .Size(colWidth, ChartHeight)
                .ValueFormatter(spec.Format)
                .MinSpan(spec.MinSpan)
                .IncludeZero()
                .YTicks(3)
                .YLabel(spec.YLabel)
                .Legend(spec.Series.Count > 1);

            bool fill = spec.Series.Count == 1;
            foreach (var s in spec.Series)
                chart.Series(s.Label, s.Color, s.Values, fill);

            chart.Show();
        }
    }
}
