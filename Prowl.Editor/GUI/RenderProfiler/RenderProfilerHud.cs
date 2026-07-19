using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// The scene-view stats HUD (Window 1). A tiny, non-intrusive overlay pinned to the bottom-left of the
/// viewport showing frame time, draw-call / pass / triangle counts read from the scene camera's
/// <see cref="Camera.LastRenderReport"/>. Renders nothing when the camera has never produced a report.
/// </summary>
public static class RenderProfilerHud
{
    public static void Draw(Paper paper, Camera? camera, FontFile font, float width, float height)
    {
        Prowl.Runtime.Rendering.RenderProfiler.RequestReport();

        var report = camera?.LastRenderReport;
        if (report == null) return;

        var c = report.Counters;
        const float boxW = 150f, rowH = 15f;
        float boxH = rowH * 4f + 10f;

        using (paper.Column("rp_hud")
            .PositionType(PositionType.SelfDirected)
            .Position(12, height - boxH - 12)
            .Size(boxW, boxH)
            .Rounded(6).Padding(8, 8, 5, 5)
            .BackgroundColor(Color.FromArgb(170, EditorTheme.Neutral400))
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .IsNotInteractable()
            .Enter())
        {
            Row(paper, "rp_hud_ms", "Frame", report.CpuFrameMs.ToString("0.00") + " ms", EditorTheme.Blue400, font, rowH);
            Row(paper, "rp_hud_draws", "Draws", c.DrawCalls.ToString(), EditorTheme.Green400, font, rowH);
            Row(paper, "rp_hud_passes", "Passes", c.Passes.ToString(), EditorTheme.Purple400, font, rowH);
            Row(paper, "rp_hud_tris", "Tris", c.TrianglesApprox.ToString(), EditorTheme.Amber400, font, rowH);
        }
    }

    private static void Row(Paper paper, string id, string label, string value, Color valueColor, FontFile font, float rowH)
    {
        using (paper.Row(id).Width(UnitValue.Percentage(100)).Height(rowH).Enter())
        {
            paper.Box(id + "_l").Width(UnitValue.Stretch()).Height(rowH)
                .Text(label, font).FontSize(EditorTheme.FontSizeSmall - 3f).TextColor(EditorTheme.InkDim)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box(id + "_v").Width(UnitValue.Auto).Height(rowH)
                .Text(value, font).FontSize(EditorTheme.FontSizeSmall - 2f).TextColor(valueColor)
                .Alignment(TextAlignment.MiddleRight).IsNotInteractable();
        }
    }
}
