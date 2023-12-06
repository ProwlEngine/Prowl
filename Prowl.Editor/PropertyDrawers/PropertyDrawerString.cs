using HexaEngine.ImGuiNET;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerString : PropertyDrawer<string?> {

    protected override bool Draw(string label, ref string value)
    {
        bool changed = false;
        value ??= string.Empty;
        
        ImGui.Columns(2);
        ImGui.Text(label);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        changed = ImGui.InputText("", ref value, 30);
        ImGui.PopItemWidth();
        ImGui.Columns(1);
        return changed;
    }
    
}
