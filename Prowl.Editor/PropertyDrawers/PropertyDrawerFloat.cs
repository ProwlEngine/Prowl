using ImGuiNET;

namespace Prowl.Editor.PropertyDrawers; 

public class PropertyDrawerFloat : PropertyDrawer<float> {
    
    protected override void DrawProperty(ref float value, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);
        ImGui.DragFloat("", ref value, 0.01f, float.MinValue, float.MaxValue, "%g");
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
    
}

public class PropertyDrawerInt : PropertyDrawer<int> {
    
    protected override void DrawProperty(ref int value, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);
        ImGui.DragInt("", ref value, 0.01f, int.MinValue, int.MaxValue, "%g");
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
    
}

public class PropertyDrawerShort : PropertyDrawer<short> {
    
    protected override void DrawProperty(ref short value, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);
        int valInt = value;
        ImGui.DragInt("", ref valInt, 0.01f, short.MinValue, short.MaxValue, "%g");
        value = (short)valInt;
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}