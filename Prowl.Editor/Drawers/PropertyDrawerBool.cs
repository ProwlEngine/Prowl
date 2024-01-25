using Hexa.NET.ImGui;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerBool : PropertyDrawer<bool> {

    protected override bool Draw(string label, ref bool value, float width)
    {
        ImGui.SetNextItemWidth(width);
        return ImGui.Checkbox(Prettify(label), ref value);
    }
}
