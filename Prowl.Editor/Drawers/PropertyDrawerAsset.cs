using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.ImGUI.Widgets;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerAsset : PropertyDrawer<IAssetRef>
{
    public static SelectHandler<PropertyDrawerAsset> SelectHandler = new();

    public virtual string Name { get; } = FontAwesome6.Circle;

    protected override bool Draw(string label, ref IAssetRef value, float width)
    {
        bool changed = false;
        DrawLabel(label, ref width);

        string path;
        if (value.IsExplicitNull) {
            path = "(Null)";
            if (ImGui.Selectable($"{Name}: {path}", SelectHandler.IsSelected(this), new System.Numerics.Vector2(width, 17))) {
                SelectHandler.SetSelection(this);
#warning TODO: Show a popup with a list of all assets of the type - property.Type.Name
            }
            GUIHelper.ItemRectFilled(0.9f, 0.1f, 0.1f, 0.3f);
        } else if (value.IsRuntimeResource) {
            path = "(Runtime)" + value.Name;
            if (ImGui.Selectable($"{Name}: {path}", SelectHandler.IsSelected(this), new System.Numerics.Vector2(width, 17))) {
                SelectHandler.SetSelection(this);
                SelectHandler.SetSelection(this);
#warning TODO: Show a popup with a list of all assets of the type - property.Type.Name
            }
            GUIHelper.ItemRectFilled(0.1f, 0.1f, 0.9f, 0.3f);
        } else if (AssetDatabase.Contains(value.AssetID)) {
            path = AssetDatabase.GUIDToAssetPath(value.AssetID);
            if (ImGui.Selectable($"{Name}: {path}", SelectHandler.IsSelected(this), new System.Numerics.Vector2(width, 17))) {
                AssetDatabase.Ping(value.AssetID);
                SelectHandler.SetSelection(this);
            }
        }

        // DragDrop code
        string payloadName = value.InstanceType.Name;
        if (value.InstanceType.IsAssignableTo(typeof(ScriptableObject)))
            payloadName = "ScriptableObject"; // Scriptable objects are a special case
        if (DragnDrop.ReceiveAsset(out Guid assetGuid, payloadName)) {
            value.AssetID = assetGuid;
            changed = true;
        }

        // Add a button for clearing the Asset
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) {
            if (SelectHandler.IsSelected(this)) {
                value = null;
                changed = true;
                SelectHandler.Clear();
            }
        }
        ImGui.Columns(1);
        return changed;
    }
}