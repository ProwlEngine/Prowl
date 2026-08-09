using Prowl.Editor.Profiling;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>Which of RenderProfilerPanel's Selected* fields currently reflects the inspected node.</summary>
public enum ProfilerSelectionType
{
    Frame,
    View,
    Pass,
    CommandBuffer,
    Pipeline,
    Object,
    DrawCall,
}

public partial class RenderProfilerPanel
{
    public ProfiledView? SelectedView { get; private set; }
    public ProfiledPass? SelectedPass { get; private set; }
    public ProfiledCommandBuffer? SelectedCommandBuffer { get; private set; }

    // Only ever populated from a Snapshot's captured pipeline/object/draw-call data - live
    // recording never records this level of detail.
    public ProfiledPipeline? SelectedPipeline { get; private set; }
    public ProfiledCallingObject? SelectedObject { get; private set; }
    public ProfiledDrawCall? SelectedDrawCall { get; private set; }

    // ProfiledDrawCall is a struct, so identity within SelectedPipeline.Draws is tracked by index
    // rather than by reference/equality.
    public int SelectedDrawCallIndex { get; private set; } = -1;

    public ProfilerSelectionType SelectionType { get; private set; } = ProfilerSelectionType.Frame;


    public void SelectView(ProfiledView view)
    {
        SelectedView = view;
        SelectedPass = null;
        SelectedCommandBuffer = null;
        ClearPipelineSelection();
        SelectionType = ProfilerSelectionType.View;
    }

    public void SelectPass(ProfiledView view, ProfiledPass pass)
    {
        SelectedView = view;
        SelectedPass = pass;
        SelectedCommandBuffer = null;
        ClearPipelineSelection();
        SelectionType = ProfilerSelectionType.Pass;
    }

    public void SelectCommandBuffer(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer commandBuffer)
    {
        SelectedView = view;
        SelectedPass = pass;
        SelectedCommandBuffer = commandBuffer;
        ClearPipelineSelection();
        SelectionType = ProfilerSelectionType.CommandBuffer;
    }

    public void SelectPipeline(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer, ProfiledPipeline pipeline)
    {
        SelectedView = view;
        SelectedPass = pass;
        SelectedCommandBuffer = commandBuffer;
        SelectedPipeline = pipeline;
        SelectedObject = null;
        SelectedDrawCall = null;
        SelectedDrawCallIndex = -1;
        SelectionType = ProfilerSelectionType.Pipeline;
    }

    public void SelectObject(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject obj)
    {
        SelectedView = view;
        SelectedPass = pass;
        SelectedCommandBuffer = commandBuffer;
        SelectedPipeline = pipeline;
        SelectedObject = obj;
        SelectedDrawCall = null;
        SelectedDrawCallIndex = -1;
        SelectionType = ProfilerSelectionType.Object;
    }

    public void SelectDrawCall(ProfiledView? view, ProfiledPass? pass, ProfiledCommandBuffer? commandBuffer, ProfiledPipeline pipeline, ProfiledCallingObject? obj, int drawIndex)
    {
        SelectedView = view;
        SelectedPass = pass;
        SelectedCommandBuffer = commandBuffer;
        SelectedPipeline = pipeline;
        SelectedObject = obj;
        SelectedDrawCall = pipeline.Draws[drawIndex];
        SelectedDrawCallIndex = drawIndex;
        SelectionType = ProfilerSelectionType.DrawCall;
    }

    private void ClearPipelineSelection()
    {
        SelectedPipeline = null;
        SelectedObject = null;
        SelectedDrawCall = null;
        SelectedDrawCallIndex = -1;
    }

    private void ClearSubFrameSelection()
    {
        SelectedView = null;
        SelectedPass = null;
        SelectedCommandBuffer = null;
        ClearPipelineSelection();
        SelectionType = ProfilerSelectionType.Frame;
    }
}
