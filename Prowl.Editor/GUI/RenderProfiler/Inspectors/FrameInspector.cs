using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Charts;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

// Stateless - every value it draws comes from the frame/history passed into Draw.
public static class ProfilerFrameInspector
{
    public static void Draw(Paper paper, ProfiledFrame? frame, IProfilerHistory history)
    {
        if (frame == null)
        {
            EditorGUI.EmptyState(paper, "rdp_frame_empty", "No frame selected", EditorTheme.DefaultFont!);
            return;
        }

        using (paper.Column("rdp_frame_viewer").Height(UnitValue.Auto).ColBetween(InspectorKit.SectionGap).Enter())
        {
            using (paper.Row("rdp_frame_header").Height(InspectorKit.SelectionViewerHeaderHeight).Enter())
            {
                Origami.Label(paper, "rdp_frame_title", $"Frame {frame.FrameIndex}")
                    .Heading()
                    .AlignLeft()
                    .Show();

                paper.Box("rdp_frame_header_spacer");

                Origami.Label(paper, "rdp_frame_fps", $"{frame.Fps:F1} FPS ({frame.FrameMilliseconds:F2} ms)")
                    .Muted()
                    .AlignRight()
                    .Show();

                Origami.Label(paper, "rdp_frame_depth", frame.HasCaptureDepth ? "Full Depth" : "Live")
                    .Variant(frame.HasCaptureDepth ? OrigamiVariant.Success : OrigamiVariant.Subtle)
                    .AlignRight()
                    .Show();
            }

            InspectorKit.SectionCard(paper, "rdp_fv_renderops", "Render Operations", () => DrawRenderOperationsSection(paper, history));
            InspectorKit.SectionCard(paper, "rdp_fv_resmem", "Resident Memory", () => DrawResidentMemorySection(paper, frame, history));
            InspectorKit.SectionCard(paper, "rdp_fv_liveobj", "Live Objects", () => DrawLiveObjectsSection(paper, frame));
            InspectorKit.SectionCard(paper, "rdp_fv_bufferops", "Buffer Operations", () => DrawBufferOperationsSection(paper, history));
            InspectorKit.SectionCard(paper, "rdp_fv_swapchain", "Swapchain", () => DrawSwapchainSection(paper, history));
            InspectorKit.SectionCard(paper, "rdp_fv_barriers", "Barriers", () => DrawBarriersSection(paper, history));
        }
    }


    // ── Sections ────────────────────────────────────────────────────

    private static void DrawRenderOperationsSection(Paper paper, IProfilerHistory history)
    {
        using (paper.Column("rdp_fv_renderops").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawGeometryChart(paper, "rdp_fv_geometry_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
            DrawRenderingChart(paper, "rdp_fv_rendering_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
            DrawPipelineStateChart(paper, "rdp_fv_pipelinestate_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
        }
    }


    private static void DrawResidentMemorySection(Paper paper, ProfiledFrame frame, IProfilerHistory history)
    {
        using (paper.Column("rdp_fv_resmem").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            using (paper.Row("rdp_fv_resmem_row").Height(UnitValue.Auto).RowBetween(8f).Enter())
            {
                using (paper.Column("rdp_fv_resmem_left").Width(InspectorKit.VramColumnWidth).Height(UnitValue.Auto).ColBetween(6f).Enter())
                {
                    DrawVramDonut(paper, "rdp_fv_vram_chart", InspectorKit.VramColumnWidth, frame);

                    Origami.Label(paper, "rdp_fv_usage_legend_title", "Usage")
                        .SM()
                        .AlignLeft()
                        .Height(InspectorKit.ChartTitleHeight)
                        .Padding(4f, 0f)
                        .Show();

                    DrawUsageLegend(paper, "rdp_fv_usage_legend", frame);
                }

                using (paper.Column("rdp_fv_resmem_right").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
                {
                    DrawUsageChart(paper, "rdp_fv_usage_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
                    DrawBufferUsageChart(paper, "rdp_fv_bufferusage_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
                }
            }
        }
    }


    // One row per live-object category instead of a StatGroup per Live/Alloc/Free series - with
    // AllocBin's full set of categories, three side-by-side StatGroups overflowed the card width and
    // overlapped.
    private static void DrawLiveObjectsSection(Paper paper, ProfiledFrame frame)
    {
        AllocBin[] bins = Enum.GetValues<AllocBin>();

        TableBuilder table = Origami.Table(paper, "rdp_fv_liveobj_table", -1, _ => { })
            .Bordered(true)
            .Width(UnitValue.Stretch())
            .RowHeight(24f)
            .Column("Name", 1.6f)
            .Column("Count", 1f, align: TextAlignment.MiddleRight)
            .Column("Allocation", 1f, align: TextAlignment.MiddleRight)
            .Column("Free", 1f, align: TextAlignment.MiddleRight);

        foreach (AllocBin bin in bins)
        {
            double count = InspectorKit.CounterValue(frame, $"Live/{bin}");
            double allocs = InspectorKit.CounterValue(frame, $"Alloc/{bin}");
            double frees = InspectorKit.CounterValue(frame, $"Free/{bin}");

            table.Row()
                .Cell(bin.ToString(), EditorTheme.Ink500)
                .CellRight(InspectorKit.FormatCountCompact(count), EditorTheme.Ink500)
                .CellRight(InspectorKit.FormatCountCompact(allocs), EditorTheme.Ink500)
                .CellRight(InspectorKit.FormatCountCompact(frees), EditorTheme.Ink500);
        }

        table.Show();
    }


    private static void DrawBufferOperationsSection(Paper paper, IProfilerHistory history)
    {
        using (paper.Column("rdp_fv_bufferops").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawBufferOpsCountChart(paper, "rdp_fv_bufferops_count_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
            DrawBufferOpsBytesChart(paper, "rdp_fv_bufferops_bytes_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
        }
    }


    private static void DrawSwapchainSection(Paper paper, IProfilerHistory history)
    {
        using (paper.Column("rdp_fv_swapchain").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawSwapchainOpsChart(paper, "rdp_fv_swap_ops_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
        }
    }


    private static void DrawBarriersSection(Paper paper, IProfilerHistory history)
    {
        using (paper.Column("rdp_fv_barriers").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawBarrierOpsChart(paper, "rdp_fv_barrier_ops_chart", UnitValue.Stretch(), InspectorKit.ChartHeight, history);
        }
    }


    // ── Individual charts ────────────────────────────────────────────

    private static void DrawGeometryChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, id, "Geometry", "Count", InspectorKit.FormatCountCompact, width, height, true,
            ("Triangles", EditorTheme.Blue500, InspectorKit.FrameSeries(history, f => f.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, InspectorKit.FrameSeries(history, f => f.InputAssemblyVertices)));
    }


    private static void DrawRenderingChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, id, "Rendering", "Count", InspectorKit.FormatCountCompact, width, height, true,
            ("Draw Calls", EditorTheme.Green500, InspectorKit.FrameSeries(history, f => f.DrawCallCount)),
            ("Resource Binds", EditorTheme.Amber500, history.Counter("ResourceSet/Binds")),
            ("Dispatches", EditorTheme.Red500, InspectorKit.FrameSeries(history, f => f.DispatchCallCount)));
    }


    private static void DrawPipelineStateChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, id, "Pipeline State", "Count", InspectorKit.FormatCountCompact, width, height, true,
            ("Pipeline Switches", EditorTheme.Purple500, InspectorKit.FrameSeries(history, f => f.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, history.Counter("Submit/Graphics")),
            ("Transfer Submits", EditorTheme.Amber500, history.Counter("Submit/Transfer")));
    }


    private static void DrawVramDonut(Paper paper, string id, float size, ProfiledFrame? frame)
    {
        double budget = frame is { HasVramBudget: true } ? frame.VramBudgetBytes : 0d;
        double used = frame?.VramUsedBytes ?? 0d;
        double knownBufferBytes = InspectorKit.CounterValue(frame, "Resident/DeviceBuffer")
            + InspectorKit.CounterValue(frame, "Resident/Texture")
            + InspectorKit.CounterValue(frame, "Resident/Shader");
        double owned = Math.Max(0d, used - knownBufferBytes);
        double free = Math.Max(0d, budget - used);

        var slices = new (string Name, double Bytes, Color Color)[]
        {
            ("Free", free, EditorTheme.Green500),
            ("Used", knownBufferBytes, EditorTheme.Blue500),
            ("Owned", owned, EditorTheme.Amber500),
        };

        Chart.Donut(paper, id, slices)
            .Title("VRAM")
            .Name(s => s.Name)
            .Value(s => s.Bytes)
            .ColorFunction((s, _) => s.Color)
            .ValueFormatter(InspectorKit.FormatBytesAuto)
            .Legend(false)
            .InnerRadius(0.62f)
            .Size(size, size)
            .Show();
    }


    private static void DrawUsageLegend(Paper paper, string id, ProfiledFrame? frame)
    {
        double bufferBytes = InspectorKit.CounterValue(frame, "Resident/DeviceBuffer");
        double textureBytes = InspectorKit.CounterValue(frame, "Resident/Texture");
        double shaderBytes = InspectorKit.CounterValue(frame, "Resident/Shader");

        var entries = new List<LegendEntry>
        {
            new("Buffer", EditorTheme.Blue500, 0, InspectorKit.FormatBytesAuto(bufferBytes)),
            new("Texture", EditorTheme.Purple500, 1, InspectorKit.FormatBytesAuto(textureBytes)),
            new("Shader", EditorTheme.Amber500, 2, InspectorKit.FormatBytesAuto(shaderBytes)),
        };

        Origami.Legend(paper, id, entries)
            .Width(InspectorKit.VramColumnWidth)
            .Show();
    }


    private static void DrawUsageChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        InspectorKit.DrawChart(paper, history, id, "Usage", "Size", InspectorKit.FormatBytesAuto, width, height, false,
            ("Buffer", EditorTheme.Blue500, history.Counter("Resident/DeviceBuffer")),
            ("Texture", EditorTheme.Purple500, history.Counter("Resident/Texture")),
            ("Shader", EditorTheme.Amber500, history.Counter("Resident/Shader")));
    }


    private static void DrawBufferUsageChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        BufferRoleBin[] roles = Enum.GetValues<BufferRoleBin>();
        var series = new (string, Color, IReadOnlyList<double>)[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            series[i] = (roles[i].ToString(), InspectorKit.SeriesPalette[i % InspectorKit.SeriesPalette.Length], history.Counter($"Resident/{roles[i]}"));

        InspectorKit.DrawChart(paper, history, id, "Buffer Usage", "Size", InspectorKit.FormatBytesAuto, width, height, false, series);
    }


    private static void DrawBufferOpsCountChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        BufferOpBin[] ops = Enum.GetValues<BufferOpBin>();
        var series = new (string, Color, IReadOnlyList<double>)[ops.Length];
        for (int i = 0; i < ops.Length; i++)
            series[i] = (ops[i].ToString(), InspectorKit.SeriesPalette[i % InspectorKit.SeriesPalette.Length], history.Counter($"BufferOp/{ops[i]}"));

        InspectorKit.DrawChart(paper, history, id, "Count", "Ops", InspectorKit.FormatCountCompact, width, height, true, series);
    }


    private static void DrawBufferOpsBytesChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        // Unmap always records 0 bytes and stays zeroed - excluded deliberately.
        var ops = new[] { BufferOpBin.Map, BufferOpBin.Update, BufferOpBin.Copy };
        var series = new (string, Color, IReadOnlyList<double>)[ops.Length];
        for (int i = 0; i < ops.Length; i++)
            series[i] = (ops[i].ToString(), InspectorKit.SeriesPalette[i % InspectorKit.SeriesPalette.Length], history.Counter($"BufferOpBytes/{ops[i]}"));

        InspectorKit.DrawChart(paper, history, id, "Bytes", "Size", InspectorKit.FormatBytesAuto, width, height, true, series);
    }


    private static void DrawSwapchainOpsChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        SwapBin[] bins = Enum.GetValues<SwapBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), InspectorKit.SeriesPalette[i % InspectorKit.SeriesPalette.Length], history.Counter($"Swap/{bins[i]}"));

        InspectorKit.DrawChart(paper, history, id, "Operations", "Ops", InspectorKit.FormatCountCompact, width, height, true, series);
    }


    private static void DrawBarrierOpsChart(Paper paper, string id, UnitValue width, float height, IProfilerHistory history)
    {
        BarrierBin[] bins = Enum.GetValues<BarrierBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), InspectorKit.SeriesPalette[i % InspectorKit.SeriesPalette.Length], history.Counter($"Barrier/{bins[i]}"));

        InspectorKit.DrawChart(paper, history, id, "Operations", "Ops", InspectorKit.FormatCountCompact, width, height, true, series);
    }
}
