﻿// This file is part of the Prowl Game Engine
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
        Prowl.Runtime.RangeAttribute? range = attributes?.OfType<Prowl.Runtime.RangeAttribute>().FirstOrDefault();

        float val = (float)value!;
        bool changed;
        if (range != null && range.IsSlider)
        {
            changed = gui.FloatSlider(ID + "Val", ref val, (float)range.Min, (float)range.Max, 0, 0, Size.Percentage(1f), Size.Percentage(1f), EditorGUI.InputFieldStyle);
        }
        else
        {
            changed = gui.InputFloat(ID + "Val", ref val, 0, 0, Size.Percentage(1f), Size.Percentage(1f), EditorGUI.InputFieldStyle);
            if (range != null)
                val = Math.Max((float)range.Min, Math.Min((float)range.Max, val));
        }
        value = val;
        return changed;
    }
}
