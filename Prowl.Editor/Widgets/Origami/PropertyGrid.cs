// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

// ════════════════════════════════════════════════════════════════
//  Field Drawer - renders a specific type in the property grid
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for type-specific field renderers. Register via
/// <see cref="FieldDrawerRegistry.Register{T}(FieldDrawer)"/>.
/// The PropertyGrid handles the label; the drawer renders the control.
/// </summary>
public abstract class FieldDrawer
{
    /// <summary>Draw the control for this field type.</summary>
    /// <param name="paper">Paper instance.</param>
    /// <param name="id">Unique element ID.</param>
    /// <param name="value">Current field value.</param>
    /// <param name="fieldType">Declared type of the field.</param>
    /// <param name="onChange">Callback when value changes.</param>
    /// <param name="depth">Current nesting depth.</param>
    public abstract void Draw(Paper paper, string id, object? value, Type fieldType,
        Action<object?> onChange, int depth);
}

/// <summary>Static registry mapping types to their FieldDrawer instances.</summary>
public static class FieldDrawerRegistry
{
    private static readonly Dictionary<Type, FieldDrawer> _drawers = new();

    public static void Register<T>(FieldDrawer drawer) => _drawers[typeof(T)] = drawer;
    public static void Register(Type type, FieldDrawer drawer) => _drawers[type] = drawer;

    public static FieldDrawer? GetDrawer(Type type)
    {
        // Exact match
        if (_drawers.TryGetValue(type, out var drawer)) return drawer;
        // Walk base types
        var current = type.BaseType;
        while (current != null && current != typeof(object))
        {
            if (_drawers.TryGetValue(current, out drawer)) return drawer;
            current = current.BaseType;
        }
        // Check interfaces
        foreach (var iface in type.GetInterfaces())
            if (_drawers.TryGetValue(iface, out drawer)) return drawer;
        return null;
    }

    public static void Clear() => _drawers.Clear();
}

// ════════════════════════════════════════════════════════════════
//  Attribute Handler - modifies rendering based on field attributes
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for attribute-driven rendering modifications. Register via
/// <see cref="AttributeHandlerRegistry.Register{TAttr}(AttributeHandler)"/>.
/// </summary>
public abstract class AttributeHandler
{
    /// <summary>Called before the field is drawn. Return false to skip the field entirely.</summary>
    public virtual bool OnBeforeDraw(Paper paper, string id, Attribute attr,
        FieldInfo field, object target, int depth) => true;

    /// <summary>Called instead of the default drawer. Return true if handled.</summary>
    public virtual bool OnDraw(Paper paper, string id, string label, Attribute attr,
        FieldInfo field, object target, Action<object?> onChange, int depth) => false;

    /// <summary>Called after the field is drawn.</summary>
    public virtual void OnAfterDraw(Paper paper, string id, Attribute attr,
        FieldInfo field, object target, int depth) { }
}

/// <summary>Static registry mapping attribute types to their handlers.</summary>
public static class AttributeHandlerRegistry
{
    private static readonly Dictionary<Type, AttributeHandler> _handlers = new();

    public static void Register<TAttr>(AttributeHandler handler) where TAttr : Attribute
        => _handlers[typeof(TAttr)] = handler;

    public static AttributeHandler? GetHandler(Type attrType)
        => _handlers.GetValueOrDefault(attrType);

    public static void Clear() => _handlers.Clear();
}

// ════════════════════════════════════════════════════════════════
//  Custom Editor - whole-object editor override
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for custom whole-object editors. When a nested object's type has
/// a registered CustomObjectEditor, it replaces the default field-by-field rendering.
/// Register via <see cref="CustomObjectEditorRegistry"/>.
/// </summary>
public abstract class CustomObjectEditor
{
    /// <summary>Draw the entire editor for this object.</summary>
    public abstract void OnGUI(Paper paper, string id, object target);
}

/// <summary>Static registry mapping types to their CustomObjectEditor.</summary>
public static class CustomObjectEditorRegistry
{
    private static readonly Dictionary<Type, CustomObjectEditor> _editors = new();

    public static void Register<T>(CustomObjectEditor editor) => _editors[typeof(T)] = editor;
    public static void Register(Type type, CustomObjectEditor editor) => _editors[type] = editor;

    public static CustomObjectEditor? GetEditor(Type type)
    {
        if (_editors.TryGetValue(type, out var editor)) return editor;
        var current = type.BaseType;
        while (current != null && current != typeof(object))
        {
            if (_editors.TryGetValue(current, out editor)) return editor;
            current = current.BaseType;
        }
        foreach (var iface in type.GetInterfaces())
            if (_editors.TryGetValue(iface, out editor)) return editor;
        return null;
    }

    public static void Clear() => _editors.Clear();
}

// ════════════════════════════════════════════════════════════════
//  Property Grid - the main reflection-based editor
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Standalone reflection-based property grid for Origami. Discovers serializable
/// fields, routes to registered FieldDrawers, processes attribute handlers,
/// handles enums, collections, dictionaries, and nested objects.
///
/// Configure via static callbacks and settings before first use.
/// </summary>
public static class PropertyGrid
{
    // ── Configuration (set by host at startup) ────────────────

    /// <summary>Max recursion depth. Default 10.</summary>
    public static int MaxDepth = 10;

    /// <summary>Called at depth 0 before any field is drawn (e.g., for undo snapshots).</summary>
    public static Action<object>? OnBeginRoot;

    /// <summary>Called after any field value changes (e.g., for OnValidate).</summary>
    public static Action<object>? OnFieldChanged;

    /// <summary>Optional set of field names to highlight as overridden (prefab system).</summary>
    public static HashSet<string>? OverriddenFields;

    /// <summary>
    /// Optional callback invoked before drawing a field. Hosts can use this to set
    /// up state needed by custom FieldDrawers (e.g., passing the declared field type
    /// for EngineObject drawers). Parameters: (fieldType, value).
    /// </summary>
    public static Action<Type, object?>? OnBeforeDrawField;

    /// <summary>
    /// Optional callback for drawing a type picker dropdown when a field's declared
    /// type is abstract/interface. Parameters: (paper, id, baseType, currentValue, onChange).
    /// If null, no type picker is drawn.
    /// </summary>
    public static Action<Paper, string, Type, object?, Action<object?>>? DrawTypePicker;

    /// <summary>
    /// Optional fallback for drawing a field when no FieldDrawer is registered.
    /// The host can route to its own editor registry (e.g., PropertyEditorRegistry).
    /// Parameters: (paper, id, label, fieldType, value, onChange, depth).
    /// Return true if handled, false to continue with built-in fallbacks.
    /// </summary>
    public static Func<Paper, string, string, Type, object?, Action<object?>, int, bool>? FallbackFieldDrawer;

    // ── Internal state ───────────────────────────────────────

    [ThreadStatic] private static object? _rootTarget;

    // ── Entry Point ──────────────────────────────────────────

    /// <summary>Draw a property grid for the given object.</summary>
    public static void Draw(Paper paper, string id, object target, Action<object>? onChange = null, int depth = 0)
    {
        if (target == null) return;
        if (depth > MaxDepth) return;

        var m = Origami.Current.Metrics;

        if (depth == 0)
        {
            _rootTarget = target;
            OnBeginRoot?.Invoke(target);
        }

        using (paper.Column($"{id}_root").ColBetween(m.SpacingMedium).Height(UnitValue.Auto).Enter())
        {
            var type = target.GetType();
            var fields = GetSerializableFields(type);

            // Process [Button] methods at the end (check by attribute name to avoid Runtime dependency)
            var buttonMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m2 => m2.GetCustomAttributes().Any(a => a.GetType().Name == "ButtonAttribute") && m2.GetParameters().Length == 0)
                .ToArray();

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                string fieldId = $"{id}_{field.Name}";

                // Process attributes (before draw)
                var attrs = field.GetCustomAttributes(true).OfType<Attribute>().ToArray();
                bool skip = false;
                bool handled = false;

                foreach (var attr in attrs)
                {
                    var handler = AttributeHandlerRegistry.GetHandler(attr.GetType());
                    if (handler != null && !handler.OnBeforeDraw(paper, fieldId, attr, field, target, depth))
                    {
                        skip = true;
                        break;
                    }
                }

                if (skip) continue;

                // Check for attribute-driven draw replacement
                foreach (var attr in attrs)
                {
                    var handler = AttributeHandlerRegistry.GetHandler(attr.GetType());
                    if (handler != null)
                    {
                        string label = FormatFieldName(field.Name);
                        if (handler.OnDraw(paper, fieldId, label, attr, field, target,
                            v => SetFieldAndNotify(field, target, v, onChange), depth))
                        {
                            handled = true;
                            break;
                        }
                    }
                }

                if (!handled)
                {
                    // Default rendering
                    var value = field.GetValue(target);
                    var fieldType = field.FieldType;
                    string label = FormatFieldName(field.Name);

                    // Prefab override highlight
                    bool isOverridden = OverriddenFields?.Contains(field.Name) ?? false;

                    DrawField(paper, fieldId, label, fieldType, value,
                        v => SetFieldAndNotify(field, target, v, onChange), depth, isOverridden);
                }

                // Post-draw attributes
                foreach (var attr in attrs)
                {
                    var handler = AttributeHandlerRegistry.GetHandler(attr.GetType());
                    handler?.OnAfterDraw(paper, fieldId, attr, field, target, depth);
                }
            }

            // Draw [Button] methods
            foreach (var method in buttonMethods)
            {
                // Get label from attribute via reflection (avoid Runtime type dependency)
                var btnAttr = method.GetCustomAttributes().First(a => a.GetType().Name == "ButtonAttribute");
                var labelProp = btnAttr.GetType().GetProperty("Label");
                string label = (labelProp?.GetValue(btnAttr) as string) ?? FormatFieldName(method.Name);
                Origami.Button(paper, $"{id}_btn_{method.Name}", label, () =>
                {
                    try { method.Invoke(target, null); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Button error: {ex.Message}"); }
                }).Show();
            }
        }

        if (depth == 0)
            _rootTarget = null;
    }

    // ── Field (label + control, public for external callers) ──

    /// <summary>
    /// Draw a single field with label and control. Routes through FieldDrawerRegistry,
    /// then enums, collections, dictionaries, nested objects, and fallback.
    /// Public so callers like MaterialPropertyDrawer can reuse the routing logic.
    /// </summary>
    public static void DrawField(Paper paper, string id, string label, Type fieldType,
        object? value, Action<object?> onChange, int depth, bool isOverridden = false)
    {
        // Notify host before drawing (e.g., to set field type for EngineObject drawers)
        OnBeforeDrawField?.Invoke(fieldType, value);

        // Check FieldDrawerRegistry first - if no drawer, try fallback (host's PropertyEditorRegistry).
        // Fallback draws the entire row (label + control) itself since legacy editors use InspectorRow.
        var drawer = FieldDrawerRegistry.GetDrawer(fieldType);
        if (drawer == null && FallbackFieldDrawer != null)
        {
            if (FallbackFieldDrawer(paper, id, label, fieldType, value, onChange, depth))
                return;
        }

        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        var ink = theme.Ink;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .RowBetween(m.SpacingMedium).Margin(0, 0, 0, m.SpacingSmall).Enter())
        {
            // Override highlight bar
            if (isOverridden)
            {
                paper.Box($"{id}_ov").Width(3).Height(m.RowHeight)
                    .BackgroundColor(theme.Primary.C400).Rounded(m.SmallRounding);
            }

            // Label
            if (font != null && !string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight).Padding(m.PaddingSmall, 0, 0, 0)
                    .IsNotInteractable()
                    .Text(label, font).TextColor(ink.C500)
                    .FontSize(m.FontSize);
            }

            // Control
            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(m.RowHeight).Enter())
            {
                DrawFieldControl(paper, $"{id}_v", fieldType, value, onChange, depth);
            }
        }
    }

    /// <summary>Draw just the control part (no label/row wrapper).</summary>
    public static void DrawFieldControl(Paper paper, string id, Type fieldType,
        object? value, Action<object?> onChange, int depth)
    {
        // 1. Registered FieldDrawer
        var drawer = FieldDrawerRegistry.GetDrawer(fieldType);
        if (drawer != null)
        {
            drawer.Draw(paper, id, value, fieldType, onChange, depth);
            return;
        }

        // 2. Enums
        if (fieldType.IsEnum)
        {
            DrawEnum(paper, id, fieldType, value, onChange);
            return;
        }

        // 3. Collections (IList: List<T>, T[])
        if (typeof(IList).IsAssignableFrom(fieldType) && fieldType != typeof(string))
        {
            DrawCollection(paper, id, fieldType, value as IList, onChange, depth);
            return;
        }

        // 4. Dictionary<K,V>
        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            DrawDictionary(paper, id, fieldType, value as IDictionary, onChange, depth);
            return;
        }

        // 5. Nested object with serializable fields
        if (value != null)
        {
            // Check for a custom whole-object editor first
            var customEditor = CustomObjectEditorRegistry.GetEditor(value.GetType());
            if (customEditor != null)
            {
                DrawNestedObjectWithCustomEditor(paper, id, fieldType, value, onChange, depth, customEditor);
                return;
            }

            var nestedFields = GetSerializableFields(value.GetType());
            if (nestedFields.Length > 0)
            {
                DrawNestedObject(paper, id, fieldType, value, onChange, depth);
                return;
            }
        }

        // 6. Null reference-type with "Create" button
        if (value == null && !fieldType.IsValueType)
        {
            DrawNullObject(paper, id, fieldType, onChange);
            return;
        }

        // 7. Fallback: read-only label
        {
            var font = Origami.Current.Font;
            var met = Origami.Current.Metrics;
            if (font != null)
                paper.Box($"{id}_fb").Height(met.RowHeight)
                    .Text(value?.ToString() ?? "(null)", font).TextColor(Origami.Current.Ink.C400)
                    .FontSize(met.FontSize)
                    .Alignment(TextAlignment.MiddleLeft);
        }
    }

    // ── Enum ─────────────────────────────────────────────────

    private static void DrawEnum(Paper paper, string id, Type enumType, object? value, Action<object?> onChange)
    {
        var names = Enum.GetNames(enumType);
        var values = Enum.GetValues(enumType);
        int currentIdx = value != null ? Array.IndexOf(values, value) : 0;
        if (currentIdx < 0) currentIdx = 0;

        Origami.Dropdown(paper, id, currentIdx, idx => onChange(values.GetValue(idx)), names).Show();
    }

    // ── Collection ───────────────────────────────────────────

    private static void DrawCollection(Paper paper, string id, Type collectionType,
        IList? list, Action<object?> onChange, int depth)
    {
        Type elementType = collectionType.IsArray
            ? collectionType.GetElementType()!
            : collectionType.GetGenericArguments()[0];

        int count = list?.Count ?? 0;
        var theme = Origami.Current;
        var m = theme.Metrics;

        Origami.Foldout(paper, $"{id}_fold", $"({count})").Body(() =>
        {
            if (list == null)
            {
                Origami.Button(paper, $"{id}_create", "Create", () =>
                {
                    onChange(collectionType.IsArray
                        ? Array.CreateInstance(elementType, 0)
                        : Activator.CreateInstance(collectionType));
                }).Show();
                return;
            }

            using (paper.Column($"{id}_items").Height(UnitValue.Auto).Padding(m.IndentWidth, 0, 0, 0).ColBetween(m.Spacing).Enter())
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    using (paper.Row($"{id}_item_{i}").Height(UnitValue.Auto).RowBetween(m.Spacing).Enter())
                    {
                        // Reorder buttons
                        Origami.IconButton(paper, $"{id}_up_{i}", theme.Icons.ChevronUp, () =>
                        {
                            if (idx <= 0) return;
                            (list[idx], list[idx - 1]) = (list[idx - 1], list[idx]);
                            onChange(list);
                        }).Disabled(idx == 0).Height(m.CompactHeight).Show();

                        Origami.IconButton(paper, $"{id}_dn_{i}", theme.Icons.ChevronDown, () =>
                        {
                            if (idx >= list.Count - 1) return;
                            (list[idx], list[idx + 1]) = (list[idx + 1], list[idx]);
                            onChange(list);
                        }).Disabled(idx >= list.Count - 1).Height(m.CompactHeight).Show();

                        using (paper.Column($"{id}_val_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            DrawField(paper, $"{id}_el_{i}", $"[{idx}]", elementType, list[i],
                                v => { list[idx] = v; onChange(list); }, depth + 1);
                        }

                        Origami.IconButton(paper, $"{id}_rm_{i}", theme.Icons.Close, () =>
                        {
                            if (collectionType.IsArray)
                            {
                                var newArr = Array.CreateInstance(elementType, list.Count - 1);
                                for (int j = 0, k = 0; j < list.Count; j++)
                                    if (j != idx) newArr.SetValue(list[j], k++);
                                onChange(newArr);
                            }
                            else
                            {
                                var newList = (IList)Activator.CreateInstance(list.GetType())!;
                                for (int j = 0; j < list.Count; j++)
                                    if (j != idx) newList.Add(list[j]);
                                onChange(newList);
                            }
                        }).Show();
                    }
                }

                Origami.Button(paper, $"{id}_add", "+ Add", () =>
                {
                    object? newElement = elementType.IsValueType
                        ? Activator.CreateInstance(elementType)
                        : elementType == typeof(string) ? "" : null;
                    if (collectionType.IsArray)
                    {
                        var newArr = Array.CreateInstance(elementType, list.Count + 1);
                        for (int j = 0; j < list.Count; j++) newArr.SetValue(list[j], j);
                        newArr.SetValue(newElement, list.Count);
                        onChange(newArr);
                    }
                    else
                    {
                        var newList = (IList)Activator.CreateInstance(list.GetType())!;
                        for (int j = 0; j < list.Count; j++) newList.Add(list[j]);
                        newList.Add(newElement);
                        onChange(newList);
                    }
                }).Show();
            }
        });
    }

    // ── Dictionary ───────────────────────────────────────────

    private static void DrawDictionary(Paper paper, string id, Type dictType,
        IDictionary? dict, Action<object?> onChange, int depth)
    {
        var typeArgs = dictType.GetGenericArguments();
        Type keyType = typeArgs[0];
        Type valueType = typeArgs[1];

        int count = dict?.Count ?? 0;
        var theme = Origami.Current;
        var m = theme.Metrics;

        Origami.Foldout(paper, $"{id}_fold", $"({count})").Body(() =>
        {
            if (dict == null)
            {
                Origami.Button(paper, $"{id}_create", "Create", () =>
                {
                    onChange(Activator.CreateInstance(dictType));
                }).Show();
                return;
            }

            using (paper.Column($"{id}_items").Height(UnitValue.Auto).Padding(m.IndentWidth, 0, 0, 0).ColBetween(m.Spacing).Enter())
            {
                int idx = 0;
                var keysToRemove = new List<object>();
                foreach (DictionaryEntry entry in dict)
                {
                    int localIdx = idx;
                    using (paper.Row($"{id}_entry_{localIdx}").Height(UnitValue.Auto).RowBetween(m.Spacing).Enter())
                    {
                        // Key (read-only display)
                        using (paper.Box($"{id}_key_{localIdx}").Width(m.LabelWidth).Height(m.RowHeight).Enter())
                        {
                            DrawFieldControl(paper, $"{id}_kv_{localIdx}", keyType, entry.Key,
                                _ => { }, depth + 1);
                        }

                        // Value
                        using (paper.Column($"{id}_val_{localIdx}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            var capturedKey = entry.Key;
                            DrawFieldControl(paper, $"{id}_vv_{localIdx}", valueType, entry.Value,
                                v => { dict[capturedKey] = v; onChange(dict); }, depth + 1);
                        }

                        // Remove
                        var keyToRemove = entry.Key;
                        Origami.IconButton(paper, $"{id}_rm_{localIdx}", theme.Icons.Close, () =>
                        {
                            dict.Remove(keyToRemove);
                            onChange(dict);
                        }).Show();
                    }
                    idx++;
                }

                Origami.Button(paper, $"{id}_add", "+ Add", () =>
                {
                    object? newKey = keyType.IsValueType ? Activator.CreateInstance(keyType)
                        : keyType == typeof(string) ? $"Key{count}" : null;
                    object? newVal = valueType.IsValueType ? Activator.CreateInstance(valueType)
                        : valueType == typeof(string) ? "" : null;
                    if (newKey != null && !dict.Contains(newKey))
                    {
                        dict.Add(newKey, newVal);
                        onChange(dict);
                    }
                }).Show();
            }
        });
    }

    // ── Nested Object ────────────────────────────────────────

    private static void DrawNestedObject(Paper paper, string id, Type declaredType,
        object value, Action<object?> onChange, int depth)
    {
        if (depth + 1 > MaxDepth) return;
        var actualType = value.GetType();
        var m = Origami.Current.Metrics;

        string typeName = actualType.Name;
        Origami.Foldout(paper, $"{id}_fold", typeName).Body(() =>
        {
            // Type picker for polymorphic fields
            if (declaredType.IsAbstract || declaredType.IsInterface)
                DrawTypePicker?.Invoke(paper, $"{id}_pick", declaredType, value, onChange);

            Draw(paper, $"{id}_inner", value, changed =>
            {
                if (actualType.IsValueType)
                    onChange(changed);
                else
                    onChange(value); // reference type already mutated
            }, depth + 1);
        });
    }

    private static void DrawNestedObjectWithCustomEditor(Paper paper, string id, Type declaredType,
        object value, Action<object?> onChange, int depth, CustomObjectEditor editor)
    {
        if (depth + 1 > MaxDepth) return;
        var actualType = value.GetType();

        Origami.Foldout(paper, $"{id}_fold", $"{actualType.Name}").Body(() =>
        {
            if (declaredType.IsAbstract || declaredType.IsInterface)
                DrawTypePicker?.Invoke(paper, $"{id}_pick", declaredType, value, onChange);

            editor.OnGUI(paper, $"{id}_custom", value);
        });
    }

    // ── Null Object ──────────────────────────────────────────

    private static void DrawNullObject(Paper paper, string id, Type fieldType, Action<object?> onChange)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;

        using (paper.Row($"{id}_null").Height(m.RowHeight).RowBetween(m.SpacingMedium).Enter())
        {
            if (font != null)
                paper.Box($"{id}_null_lbl").Width(UnitValue.Stretch()).Height(m.RowHeight)
                    .Text("(null)", font).TextColor(theme.Ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable();

            if (!fieldType.IsAbstract && !fieldType.IsInterface)
            {
                Origami.Button(paper, $"{id}_create", "Create", () =>
                {
                    try { onChange(Activator.CreateInstance(fieldType)); }
                    catch { }
                }).Show();
            }
            else
            {
                DrawTypePicker?.Invoke(paper, $"{id}_pick", fieldType, null, onChange);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private static void SetFieldAndNotify(FieldInfo field, object target, object? value, Action<object>? rootOnChange)
    {
        field.SetValue(target, value);
        if (_rootTarget != null) OnFieldChanged?.Invoke(_rootTarget);
        rootOnChange?.Invoke(target);
    }

    /// <summary>Convert "myFieldName" to "My Field Name".</summary>
    public static string FormatFieldName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Strip leading _ or m_
        if (name.StartsWith("m_") && name.Length > 2) name = name[2..];
        else if (name.StartsWith('_') && name.Length > 1) name = name[1..];

        var sb = new System.Text.StringBuilder(name.Length + 4);
        sb.Append(char.ToUpper(name[0]));
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0 && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    /// <summary>Get serializable fields for a type (matches Echo's logic).</summary>
    public static FieldInfo[] GetSerializableFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var fields = new List<FieldInfo>();
        var current = type;

        while (current != null && current != typeof(object))
        {
            foreach (var field in current.GetFields(flags))
            {
                if (field.IsStatic) continue;
                bool shouldSerialize = field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null;
                if (!shouldSerialize) continue;
                bool shouldIgnore = field.GetCustomAttribute<SerializeIgnoreAttribute>() != null
                    || field.GetCustomAttribute<NonSerializedAttribute>() != null;
                if (shouldIgnore) continue;
                // Check for HideInInspector (runtime attribute)
                if (field.GetCustomAttributes().Any(a => a.GetType().Name == "HideInInspectorAttribute"))
                    continue;
                fields.Add(field);
            }
            current = current.BaseType;
        }
        return fields.ToArray();
    }
}
