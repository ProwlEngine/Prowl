// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Color))]
public class Color_PropertyDrawer : PropertyDrawer
{
    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

        Color val = value is Color v ? v : throw new Exception();
        var style = EditorGUI.InputFieldStyle;
        style.TextColor = val with { a = 1 };

        double r = val.r;
        bool changed = EditorGUI.InputDouble(ID + "R", ref r, 0, 0, 0, style);
        val.r = (float)r;
        double g = val.g;
        changed |= EditorGUI.InputDouble(ID + "G", ref g, 0, 0, 0, style);
        val.g = (float)g;
        double b = val.b;
        changed |= EditorGUI.InputDouble(ID + "B", ref b, 0, 0, 0, style);
        val.b = (float)b;
        double a = val.a;
        changed |= EditorGUI.InputDouble(ID + "A", ref a, 0, 0, 0, style);
        val.a = (float)a;

        value = val;
        return changed;
    }
}
