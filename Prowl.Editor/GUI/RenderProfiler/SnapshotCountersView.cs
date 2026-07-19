using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Snapshot Counters tab: a static readout of the frozen <see cref="FrameCounters"/> and its open
/// <see cref="FrameCounters.ExtraCounters"/> dictionary. Because a snapshot is a single frame there is no
/// series to plot; values are shown as labelled cards with a proportion bar scaled within their group.
/// </summary>
public static class SnapshotCountersView
{
    public static void Draw(Paper paper, RenderFrameReport report, FontFile font, float width, float height)
    {
        var c = report.Counters;

        var items = new List<(string label, double value, string display, Color color)>
        {
            ("CPU Frame", report.CpuFrameMs, report.CpuFrameMs.ToString("0.00") + " ms", EditorTheme.Blue400),
            ("Passes", c.Passes, c.Passes.ToString(), EditorTheme.Purple400),
            ("Draw Calls", c.DrawCalls, c.DrawCalls.ToString(), EditorTheme.Green400),
            ("Triangles", c.TrianglesApprox, c.TrianglesApprox.ToString(), EditorTheme.Amber400),
            ("Collected", c.RenderablesCollected, c.RenderablesCollected.ToString(), EditorTheme.Ink500),
            ("Visible", c.RenderablesVisible, c.RenderablesVisible.ToString(), EditorTheme.Green400),
            ("Culled", c.RenderablesCulled, c.RenderablesCulled.ToString(), EditorTheme.Amber400),
            ("Pooled RTs", c.PooledRtCount, c.PooledRtCount.ToString(), EditorTheme.Blue400),
            ("Pooled RT MB", c.PooledRtBytes / (1024.0 * 1024.0), (c.PooledRtBytes / (1024.0 * 1024.0)).ToString("0.00") + " MB", EditorTheme.Red400),
        };

        if (c.ExtraCounters != null)
            foreach (var kv in c.ExtraCounters)
                items.Add((kv.Key, kv.Value, kv.Value.ToString("0.###"), EditorTheme.Purple400));

        double max = 1;
        foreach (var it in items) if (it.value > max) max = it.value;

        using (paper.Column("rp_snap_ctr_root").Size(width, height).Enter())
            Origami.ScrollView(paper, "rp_snap_ctr_scroll", width, height).Body(() =>
            {
                using (paper.Column("rp_snap_ctr_list").Width(width - 12).Height(UnitValue.Auto).ColBetween(6).Padding(8, 8, 8, 8).Enter())
                    for (int i = 0; i < items.Count; i++)
                        Card(paper, i, items[i].label, items[i].display, items[i].value / max, items[i].color, font);
            });
    }

    private static void Card(Paper paper, int index, string label, string display, double fraction, Color color, FontFile font)
    {
        using (paper.Column($"rp_snap_ctr_c_{index}").Width(UnitValue.Percentage(100)).Height(52)
            .Rounded(8).Padding(10, 10, 6, 6)
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            using (paper.Row($"rp_snap_ctr_c_{index}_hdr").Width(UnitValue.Percentage(100)).Height(18).Enter())
            {
                paper.Box($"rp_snap_ctr_c_{index}_l").Width(UnitValue.Stretch()).Height(18).Text(label, font)
                    .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink500).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"rp_snap_ctr_c_{index}_v").Width(UnitValue.Auto).Height(18).Text(display, font)
                    .FontSize(EditorTheme.FontSizeSmall).TextColor(color).Alignment(TextAlignment.MiddleRight).IsNotInteractable();
            }

            float pct = (float)(fraction * 100.0);
            if (pct < 1f) pct = 1f;
            if (pct > 100f) pct = 100f;
            using (paper.Box($"rp_snap_ctr_c_{index}_track").Width(UnitValue.Percentage(100)).Height(6).Rounded(3)
                .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral400, 60)).Enter())
                paper.Box($"rp_snap_ctr_c_{index}_fill").Width(UnitValue.Percentage(pct)).Height(6).Rounded(3)
                    .BackgroundColor(color).IsNotInteractable();
        }
    }
}
