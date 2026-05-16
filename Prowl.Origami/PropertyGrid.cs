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
    public abstract void Draw(Paper paper, string id, object? value, Type fieldType,
        Action<object?> onChange, int depth);
}

/// <summary>Registry mapping types to their FieldDrawer instances.</summary>
public sealed class FieldDrawerRegistry
{
    private readonly Dictionary<Type, FieldDrawer> _drawers = new();

    public void Register<T>(FieldDrawer drawer) => _drawers[typeof(T)] = drawer;
    public void Register(Type type, FieldDrawer drawer) => _drawers[type] = drawer;

    public FieldDrawer? GetDrawer(Type type)
    {
        if (_drawers.TryGetValue(type, out var drawer)) return drawer;
        var current = type.BaseType;
        while (current != null && current != typeof(object))
        {
            if (_drawers.TryGetValue(current, out drawer)) return drawer;
            current = current.BaseType;
        }
        foreach (var iface in type.GetInterfaces())
            if (_drawers.TryGetValue(iface, out drawer)) return drawer;
        return null;
    }

    public void Clear() => _drawers.Clear();
}

// ════════════════════════════════════════════════════════════════
//  Attribute Handler - modifies rendering based on field attributes
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for attribute-driven rendering modifications.
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

/// <summary>Registry mapping attribute types to their handlers.</summary>
public sealed class AttributeHandlerRegistry
{
    private readonly Dictionary<Type, AttributeHandler> _handlers = new();

    public void Register<TAttr>(AttributeHandler handler) where TAttr : Attribute
        => _handlers[typeof(TAttr)] = handler;

    public AttributeHandler? GetHandler(Type attrType)
        => _handlers.GetValueOrDefault(attrType);

    public void Clear() => _handlers.Clear();
}

// ════════════════════════════════════════════════════════════════
//  Custom Object Editor - whole-object editor override
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for custom whole-object editors. When a nested object's type has
/// a registered CustomObjectEditor, it replaces the default field-by-field rendering.
/// </summary>
public abstract class CustomObjectEditor
{
    public abstract void OnGUI(Paper paper, string id, object target);
}

/// <summary>Registry mapping types to their CustomObjectEditor.</summary>
public sealed class CustomObjectEditorRegistry
{
    private readonly Dictionary<Type, CustomObjectEditor> _editors = new();

    public void Register<T>(CustomObjectEditor editor) => _editors[typeof(T)] = editor;
    public void Register(Type type, CustomObjectEditor editor) => _editors[type] = editor;

    public CustomObjectEditor? GetEditor(Type type)
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

    public void Clear() => _editors.Clear();
}

// ════════════════════════════════════════════════════════════════
//  PropertyGrid Config - holds all registries and callbacks
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for a PropertyGrid instance. Create one per context (e.g., one for
/// the editor, one for game UI) and pass it to the builder. Each config has its own
/// registries, callbacks, and settings so they don't interfere.
/// </summary>
public sealed class PropertyGridConfig
{
    /// <summary>Type-specific field drawers (bool, float, Color, etc.).</summary>
    public FieldDrawerRegistry Drawers { get; } = new();

    /// <summary>Attribute-driven rendering modifiers ([Range], [Header], etc.).</summary>
    public AttributeHandlerRegistry Handlers { get; } = new();

    /// <summary>Custom whole-object editors.</summary>
    public CustomObjectEditorRegistry CustomEditors { get; } = new();

    /// <summary>Max recursion depth. Default 10.</summary>
    public int MaxDepth = 10;

    /// <summary>Called at depth 0 before any field is drawn (e.g., for undo snapshots).</summary>
    public Action<object>? OnBeginRoot;

    /// <summary>Called after any field value changes (e.g., for OnValidate).</summary>
    public Action<object>? OnFieldChanged;

    /// <summary>
    /// Called before drawing each field. Hosts can set up state needed by custom drawers
    /// (e.g., passing the declared field type for EngineObject drawers).
    /// </summary>
    public Action<Type, object?>? OnBeforeDrawField;

    /// <summary>
    /// Draws a type picker for polymorphic fields (abstract/interface).
    /// Parameters: (paper, id, baseType, currentValue, onChange).
    /// </summary>
    public Action<Paper, string, Type, object?, Action<object?>>? DrawTypePicker;

    /// <summary>
    /// Fallback for drawing a field when no FieldDrawer is registered.
    /// The host can route to its own editor registry (e.g., PropertyEditorRegistry).
    /// Parameters: (paper, id, label, fieldType, value, onChange, depth).
    /// Return true if handled.
    /// </summary>
    public Func<Paper, string, string, Type, object?, Action<object?>, int, bool>? FallbackFieldDrawer;
}

// ════════════════════════════════════════════════════════════════
//  PropertyGrid Builder - fluent API following Origami conventions
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Fluent builder for a property grid. Construct via
/// <see cref="Origami.PropertyGrid(Paper, string, object, PropertyGridConfig)"/>
/// and call <see cref="Show"/> to render.
/// </summary>
public sealed class PropertyGridBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly object _target;
    private readonly PropertyGridConfig _config;

    private Action<object>? _onChange;
    private HashSet<string>? _overrides;
    private int _depth;

    internal PropertyGridBuilder(Paper paper, string id, object target, PropertyGridConfig config)
    {
        _paper = paper;
        _id = id;
        _target = target;
        _config = config;
    }

    /// <summary>Callback when any field value changes.</summary>
    public PropertyGridBuilder OnChanged(Action<object> onChange) { _onChange = onChange; return this; }

    /// <summary>Set of field names to highlight as overridden (prefab system).</summary>
    public PropertyGridBuilder Overrides(HashSet<string>? overrides) { _overrides = overrides; return this; }

    /// <summary>Starting nesting depth (default 0).</summary>
    public PropertyGridBuilder Depth(int depth) { _depth = depth; return this; }

    /// <summary>Render the property grid.</summary>
    public void Show()
    {
        PropertyGridRenderer.Draw(_paper, _id, _target, _config, _onChange, _overrides, _depth);
    }
}

// ════════════════════════════════════════════════════════════════
//  PropertyGrid Renderer - internal drawing logic
// ════════════════════════════════════════════════════════════════

/// <summary>Internal rendering logic for the PropertyGrid builder.</summary>
internal static class PropertyGridRenderer
{
    [ThreadStatic] private static object? _rootTarget;
    [ThreadStatic] private static PropertyGridConfig? _activeConfig;

    public static void Draw(Paper paper, string id, object target, PropertyGridConfig config,
        Action<object>? onChange, HashSet<string>? overrides, int depth)
    {
        if (target == null) return;
        if (depth > config.MaxDepth) return;

        var m = Origami.Current.Metrics;
        bool isRoot = depth == 0;

        if (isRoot)
        {
            _rootTarget = target;
            _activeConfig = config;
            config.OnBeginRoot?.Invoke(target);
        }

        using (paper.Column($"{id}_root").ColBetween(m.SpacingMedium).Height(UnitValue.Auto).Enter())
        {
            var type = target.GetType();
            var fields = GetSerializableFields(type);

            var buttonMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m2 => m2.GetCustomAttributes().Any(a => a.GetType().Name == "ButtonAttribute") && m2.GetParameters().Length == 0)
                .ToArray();

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                string fieldId = $"{id}_{field.Name}";

                var attrs = field.GetCustomAttributes(true).OfType<Attribute>().ToArray();
                bool skip = false;
                bool handled = false;

                // Pre-draw attribute handlers
                foreach (var attr in attrs)
                {
                    var handler = config.Handlers.GetHandler(attr.GetType());
                    if (handler != null && !handler.OnBeforeDraw(paper, fieldId, attr, field, target, depth))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;

                // Attribute-driven draw replacement
                foreach (var attr in attrs)
                {
                    var handler = config.Handlers.GetHandler(attr.GetType());
                    if (handler != null)
                    {
                        string label = FormatFieldName(field.Name);
                        if (handler.OnDraw(paper, fieldId, label, attr, field, target,
                            v => SetFieldAndNotify(config, field, target, v, onChange), depth))
                        {
                            handled = true;
                            break;
                        }
                    }
                }

                if (!handled)
                {
                    var value = field.GetValue(target);
                    var fieldType = field.FieldType;
                    string label = FormatFieldName(field.Name);
                    bool isOverridden = overrides?.Contains(field.Name) ?? false;

                    DrawField(paper, fieldId, label, fieldType, value, config,
                        v => SetFieldAndNotify(config, field, target, v, onChange), depth, isOverridden);
                }

                // Post-draw attribute handlers
                foreach (var attr in attrs)
                {
                    var handler = config.Handlers.GetHandler(attr.GetType());
                    handler?.OnAfterDraw(paper, fieldId, attr, field, target, depth);
                }
            }

            // [Button] methods
            foreach (var method in buttonMethods)
            {
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

        if (isRoot)
        {
            _rootTarget = null;
            _activeConfig = null;
        }
    }

    // ── DrawField (public for external callers like MaterialPropertyDrawer) ──

    public static void DrawField(Paper paper, string id, string label, Type fieldType,
        object? value, PropertyGridConfig config, Action<object?> onChange, int depth, bool isOverridden = false)
    {
        // Notify host before drawing
        config.OnBeforeDrawField?.Invoke(fieldType, value);

        // Try fallback (host's PropertyEditorRegistry) before our own layout
        var drawer = config.Drawers.GetDrawer(fieldType);
        if (drawer == null && config.FallbackFieldDrawer != null)
        {
            if (config.FallbackFieldDrawer(paper, id, label, fieldType, value, onChange, depth))
                return;
        }

        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        var ink = theme.Ink;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .RowBetween(m.SpacingMedium).Margin(0, 0, 0, m.SpacingSmall).Enter())
        {
            if (isOverridden)
            {
                paper.Box($"{id}_ov").Width(3).Height(m.RowHeight)
                    .BackgroundColor(theme.Primary.C400).Rounded(m.SmallRounding);
            }

            if (font != null && !string.IsNullOrEmpty(label))
            {
                paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight).Padding(m.PaddingSmall, 0, 0, 0)
                    .IsNotInteractable()
                    .Text(label, font).TextColor(ink.C500)
                    .FontSize(m.FontSize);
            }

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(m.RowHeight).Enter())
            {
                DrawFieldControl(paper, $"{id}_v", fieldType, value, config, onChange, depth);
            }
        }
    }

    // ── DrawFieldControl ─────────────────────────────────────

    public static void DrawFieldControl(Paper paper, string id, Type fieldType,
        object? value, PropertyGridConfig config, Action<object?> onChange, int depth)
    {
        // 1. Registered FieldDrawer
        var drawer = config.Drawers.GetDrawer(fieldType);
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
            DrawCollection(paper, id, fieldType, value as IList, config, onChange, depth);
            return;
        }

        // 4. Dictionary<K,V>
        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            DrawDictionary(paper, id, fieldType, value as IDictionary, config, onChange, depth);
            return;
        }

        // 5. Nested object
        if (value != null)
        {
            var customEditor = config.CustomEditors.GetEditor(value.GetType());
            if (customEditor != null)
            {
                DrawNestedObjectWithCustomEditor(paper, id, fieldType, value, config, onChange, depth, customEditor);
                return;
            }

            var nestedFields = GetSerializableFields(value.GetType());
            if (nestedFields.Length > 0)
            {
                DrawNestedObject(paper, id, fieldType, value, config, onChange, depth);
                return;
            }
        }

        // 6. Null reference-type with "Create" button
        if (value == null && !fieldType.IsValueType)
        {
            DrawNullObject(paper, id, fieldType, config, onChange);
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
        IList? list, PropertyGridConfig config, Action<object?> onChange, int depth)
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
                            DrawField(paper, $"{id}_el_{i}", $"[{idx}]", elementType, list[i], config,
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
        IDictionary? dict, PropertyGridConfig config, Action<object?> onChange, int depth)
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
                foreach (DictionaryEntry entry in dict)
                {
                    int localIdx = idx;
                    using (paper.Row($"{id}_entry_{localIdx}").Height(UnitValue.Auto).RowBetween(m.Spacing).Enter())
                    {
                        using (paper.Box($"{id}_key_{localIdx}").Width(m.LabelWidth).Height(m.RowHeight).Enter())
                        {
                            DrawFieldControl(paper, $"{id}_kv_{localIdx}", keyType, entry.Key, config, _ => { }, depth + 1);
                        }

                        using (paper.Column($"{id}_val_{localIdx}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            var capturedKey = entry.Key;
                            DrawFieldControl(paper, $"{id}_vv_{localIdx}", valueType, entry.Value, config,
                                v => { dict[capturedKey] = v; onChange(dict); }, depth + 1);
                        }

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
        object value, PropertyGridConfig config, Action<object?> onChange, int depth)
    {
        if (depth + 1 > config.MaxDepth) return;
        var actualType = value.GetType();

        Origami.Foldout(paper, $"{id}_fold", actualType.Name).Body(() =>
        {
            if (declaredType.IsAbstract || declaredType.IsInterface)
                config.DrawTypePicker?.Invoke(paper, $"{id}_pick", declaredType, value, onChange);

            Draw(paper, $"{id}_inner", value, config, changed =>
            {
                if (actualType.IsValueType) onChange(changed); else onChange(value);
            }, null, depth + 1);
        });
    }

    private static void DrawNestedObjectWithCustomEditor(Paper paper, string id, Type declaredType,
        object value, PropertyGridConfig config, Action<object?> onChange, int depth, CustomObjectEditor editor)
    {
        if (depth + 1 > config.MaxDepth) return;

        Origami.Foldout(paper, $"{id}_fold", value.GetType().Name).Body(() =>
        {
            if (declaredType.IsAbstract || declaredType.IsInterface)
                config.DrawTypePicker?.Invoke(paper, $"{id}_pick", declaredType, value, onChange);

            editor.OnGUI(paper, $"{id}_custom", value);
        });
    }

    // ── Null Object ──────────────────────────────────────────

    private static void DrawNullObject(Paper paper, string id, Type fieldType, PropertyGridConfig config, Action<object?> onChange)
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
                config.DrawTypePicker?.Invoke(paper, $"{id}_pick", fieldType, null, onChange);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private static void SetFieldAndNotify(PropertyGridConfig config, FieldInfo field,
        object target, object? value, Action<object>? rootOnChange)
    {
        field.SetValue(target, value);
        if (_rootTarget != null) config.OnFieldChanged?.Invoke(_rootTarget);
        rootOnChange?.Invoke(target);
    }

    /// <summary>Convert "myFieldName" to "My Field Name".</summary>
    public static string FormatFieldName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
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
                if (field.GetCustomAttributes().Any(a => a.GetType().Name == "HideInInspectorAttribute"))
                    continue;
                fields.Add(field);
            }
            current = current.BaseType;
        }
        return fields.ToArray();
    }
}
