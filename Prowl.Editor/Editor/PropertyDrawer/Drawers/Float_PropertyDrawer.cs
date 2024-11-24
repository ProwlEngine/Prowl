// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(float))]
public class Float_PropertyDrawer : PropertyDrawer
{
    public override double MinWidth => EditorStylePrefs.Instance.ItemSize * 2;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value, List<Attribute>? attributes = null)
    {
        float val = (float)value!;
        bool changed = gui.InputFloat(ID + "Val", ref val, 0, 0, Size.Percentage(1f), Size.Percentage(1f), EditorGUI.InputFieldStyle);
        value = val;
        return changed;
    }
}
