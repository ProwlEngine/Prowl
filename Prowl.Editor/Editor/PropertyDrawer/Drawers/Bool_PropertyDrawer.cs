// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(bool))]
public class Bool_PropertyDrawer : PropertyDrawer
{
    public override double MinWidth => EditorStylePrefs.Instance.ItemSize;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        bool val = value is bool v ? v : Convert.ToBoolean(value);
        bool changed = Gui.ActiveGUI.Checkbox(ID + "Val", ref val, 0, 0, out _, EditorGUI.GetInputStyle());
        value = val;
        return changed;
    }
}
