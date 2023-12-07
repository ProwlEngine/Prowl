using HexaEngine.ImGuiNET;
using HexaEngine.ImNodesNET;
using Prowl.Runtime;
using Prowl.Runtime.ImGUI.Widgets;

namespace Prowl.Editor.EditorWindows;

public class RenderPipelineWindow : EditorWindow
{
    AssetRef<RenderPipeline> CurrentRenderPipeline;
    private ImNodesEditorContextPtr context;

    public RenderPipelineWindow() : base() => Title = "Render Pipeline Editor";

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        var cStart = ImGui.GetCursorPos();
        ImGui.Dummy(ImGui.GetContentRegionAvail());
        if (DragnDrop.ReceiveAsset<ScriptableObject>(out var asset) && asset.Res is RenderPipeline rp)
            CurrentRenderPipeline = rp;
        ImGui.SetCursorPos(cStart);

        if (CurrentRenderPipeline.IsAvailable == false) return;

        if(context.IsNull)
            context = ImNodes.EditorContextCreate();
        ImNodes.EditorContextSet(context);

        const int hardcoded_node_id = 1;

        ImNodes.BeginNodeEditor();


        ImNodes.BeginNode(hardcoded_node_id);
        ImGui.Dummy(new System.Numerics.Vector2(80.0f, 45.0f));
        ImNodes.EndNode();

        ImNodes.EndNodeEditor();
    }

}