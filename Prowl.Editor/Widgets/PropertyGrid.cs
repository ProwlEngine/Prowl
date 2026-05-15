using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;

using Color = System.Drawing.Color;
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
    /// Set of overridden field names for the current component being drawn.
    /// Set by the inspector before drawing a prefab instance's component.
    /// Field names match the PropertyOverride path suffix (e.g., "AnimationSpeed", "_enabled").
    /// </summary>
    [ThreadStatic]
    public static HashSet<string>? OverriddenFields;

    /// <summary>
    /// The root EngineObject being edited. Set at depth 0 so nested property changes
    /// can call OnValidate on the correct object.
    /// </summary>
    [ThreadStatic]
    private static EngineObject? _validationRoot;
    /// <summary>
    /// Draw the property grid for an object.
    /// </summary>
    public static void Draw(Paper paper, string id, object target, Action<object>? onChanged = null, int depth = 0)
    {
        using (paper.Column($"{id}_root").ColBetween(6f).Height(UnitValue.Auto).Enter())
        {

            if (target == null) return;
            if (depth > 10) { Origami.Label(paper, $"{id}_deep", "(max depth)").TextColor(EditorTheme.Ink400).Show(); return; }

            // Pre-snapshot at top level: captures state BEFORE any widgets mutate
            // nested objects, collections, curves, etc. in-place
            if (depth == 0)
            {
                Undo.Snapshot(target);
                _validationRoot = target as EngineObject;
            }

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
                    Origami.Header(paper, $"{fieldId}_header", header.Text).Show();

                string label = NicifyName(field.Name);
                object? value = field.GetValue(target);
                Type fieldType = field.FieldType;

                // [ReadOnly]
                if (field.GetCustomAttribute<ReadOnlyAttribute>() != null)
                {
                    Origami.Label(paper, fieldId, $"{label}: {value ?? "(null)"}").Show();
                    continue;
                }

                // [Range] override > render as a slider (single value) or range slider
                // (Float2/Double2/Int2 — interpreted as low/high pair within the same outer
                // bounds). Falls through to the default editor for any other type.
                var range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    bool handled = TryDrawRangeAttribute(paper, fieldId, label, fieldType, value, range,
                        v => { field.SetValue(target, v); onChanged?.Invoke(target); NotifyValidation(); });
                    if (handled) continue;
                }

                // Check if this field is overridden on a prefab instance
                bool isOverridden = OverriddenFields != null && OverriddenFields.Contains(field.Name);

                if (isOverridden)
                {
                    using (paper.Row($"{fieldId}_ov").Height(UnitValue.Auto)
                        .BackgroundColor(Color.FromArgb(25, EditorTheme.Purple400))
                        .Rounded(3)
                        .Enter())
                    {
                        paper.Box($"{fieldId}_ov_bar")
                            .Width(3).Height(UnitValue.Stretch())
                            .BackgroundColor(EditorTheme.Purple400).Rounded(1);

                        using (paper.Column($"{fieldId}_ov_c").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            DrawField(paper, fieldId, label, fieldType, value, newVal =>
                            {
                                field.SetValue(target, newVal);
                                onChanged?.Invoke(target);
                                NotifyValidation();
                            }, depth);
                        }
                    }
                }
                else
                {
                    DrawField(paper, fieldId, label, fieldType, value, newVal =>
                    {
                        field.SetValue(target, newVal);
                        onChanged?.Invoke(target);
                    }, depth);
                }
            }

            // [Button] methods
            DrawButtonMethods(paper, $"{id}_btns", target);

            // Clear root at depth 0 exit
            if (depth == 0)
                _validationRoot = null;
        }
    }

    /// <summary>Call OnValidate on the root EngineObject if one is being edited.</summary>
    private static void NotifyValidation()
    {
        try { _validationRoot?.OnValidate(); }
        catch (Exception ex) { Runtime.Debug.LogWarning($"OnValidate error: {ex.Message}"); }
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
            InspectorRow.Draw(paper, id, label, () =>
                Origami.Dropdown(paper, $"{id}_dd", selectedIdx,
                    idx => { if (idx >= 0 && idx < values.Length) onChange(values.GetValue(idx)); }, names)
                    .Show());
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

        // 5. AssetRef<T> (implements IAssetRef)
        if (typeof(IAssetRef).IsAssignableFrom(type))
        {
            new AssetRefPropertyEditor().OnGUI(paper, id, label, value, onChange, depth);
            return;
        }

        // 6. EngineObject (fallback if not caught by registry handles inheritance)
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
        Origami.Label(paper, id, $"{label}: {value ?? "(null)"}").TextColor(EditorTheme.Ink400).Show();
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
                Origami.Label(paper, $"{id}_lbl", $"{label}: (null)").Show();
                if (!type.IsAbstract && !type.IsInterface)
                    Origami.Button(paper, $"{id}_create", EditorIcons.Plus + " Create", () => { onChange(Activator.CreateInstance(type)); }).Show();
                else
                    DrawTypePicker(paper, $"{id}_pick", type, null, onChange);
            }
            return;
        }

        Type actualType = value.GetType();

        // Check for a custom editor registered for this type
        var customEditor = CustomEditorRegistry.GetEditor(actualType);
        if (customEditor != null)
        {
            Origami.Foldout(paper, $"{id}_fold", $"{label} ({actualType.Name})").Body(() =>
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    DrawTypePicker(paper, $"{id}_pick", type, value, onChange);
                    Origami.Separator(paper, $"{id}_tpsep").Show();
                }
                customEditor.OnGUI(paper, id, value);
            });
            return;
        }

        var fields = GetSerializableFields(actualType);
        if (fields.Length == 0)
        {
            Origami.Label(paper, id, $"{label}: {value}").Show();
            return;
        }

        Origami.Foldout(paper, $"{id}_fold", $"{label} ({actualType.Name})").Body(() =>
        {
            if (type.IsAbstract || type.IsInterface)
            {
                DrawTypePicker(paper, $"{id}_pick", type, value, onChange);
                Origami.Separator(paper, $"{id}_tpsep").Show();
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
    //  [Range] handling — slider for scalars, range slider for Float2/Double2/Int2
    // ================================================================

    /// <summary>
    /// Render a <see cref="RangeAttribute"/>-decorated field as a slider (scalars) or two-thumb
    /// range slider (Float2 / Double2 / Int2). Returns false if the field type isn't supported,
    /// in which case the caller falls back to the default editor.
    /// </summary>
    static bool TryDrawRangeAttribute(Paper paper, string id, string label, Type fieldType, object? value,
        RangeAttribute range, Action<object?> onChange)
    {
        if (fieldType == typeof(float))
        {
            InspectorRow.Draw(paper, id, label, () =>
                Origami.Slider(paper, $"{id}_v", (float)(value ?? 0f),
                    v => onChange(v), range.Min, range.Max).Show());
            return true;
        }
        if (fieldType == typeof(int))
        {
            InspectorRow.Draw(paper, id, label, () =>
                Origami.IntSlider(paper, $"{id}_v", (int)(value ?? 0),
                    v => onChange(v), (int)range.Min, (int)range.Max).Show());
            return true;
        }
        if (fieldType == typeof(double))
        {
            InspectorRow.Draw(paper, id, label, () =>
                Origami.Slider<double>(paper, $"{id}_v", (double)(value ?? 0.0),
                    v => onChange(v), range.Min, range.Max).Show());
            return true;
        }

        // [Range] on a 2-component vector > range slider, with .X = low, .Y = high.
        if (fieldType == typeof(Float2))
        {
            var v = (Float2)(value ?? Float2.Zero);
            InspectorRow.Draw(paper, id, label, () =>
                Origami.RangeSlider(paper, $"{id}_v", (float)v.X, (float)v.Y,
                    (lo, hi) => onChange(new Float2(lo, hi)),
                    range.Min, range.Max).Show());
            return true;
        }
        if (fieldType == typeof(Double2))
        {
            var v = (Double2)(value ?? Double2.Zero);
            InspectorRow.Draw(paper, id, label, () =>
                Origami.RangeSlider<double>(paper, $"{id}_v", v.X, v.Y,
                    (lo, hi) => onChange(new Double2(lo, hi)),
                    range.Min, range.Max).Show());
            return true;
        }
        if (fieldType == typeof(Int2))
        {
            var v = (Int2)(value ?? Int2.Zero);
            InspectorRow.Draw(paper, id, label, () =>
                Origami.IntRangeSlider(paper, $"{id}_v", v.X, v.Y,
                    (lo, hi) => onChange(new Int2(lo, hi)),
                    (int)range.Min, (int)range.Max).Show());
            return true;
        }

        return false;
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
            Origami.Label(paper, $"{id}_none", "(no implementations)").TextColor(EditorTheme.Ink400).Show();
            return;
        }

        Type? currentType = currentValue?.GetType();
        int selectedIndex = currentType != null ? Array.IndexOf(types, currentType) + 1 : 0;
        var names = types.Select(t => t.Name).Prepend("(null)").ToArray();

        InspectorRow.Draw(paper, $"{id}_row", "Type", () =>
            Origami.Dropdown(paper, $"{id}_dd", selectedIndex,
                idx =>
                {
                    if (idx == 0) onChange(null);
                    else if (idx >= 1 && idx <= types.Length) onChange(Activator.CreateInstance(types[idx - 1]));
                }, names)
                .Show());
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
            Origami.Button(paper, $"{id}_{idx++}", label, () => { method.Invoke(target, null); }).Show();
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
