using Prowl.Editor.Profiling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private const float SelectionViewerHeaderHeight = 24f;

    private void DrawSelectionViewer(Paper paper, float width, float height)
    {
        using (paper.Box("rdp_detail_panel").Enter())
        {
            Origami.ScrollView(paper, "rdp_detail_scroll", width, height)
                .Padding(8)
                .ColSpacing(6)
                .Body(() =>
                {
                    switch (SelectionType)
                    {
                        case ProfilerSelectionType.Frame:
                            DrawFrameViewer(paper);
                            break;
                        case ProfilerSelectionType.View:
                            DrawViewViewer(paper);
                            break;
                        case ProfilerSelectionType.Pass:
                            DrawPassViewer(paper);
                            break;
                        case ProfilerSelectionType.CommandBuffer:
                            DrawCommandBufferViewer(paper);
                            break;
                        case ProfilerSelectionType.Pipeline:
                            DrawPipelineViewer(paper);
                            break;
                        case ProfilerSelectionType.Object:
                            DrawObjectViewer(paper);
                            break;
                        case ProfilerSelectionType.DrawCall:
                            DrawDrawCallViewer(paper);
                            break;
                    }
                });
        }
    }


    // DrawFrameViewer lives in Viewers/RenderProfiler.FrameViewer.cs.


    // Placeholder sub-viewers - filled in as each selection kind's viewer is implemented.
    private void DrawViewViewer(Paper paper) { }
    private void DrawPassViewer(Paper paper) { }
    private void DrawCommandBufferViewer(Paper paper) { }
    private void DrawPipelineViewer(Paper paper) { }
    private void DrawObjectViewer(Paper paper) { }
    private void DrawDrawCallViewer(Paper paper) { }
}
