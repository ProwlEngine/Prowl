using HexaEngine.ImGuiNET;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerBool : PropertyDrawer<bool> {

    protected override bool Draw(string label, ref bool value)
    {
        return ImGui.Checkbox(label, ref value);
    }
}