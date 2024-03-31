using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Editor.EditorWindows;
using Prowl.Editor.ImGUI.Widgets;
using Prowl.Icons;
using Prowl.Runtime;
using System.IO;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerAsset : PropertyDrawer<IAssetRef>
{
    public static PropertyDrawerAsset Selected;
    public static Guid assignedGUID;
    public static short assignedFileID;
    public static int guidAssignedToID = -1;

    protected override bool Draw(string label, ref IAssetRef value, float width)
    {
        var imguiID = ImGui.GetItemID();
        bool changed = false;
        DrawLabel(label, ref width);

        if(guidAssignedToID != -1  && guidAssignedToID == imguiID)
        {
            value.AssetID = assignedGUID;
            value.FileID = assignedFileID;
            assignedGUID = Guid.Empty;
            assignedFileID = 0;
            guidAssignedToID = -1;
            changed = true;
        }

        DrawAssetSelector(imguiID, value.InstanceType);
        if (value.IsExplicitNull)
        {
            if (ImGui.Selectable($"(Null)", Selected == this, new System.Numerics.Vector2(width, 21)))
                Selected = this;
            GUIHelper.ItemRectFilled(0.9f, 0.1f, 0.1f, 0.3f);
        }
        else if (value.IsRuntimeResource)
        {
            if (ImGui.Selectable("(Runtime)" + value.Name, Selected == this, new System.Numerics.Vector2(width, 21)))
            {
                Selected = this;
            }
            GUIHelper.ItemRectFilled(0.1f, 0.1f, 0.9f, 0.3f);
        }
        else if (AssetDatabase.TryGetFile(value.AssetID, out var assetPath))
        {
            if (ImGui.Selectable(AssetDatabase.ToRelativePath(assetPath), Selected == this, new System.Numerics.Vector2(width, 21)))
            {
                AssetDatabase.Ping(value.AssetID);
                Selected = this;
            }
        }

        if (value.IsAvailable && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            GlobalSelectHandler.Select(value);

        // DragDrop code
        string payloadName = value.InstanceType.Name;
        if (value.InstanceType.IsAssignableTo(typeof(ScriptableObject)))
            payloadName = "ScriptableObject"; // Scriptable objects are a special case
        if (DragnDrop.ReceiveAsset(out Guid assetGuid, payloadName)) {
            value.AssetID = assetGuid;
            changed = true;
        }

        // Add a button for clearing the Asset
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && ImGui.IsWindowFocused()) {
            if (Selected == this) {
                value.AssetID = Guid.Empty;
                value.FileID = 0;
                changed = true;
            }
        }
        ImGui.Columns(1);
        return changed;
    }

    private void DrawAssetSelector(int imguiID, Type instanceType)
    {
        if (ImGui.Selectable($" {FontAwesome6.MagnifyingGlass}", Selected == this, new System.Numerics.Vector2(21, 21)))
        {
            Selected = this;
            int id = imguiID;
            new AssetSelectorWindow(instanceType, (guid, fileid) => { assignedGUID = guid; guidAssignedToID = id; assignedFileID = fileid; });
        }
        ImGui.SameLine();
    }
}
