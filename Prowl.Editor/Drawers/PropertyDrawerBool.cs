using Hexa.NET.ImGui;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerBool : PropertyDrawer<bool> {

    protected override bool Draw(string label, ref bool value, float width)
    {
        DrawLabel(label, ref width);
        bool changed = ImGui.Checkbox("##Checkbox", ref value);
        ImGui.Columns(1);
        return changed;
    }
}
