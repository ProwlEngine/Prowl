using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel : DockPanel
{
    private static readonly AllocBin[] s_allocBins = Enum.GetValues<AllocBin>();
    private static readonly BufferRoleBin[] s_bufferRoleBins = Enum.GetValues<BufferRoleBin>();
    private static readonly BufferOpBin[] s_bufferOpBins = Enum.GetValues<BufferOpBin>();
    private static readonly SwapBin[] s_swapBins = Enum.GetValues<SwapBin>();
    private static readonly BarrierBin[] s_barrierBins = Enum.GetValues<BarrierBin>();
    private static readonly SubmitKind[] s_submitKinds = Enum.GetValues<SubmitKind>();

    private static readonly Color[] s_palette =
    {
        EditorTheme.Purple400, EditorTheme.Blue400, EditorTheme.Red400, EditorTheme.Green400, EditorTheme.Amber400,
        EditorTheme.Purple600, EditorTheme.Blue600, EditorTheme.Red600, EditorTheme.Green600, EditorTheme.Amber600,
        EditorTheme.Purple300, EditorTheme.Blue300, EditorTheme.Red300, EditorTheme.Green300, EditorTheme.Amber300,
    };

    private static Color PaletteColor(int index) => s_palette[index % s_palette.Length];

    private void DrawNativeStatsViewer(Paper paper, float width, float height)
    {
        using (paper.Column("rdp_native_box").Padding(6).Enter())
        {
            float scrollWidth = width - 12f;
            float scrollHeight = height - ToolbarChromeHeight - 12f;

            Origami.ScrollView(paper, "rdp_native_scrollview", scrollWidth, scrollHeight).ColSpacing(ChartSpacing).Body(viewport =>
            {
                float cursorY = 0f;

                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_live", "Live Objects", null, s_allocBins, bin => $"Live/{bin}");
                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_resident", "Resident Memory", FormatBytes, s_allocBins, bin => $"Resident/{bin}");

                using (paper.Row("rdp_alloc_free_row").RowBetween(6).Height(ChartHeight).Enter())
                {
                    float cursorCopy = cursorY;
                    DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_alloc", "Allocations", null, s_allocBins, bin => $"Alloc/{bin}");
                    DrawEnumChart(paper, viewport, ref cursorCopy, "rdp_native_free", "Frees", null, s_allocBins, bin => $"Free/{bin}");
                }

                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_bufmem", "Buffer Memory by Role", FormatBytes, s_bufferRoleBins, role => $"Resident/{role}");

                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_bufopcount", "Buffer Op Count", null, s_bufferOpBins, op => $"BufferOp/{op}");
                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_bufopbytes", "Buffer Op Bytes", FormatBytes, s_bufferOpBins, op => $"BufferOpBytes/{op}");

                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_swap", "Swapchain Ops", null, s_swapBins, bin => $"Swap/{bin}");
                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_barrier", "Barriers", null, s_barrierBins, bin => $"Barrier/{bin}");
                DrawEnumChart(paper, viewport, ref cursorY, "rdp_native_submit", "Submits", null, s_submitKinds, kind => $"Submit/{kind}");

                DrawChart(paper, viewport, ref cursorY, "rdp_native_misc", "Draws / Dispatches / Binds", null,
                    ("Resource Set Binds", PaletteColor(0), _profiler.CounterHistory("ResourceSet/Binds")),
                    ("Draw Count", PaletteColor(1), _profiler.CounterHistory("Draw/Count")),
                    ("Dispatch Count", PaletteColor(2), _profiler.CounterHistory("Dispatch/Count")));
            });
        }
    }

    private void DrawEnumChart<T>(
        Paper paper,
        in ScrollViewport viewport,
        ref float cursorY,
        string id, string yLabel,
        Func<double, string>? valueFormatter,
        T[] bins,
        Func<T, string> counterName)
        where T : Enum
    {
        var series = new (string Label, Color Color, IReadOnlyList<double> Values)[bins.Length];
        for (int i = 0; i < bins.Length; i++)
            series[i] = (bins[i].ToString(), PaletteColor(i), _profiler.CounterHistory(counterName(bins[i])));

        DrawChart(paper, viewport, ref cursorY, id, yLabel, valueFormatter, series);
    }

    private static string FormatBytes(double bytes)
    {
        double abs = Math.Abs(bytes);
        if (abs < 1024.0)
            return $"{bytes:0} B";
        if (abs < 1024.0 * 1024.0)
            return $"{bytes / 1024.0:0.#} KB";
        if (abs < 1024.0 * 1024.0 * 1024.0)
            return $"{bytes / (1024.0 * 1024.0):0.#} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.#} GB";
    }
}
