using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.GUI;

public static class PropertyGridUtils
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

    /// <summary>Convert "myFieldName" to "My Field Name".</summary>
    public static string NicifyName(string name) => PropertyGridRenderer.FormatFieldName(name);

    /// <summary>Get serializable fields for a type.</summary>
    public static FieldInfo[] GetSerializableFields(Type type) => PropertyGridRenderer.GetSerializableFields(type);
}
