// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Vector2))]
public class Vector2_PropertyDrawer : PropertyDrawer
{
    public override double MinWidth => 125;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

        Vector2 val = value is Vector2 v ? v : throw new Exception();
        bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
        changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
        value = val;
        return changed;
    }
}
