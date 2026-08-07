// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// Draws a non-flags enum using its members' [InspectorName]s — the one place that naming lives, whether or not the field itself carries the attribute.
/// The property grid has no per-member naming of its own, so the enum type gets its own drawer, registered on demand.
/// InspectorNameAttributeHandler's own enum branch delegates here so there is one dropdown.
/// </summary>
public sealed class InspectorNameEnumDrawer : OrigamiUI.FieldDrawer
{
    /// <summary>Stateless, so every enum that wants one shares the instance.</summary>
    private static readonly InspectorNameEnumDrawer s_instance = new();

    /// <summary>Display name for an enum member: its [InspectorName] if present, else the
    /// nicified member name.</summary>
    public static string GetEnumDisplayName(Type enumType, object value)
    {
        string name = Enum.GetName(enumType, value) ?? value.ToString() ?? "";
        var attr = enumType.GetField(name)?.GetCustomAttribute<InspectorNameAttribute>();
        return attr?.DisplayName ?? PropertyGridUtils.NicifyName(name);
    }

    /// <summary>Whether this type needs the drawer at all — only enums that actually rename a
    /// member, so every other enum keeps the grid's own rendering.</summary>
    public static bool AppliesTo(Type type)
        => type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false)
            && Array.Exists(type.GetFields(BindingFlags.Public | BindingFlags.Static),
                f => f.IsDefined(typeof(InspectorNameAttribute), false));

    public static void EnsureRegistered(OrigamiUI.FieldDrawerRegistry drawers, Type type)
    {
        if (drawers.GetDrawer(type) == null && AppliesTo(type))
            drawers.Register(type, s_instance);
    }

    public override void Draw(Paper paper, string id, object? value, Type type,
        Action<object?> onChange, int depth)
    {
        object current = value ?? Enum.GetValues(type).GetValue(0)!;
        var values = new List<object>();
        foreach (object v in Enum.GetValues(type)) values.Add(v);

        OrigamiUI.Origami.Dropdown(paper, $"{id}_dd", current, v => onChange(v), values)
            .Display(v => GetEnumDisplayName(type, v))
            .Show();
    }
}
