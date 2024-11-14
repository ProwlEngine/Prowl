// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Color))]
public class Color_PropertyDrawer : PropertyDrawer
{
    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        Color color = (Color)value;
        bool changed = gui.ColorPicker(ID, $"{ID}_ColorPopup", ref color, true, 0, 0, Size.Percentage(1), Size.Percentage(1), EditorGUI.InputStyle);
        value = color;

        return changed;
    }
}
