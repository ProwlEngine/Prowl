// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Vector3))]
public class Vector3_PropertyDrawer : PropertyDrawer
{
    public override double MinWidth => 150;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value, List<Attribute>? attributes = null)
    {
        gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

        Vector3 val = (Vector3)value;
        bool changed = gui.InputDouble(ID + "X", ref val.x, 0, 0, 0, Size.Percentage(1), EditorGUI.VectorXStyle);
        changed |= gui.InputDouble(ID + "Y", ref val.y, 0, 0, 0, Size.Percentage(1), EditorGUI.VectorYStyle);
        changed |= gui.InputDouble(ID + "Z", ref val.z, 0, 0, 0, Size.Percentage(1), EditorGUI.VectorZStyle);
        value = val;
        return changed;
    }
}
