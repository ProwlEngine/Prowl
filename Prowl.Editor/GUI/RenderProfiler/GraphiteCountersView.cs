using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Graphite device counters live tab. Aggregates <see cref="FrameCounters.GraphiteCounters"/> across
/// the ring buffer into curated, logically grouped charts (allocations vs frees overlaid, total
/// allocations, resident memory, buffer memory by role, buffer ops, live objects, swaps), stacked
/// one per row. Every chart is always shown, even if its series are flat at zero.
/// </summary>
public static class GraphiteCountersView
{
    private static readonly string[] AllocBins =
    {
        "DeviceBuffer", "Texture", "TextureView", "Sampler", "Framebuffer",
        "Pipeline", "Shader", "ResourceLayout", "ResourceSet", "CommandBuffer",
    };

    private static readonly string[] BufferRoles =
    {
        "Vertex", "Index", "Uniform", "StructuredReadOnly", "StructuredReadWrite",
        "Indirect", "Staging", "Dynamic",
    };

    private static readonly string[] BufferOps = { "Map", "Unmap", "Update", "Copy" };
    private static readonly string[] Swaps = { "Present", "Resize", "Acquire" };

    public static void Draw(Paper paper, List<RenderFrameReport> history, FontFile font, float width, float height)
    {
        if (history.Count == 0)
        {
            using (paper.Column("rp_gctr_root").Size(width, height).Enter())
                EditorGUI.EmptyState(paper, "rp_gctr_empty", "No frames buffered yet", font);
            return;
        }

        var charts = new List<ProfilerCharts.ChartSpec>();

        double[] allocTotal = SumBins(history, "Allocated", AllocBins, "Count");
        double[] freeTotal = SumBins(history, "Freed", AllocBins, "Count");

        AddChart(charts, "Allocations vs Frees", v => v.ToString("0"), "count", new[]
        {
            ("Allocated", ProfilerCharts.Palette[4], allocTotal),
            ("Freed", ProfilerCharts.Palette[1], freeTotal),
        });

        AddChart(charts, "Total Allocations", v => v.ToString("0"), "count", new[]
        {
            ("Allocated", ProfilerCharts.Palette[0], allocTotal),
        });

        AddChart(charts, "Resident Memory", v => v.ToString("0.0") + " MB", "MB", new[]
        {
            ("Textures", ProfilerCharts.Palette[3], Single(history, "Live", "Texture", "MB")),
            ("Buffers", ProfilerCharts.Palette[0], Single(history, "Live", "DeviceBuffer", "MB")),
        });

        AddChart(charts, "Buffer Memory by Role", v => v.ToString("0.0") + " MB", "MB",
            PerBin(history, "BufferMem", BufferRoles, "MB"));

        AddChart(charts, "Buffer Ops", v => v.ToString("0"), "count",
            PerBin(history, "BufferOps", BufferOps, "Count"));

        AddChart(charts, "Live Objects", v => v.ToString("0"), "count",
            PerBin(history, "Live", AllocBins, "Count"));

        AddChart(charts, "Swapchain", v => v.ToString("0"), "count",
            PerBin(history, "Swaps", Swaps, "Count"));

        ProfilerCharts.DrawGrid(paper, charts, font, width, height, "rp_gctr");
    }

    private static void AddChart(List<ProfilerCharts.ChartSpec> charts, string title, System.Func<double, string> format, string yLabel,
        IReadOnlyList<(string Label, Color Color, double[] Values)> series)
    {
        charts.Add(new ProfilerCharts.ChartSpec { Title = title, YLabel = yLabel, Format = format, Series = new List<(string, Color, double[])>(series) });
    }

    private static (string, Color, double[])[] PerBin(List<RenderFrameReport> history, string group, string[] bins, string unit)
    {
        var result = new (string, Color, double[])[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            result[i] = (bins[i], ProfilerCharts.Palette[i % ProfilerCharts.Palette.Length], Single(history, group, bins[i], unit));
        return result;
    }

    private static double[] Single(List<RenderFrameReport> history, string group, string bin, string unit)
    {
        string key = $"{group}/{bin} {unit}";
        int n = history.Count;
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            var g = history[i].Counters.GraphiteCounters;
            values[i] = g != null && g.TryGetValue(key, out double v) ? v : 0d;
        }
        return values;
    }

    private static double[] SumBins(List<RenderFrameReport> history, string group, string[] bins, string unit)
    {
        int n = history.Count;
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            var g = history[i].Counters.GraphiteCounters;
            if (g == null) continue;

            double sum = 0;
            foreach (string bin in bins)
                if (g.TryGetValue($"{group}/{bin} {unit}", out double v))
                    sum += v;
            values[i] = sum;
        }
        return values;
    }
}
