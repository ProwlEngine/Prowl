using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.PaperUI;
using Prowl.Scribe;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Graphite device counters live tab. Reads <see cref="ProfileSnapshot"/> straight from Graphite (via
/// <see cref="RenderProfilerPanel"/>'s own sample history, one snapshot per editor frame) and charts the
/// bins directly - no intermediate copy lives on the render report, since this is Graphite's own state,
/// not render-graph state. Aggregates into curated, logically grouped charts (allocations vs frees
/// overlaid, total allocations, resident memory, buffer memory by role, buffer ops, live objects, swaps),
/// stacked one per row. Every chart is always shown, even if its series are flat at zero.
/// </summary>
public static class GraphiteCountersView
{
    private static readonly AllocBin[] AllocBins =
    {
        AllocBin.DeviceBuffer, AllocBin.Texture, AllocBin.TextureView, AllocBin.Sampler, AllocBin.Framebuffer,
        AllocBin.Pipeline, AllocBin.Shader, AllocBin.ResourceLayout, AllocBin.ResourceSet, AllocBin.CommandBuffer,
    };

    private static readonly BufferRoleBin[] BufferRoles =
    {
        BufferRoleBin.Vertex, BufferRoleBin.Index, BufferRoleBin.Uniform, BufferRoleBin.StructuredReadOnly,
        BufferRoleBin.StructuredReadWrite, BufferRoleBin.Indirect, BufferRoleBin.Staging, BufferRoleBin.Dynamic,
    };

    private static readonly BufferOpBin[] BufferOps = { BufferOpBin.Map, BufferOpBin.Unmap, BufferOpBin.Update, BufferOpBin.Copy };
    private static readonly SwapBin[] Swaps = { SwapBin.Present, SwapBin.Resize, SwapBin.Acquire };

    public static void Draw(Paper paper, List<ProfileSnapshot> history, FontFile font, float width, float height)
    {
        if (history.Count == 0)
        {
            using (paper.Column("rp_gctr_root").Size(width, height).Enter())
                EditorGUI.EmptyState(paper, "rp_gctr_empty", "No frames buffered yet", font);
            return;
        }

        var charts = new List<ProfilerCharts.ChartSpec>();

        double[] allocTotal = SumCounts(history, s => s.Allocated, AllocBins);
        double[] freeTotal = SumCounts(history, s => s.Freed, AllocBins);

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
            ("Textures", ProfilerCharts.Palette[3], Bytes(history, s => s.Live, AllocBin.Texture)),
            ("Buffers", ProfilerCharts.Palette[0], Bytes(history, s => s.Live, AllocBin.DeviceBuffer)),
        });

        AddChart(charts, "Buffer Memory by Role", v => v.ToString("0.0") + " MB", "MB",
            PerBufferRole(history, s => s.BufferMem, BufferRoles, useBytes: true));

        AddChart(charts, "Buffer Ops", v => v.ToString("0"), "count",
            PerBufferOp(history, s => s.BufferOps, BufferOps));

        AddChart(charts, "Live Objects", v => v.ToString("0"), "count",
            PerAlloc(history, s => s.Live, AllocBins));

        AddChart(charts, "Swapchain", v => v.ToString("0"), "count",
            PerSwap(history, s => s.Swaps, Swaps));

        ProfilerCharts.DrawGrid(paper, charts, font, width, height, "rp_gctr");
    }

    private static void AddChart(List<ProfilerCharts.ChartSpec> charts, string title, System.Func<double, string> format, string yLabel,
        IReadOnlyList<(string Label, Color Color, double[] Values)> series)
    {
        charts.Add(new ProfilerCharts.ChartSpec { Title = title, YLabel = yLabel, Format = format, Series = new List<(string, Color, double[])>(series) });
    }

    private static (string, Color, double[])[] PerAlloc(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<AllocBin>> select, AllocBin[] bins)
    {
        var result = new (string, Color, double[])[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            result[i] = (bins[i].ToString(), ProfilerCharts.Palette[i % ProfilerCharts.Palette.Length], Counts(history, select, bins[i]));
        return result;
    }

    private static (string, Color, double[])[] PerBufferRole(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<BufferRoleBin>> select, BufferRoleBin[] bins, bool useBytes)
    {
        var result = new (string, Color, double[])[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            result[i] = (bins[i].ToString(), ProfilerCharts.Palette[i % ProfilerCharts.Palette.Length],
                useBytes ? Bytes(history, select, bins[i]) : Counts(history, select, bins[i]));
        return result;
    }

    private static (string, Color, double[])[] PerBufferOp(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<BufferOpBin>> select, BufferOpBin[] bins)
    {
        var result = new (string, Color, double[])[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            result[i] = (bins[i].ToString(), ProfilerCharts.Palette[i % ProfilerCharts.Palette.Length], Counts(history, select, bins[i]));
        return result;
    }

    private static (string, Color, double[])[] PerSwap(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<SwapBin>> select, SwapBin[] bins)
    {
        var result = new (string, Color, double[])[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            result[i] = (bins[i].ToString(), ProfilerCharts.Palette[i % ProfilerCharts.Palette.Length], Counts(history, select, bins[i]));
        return result;
    }

    private static double[] Counts<TBin>(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<TBin>> select, TBin bin)
        where TBin : unmanaged, System.Enum
    {
        int n = history.Count;
        var values = new double[n];
        for (int i = 0; i < n; i++)
            values[i] = select(history[i])[bin].Count;
        return values;
    }

    private static double[] Bytes<TBin>(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<TBin>> select, TBin bin)
        where TBin : unmanaged, System.Enum
    {
        int n = history.Count;
        var values = new double[n];
        for (int i = 0; i < n; i++)
            values[i] = select(history[i])[bin].Bytes / (1024.0 * 1024.0);
        return values;
    }

    private static double[] SumCounts(List<ProfileSnapshot> history, System.Func<ProfileSnapshot, ProfileBinGroup<AllocBin>> select, AllocBin[] bins)
    {
        int n = history.Count;
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            ProfileBinGroup<AllocBin> group = select(history[i]);
            double sum = 0;
            foreach (AllocBin bin in bins)
                sum += group[bin].Count;
            values[i] = sum;
        }
        return values;
    }
}
