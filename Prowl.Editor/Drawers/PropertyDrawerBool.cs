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

public class PropertyDrawerEnum : PropertyDrawer<Enum> {

    protected override bool Draw(string label, ref Enum value, float width)
    {
        DrawLabel(label, ref width);
        bool changed = false;
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo("##Combo", value.ToString()))
        {
            foreach (var enumValue in Enum.GetValues(value.GetType()))
            {
                if (ImGui.Selectable(enumValue.ToString(), enumValue.Equals(value)))
                {
                    value = (Enum)enumValue;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.Columns(1);
        return changed;
    }
}
