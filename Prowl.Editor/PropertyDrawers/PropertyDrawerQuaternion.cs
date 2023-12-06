using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerQuaternion : PropertyDrawer<Quaternion>
{
    protected override bool Draw(string label, ref Quaternion v3)
    {
        bool changed = false;
        ImGui.Columns(2);
        ImGui.Text(label);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 - 20);
        ImGui.Text("X");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##X", ref v3.X, 0.01f);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##Y", ref v3.Y, 0.01f);
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##Z", ref v3.Z, 0.01f);
        ImGui.Text("W");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##W", ref v3.W, 0.01f);

        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}
