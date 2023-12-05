using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerQuaternion : PropertyDrawer<Quaternion> {
    protected override void DrawProperty(ref Quaternion v3, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 - 20);
        ImGui.PushID(property.Name);
        ImGui.Text("X");
        ImGui.SameLine();
        ImGui.InputDouble("##X", ref v3.X, 0.01, 0.1);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        ImGui.InputDouble("##Y", ref v3.Y, 0.01, 0.1);
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        ImGui.InputDouble("##Z", ref v3.Z, 0.01, 0.1);
        ImGui.Text("W");
        ImGui.SameLine();
        ImGui.InputDouble("##W", ref v3.W, 0.01, 0.1);

        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}
