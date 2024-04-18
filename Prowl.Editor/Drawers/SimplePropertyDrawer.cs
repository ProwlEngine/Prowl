using Hexa.NET.ImGui;
using System.Threading.Channels;

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

#region Primitive Types

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

#endregion

#region Vectors

public class PropertyDrawerVector2 : SimplePropertyDrawer<Runtime.Vector2>
{
    public override bool DrawControl(ref Runtime.Vector2 value)
    {
        System.Numerics.Vector2 vec = value;
        bool changed = ImGui.DragFloat2("", ref vec, 1f);
        value = vec;
        return changed;
    }
}

public class PropertyDrawerVector2Int : SimplePropertyDrawer<Runtime.Vector2Int>
{
    public override bool DrawControl(ref Runtime.Vector2Int value)
    {
        bool changed = ImGui.DragInt2("", ref value.x, 1);
        return changed;
    }
}

public class PropertyDrawerSystemVector2 : SimplePropertyDrawer<System.Numerics.Vector2>
{
    public override bool DrawControl(ref System.Numerics.Vector2 value) =>  ImGui.DragFloat2("", ref value, 1f);
}

public class PropertyDrawerVector3 : SimplePropertyDrawer<Runtime.Vector3>
{
    public override bool DrawControl(ref Runtime.Vector3 value)
    {
        System.Numerics.Vector3 vec = value;
        bool changed = ImGui.DragFloat3("", ref vec, 1f);
        value = vec;
        return changed;
    }
}

public class PropertyDrawerSystemVector3 : SimplePropertyDrawer<System.Numerics.Vector3>
{
    public override bool DrawControl(ref System.Numerics.Vector3 value) =>  ImGui.DragFloat3("", ref value, 1f);
}

public class PropertyDrawerVector4 : SimplePropertyDrawer<Runtime.Vector4>
{
    public override bool DrawControl(ref Runtime.Vector4 value)
    {
        System.Numerics.Vector4 vec = value;
        bool changed = ImGui.DragFloat4("", ref vec, 1f);
        value = vec;
        return changed;
    }
}

public class PropertyDrawerSystemVector4 : SimplePropertyDrawer<System.Numerics.Vector4>
{
    public override bool DrawControl(ref System.Numerics.Vector4 value) =>  ImGui.DragFloat4("", ref value, 1f);
}

#endregion

#region Colors

public class PropertyDrawerColor : SimplePropertyDrawer<Runtime.Color>
{
    public override bool DrawControl(ref Runtime.Color value)
    {
        System.Numerics.Vector4 vec = value;
        bool changed = ImGui.ColorEdit4("", ref vec);
        value = vec;
        return changed;
    }
}

public class PropertyDrawerColor32 : SimplePropertyDrawer<Runtime.Color32>
{
    public override bool DrawControl(ref Runtime.Color32 value)
    {
        System.Numerics.Vector4 vec = (Runtime.Color)value;
        bool changed = ImGui.ColorEdit4("", ref vec);
        value = (Runtime.Color32)(Runtime.Color)vec;
        return changed;
    }
}

#endregion