// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Enum))]
public class Enum_PropertyDrawer : PropertyDrawer
{
    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        Enum enumValue = (Enum)value;
        Array values = Enum.GetValues(targetType);
        int selectedIndex = Array.IndexOf(values, enumValue);

        string[] names = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            FieldInfo fieldInfo = targetType.GetField(values.GetValue(i).ToString());
            TextAttribute attribute = fieldInfo.GetCustomAttribute<TextAttribute>();
            names[i] = RuntimeUtils.Prettify(attribute != null ? attribute.text : fieldInfo.Name);
        }

        bool changed = gui.Combo("#_PropID", "#_PropPopupID", ref selectedIndex, names, 0, 0, Size.Percentage(1f), EditorStylePrefs.Instance.ItemSize);

        if (selectedIndex >= 0 && selectedIndex < values.Length)
        {
            value = values.GetValue(selectedIndex);
        }

        return changed;
    }
}
