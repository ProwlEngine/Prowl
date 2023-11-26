using Prowl.Runtime;
using HexaEngine.ImGuiNET;

namespace Prowl.Editor.PropertyDrawers;

public class PropertyDrawerColor : PropertyDrawer<Color> {
    
    protected override void DrawProperty(ref Color value, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);
        
        System.Numerics.Vector4 v4 = value;
        ImGui.ColorEdit4("", ref v4);
        value = new Color(v4.X, v4.Y, v4.Z, v4.W);
        
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}

public class PropertyDrawerColor32 : PropertyDrawer<Color32> {
    
    protected override void DrawProperty(ref Color32 value, Property property) {
        ImGui.Columns(2);
        ImGui.Text(property.Name);
        ImGui.SetColumnWidth(0, 70);
        ImGui.NextColumn();
        
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushID(property.Name);
        
        System.Numerics.Vector4 v4 = new System.Numerics.Vector4(value.red / 255f, value.green / 255f, value.blue / 255f, value.alpha / 255f);
        ImGui.ColorEdit4("", ref v4);
        value = new Color32((byte)(v4.X * 255f), (byte)(v4.Y * 255f), (byte)(v4.Z * 255f), (byte)(v4.W * 255f));
        
        ImGui.PopID();
        ImGui.PopItemWidth();
        ImGui.Columns(1);
    }
}
