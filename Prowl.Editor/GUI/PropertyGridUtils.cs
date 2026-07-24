using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI;

public static class PropertyGridUtils
{
    /// <summary>
    /// Build the "Name (Type)" label an object-reference field shows in the property grid. 
    /// A scene component has no name of its own so it uses the name of the GameObject it lives on and typed
    /// by the component (e.g. "Player (Rigidbody)"). GameObjects and assets carry a real Name, so they use it.
    /// Field Type is used as a fallback for empty references.
    /// </summary>
    public static string DescribeObjectRef(EngineObject? instance, Type fieldType)
    {
        if (instance == null)
            return $"None ({fieldType.Name})";

        if (instance is MonoBehaviour mb && mb.GameObject != null)
            return $"{mb.GameObject.Name} ({instance.GetType().Name})";

        bool isAsset = instance.AssetID != Guid.Empty;
        string suffix = isAsset || instance is GameObject ? instance.GetType().Name : "Instance";
        return $"{instance.Name} ({suffix})";
    }

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

    /// <summary>Draw a property grid editing several objects at once (shared fields only; differing
    /// values are flagged as mixed and edits apply to every target).</summary>
    public static void DrawMulti(Paper paper, string id, IReadOnlyList<object> targets, Action<object>? onChanged = null, int depth = 0)
    {
        Origami.PropertyGrid(paper, id, targets, EditorApplication.PropertyGridConfig)
            .OnChanged(onChanged ?? (_ => { }))
            .Overrides(OverriddenFields)
            .Depth(depth)
            .Show();
    }

    /// <summary>Draw a single field with label and control.</summary>
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
