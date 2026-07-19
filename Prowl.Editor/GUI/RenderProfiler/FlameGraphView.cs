using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;
using Prowl.Scribe;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Flame Graph live tab. Maps the frame's <see cref="PassReport"/> / <see cref="SampleScope"/> tree
/// onto the generic Origami <see cref="FlameNode"/> model and renders it with the FlameGraph widget.
/// Each pass becomes a root bar whose duration is <see cref="PassReport.CpuMs"/>; passes are laid out
/// sequentially along the shared millisecond axis, and each pass's sample scopes are nested inside it
/// with the same sequential layout (real per-scope offsets are not tracked, so children are packed in
/// declaration order within the parent span).
/// </summary>
public static class FlameGraphView
{
    public static void Draw(Paper paper, RenderFrameReport? report, FontFile font, float width, float height)
    {
        using (paper.Column("rp_flame_root").Size(width, height).Padding(8, 8, 8, 8).Enter())
        {
            if (report == null || report.Passes.Count == 0)
            {
                EditorGUI.EmptyState(paper, "rp_flame_empty", "No pass timings this frame", font);
                return;
            }

            var roots = BuildRoots(report);

            Origami.FlameGraph(paper, "rp_flame")
                .Roots(roots)
                .Size(width - 16f, height - 16f)
                .ValueFormatter(v => v.ToString("0.###") + " ms")
                .Zoomable()
                .Pannable()
                .Show();
        }
    }

    private static List<FlameNode> BuildRoots(RenderFrameReport report)
    {
        var roots = new List<FlameNode>(report.Passes.Count);
        double cursor = 0d;
        foreach (var pass in report.Passes)
        {
            double dur = pass.CpuMs > 0 ? pass.CpuMs : 0.0001d;
            var node = new FlameNode(pass.Name, cursor, dur)
            {
                Tooltip = $"{pass.DrawCalls.Count} draw calls",
                UserData = pass,
            };
            MapScopes(pass.Root, node, cursor);
            roots.Add(node);
            cursor += dur;
        }
        return roots;
    }

    private static void MapScopes(SampleScope? scope, FlameNode parent, double parentStart)
    {
        if (scope == null || scope.Children == null) return;
        double cursor = parentStart;
        foreach (var child in scope.Children)
        {
            double dur = child.CpuMs > 0 ? child.CpuMs : 0.0001d;
            var node = new FlameNode(child.Name, cursor, dur);
            MapScopes(child, node, cursor);
            parent.Children.Add(node);
            cursor += dur;
        }
    }
}
