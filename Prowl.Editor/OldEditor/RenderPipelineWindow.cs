using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Editor.NodeSystem;
using Prowl.Editor;
using Prowl.Icons;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows;

public class RenderPipelineWindow : OldEditorWindow
{
    AssetRef<RenderPipeline> CurrentRenderPipeline;
    bool changed = false;

    public RenderPipelineWindow() : base() => Title = FontAwesome6.DiagramProject + " Render Pipeline Editor";

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        // Drag and drop support for the render pipeline asset
        var cStart = ImGui.GetCursorPos();
        ImGui.Dummy(ImGui.GetContentRegionAvail());

        if (DragnDrop.Drop<RenderPipeline>(out var asset))
            CurrentRenderPipeline = asset;
        ImGui.SetCursorPos(cStart);

        if (CurrentRenderPipeline.IsAvailable == false)
        {
            GUIHelper.TextCenter("No Render Pipeline selected, Drag & Drop one into this window to start editing it!", 2f, true);
            return;
        }

        var size = changed ? new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 30) : ImGui.GetContentRegionAvail();
        ImGui.BeginChild("RenderPipeline", size, ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        bool changedThisFrame = NodeSystemDrawer.Draw(CurrentRenderPipeline.Res);
        ImGui.EndChild();

        if(changedThisFrame)
            CurrentRenderPipeline.Res!.OnValidate();

        changed |= changedThisFrame;

        if (changed && AssetDatabase.TryGetFile(CurrentRenderPipeline.Res!.AssetID, out var assetFile))
        {
            // Show Save/Decline buttons in the bottom right corner
            //ImGui.SetCursorPos(new System.Numerics.Vector2(ImGui.GetStyle().WindowPadding.X + 100, ImGui.GetStyle().WindowPadding.Y + 30));
            if (ImGui.Button("Save"))
            {
                // Need to save original asset
                StringTagConverter.WriteToFile(Serializer.Serialize(CurrentRenderPipeline.Res!), assetFile);
                AssetDatabase.Reimport(assetFile, false);
                changed = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Decline"))
            {
                AssetDatabase.Reimport(assetFile, false);
                changed = false;
            }

        }
    }
}
