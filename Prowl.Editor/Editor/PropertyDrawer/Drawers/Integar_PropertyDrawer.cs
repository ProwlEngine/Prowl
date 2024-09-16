// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers;

public class Integar_PropertyDrawer<T> : PropertyDrawer
{
    public override double MinWidth => EditorStylePrefs.Instance.ItemSize * 2;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        long val = Convert.ToInt64(value);
        bool changed = EditorGUI.InputLong(ID + "Val", ref val, 0, 0, Size.Percentage(1f), EditorGUI.InputFieldStyle);
        if (changed)
            value = (T)Convert.ChangeType(val, typeof(T));
        return changed;
    }
}

[Drawer(typeof(byte))] public class Byte_PropertyDrawer : Integar_PropertyDrawer<byte> { }
[Drawer(typeof(sbyte))] public class SByte_PropertyDrawer : Integar_PropertyDrawer<sbyte> { }
[Drawer(typeof(short))] public class Short_PropertyDrawer : Integar_PropertyDrawer<short> { }
[Drawer(typeof(ushort))] public class UShort_PropertyDrawer : Integar_PropertyDrawer<ushort> { }
[Drawer(typeof(int))] public class Int_PropertyDrawer : Integar_PropertyDrawer<int> { }
[Drawer(typeof(uint))] public class UInt_PropertyDrawer : Integar_PropertyDrawer<uint> { }
[Drawer(typeof(long))] public class Long_PropertyDrawer : Integar_PropertyDrawer<long> { }
[Drawer(typeof(ulong))] public class ULong_PropertyDrawer : Integar_PropertyDrawer<ulong> { }
