// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.GUI;

using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(string))]
public class String_PropertyDrawer : PropertyDrawer
{
    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        string val = value as string ?? "";
        bool changed = ActiveGUI.InputField(ID, ref val, 255, InputFieldFlags.None, 0, 0, Size.Percentage(1f), null, EditorGUI.InputStyle);
        value = val;
        return changed;
    }
}
