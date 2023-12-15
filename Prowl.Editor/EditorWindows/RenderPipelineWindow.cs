using HexaEngine.ImGuiNET;
using Prowl.Editor.Drawers.NodeSystem;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.ImGUI.Widgets;

namespace Prowl.Editor.EditorWindows;

public class RenderPipelineWindow : EditorWindow
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
        if (DragnDrop.ReceiveAsset<ScriptableObject>(out var asset) && asset.Res is RenderPipeline rp)
            CurrentRenderPipeline = rp;
        ImGui.SetCursorPos(cStart);

        if (CurrentRenderPipeline.IsAvailable == false) return;

        var size = changed ? new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 30) : ImGui.GetContentRegionAvail();
        ImGui.BeginChild("RenderPipeline", size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        bool changedThisFrame = NodeSystemDrawer.Draw(CurrentRenderPipeline.Res);
        ImGui.EndChild();

        if(changedThisFrame)
            CurrentRenderPipeline.Res!.OnValidate();

        changed |= changedThisFrame;

        if (changed)
        {
            string relativeAssetPath = AssetDatabase.GUIDToAssetPath(CurrentRenderPipeline.Res!.AssetID);
            var assetFile = AssetDatabase.RelativeToFile(relativeAssetPath);

            // Show Save/Decline buttons in the bottom right corner
            //ImGui.SetCursorPos(new System.Numerics.Vector2(ImGui.GetStyle().WindowPadding.X + 100, ImGui.GetStyle().WindowPadding.Y + 30));
            if (ImGui.Button("Save"))
            {
                // Need to save original asset
                StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(CurrentRenderPipeline.Res!), assetFile);
                AssetDatabase.Reimport(AssetDatabase.FileToRelative(assetFile), false);
                changed = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Decline"))
            {
                AssetDatabase.Reimport(AssetDatabase.FileToRelative(assetFile), false);
                changed = false;
            }

        }
    }
}