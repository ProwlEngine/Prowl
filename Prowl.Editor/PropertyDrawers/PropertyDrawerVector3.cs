using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerSystemVector3 : PropertyDrawer<System.Numerics.Vector3> {

    protected override bool Draw(string label, ref System.Numerics.Vector3 v3)
    {
        bool changed = false;
        ImGui.Columns(2);
        ImGui.Text(label);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 3 - 20);
        ImGui.Text("X");
        ImGui.SameLine();
        changed |= ImGui.DragFloat("##X", ref v3.X);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        changed |= ImGui.DragFloat("##Y", ref v3.Y);
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        changed |= ImGui.DragFloat("##Z", ref v3.Z);
        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}

public class PropertyDrawerVector3 : PropertyDrawer<Vector3> {

    protected override bool Draw(string label, ref Vector3 v3)
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
        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}