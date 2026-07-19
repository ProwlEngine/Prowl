using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Engine counters live tab. Plots the frame ring buffer as rolling charts (via the Origami chart
/// widget): CPU frame time, draw calls, pooled render-target memory and visible renderables. Graphite
/// device counters live in their own tab (see <see cref="GraphiteCountersView"/>).
/// </summary>
public static class CountersView
{
    public static void Draw(Paper paper, List<RenderFrameReport> history, FontFile font, float width, float height)
    {
        if (history.Count == 0)
        {
            using (paper.Column("rp_ctr_root").Size(width, height).Enter())
                EditorGUI.EmptyState(paper, "rp_ctr_empty", "No frames buffered yet", font);
            return;
        }

        int n = history.Count;
        var cpu = new double[n];
        var draws = new double[n];
        var rtBytes = new double[n];
        var visible = new double[n];

        for (int i = 0; i < n; i++)
        {
            var r = history[i];
            cpu[i] = r.CpuFrameMs;
            draws[i] = r.Counters.DrawCalls;
            rtBytes[i] = r.Counters.PooledRtBytes / (1024.0 * 1024.0);
            visible[i] = r.Counters.RenderablesVisible;
        }

        var charts = new List<ProfilerCharts.ChartSpec>
        {
            new()
            {
                Title = "CPU Frame Time", YLabel = "ms", Format = v => v.ToString("0.00") + " ms",
                Series = { ("ms", EditorTheme.Blue400, cpu) },
            },
            new()
            {
                Title = "Draw Calls", Format = v => v.ToString("0"),
                Series = { ("draws", EditorTheme.Green400, draws) },
            },
            new()
            {
                Title = "Pooled Render Targets", YLabel = "MB", Format = v => v.ToString("0.0") + " MB",
                Series = { ("MB", EditorTheme.Amber400, rtBytes) },
            },
            new()
            {
                Title = "Renderables Visible", Format = v => v.ToString("0"),
                Series = { ("visible", EditorTheme.Purple400, visible) },
            },
        };

        ProfilerCharts.DrawGrid(paper, charts, font, width, height, "rp_ctr");
    }
}
