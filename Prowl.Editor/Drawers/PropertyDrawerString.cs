using Hexa.NET.ImGui;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerString : PropertyDrawer<string?> {

    protected override bool Draw(string label, ref string value, float width)
    {
        bool changed = false;
        value ??= string.Empty;
        DrawLabel(label, ref width);

        ImGui.PushItemWidth(width);
        changed = ImGui.InputText("", ref value, 30);
        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
    
}
