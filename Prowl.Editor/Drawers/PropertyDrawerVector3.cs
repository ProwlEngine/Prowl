using Hexa.NET.ImGui;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerSystemVector3 : PropertyDrawer<System.Numerics.Vector3> {

    protected override bool Draw(string label, ref System.Numerics.Vector3 v3, float width)
    {
        bool changed = false;
        DrawLabel(label, ref width);

        ImGui.PushItemWidth(width / 3 - 13.5f);
        ImGui.Text("X");
        ImGui.SameLine();
        changed |= ImGui.DragFloat("##X", ref v3.X, "%g");
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        changed |= ImGui.DragFloat("##Y", ref v3.Y, "%g");
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        changed |= ImGui.DragFloat("##Z", ref v3.Z, "%g");
        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}

public class PropertyDrawerVector3 : PropertyDrawer<Vector3> {

    protected override bool Draw(string label, ref Vector3 v3, float width)
    {
        bool changed = false;
        DrawLabel(label, ref width);

        ImGui.PushItemWidth(width / 3 - 13.5f);
        ImGui.Text("X");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##X", ref v3.x, 0.01f);
        ImGui.SameLine();
        ImGui.Text("Y");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##Y", ref v3.y, 0.01f);
        ImGui.SameLine();
        ImGui.Text("Z");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##Z", ref v3.z, 0.01f);
        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}
