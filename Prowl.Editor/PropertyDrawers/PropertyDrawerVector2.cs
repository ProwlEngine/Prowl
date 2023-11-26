using HexaEngine.ImGuiNET;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerVector2 : PropertyDrawer<System.Numerics.Vector2> {
    
    protected override void DrawProperty(ref System.Numerics.Vector2 v2, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 2 - 20);
        ImGui.PushID(property.Name);
        ImGui.Text("X");
        ImGui.SameLine();
        ImGui.DragFloat("##X", ref v2.X);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        ImGui.DragFloat("##Y", ref v2.Y);
        
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
    
}
