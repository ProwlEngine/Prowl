using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler.Inspectors;

// Stateless - a command buffer's Id isn't stable across frames (see ProfiledPass), so unlike
// FrameInspector/ViewInspector/PassInspector this has no history to chart, just this frame's numbers.
public static class ProfilerCommandBufferInspector
{
    public static void Draw(Paper paper, ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer)
    {
        if (commandBuffer == null)
        {
            Origami.Label(paper, "rdp_cb_empty", "No command buffer selected")
                .Muted()
                .Show();
            return;
        }

        using (paper.Column("rdp_cb_viewer").Height(UnitValue.Auto).ColBetween(InspectorKit.SectionGap).Enter())
        {
            using (paper.Row("rdp_cb_header").Height(InspectorKit.SelectionViewerHeaderHeight).ColBetween(8f).Enter())
            {
                Origami.Label(paper, "rdp_cb_title", commandBuffer.Name)
                    .Heading()
                    .AlignLeft()
                    .Show();

                if (pass != null)
                {
                    Origami.Label(paper, "rdp_cb_pass", pass.Name)
                        .Muted()
                        .AlignLeft()
                        .Show();
                }

                if (view != null)
                {
                    Origami.Label(paper, "rdp_cb_view", view.Name)
                        .Muted()
                        .AlignLeft()
                        .Show();
                }

                paper.Box("rdp_cb_header_spacer");

                Origami.Label(paper, "rdp_cb_gpu", $"{commandBuffer.GpuMilliseconds:F2} ms")
                    .Muted()
                    .AlignRight()
                    .Show();
            }

            InspectorKit.SectionCard(paper, "rdp_cb_renderops", "Render Operations", () => DrawRenderOperationsSection(paper, view, commandBuffer));
        }
    }


    // ── Render operations (mirrors PassInspector/ViewInspector's stats, single-frame only) ──

    private static void DrawRenderOperationsSection(Paper paper, ProfiledView? view, ProfiledCommandBuffer commandBuffer)
    {
        using (paper.Column("rdp_cb_renderops_col").Height(UnitValue.Auto).ColBetween(InspectorKit.ChartRowGap).Enter())
        {
            DrawGeometryStats(paper, commandBuffer);
            DrawRenderingStats(paper, commandBuffer);
            DrawPixelProcessingStats(paper, view, commandBuffer);
            DrawPipelineStateStats(paper, commandBuffer);
        }
    }


    private static void DrawGeometryStats(Paper paper, ProfiledCommandBuffer commandBuffer)
    {
        InspectorKit.StatGroup(paper, "rdp_cb_geometry", "Geometry",
            ("Primitives", InspectorKit.FormatCountCompact(commandBuffer.InputAssemblyPrimitives)),
            ("Vertices Input", InspectorKit.FormatCountCompact(commandBuffer.InputAssemblyVertices)),
            ("Primitives Drawn", InspectorKit.FormatCountCompact(commandBuffer.ClippingPrimitives)));
    }


    private static void DrawRenderingStats(Paper paper, ProfiledCommandBuffer commandBuffer)
    {
        InspectorKit.StatGroup(paper, "rdp_cb_rendering", "Rendering",
            ("Draw Calls", InspectorKit.FormatCountCompact(commandBuffer.DrawCallCount)),
            ("Dispatches", InspectorKit.FormatCountCompact(commandBuffer.DispatchCallCount)));
    }


    // Overdraw is derived the same way as ProfiledView.Overdraw - FragmentShaderInvocations divided
    // by the source view's pixel count - but scoped to just this command buffer.
    private static void DrawPixelProcessingStats(Paper paper, ProfiledView? view, ProfiledCommandBuffer commandBuffer)
    {
        ulong pixels = view == null ? 0ul : (ulong)view.PixelWidth * view.PixelHeight;
        double overdraw = pixels == 0 ? 0d : commandBuffer.FragmentShaderInvocations / (double)pixels;

        InspectorKit.StatGroup(paper, "rdp_cb_pixelproc", "Pixel Processing",
            ("Fragment Invocations", InspectorKit.FormatCountCompact(commandBuffer.FragmentShaderInvocations)),
            ("Overdraw", $"{overdraw:F2}x"),
            ("Overdraw %", $"{overdraw * 100d:F0}%"));
    }


    private static void DrawPipelineStateStats(Paper paper, ProfiledCommandBuffer commandBuffer)
    {
        InspectorKit.StatGroup(paper, "rdp_cb_pipelinestate", "Pipeline State",
            ("Pipeline Switches", InspectorKit.FormatCountCompact(commandBuffer.PipelineSwitchCount)));
    }
}
