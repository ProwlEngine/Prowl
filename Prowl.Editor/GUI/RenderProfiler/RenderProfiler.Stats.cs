using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel : DockPanel
{
    private const float ChartHeight = 150f;
    private const float ChartSpacing = 6f;

    private double[] _msStats = [];
    private double[] _viewMsGpu = [];
    private double[] _viewTotal = [];
    private double[] _viewObj = [];
    private double[] _viewCull = [];
    private double[] _viewRend = [];
    private double[] _viewDraws = [];
    private double[] _viewTris = [];
    private double[] _viewOverdraw = [];
    private double[] _vramBudget = [];
    private double[] _vramUsed = [];

    private void DrawStatsViewer(Paper paper, float width, float height)
    {
        IReadOnlyList<ProfiledFrame> history = _profiler.History;

        if (_msStats.Length != history.Count)
        {
            _msStats = new double[history.Count];
            _viewMsGpu = new double[history.Count];
            _viewTotal = new double[history.Count];
            _viewObj = new double[history.Count];
            _viewCull = new double[history.Count];
            _viewRend = new double[history.Count];
            _viewDraws = new double[history.Count];
            _viewTris = new double[history.Count];
            _viewOverdraw = new double[history.Count];
            _vramBudget = new double[history.Count];
            _vramUsed = new double[history.Count];
        }

        for (int i = 0; i < history.Count; i++)
        {
            ProfiledFrame frame = history[i];

            _msStats[i] = frame.FrameMilliseconds;
            _vramBudget[i] = frame.HasVramBudget ? frame.VramBudgetBytes / (1024.0 * 1024.0) : 0;
            _vramUsed[i] = frame.HasVramBudget ? frame.VramUsedBytes / (1024.0 * 1024.0) : 0;

            if (_selectedView >= frame.Views.Count)
            {
                _viewMsGpu[i] = 0;
                _viewObj[i] = 0;
                _viewCull[i] = 0;
                _viewRend[i] = 0;
                _viewDraws[i] = 0;
                _viewTotal[i] = 0;
                _viewTris[i] = 0;
                _viewOverdraw[i] = 0;

                continue;
            }

            ProfiledView view = frame.Views[_selectedView];

            _viewCull[i] = view.CulledObjects;
            _viewObj[i] = view.RegisteredObjects;
            _viewRend[i] = view.RenderedObjects;
            _viewDraws[i] = view.DrawCallCount;
            _viewTotal[i] = view.TotalObjects;
            _viewMsGpu[i] = view.GpuMilliseconds;
            _viewTris[i] = view.TrianglesDrawn;
            _viewOverdraw[i] = view.Overdraw;
        }

        using (paper.Column("rdp_stats_box").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Padding(6).Enter())
        {
            const float viewSelectHeight = 48f;

            using (paper.Box("rdp_view_select").Width(UnitValue.Stretch()).Height(viewSelectHeight).Enter())
            {
                Origami.Dropdown(paper, "rdp_view_dropdown", _selectedView, (x) => _selectedView = x, _selectedProfiledFrame.Views.Select(x => x.Name).ToArray())
                    .Width(200)
                    .Height(24)
                    .Show();
            }

            using (paper.Box("rdp_stats").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float scrollWidth = width - 12f;
                float scrollHeight = height - ToolbarChromeHeight - 12f - viewSelectHeight;

                Origami.ScrollView(paper, "rdp_stats_scrollview", scrollWidth, scrollHeight).ColSpacing(ChartSpacing).Body(viewport =>
                {
                    float cursorY = 0f;

                    DrawChart(paper, viewport, ref cursorY, "rdp_chart_objects", "Objects",
                        ("Total", EditorTheme.Purple500, _viewTotal),
                        ("Registered", EditorTheme.Blue500, _viewObj),
                        ("Culled", EditorTheme.Red500, _viewCull),
                        ("Rendered", EditorTheme.Green500, _viewRend));

                    DrawChart(paper, viewport, ref cursorY, "rdp_chart_ms", "Milliseconds",
                        ("Frame", EditorTheme.Purple500, _msStats),
                        ("View (GPU)", EditorTheme.Amber500, _viewMsGpu));

                    DrawChart(paper, viewport, ref cursorY, "rdp_chart_draws", "Draw Call Count",
                        ("Draw Calls", EditorTheme.Purple500, _viewDraws));

                    // GPU-reported (VK_QUERY_TYPE_PIPELINE_STATISTICS ClippingPrimitives) - real hardware
                    // count, correctly includes indirect draws, excludes anything clipped/culled before
                    // rasterization. Reads 0 while the profiler isn't recording or the device doesn't
                    // support pipelineStatisticsQuery.
                    DrawChart(paper, viewport, ref cursorY, "rdp_chart_tris", "Triangles (GPU)",
                        ("Triangles", EditorTheme.Green500, _viewTris));

                    // FragmentShaderInvocations / (view pixel width * height), from the same
                    // pipeline-statistics query as Triangles above. 1.0 = every pixel shaded exactly
                    // once; higher means overlapping/unsorted geometry shaded pixels more than once.
                    // 0 while the profiler isn't recording or the device doesn't support
                    // pipelineStatisticsQuery.
                    DrawChart(paper, viewport, ref cursorY, "rdp_chart_overdraw", "Overdraw (GPU)",
                        ("Overdraw", EditorTheme.Red500, _viewOverdraw));

                    // Driver-reported VRAM (VK_EXT_memory_budget), not this process's own tracked
                    // Resident/{bin} counters - accounts for other processes sharing the GPU. Both read
                    // 0 if the extension is unavailable.
                    DrawChart(paper, viewport, ref cursorY, "rdp_chart_vram", "VRAM (MB)",
                        ("Budget", EditorTheme.Neutral500, _vramBudget),
                        ("Used", EditorTheme.Amber500, _vramUsed));
                });
            }
        }
    }

    private void DrawChart(
        Paper paper,
        in ScrollViewport viewport,
        ref float cursorY,
        string id, string yLabel,
        params (string Label, Color Color, double[] Values)[] series)
    {
        var converted = new (string Label, Color Color, IReadOnlyList<double> Values)[series.Length];
        for (int i = 0; i < series.Length; i++)
            converted[i] = series[i];

        DrawChart(paper, viewport, ref cursorY, id, yLabel, null, converted);
    }

    private void DrawChart(
        Paper paper,
        in ScrollViewport viewport,
        ref float cursorY,
        string id, string yLabel,
        Func<double, string>? valueFormatter,
        params (string Label, Color Color, IReadOnlyList<double> Values)[] series)
    {
        float top = cursorY;
        float bottom = top + ChartHeight;
        cursorY = bottom + ChartSpacing;

        bool visible = bottom >= viewport.ScrollY && top <= viewport.ScrollY + viewport.Height;
        if (!visible)
        {
            paper.Box(id).Height(ChartHeight).IsNotInteractable();
            return;
        }

        ChartBuilder chart = Origami.Chart(paper, id)
            .Height(ChartHeight)
            .Axes()
            .YLabel(yLabel)
            .Legend(series.Length > 1)
            .BackgroundColor(EditorTheme.Neutral300)
            .LegendShowValue(false)
            .AxisFontSize(13)
            .YTicks(4)
            .Padding(10)
            .Sampleable()
            .GridLineColor(EditorTheme.Neutral500)
            .GridTickLines(0, 1);

        if (valueFormatter != null)
            chart.ValueFormatter(valueFormatter);

        foreach ((string label, Color color, IReadOnlyList<double> values) in series)
            chart.Series(new ChartSeries() { Label = label, Color = color, Values = values });

        chart.Show();
    }
}
