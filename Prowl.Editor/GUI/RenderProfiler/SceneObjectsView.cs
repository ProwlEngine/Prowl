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
/// Scene Objects live tab: the renderables drawn this frame. Shows the collected / culled / visible
/// culler totals as a summary strip and lists every <see cref="DrawCallReport"/> across the frame's
/// passes with its mesh / material / shader / pass names and index+instance counts.
/// </summary>
public static class SceneObjectsView
{
    public static void Draw(Paper paper, RenderFrameReport? report, FontFile font, float width, float height)
    {
        using (paper.Column("rp_so_root").Size(width, height).Enter())
        {
            if (report == null)
            {
                EditorGUI.EmptyState(paper, "rp_so_empty", "No frame captured yet", font);
                return;
            }

            DrawSummary(paper, report.Counters, font, width);
            DrawList(paper, report, font, width, height - 46f);
        }
    }

    private static void DrawSummary(Paper paper, FrameCounters c, FontFile font, float width)
    {
        using (paper.Row("rp_so_summary").Width(width).Height(46).Padding(10, 10, 8, 6).RowBetween(8).Enter())
        {
            Stat(paper, "rp_so_collected", "Collected", c.RenderablesCollected.ToString(), EditorTheme.Ink500, font);
            Stat(paper, "rp_so_visible", "Visible", c.RenderablesVisible.ToString(), EditorTheme.Green400, font);
            Stat(paper, "rp_so_culled", "Culled", c.RenderablesCulled.ToString(), EditorTheme.Amber400, font);
            Stat(paper, "rp_so_draws", "Draw Calls", c.DrawCalls.ToString(), EditorTheme.Blue400, font);
            Stat(paper, "rp_so_tris", "Triangles", c.TrianglesApprox.ToString(), EditorTheme.Purple400, font);
        }
    }

    private static void Stat(Paper paper, string id, string label, string value, Color valueColor, FontFile font)
    {
        using (paper.Column(id).Width(UnitValue.Stretch()).Height(UnitValue.Percentage(100))
            .Rounded(6).Padding(8, 8, 4, 4)
            .BackgroundColor(EditorTheme.Glass)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Enter())
        {
            paper.Box(id + "_v").Height(18).Text(value, font).FontSize(EditorTheme.FontSize)
                .TextColor(valueColor).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box(id + "_l").Height(12).Text(label, font).FontSize(EditorTheme.FontSizeSmall - 2f)
                .TextColor(EditorTheme.InkDim).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
        }
    }

    private static void DrawList(Paper paper, RenderFrameReport report, FontFile font, float width, float height)
    {
        var rows = new List<(string mesh, string mat, string shader, string pass, string counts)>();
        foreach (var pass in report.Passes)
            foreach (var dc in pass.DrawCalls)
            {
                string mesh = string.IsNullOrEmpty(dc.MeshName) ? "(mesh)" : dc.MeshName;
                if (dc.SubMeshIndex >= 0) mesh += $" [{dc.SubMeshIndex}]";
                string mat = string.IsNullOrEmpty(dc.MaterialName) ? "(material)" : dc.MaterialName;
                string shader = string.IsNullOrEmpty(dc.ShaderName) ? "(shader)" : dc.ShaderName;
                string counts = $"{dc.IndexCount} idx x{dc.InstanceCount}";
                rows.Add((mesh, mat, shader, pass.Name, counts));
            }

        Origami.ScrollView(paper, "rp_so_scroll", width, height).Body(() =>
        {
            if (rows.Count == 0)
            {
                EditorGUI.EmptyState(paper, "rp_so_none", "No draw calls this frame", font);
                return;
            }

            using (paper.Column("rp_so_list").Width(width - 12).Height(UnitValue.Auto).ColBetween(2).Enter())
            {
                using (paper.Row("rp_so_head").Width(UnitValue.Percentage(100)).Height(22).Padding(8, 8, 0, 0).Enter())
                {
                    HeadCell(paper, "rp_so_h_mesh", "Mesh", font, 0.28f);
                    HeadCell(paper, "rp_so_h_mat", "Material", font, 0.22f);
                    HeadCell(paper, "rp_so_h_shader", "Shader", font, 0.22f);
                    HeadCell(paper, "rp_so_h_pass", "Pass", font, 0.16f);
                    HeadCell(paper, "rp_so_h_counts", "Counts", font, 0.12f);
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    using (paper.Row($"rp_so_row_{i}").Width(UnitValue.Percentage(100)).Height(24).Padding(8, 8, 0, 0).Rounded(4)
                        .BackgroundColor(i % 2 == 0 ? Color.Transparent : EditorTheme.WithAlpha(EditorTheme.Neutral400, 40))
                        .Hovered.BackgroundColor(EditorTheme.Hover).End()
                        .Enter())
                    {
                        Cell(paper, $"rp_so_c_mesh_{i}", r.mesh, font, EditorTheme.Ink500, 0.28f);
                        Cell(paper, $"rp_so_c_mat_{i}", r.mat, font, EditorTheme.Ink300, 0.22f);
                        Cell(paper, $"rp_so_c_shader_{i}", r.shader, font, EditorTheme.Ink300, 0.22f);
                        Cell(paper, $"rp_so_c_pass_{i}", r.pass, font, EditorTheme.Blue400, 0.16f);
                        Cell(paper, $"rp_so_c_counts_{i}", r.counts, font, EditorTheme.InkDim, 0.12f);
                    }
                }
            }
        });
    }

    private static void HeadCell(Paper paper, string id, string text, FontFile font, float frac)
        => paper.Box(id).Width(UnitValue.Percentage(frac * 100f)).Height(22)
            .Text(text, font).FontSize(EditorTheme.FontSizeSmall - 1f).TextColor(EditorTheme.InkDim)
            .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

    private static void Cell(Paper paper, string id, string text, FontFile font, Color color, float frac)
        => paper.Box(id).Width(UnitValue.Percentage(frac * 100f)).Height(24)
            .Text(text, font).FontSize(EditorTheme.FontSizeSmall).TextColor(color)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
}
