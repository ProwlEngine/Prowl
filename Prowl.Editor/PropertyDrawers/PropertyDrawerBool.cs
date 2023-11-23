using ImGuiNET;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerBool : PropertyDrawer<bool> {
    
    protected override void DrawProperty(ref bool value, Property property) {
        ImGui.Checkbox(property.Name, ref value);
    }
}