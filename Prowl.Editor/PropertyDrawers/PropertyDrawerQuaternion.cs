using HexaEngine.ImGuiNET;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerQuaternion : PropertyDrawer<System.Numerics.Quaternion> {
    protected override void DrawProperty(ref System.Numerics.Quaternion v3, Property property) {
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
        ImGui.Text("W");
        ImGui.SameLine();
        ImGui.DragFloat("##W", ref v3.W);

        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}
