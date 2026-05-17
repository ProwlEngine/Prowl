using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.GUI;

/// <summary>
/// Thin facade over the Origami PropertyGrid for backward compatibility.
/// All calls delegate to PropertyGridRenderer using EditorApplication.PropertyGridConfig.
/// New code should use Origami.PropertyGrid() directly.
/// </summary>
public static class PropertyGrid
{
    /// <summary>
    /// Set of overridden field names for the current component being drawn.
    /// Set by the inspector before drawing a prefab instance's component.
    /// </summary>
    [ThreadStatic]
    public static HashSet<string>? OverriddenFields;

    /// <summary>Draw a full property grid for an object.</summary>
    public static void Draw(Paper paper, string id, object target, Action<object>? onChanged = null, int depth = 0)
    {
        Origami.PropertyGrid(paper, id, target, EditorApplication.PropertyGridConfig)
            .OnChanged(onChanged ?? (_ => { }))
            .Overrides(OverriddenFields)
            .Depth(depth)
            .Show();
    }

    /// <summary>Draw a single field with label and control. Routes through the editor's PropertyEditorRegistry.</summary>
    public static void DrawField(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth = 0)
    {
        PropertyGridRenderer.DrawField(paper, id, label, type, value,
            EditorApplication.PropertyGridConfig, onChange, depth);
    }

    /// <summary>Draw a type picker for polymorphic fields. Delegates to the config's DrawTypePicker callback.</summary>
    public static void DrawTypePicker(Paper paper, string id, Type baseType, object? currentValue, Action<object?> onChange)
    {
        // Direct implementation to avoid circular delegation
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Take(20).ToArray();

        if (types.Length == 0) return;

        Type? currentType = currentValue?.GetType();
        int selectedIndex = currentType != null ? Array.IndexOf(types, currentType) + 1 : 0;
        var names = types.Select(t => t.Name).Prepend("(null)").ToArray();

        Origami.Dropdown(paper, $"{id}_dd", selectedIndex,
            idx =>
            {
                if (idx == 0) onChange(null);
                else if (idx >= 1 && idx <= types.Length) onChange(Activator.CreateInstance(types[idx - 1]));
            }, names).Show();
    }

    /// <summary>Convert "myFieldName" to "My Field Name".</summary>
    public static string NicifyName(string name) => PropertyGridRenderer.FormatFieldName(name);

    /// <summary>Get serializable fields for a type.</summary>
    public static FieldInfo[] GetSerializableFields(Type type) => PropertyGridRenderer.GetSerializableFields(type);
}
