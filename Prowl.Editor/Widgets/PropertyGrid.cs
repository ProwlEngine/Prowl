using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Inspector;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Reflection-based property grid. Discovers serializable fields and delegates
/// all drawing to PropertyEditor subclasses. Supports attributes like [Range],
/// [Header], [Space], [ShowIf], [ReadOnly], [Button], [Tooltip].
/// </summary>
public static class PropertyGrid
{
    /// <summary>
    /// Draw the property grid for an object.
    /// </summary>
    public static void Draw(Paper paper, string id, object target, Action<object>? onChanged = null, int depth = 0)
    {
        using (paper.Column($"{id}_root").ColBetween(6f).Height(UnitValue.Auto).Enter())
        {

            if (target == null) return;
            if (depth > 10) { EditorGUI.Label(paper, $"{id}_deep", "(max depth)", EditorTheme.Ink400); return; }

            var type = target.GetType();
            var fields = GetSerializableFields(type);

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                string fieldId = $"{id}_{field.Name}_{i}";

                // [ShowIf]
                var showIf = field.GetCustomAttribute<ShowIfAttribute>();
                if (showIf != null && !EvaluateCondition(target, showIf.ConditionMember))
                    continue;

                // [Space]
                var space = field.GetCustomAttribute<SpaceAttribute>();
                if (space != null)
                    paper.Box($"{fieldId}_space").Height(space.Height);

                // [Header]
                var header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null)
                    EditorGUI.Header(paper, $"{fieldId}_header", header.Text);

                string label = NicifyName(field.Name);
                object? value = field.GetValue(target);
                Type fieldType = field.FieldType;

                // [ReadOnly]
                if (field.GetCustomAttribute<ReadOnlyAttribute>() != null)
                {
                    EditorGUI.Label(paper, fieldId, $"{label}: {value ?? "(null)"}");
                    continue;
                }

                // [Range] override for numeric types
                var range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null && (fieldType == typeof(float) || fieldType == typeof(int)))
                {
                    if (fieldType == typeof(float))
                        EditorGUI.Slider(paper, fieldId, label, (float)(value ?? 0f), range.Min, range.Max)
                            .OnValueChanged(v => { field.SetValue(target, v); onChanged?.Invoke(target); });
                    else
                        EditorGUI.IntSlider(paper, fieldId, label, (int)(value ?? 0), (int)range.Min, (int)range.Max)
                            .OnValueChanged(v => { field.SetValue(target, v); onChanged?.Invoke(target); });
                    continue;
                }

                // Default: dispatch to DrawField
                DrawField(paper, fieldId, label, fieldType, value, newVal =>
                {
                    field.SetValue(target, newVal);
                    onChanged?.Invoke(target);
                }, depth);
            }

            // [Button] methods
            DrawButtonMethods(paper, $"{id}_btns", target);
        }
    }

    /// <summary>
    /// Draw a single field. Routes to PropertyEditor registry, then enums, then nested objects.
    /// </summary>
    public static void DrawField(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth = 0)
    {
        // 1. PropertyEditor registry (primitives, math types, EngineObject, collections, etc.)
        // For EngineObject types, always pass the declared field type to the editor
        if (typeof(EngineObject).IsAssignableFrom(type))
            Inspector.EngineObjectPropertyEditor.SetFieldType(type);

        var editor = Inspector.PropertyEditorRegistry.GetEditor(type);
        if (editor != null)
        {
            editor.OnGUI(paper, id, label, value, onChange, depth);
            return;
        }

        // 2. Enums
        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            int selectedIdx = value != null ? Array.IndexOf(values, value) : 0;
            EditorGUI.Dropdown(paper, id, label, selectedIdx, names)
                .OnValueChanged(idx => { if (idx >= 0 && idx < values.Length) onChange(values.GetValue(idx)); });
            return;
        }

        // 3. Collections (List<T>, T[])
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
        {
            new CollectionPropertyEditor().OnGUI(paper, id, label, value, onChange, depth);
            return;
        }

        // 4. Dictionary<K,V>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            new DictionaryPropertyEditor().OnGUI(paper, id, label, value, onChange, depth);
            return;
        }

        // 5. EngineObject (fallback if not caught by registry — handles inheritance)
        if (typeof(EngineObject).IsAssignableFrom(type))
        {
            EngineObjectPropertyEditor.SetFieldType(type);
            new EngineObjectPropertyEditor().OnGUI(paper, id, label, value, onChange, depth);
            return;
        }

        // 6. Nested object (class or struct with serializable fields)
        if ((type.IsClass || type.IsValueType) && !type.IsPrimitive)
        {
            DrawNestedObject(paper, id, label, type, value, onChange, depth);
            return;
        }

        // 7. Fallback
        EditorGUI.Label(paper, id, $"{label}: {value ?? "(null)"}", EditorTheme.Ink400);
    }

    // ================================================================
    //  Nested Object
    // ================================================================

    static void DrawNestedObject(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth)
    {
        if (value == null)
        {
            using (paper.Row($"{id}_null").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                EditorGUI.Label(paper, $"{id}_lbl", $"{label}: (null)");
                if (!type.IsAbstract && !type.IsInterface)
                    EditorGUI.Button(paper, $"{id}_create", EditorIcons.Plus + " Create")
                        .OnValueChanged(v => onChange(Activator.CreateInstance(type)));
                else
                    DrawTypePicker(paper, $"{id}_pick", type, null, onChange);
            }
            return;
        }

        Type actualType = value.GetType();
        var fields = GetSerializableFields(actualType);
        if (fields.Length == 0)
        {
            EditorGUI.Label(paper, id, $"{label}: {value}");
            return;
        }

        EditorGUI.Foldout(paper, $"{id}_fold", $"{label} ({actualType.Name})", () =>
        {
            if (type.IsAbstract || type.IsInterface)
            {
                DrawTypePicker(paper, $"{id}_pick", type, value, onChange);
                EditorGUI.Separator(paper, $"{id}_tpsep");
            }

            using (paper.Column($"{id}_nested").Height(UnitValue.Auto).ChildLeft(12).ColBetween(6).Margin(0, 0, 6, 0).Enter())
            {
                Draw(paper, $"{id}_props", value, changed =>
                {
                    if (actualType.IsValueType) onChange(changed); else onChange?.Invoke(value);
                }, depth + 1);
            }
        });
    }

    // ================================================================
    //  Type Picker (for polymorphism)
    // ================================================================

    static void DrawTypePicker(Paper paper, string id, Type baseType, object? currentValue, Action<object?> onChange)
    {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Take(20).ToArray();

        if (types.Length == 0)
        {
            EditorGUI.Label(paper, $"{id}_none", "(no implementations)", EditorTheme.Ink400);
            return;
        }

        Type? currentType = currentValue?.GetType();
        int selectedIndex = currentType != null ? Array.IndexOf(types, currentType) + 1 : 0;
        var names = types.Select(t => t.Name).Prepend("(null)").ToArray();

        EditorGUI.Dropdown(paper, $"{id}_dd", "Type", selectedIndex, names)
            .OnValueChanged(idx =>
            {
                if (idx == 0) onChange(null);
                else if (idx >= 1 && idx <= types.Length) onChange(Activator.CreateInstance(types[idx - 1]));
            });
    }

    // ================================================================
    //  [Button] Methods
    // ================================================================

    static void DrawButtonMethods(Paper paper, string id, object target)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int idx = 0;
        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<ButtonAttribute>();
            if (attr == null || method.GetParameters().Length > 0) continue;
            string label = attr.Label ?? NicifyName(method.Name);
            EditorGUI.Button(paper, $"{id}_{idx++}", label)
                .OnValueChanged(_ => method.Invoke(target, null));
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    static bool EvaluateCondition(object target, string memberName)
    {
        var type = target.GetType();
        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) return field.GetValue(target) is true;
        var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null) return prop.GetValue(target) is true;
        return true;
    }

    public static FieldInfo[] GetSerializableFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var fields = new List<FieldInfo>();
        Type? current = type;

        while (current != null && current != typeof(object))
        {
            foreach (var field in current.GetFields(flags))
            {
                bool shouldSerialize = field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null;
                if (!shouldSerialize) continue;
                bool shouldIgnore = field.GetCustomAttribute<SerializeIgnoreAttribute>() != null ||
                                    field.GetCustomAttribute<NonSerializedAttribute>() != null ||
                                    field.GetCustomAttribute<HideInInspectorAttribute>() != null;
                if (shouldIgnore) continue;
                fields.Add(field);
            }
            current = current.BaseType;
        }

        return fields.ToArray();
    }

    public static string NicifyName(string name)
    {
        if (name.StartsWith("_")) name = name[1..];
        if (name.StartsWith("m_")) name = name[2..];

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                result.Append(' ');
            result.Append(i == 0 ? char.ToUpper(c) : c);
        }
        return result.ToString();
    }
}
