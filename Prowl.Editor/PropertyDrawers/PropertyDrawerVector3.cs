using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerSystemVector3 : PropertyDrawer<System.Numerics.Vector3> {
    
    protected override void DrawProperty(ref System.Numerics.Vector3 v3, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 - 20);
        ImGui.PushID(property.Name);
        ImGui.Text("X");
        ImGui.SameLine();
        ImGui.DragFloat("##X", ref v3.X);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        ImGui.DragFloat("##Y", ref v3.Y);
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        ImGui.DragFloat("##Z", ref v3.Z);
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}

public class PropertyDrawerVector3 : PropertyDrawer<Vector3> {
    
    protected override void DrawProperty(ref Vector3 v3, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 - 20);
        ImGui.PushID(property.Name);
        ImGui.Text("X");
        ImGui.SameLine();
        GUIHelper.DragDouble("##X", ref v3.X, 0.01f);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        GUIHelper.DragDouble("##Y", ref v3.Y, 0.01f);
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        GUIHelper.DragDouble("##Z", ref v3.Z, 0.01f);
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}