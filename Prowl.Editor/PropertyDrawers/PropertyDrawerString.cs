using ImGuiNET;

namespace Prowl.Editor.PropertyDrawers; 

public class PropertyDrawerString : PropertyDrawer<string?> {
    
    protected override void DrawProperty(ref string? value, Property property) {
        
        value ??= string.Empty;
        
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);
        ImGui.InputText("", ref value, 30);
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
    
}
