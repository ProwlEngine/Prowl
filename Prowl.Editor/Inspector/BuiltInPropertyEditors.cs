using System;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

// ================================================================
//  Numeric Types
// ================================================================

[CustomPropertyEditor(typeof(bool))]
public class BoolPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Toggle(paper, id, label, (bool)(value ?? false))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(int))]
public class IntPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.IntField(paper, id, (int)(value ?? 0), label: label)
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(float))]
public class FloatPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.FloatField(paper, id, (float)(value ?? 0f), label: label)
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(double))]
public class DoublePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.FloatField(paper, id, (float)(double)(value ?? 0.0), label)
            .OnValueChanged(v => onChange((double)v));
    }
}

[CustomPropertyEditor(typeof(string))]
public class StringPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.TextField(paper, id, label, (string)(value ?? ""))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(byte))]
public class BytePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.IntSlider(paper, id, label, (int)(byte)(value ?? (byte)0), 0, 255)
            .OnValueChanged(v => onChange((byte)v));
    }
}

[CustomPropertyEditor(typeof(short))]
public class ShortPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.IntField(paper, id, (int)(short)(value ?? (short)0), label)
            .OnValueChanged(v => onChange((short)Math.Clamp(v, short.MinValue, short.MaxValue)));
    }
}

[CustomPropertyEditor(typeof(ushort))]
public class UShortPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.IntField(paper, id, (int)(ushort)(value ?? (ushort)0), label)
            .OnValueChanged(v => onChange((ushort)Math.Clamp(v, ushort.MinValue, ushort.MaxValue)));
    }
}

[CustomPropertyEditor(typeof(long))]
public class LongPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.IntField(paper, id, (int)Math.Clamp((long)(value ?? 0L), int.MinValue, int.MaxValue), label)
            .OnValueChanged(v => onChange((long)v));
    }
}

[CustomPropertyEditor(typeof(uint))]
public class UIntPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.IntField(paper, id, (int)Math.Min((uint)(value ?? 0u), int.MaxValue), label)
            .OnValueChanged(v => onChange((uint)Math.Max(v, 0)));
    }
}

// ================================================================
//  Math Types
// ================================================================

[CustomPropertyEditor(typeof(Float2))]
public class Float2PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Vector2Field(paper, id, label, (Float2)(value ?? Float2.Zero))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(Float3))]
public class Float3PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Vector3Field(paper, id, label, (Float3)(value ?? Float3.Zero))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(Float4))]
public class Float4PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Vector4Field(paper, id, label, (Float4)(value ?? Float4.Zero))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(Prowl.Vector.Color))]
public class ColorPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.ColorField(paper, id, label, (Prowl.Vector.Color)(value ?? new Prowl.Vector.Color(1, 1, 1, 1)))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(Quaternion))]
public class QuaternionPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var q = (Quaternion)(value ?? Quaternion.Identity);
        EditorGUI.Vector3Field(paper, id, label, q.EulerAngles)
            .OnValueChanged(v => onChange(Quaternion.FromEuler(v)));
    }
}

// ================================================================
//  Special Types
// ================================================================

[CustomPropertyEditor(typeof(AnimationCurve))]
public class AnimationCurvePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        CurveEditor.CurveField(paper, id, label, (AnimationCurve)(value ?? new AnimationCurve()))
            .OnValueChanged(v => onChange(v));
    }
}

[CustomPropertyEditor(typeof(Guid))]
public class GuidPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Label(paper, id, $"{label}: {value}");
    }
}
