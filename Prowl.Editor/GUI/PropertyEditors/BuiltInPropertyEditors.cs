// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Inspector;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.PropertyEditors;

// ================================================================
//  Numeric Types
// ================================================================

[CustomPropertyEditor(typeof(bool))]
public class BoolPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
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
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<int>(paper, $"{id}_v", (int)(value ?? 0), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(float))]
public class FloatPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<float>(paper, $"{id}_v", (float)(value ?? 0f), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(double))]
public class DoublePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<double>(paper, $"{id}_v", (double)(value ?? 0.0), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(string))]
public class StringPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.TextField(paper, $"{id}_v", (string)(value ?? string.Empty), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(byte))]
public class BytePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
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
        EditorGUI.Row(paper, id, label, () =>
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
        EditorGUI.Row(paper, id, label, () =>
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
        EditorGUI.Row(paper, id, label, () =>
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
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<long>(paper, $"{id}_v", (long)(value ?? 0L), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(ulong))]
public class ULongPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<ulong>(paper, $"{id}_v", (ulong)(value ?? 0UL), v => onChange(v))
                .Show());
    }
}

[CustomPropertyEditor(typeof(uint))]
public class UIntPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<uint>(paper, $"{id}_v", (uint)(value ?? 0u), v => onChange(v))
                .Show());
    }
}

// ================================================================
//  Math Types  (vector / colour / curve composites - outside the
//  Origami dropdown/textfield migration scope; left on the legacy widgets)
// ================================================================

[CustomPropertyEditor(typeof(Float2))]
public class Float2PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Float2Field(paper, $"{id}_vf", (Float2)(value ?? Float2.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Float3))]
public class Float3PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Float3Field(paper, $"{id}_vf", (Float3)(value ?? Float3.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Float4))]
public class Float4PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Float4Field(paper, $"{id}_vf", (Float4)(value ?? Float4.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Double2))]
public class Double2PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Double2Field(paper, $"{id}_vf", (Double2)(value ?? Double2.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Double3))]
public class Double3PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Double3Field(paper, $"{id}_vf", (Double3)(value ?? Double3.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Double4))]
public class Double4PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Double4Field(paper, $"{id}_vf", (Double4)(value ?? Double4.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Int2))]
public class Int2PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Int2Field(paper, $"{id}_vf", (Int2)(value ?? Int2.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Int3))]
public class Int3PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Int3Field(paper, $"{id}_vf", (Int3)(value ?? Int3.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Int4))]
public class Int4PropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.Int4Field(paper, $"{id}_vf", (Int4)(value ?? Int4.Zero), v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Color))]
public class ColorPropertyEditor : PropertyEditor
{
    private static ColorPalette? s_palette;

    private static ColorPalette GetEditorPalette()
    {
        if (s_palette != null) return s_palette;

        var settings = EditorRegistries.GetSettings<ProjectsEditorSettings>();
        // Convert hex strings to Color list, keep in sync
        var colors = new List<Color>();
        foreach (var hex in settings.ColorPalette)
        {
            var sc = ColorRamp.ParseHex(hex);
            colors.Add(new Color(sc.R / 255f, sc.G / 255f, sc.B / 255f, 1f));
        }

        s_palette = new ColorPalette(colors)
        {
            OnAdd = () =>
            {
                // Return the color to add - caller provides current color via the palette list
                return null; // Will be overridden per-instance below
            },
            OnRemoved = idx =>
            {
                if (idx >= 0 && idx < settings.ColorPalette.Count)
                {
                    settings.ColorPalette.RemoveAt(idx);
                    EditorRegistries.SaveSettings();
                }
            },
        };
        return s_palette;
    }

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var color = (Color)(value ?? new Color(1, 1, 1, 1));
        var palette = GetEditorPalette();

        // Set the OnAdd to capture the current color value
        var capturedColor = color;
        palette.OnAdd = () =>
        {
            var settings = EditorRegistries.GetSettings<ProjectsEditorSettings>();
            int r = Math.Clamp((int)(capturedColor.R * 255), 0, 255);
            int g = Math.Clamp((int)(capturedColor.G * 255), 0, 255);
            int b = Math.Clamp((int)(capturedColor.B * 255), 0, 255);
            string hex = $"#{r:X2}{g:X2}{b:X2}";
            if (!settings.ColorPalette.Contains(hex))
            {
                settings.ColorPalette.Add(hex);
                palette.Colors.Add(new Color(capturedColor.R, capturedColor.G, capturedColor.B, 1f));
                EditorRegistries.SaveSettings();
            }
            return null; // Already added manually
        };

        EditorGUI.Row(paper, id, label, () =>
            Origami.ColorField(paper, $"{id}_cf", color, v => onChange(v))
                .Palette(palette).Show());
    }
}

[CustomPropertyEditor(typeof(Quaternion))]
public class QuaternionPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var q = (Quaternion)(value ?? Quaternion.Identity);
        EditorGUI.Row(paper, id, label, () =>
            Origami.Float3Field(paper, $"{id}_vf", q.EulerAngles, v => onChange(Quaternion.FromEuler(v))).Show());
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
        EditorGUI.Row(paper, id, label, () =>
            CurveField.Create(paper, $"{id}_cf",
                (AnimationCurve)(value ?? new AnimationCurve()),
                v => onChange(v)).Show());
    }
}

[CustomPropertyEditor(typeof(Guid))]
public class GuidPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        Origami.Label(paper, id, $"{label}: {value}").Show();
    }
}
