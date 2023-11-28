using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Icons;
using HexaEngine.ImGuiNET;
using System.Numerics;
using Prowl.Runtime.ImGUI.Widgets;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerAsset : PropertyDrawer<IAssetRef>
{
    public virtual string Name { get; } = FontAwesome6.Circle;

    protected override void DrawProperty(ref IAssetRef value, Property property)
    {
        Draw(ref value, property);
    }

    protected virtual void Draw(ref IAssetRef value, Property property)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);

        var pos = ImGui.GetCursorPos();

        string path;

        if (value.IsExplicitNull)
        {
            path = "(Null)";
            drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.9f, 0.1f, 0.1f, 0.3f)));
            if (ImGui.Selectable($"{Name}: {path}", false))
            {
#warning TODO: Show a popup with a list of all assets of the type - property.Type.Name
            }
        }
        else if (value.IsRuntimeResource)
        {
            path = "(Runtime)" + value.Name;
            drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.9f, 0.3f)));
            if (ImGui.Selectable($"{Name}: {path}", false))
            {
#warning TODO: Show a popup with a list of all assets of the type - property.Type.Name
            }
        }
        else if (AssetDatabase.Contains(value.AssetID))
        {
            path = AssetDatabase.GUIDToAssetPath(value.AssetID);
            if (ImGui.Selectable($"{Name}: {path}", false))
                Selection.Select(this, false);
        }

        // DragDrop code
        if(DragnDrop.ReceiveAsset(out Guid assetGuid, value.TypeName))
            value.AssetID = assetGuid;

        // Add a button for clearing the Asset
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (Selection.Current is PropertyDrawerAsset drawer && drawer == this)
            {
                value = null;
                Selection.Clear(false);
            }
        }

        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}