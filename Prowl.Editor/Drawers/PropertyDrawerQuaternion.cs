using Hexa.NET.ImGui;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerQuaternion : PropertyDrawer<Quaternion>
{
    protected override bool Draw(string label, ref Quaternion v3, float width)
    {
        bool changed = false;
        DrawLabel(label, ref width);

        ImGui.PushItemWidth(width / 3 - 10);
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
        ImGui.Text("W");
        ImGui.SameLine();
        changed |= GUIHelper.DragDouble("##W", ref v3.w, 0.01f);

        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
}
