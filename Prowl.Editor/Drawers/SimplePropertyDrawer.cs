using Hexa.NET.ImGui;
using Prowl.Runtime;

namespace Prowl.Editor.PropertyDrawers;

public abstract class SimplePropertyDrawer<T> : PropertyDrawer<T>
{
    protected override bool Draw(string label, ref T value, float width)
    {
        DrawLabel(label, ref width);
        ImGui.SetNextItemWidth(width);
        bool changed = DrawControl(ref value);
        ImGui.Columns(1);
        return changed;
    }

    public abstract bool DrawControl(ref T value);

}

public class PropertyDrawerByte : SimplePropertyDrawer<byte>
{
    public override bool DrawControl(ref byte value) => GUIHelper.DragByte("", ref value, 1);
}

public class PropertyDrawerSByte : SimplePropertyDrawer<sbyte>
{
    public override bool DrawControl(ref sbyte value) => GUIHelper.DragSByte("", ref value, 1);
}

public class PropertyDrawerShort : SimplePropertyDrawer<short>
{
    public override bool DrawControl(ref short value) => GUIHelper.DragShort("", ref value, 1);
}

public class PropertyDrawerUShort : SimplePropertyDrawer<ushort>
{
    public override bool DrawControl(ref ushort value) => GUIHelper.DragUShort("", ref value, 1);
}

public class PropertyDrawerInt : SimplePropertyDrawer<int>
{
    public override bool DrawControl(ref int value) => GUIHelper.DragInt("", ref value, 1);
}

public class PropertyDrawerUInt : SimplePropertyDrawer<uint>
{
    public override bool DrawControl(ref uint value) => GUIHelper.DragUInt("", ref value, 1);
}

public class PropertyDrawerLong : SimplePropertyDrawer<long>
{
    public override bool DrawControl(ref long value) => GUIHelper.DragLong("", ref value, 1);
}

public class PropertyDrawerULong : SimplePropertyDrawer<ulong>
{
    public override bool DrawControl(ref ulong value) => GUIHelper.DragULong("", ref value, 1);
}

public class PropertyDrawerFloat : SimplePropertyDrawer<float>
{
    public override bool DrawControl(ref float value) => GUIHelper.DragFloat("", ref value, 1);
}

public class PropertyDrawerDouble : SimplePropertyDrawer<double>
{
    public override bool DrawControl(ref double value) => GUIHelper.DragDouble("", ref value, 1);
}

public class PropertyDrawerString : SimplePropertyDrawer<string>
{
    public override bool DrawControl(ref string value) => ImGui.InputText("", ref value, 256);
}

public class PropertyDrawerBool : SimplePropertyDrawer<bool>
{
    public override bool DrawControl(ref bool value) => ImGui.Checkbox("", ref value);
}