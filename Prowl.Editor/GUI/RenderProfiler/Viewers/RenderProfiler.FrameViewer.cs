using System;
using System.Collections.Generic;

using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Charts;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private void DrawFrameViewer(Paper paper)
    {
        ProfiledFrame? frame = SelectedFrame;
        if (frame == null)
        {
            Origami.Label(paper, "rdp_frame_empty", "No frame selected")
                .Muted()
                .Show();
            return;
        }

        using (paper.Column("rdp_frame_viewer").Height(UnitValue.Auto).ColBetween(SectionGap).Enter())
        {
            using (paper.Row("rdp_frame_header").Height(SelectionViewerHeaderHeight).Enter())
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

            SectionCard(paper, "rdp_fv_renderops", "Render Operations", () => DrawRenderOperationsSection(paper));
            SectionCard(paper, "rdp_fv_resmem", "Resident Memory", () => DrawResidentMemorySection(paper));
            SectionCard(paper, "rdp_fv_liveobj", "Live Objects", () => DrawLiveObjectsSection(paper));
            SectionCard(paper, "rdp_fv_bufferops", "Buffer Operations", () => DrawBufferOperationsSection(paper));
            SectionCard(paper, "rdp_fv_swapchain", "Swapchain", () => DrawSwapchainSection(paper));
            SectionCard(paper, "rdp_fv_barriers", "Barriers", () => DrawBarriersSection(paper));
        }
    }


    // ── Sections ────────────────────────────────────────────────────

    private void DrawRenderOperationsSection(Paper paper)
    {
        using (paper.Column("rdp_fv_renderops").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawGeometryChart(paper, "rdp_fv_geometry_chart", UnitValue.Stretch(), ChartHeight);
            DrawRenderingChart(paper, "rdp_fv_rendering_chart", UnitValue.Stretch(), ChartHeight);
            DrawPipelineStateChart(paper, "rdp_fv_pipelinestate_chart", UnitValue.Stretch(), ChartHeight);
        }
    }


    private void DrawResidentMemorySection(Paper paper)
    {
        using (paper.Column("rdp_fv_resmem").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            using (paper.Row("rdp_fv_resmem_row").Height(UnitValue.Auto).RowBetween(8f).Enter())
            {
                using (paper.Column("rdp_fv_resmem_left").Width(VramColumnWidth).Height(UnitValue.Auto).ColBetween(6f).Enter())
                {
                    DrawVramDonut(paper, "rdp_fv_vram_chart", VramColumnWidth);

                    Origami.Label(paper, "rdp_fv_usage_legend_title", "Usage")
                        .SM()
                        .AlignLeft()
                        .Height(ChartTitleHeight)
                        .Padding(4f, 0f)
                        .Show();

                    DrawUsageLegend(paper, "rdp_fv_usage_legend");
                }

                using (paper.Column("rdp_fv_resmem_right").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
                {
                    DrawUsageChart(paper, "rdp_fv_usage_chart", UnitValue.Stretch(), ChartHeight);
                    DrawBufferUsageChart(paper, "rdp_fv_bufferusage_chart", UnitValue.Stretch(), ChartHeight);
                }
            }
        }
    }


    private void DrawLiveObjectsSection(Paper paper)
    {
        using (paper.Column("rdp_fv_liveobj").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawLiveObjectsCountChart(paper, "rdp_fv_liveobj_count_chart", UnitValue.Stretch(), ChartHeight);

            using (paper.Row("rdp_fv_liveobj_alloc_free_orw").Height(UnitValue.Auto).RowBetween(ChartRowGap).Enter())
            {
                DrawLiveObjectsAllocChart(paper, "rdp_fv_liveobj_alloc_chart", UnitValue.Stretch(), ChartHeight);
                DrawLiveObjectsFreeChart(paper, "rdp_fv_liveobj_free_chart", UnitValue.Stretch(), ChartHeight);
            }
        }
    }


    private void DrawBufferOperationsSection(Paper paper)
    {
        using (paper.Column("rdp_fv_bufferops").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawBufferOpsCountChart(paper, "rdp_fv_bufferops_count_chart", UnitValue.Stretch(), ChartHeight);
            DrawBufferOpsBytesChart(paper, "rdp_fv_bufferops_bytes_chart", UnitValue.Stretch(), ChartHeight);
        }
    }


    private void DrawSwapchainSection(Paper paper)
    {
        using (paper.Column("rdp_fv_swapchain").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawSwapchainOpsChart(paper, "rdp_fv_swap_ops_chart", UnitValue.Stretch(), ChartHeight);
        }
    }


    private void DrawBarriersSection(Paper paper)
    {
        using (paper.Column("rdp_fv_barriers").Height(UnitValue.Auto).ColBetween(ChartRowGap).Enter())
        {
            DrawBarrierOpsChart(paper, "rdp_fv_barrier_ops_chart", UnitValue.Stretch(), ChartHeight);
        }
    }


    // ── Individual charts ────────────────────────────────────────────

    private void DrawGeometryChart(Paper paper, string id, UnitValue width, float height)
    {
        DrawLineChart(paper, id, "Geometry", "Count", FormatCountCompact, width, height, true,
            ("Triangles", EditorTheme.Blue500, FrameSeries(f => f.TrianglesDrawn)),
            ("Vertices", EditorTheme.Purple500, FrameSeries(f => f.InputAssemblyVertices)));
    }


    private void DrawRenderingChart(Paper paper, string id, UnitValue width, float height)
    {
        DrawLineChart(paper, id, "Rendering", "Count", FormatCountCompact, width, height, true,
            ("Draw Calls", EditorTheme.Green500, FrameSeries(f => f.DrawCallCount)),
            ("Resource Binds", EditorTheme.Amber500, CounterSeries("ResourceSet/Binds")),
            ("Dispatches", EditorTheme.Red500, FrameSeries(f => f.DispatchCallCount)));
    }


    private void DrawPipelineStateChart(Paper paper, string id, UnitValue width, float height)
    {
        DrawLineChart(paper, id, "Pipeline State", "Count", FormatCountCompact, width, height, true,
            ("Pipeline Switches", EditorTheme.Purple500, FrameSeries(f => f.PipelineSwitchCount)),
            ("Command Submits", EditorTheme.Blue500, CounterSeries("Submit/Graphics")),
            ("Transfer Submits", EditorTheme.Amber500, CounterSeries("Submit/Transfer")));
    }


    private void DrawVramDonut(Paper paper, string id, float size)
    {
        ProfiledFrame? frame = SelectedFrame;

        double budget = frame is { HasVramBudget: true } ? frame.VramBudgetBytes : 0d;
        double used = frame?.VramUsedBytes ?? 0d;
        double knownBufferBytes = CounterValue(frame, "Resident/DeviceBuffer")
            + CounterValue(frame, "Resident/Texture")
            + CounterValue(frame, "Resident/Shader");
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
            .ValueFormatter(FormatBytesAuto)
            .Legend(false)
            .InnerRadius(0.62f)
            .Size(size, size)
            .Show();
    }


    private void DrawUsageLegend(Paper paper, string id)
    {
        ProfiledFrame? frame = SelectedFrame;

        double bufferBytes = CounterValue(frame, "Resident/DeviceBuffer");
        double textureBytes = CounterValue(frame, "Resident/Texture");
        double shaderBytes = CounterValue(frame, "Resident/Shader");

        var entries = new List<LegendEntry>
        {
            new("Buffer", EditorTheme.Blue500, 0, FormatBytesAuto(bufferBytes)),
            new("Texture", EditorTheme.Purple500, 1, FormatBytesAuto(textureBytes)),
            new("Shader", EditorTheme.Amber500, 2, FormatBytesAuto(shaderBytes)),
        };

        Origami.Legend(paper, id, entries).Show();
    }


    private void DrawUsageChart(Paper paper, string id, UnitValue width, float height)
    {
        DrawLineChart(paper, id, "Usage", "Size", FormatBytesAuto, width, height, false,
            ("Buffer", EditorTheme.Blue500, CounterSeries("Resident/DeviceBuffer")),
            ("Texture", EditorTheme.Purple500, CounterSeries("Resident/Texture")),
            ("Shader", EditorTheme.Amber500, CounterSeries("Resident/Shader")));
    }


    private void DrawBufferUsageChart(Paper paper, string id, UnitValue width, float height)
    {
        BufferRoleBin[] roles = Enum.GetValues<BufferRoleBin>();
        var series = new (string, Color, IReadOnlyList<double>)[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            series[i] = (roles[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"Resident/{roles[i]}"));

        DrawLineChart(paper, id, "Buffer Usage", "Size", FormatBytesAuto, width, height, false, series);
    }


    private void DrawLiveObjectsCountChart(Paper paper, string id, UnitValue width, float height)
    {
        AllocBin[] bins = Enum.GetValues<AllocBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"Live/{bins[i]}"));

        DrawLineChart(paper, id, "Count", "Objects", FormatCountCompact, width, height, false, series);
    }


    private void DrawLiveObjectsAllocChart(Paper paper, string id, UnitValue width, float height)
    {
        AllocBin[] bins = Enum.GetValues<AllocBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"Alloc/{bins[i]}"));

        DrawLineChart(paper, id, "Allocations", "Objects", FormatCountCompact, width, height, false, series);
    }


    private void DrawLiveObjectsFreeChart(Paper paper, string id, UnitValue width, float height)
    {
        AllocBin[] bins = Enum.GetValues<AllocBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"Free/{bins[i]}"));

        DrawLineChart(paper, id, "Frees", "Objects", FormatCountCompact, width, height, false, series);
    }


    private void DrawBufferOpsCountChart(Paper paper, string id, UnitValue width, float height)
    {
        BufferOpBin[] ops = Enum.GetValues<BufferOpBin>();
        var series = new (string, Color, IReadOnlyList<double>)[ops.Length];
        for (int i = 0; i < ops.Length; i++)
            series[i] = (ops[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"BufferOp/{ops[i]}"));

        DrawLineChart(paper, id, "Count", "Ops", FormatCountCompact, width, height, true, series);
    }


    private void DrawBufferOpsBytesChart(Paper paper, string id, UnitValue width, float height)
    {
        // Unmap always records 0 bytes and stays zeroed - excluded deliberately.
        var ops = new[] { BufferOpBin.Map, BufferOpBin.Update, BufferOpBin.Copy };
        var series = new (string, Color, IReadOnlyList<double>)[ops.Length];
        for (int i = 0; i < ops.Length; i++)
            series[i] = (ops[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"BufferOpBytes/{ops[i]}"));

        DrawLineChart(paper, id, "Bytes", "Size", FormatBytesAuto, width, height, true, series);
    }


    private void DrawSwapchainOpsChart(Paper paper, string id, UnitValue width, float height)
    {
        SwapBin[] bins = Enum.GetValues<SwapBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"Swap/{bins[i]}"));

        DrawLineChart(paper, id, "Operations", "Ops", FormatCountCompact, width, height, true, series);
    }


    private void DrawBarrierOpsChart(Paper paper, string id, UnitValue width, float height)
    {
        BarrierBin[] bins = Enum.GetValues<BarrierBin>();
        var series = new (string, Color, IReadOnlyList<double>)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), SeriesPalette[i % SeriesPalette.Length], CounterSeries($"Barrier/{bins[i]}"));

        DrawLineChart(paper, id, "Operations", "Ops", FormatCountCompact, width, height, true, series);
    }


    // Shared card/chart/formatting helpers (SectionCard, DrawLineChart, FormatBytesAuto, FrameSeries,
    // CounterSeries, ...) live in RenderProfiler.ViewerShared.cs.
}
