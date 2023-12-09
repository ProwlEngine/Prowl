using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers; 

public class PropertyDrawerDouble : PropertyDrawer<double> {

    protected override bool Draw(string label, ref double value, float width)
    {
        DrawLabel(label, ref width);

        ImGui.SetNextItemWidth(width);
        bool changed = GUIHelper.DragDouble("", ref value, 0.01f);
        ImGui.Columns(1);
        return changed;
    }
    
}

public class PropertyDrawerFloat : PropertyDrawer<float> {

    protected override bool Draw(string label, ref float value, float width)
    {
        DrawLabel(label, ref width);
        ImGui.SetNextItemWidth(width);
        bool changed = ImGui.DragFloat("", ref value, 0.01f, float.MinValue, float.MaxValue, "%g");
        ImGui.Columns(1);
        return changed;
    }
    
}

public class PropertyDrawerInt : PropertyDrawer<int> {

    protected override bool Draw(string label, ref int value, float width)
    {
        DrawLabel(label, ref width);
        ImGui.SetNextItemWidth(width);
        bool changed = ImGui.DragInt("", ref value, 0.01f, int.MinValue, int.MaxValue, "%g");
        ImGui.Columns(1);
        return changed;
    }
    
}

public class PropertyDrawerShort : PropertyDrawer<short> {

    protected override bool Draw(string label, ref short value, float width)
    {
        DrawLabel(label, ref width);
        ImGui.SetNextItemWidth(width);
        int valInt = value;
        bool changed = ImGui.DragInt("", ref valInt, 0.01f, short.MinValue, short.MaxValue, "%g");
        value = (short)valInt;
        ImGui.Columns(1);
        return changed;
    }
}