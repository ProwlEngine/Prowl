using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.ImGUI.Widgets;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerAsset : PropertyDrawer<IAssetRef>
{
    public virtual string Name { get; } = FontAwesome6.Circle;

    protected override bool Draw(string label, ref IAssetRef value, float width)
    {
        bool changed = false;
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        DrawLabel(label, ref width);

        ImGui.PushItemWidth(width);

        string path;
        if (value.IsExplicitNull)
        {
            path = "(Null)";
            drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new System.Numerics.Vector4(0.9f, 0.1f, 0.1f, 0.3f)));
            if (ImGui.Selectable($"{Name}: {path}", false))
            {
                AssetDatabase.Ping(value.AssetID);
#warning TODO: Show a popup with a list of all assets of the type - property.Type.Name
            }
        }
        else if (value.IsRuntimeResource)
        {
            path = "(Runtime)" + value.Name;
            drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.9f, 0.3f)));
            if (ImGui.Selectable($"{Name}: {path}", false))
            {
                AssetDatabase.Ping(value.AssetID);
#warning TODO: Show a popup with a list of all assets of the type - property.Type.Name
            }
        }
        else if (AssetDatabase.Contains(value.AssetID))
        {
            path = AssetDatabase.GUIDToAssetPath(value.AssetID);
            if (ImGui.Selectable($"{Name}: {path}", false))
            {
                AssetDatabase.Ping(value.AssetID);
                Selection.Select(this, false);
            }
        }

        // DragDrop code
        if (DragnDrop.ReceiveAsset(out Guid assetGuid, value.TypeName)) {
            value.AssetID = assetGuid;
            changed = true;
        }

        // Add a button for clearing the Asset
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (Selection.IsSelected(this))
            {
                value = null;
                changed = true;
                Selection.Clear();
            }
        }

        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}