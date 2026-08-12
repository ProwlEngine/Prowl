using Prowl.Editor.GUI.RenderProfiler.Inspectors;
using Prowl.Editor.Profiling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private void DrawSelectionInspector(Paper paper, float width, float height)
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
                            ProfilerFrameInspector.Draw(paper, SelectedFrame, History);
                            break;
                        case ProfilerSelectionType.View:
                            _viewInspector.Draw(paper, SelectedView, History, width);
                            break;
                        case ProfilerSelectionType.Pass:
                            _passInspector.Draw(paper, SelectedView, SelectedPass, History, null);
                            break;
                        case ProfilerSelectionType.CommandBuffer:
                            ProfilerCommandBufferInspector.Draw(paper, SelectedView, SelectedPass, SelectedCommandBuffer);
                            break;
                        case ProfilerSelectionType.Pipeline:
                            DrawPipelineInspector(paper);
                            break;
                        case ProfilerSelectionType.Object:
                            DrawObjectInspector(paper);
                            break;
                        case ProfilerSelectionType.DrawCall:
                            DrawDrawCallInspector(paper);
                            break;
                    }
                });
        }
    }


    // Placeholder sub-inspectors - filled in as each selection kind's inspector is implemented.
    private void DrawPipelineInspector(Paper paper) { }
    private void DrawObjectInspector(Paper paper) { }
    private void DrawDrawCallInspector(Paper paper) { }
}
