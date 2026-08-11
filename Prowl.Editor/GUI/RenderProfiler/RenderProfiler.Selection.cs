using Prowl.Editor.Profiling;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel
{
    private readonly ProfilerSelection _selection = new();

    public ProfiledView? SelectedView => _selection.SelectedView;
    public ProfiledPass? SelectedPass => _selection.SelectedPass;
    public ProfiledCommandBuffer? SelectedCommandBuffer => _selection.SelectedCommandBuffer;
    public ProfiledPipeline? SelectedPipeline => _selection.SelectedPipeline;
    public ProfiledCallingObject? SelectedObject => _selection.SelectedObject;
    public ProfiledDrawCall? SelectedDrawCall => _selection.SelectedDrawCall;
    public int SelectedDrawCallIndex => _selection.SelectedDrawCallIndex;
    public ProfilerSelectionType SelectionType => _selection.SelectionType;

    public void SelectView(ProfiledView view) => _selection.SelectView(view);
    public void SelectPass(ProfiledView view, ProfiledPass pass) => _selection.SelectPass(view, pass);
    public void SelectCommandBuffer(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer commandBuffer) => _selection.SelectCommandBuffer(view, pass, commandBuffer);
    public void SelectPipeline(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer, ProfiledPipeline pipeline) => _selection.SelectPipeline(view, pass, commandBuffer, pipeline);
    public void SelectObject(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject obj) => _selection.SelectObject(view, pass, commandBuffer, pipeline, obj);
    public void SelectDrawCall(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject? obj, int drawIndex) => _selection.SelectDrawCall(view, pass, commandBuffer, pipeline, obj, drawIndex);

    private void ClearSubFrameSelection() => _selection.ClearSubFrameSelection();
}
