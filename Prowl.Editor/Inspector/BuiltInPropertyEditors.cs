// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
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
        InspectorRow.Draw(paper, id, label, () =>
            Origami.Switch(paper, $"{id}_v", (bool)(value ?? false), v => onChange(v))
                .Primary()
                .Show());
    }
}

[CustomPropertyEditor(typeof(int))]
public class IntPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<int>(paper, $"{id}_v", (int)(value ?? 0), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(float))]
public class FloatPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<float>(paper, $"{id}_v", (float)(value ?? 0f), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(double))]
public class DoublePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<double>(paper, $"{id}_v", (double)(value ?? 0.0), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(string))]
public class StringPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.TextField(paper, $"{id}_v", (string)(value ?? string.Empty), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(byte))]
public class BytePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<byte>(paper, $"{id}_v", (byte)(value ?? (byte)0), v => onChange(v))
                .Min((byte)0).Max((byte)255)
                .Show());
    }
}

[CustomPropertyEditor(typeof(sbyte))]
public class SBytePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<sbyte>(paper, $"{id}_v", (sbyte)(value ?? (sbyte)0), v => onChange(v))
                .Min(sbyte.MinValue).Max(sbyte.MaxValue)
                .Show());
    }
}

[CustomPropertyEditor(typeof(short))]
public class ShortPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<short>(paper, $"{id}_v", (short)(value ?? (short)0), v => onChange(v))
                .Min(short.MinValue).Max(short.MaxValue)
                .Show());
    }
}

[CustomPropertyEditor(typeof(ushort))]
public class UShortPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<ushort>(paper, $"{id}_v", (ushort)(value ?? (ushort)0), v => onChange(v))
                .Min(ushort.MinValue).Max(ushort.MaxValue)
                .Show());
    }
}

[CustomPropertyEditor(typeof(long))]
public class LongPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<long>(paper, $"{id}_v", (long)(value ?? 0L), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(ulong))]
public class ULongPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<ulong>(paper, $"{id}_v", (ulong)(value ?? 0UL), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(uint))]
public class UIntPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<uint>(paper, $"{id}_v", (uint)(value ?? 0u), v => onChange(v))
                .Show());
    }
}

// ================================================================
//  Math Types  (vector / colour / curve composites — outside the
//  Origami dropdown/textfield migration scope; left on the legacy widgets)
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
