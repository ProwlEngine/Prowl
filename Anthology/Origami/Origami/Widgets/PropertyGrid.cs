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

    /// <summary>When true, nested objects, lists and their entries start expanded. Default false
    /// (everything collapsed until the user opens it).</summary>
    public bool ExpandByDefault = false;

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

    /// <summary>
    /// True when the host has a single-control editor for this type (e.g. AssetRef, EngineObject,
    /// Color). Such types render as a simple one-line collection row instead of the nested (foldout
    /// per element) list, even though they have serializable fields.
    /// </summary>
    public Func<Type, bool>? IsSimpleFieldType;
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
    private readonly IReadOnlyList<object> _targets;
    private readonly PropertyGridConfig _config;

    private Action<object>? _onChange;
    private HashSet<string>? _overrides;
    private int _depth;

    internal PropertyGridBuilder(Paper paper, string id, object target, PropertyGridConfig config)
    {
        _paper = paper;
        _id = id;
        _targets = new[] { target };
        _config = config;
    }

    internal PropertyGridBuilder(Paper paper, string id, IReadOnlyList<object> targets, PropertyGridConfig config)
    {
        _paper = paper;
        _id = id;
        _targets = targets;
        _config = config;
    }

    /// <summary>Callback when any field value changes.</summary>
    public PropertyGridBuilder OnChanged(Action<object> onChange) { _onChange = onChange; return this; }

    /// <summary>Set of field names to highlight as overridden (prefab system).</summary>
    public PropertyGridBuilder Overrides(HashSet<string>? overrides) { _overrides = overrides; return this; }

    /// <summary>Starting nesting depth (default 0).</summary>
    public PropertyGridBuilder Depth(int depth) { _depth = depth; return this; }

    /// <summary>Start nested objects, lists and their entries expanded (default is collapsed).</summary>
    public PropertyGridBuilder ExpandByDefault(bool expand = true) { _config.ExpandByDefault = expand; return this; }

    /// <summary>Render the property grid.</summary>
    public void Show()
    {
        PropertyGridRenderer.DrawTargets(_paper, _id, _targets, _config, _onChange, _overrides, _depth);
    }
}

// ════════════════════════════════════════════════════════════════
//  PropertyGrid Renderer - internal drawing logic
// ════════════════════════════════════════════════════════════════

/// <summary>Rendering logic for the PropertyGrid builder.</summary>
public static class PropertyGridRenderer
{
    [ThreadStatic] private static object? _rootTarget;
    [ThreadStatic] private static PropertyGridConfig? _activeConfig;

    /// <summary>
    /// True while the field currently being drawn has differing values across a multi-object selection.
    /// FieldDrawers may read this to render a "mixed" placeholder; the grid also marks it visually.
    /// </summary>
    [ThreadStatic] public static bool IsMixedField;

    /// <summary>Single-target entry point.</summary>
    public static void Draw(Paper paper, string id, object target, PropertyGridConfig config,
        Action<object>? onChange, HashSet<string>? overrides, int depth)
    {
        if (target == null) return;
        DrawTargets(paper, id, new[] { target }, config, onChange, overrides, depth);
    }

    /// <summary>
    /// Multi-target entry point. Draws the fields common to every target; a field whose value differs
    /// across targets is flagged as mixed, and every edit is applied to all targets. Fields that don't
    /// exist (by name and type) on every target are omitted.
    /// </summary>
    public static void DrawTargets(Paper paper, string id, IReadOnlyList<object> targets, PropertyGridConfig config,
        Action<object>? onChange, HashSet<string>? overrides, int depth)
    {
        if (targets == null || targets.Count == 0) return;
        if (depth > config.MaxDepth) return;

        object representative = targets[0];
        if (representative == null) return;

        var m = Origami.Current.Metrics;
        bool isRoot = depth == 0;
        bool multi = targets.Count > 1;

        if (isRoot)
        {
            _rootTarget = representative;
            _activeConfig = config;
            for (int t = 0; t < targets.Count; t++)
                if (targets[t] != null) config.OnBeginRoot?.Invoke(targets[t]);
        }

        using (paper.Column($"{id}_root").ColBetween(m.SpacingLarge).Height(UnitValue.Auto).Enter())
        {
            var type = representative.GetType();
            var fields = multi ? CommonSerializableFields(targets) : GetSerializableFields(type);

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

                // Pre-draw attribute handlers (operate on the representative)
                foreach (var attr in attrs)
                {
                    var handler = config.Handlers.GetHandler(attr.GetType());
                    if (handler != null && !handler.OnBeforeDraw(paper, fieldId, attr, field, representative, depth))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;

                bool isMixed = multi && IsFieldMixed(targets, field);
                Action<object?> applyAll = v => SetFieldOnAllAndNotify(config, field, targets, v, onChange);

                // Attribute-driven draw replacement
                foreach (var attr in attrs)
                {
                    var handler = config.Handlers.GetHandler(attr.GetType());
                    if (handler != null)
                    {
                        string label = FormatFieldName(field.Name);
                        IsMixedField = isMixed;
                        bool h = handler.OnDraw(paper, fieldId, label, attr, field, representative, applyAll, depth);
                        IsMixedField = false;
                        if (h) { handled = true; break; }
                    }
                }

                if (!handled)
                {
                    var value = field.GetValue(representative);
                    var fieldType = field.FieldType;
                    string label = FormatFieldName(field.Name);
                    bool isOverridden = overrides?.Contains(field.Name) ?? false;

                    DrawField(paper, fieldId, label, fieldType, value, config, applyAll, depth, isOverridden, isMixed);
                }

                // Post-draw attribute handlers
                foreach (var attr in attrs)
                {
                    var handler = config.Handlers.GetHandler(attr.GetType());
                    handler?.OnAfterDraw(paper, fieldId, attr, field, representative, depth);
                }
            }

            // [Button] methods (invoked on every target)
            foreach (var method in buttonMethods)
            {
                var btnAttr = method.GetCustomAttributes().First(a => a.GetType().Name == "ButtonAttribute");
                var labelProp = btnAttr.GetType().GetProperty("Label");
                string label = (labelProp?.GetValue(btnAttr) as string) ?? FormatFieldName(method.Name);
                Origami.Button(paper, $"{id}_btn_{method.Name}", label, () =>
                {
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (targets[t] == null) continue;
                        try { method.Invoke(targets[t], null); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Button error: {ex.Message}"); }
                    }
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
        object? value, PropertyGridConfig config, Action<object?> onChange, int depth, bool isOverridden = false, bool isMixed = false)
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

        // Complex collections (element type has its own fields) and dictionaries span the full section
        // width with a self-labelled header. Simple collections (leaf element types like Material refs,
        // colors, primitives) keep the normal label gutter and draw their compact card in the control
        // column - matching Nebula's `.i2field > .i2label + .coll` vs full-width `.i2field.full > .nl`.
        bool isLeafCollection = false;
        if (drawer == null && fieldType != typeof(string))
        {
            if (typeof(IList).IsAssignableFrom(fieldType))
            {
                Type et = fieldType.IsArray ? fieldType.GetElementType()! : fieldType.GetGenericArguments()[0];
                if (IsLeafElementType(et, config))
                    isLeafCollection = true;
                else
                {
                    // Full-width card, but keep the standard field gutter so it doesn't touch the edges.
                    using (paper.Row($"{id}_fw").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
                        DrawCollection(paper, id, fieldType, value as IList, config, onChange, depth, label);
                    return;
                }
            }
            else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                using (paper.Row($"{id}_fw").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
                    DrawDictionary(paper, id, fieldType, value as IDictionary, config, onChange, depth, label);
                return;
            }
        }

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(m.Padding).Enter())
        {
            // Override (prefab) / mixed (multi-select) marker: a small accent / amber dot before the label.
            if (isOverridden || isMixed)
            {
                var dot = isOverridden ? theme.Primary.C500 : theme.Amber.C400;
                using (paper.Box($"{id}_ov").Width(8).Height(m.RowHeight).IsNotInteractable().Enter())
                    paper.Box($"{id}_ovd").Width(5).Height(5).Rounded(3)
                        .Margin(1, UnitValue.Stretch(), UnitValue.Stretch(), UnitValue.Stretch())
                        .BackgroundColor(dot).IsNotInteractable();
            }

            if (font != null && !string.IsNullOrEmpty(label))
            {
                bool isNumeric = IsNumericType(fieldType);
                // A tall card (simple collection) top-aligns its label; scalar fields centre it.
                var lbl = paper.Box($"{id}_lbl")
                    .Width(m.LabelWidth).Height(m.RowHeight)
                    .Margin(0, 0, isLeafCollection ? UnitValue.Pixels(4) : UnitValue.Stretch(), UnitValue.Stretch())
                    .Text(label, font)
                    .TextColor(isMixed ? theme.Amber.C400 : isOverridden ? theme.Primary.C700 : ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                if (isNumeric && !Origami.IsReadOnly)
                {
                    // Draggable label for numeric fields - horizontal drag adjusts value
                    // Ctrl = x10, Shift = x0.01, default = x0.1
                    lbl.OnDragStart(e =>
                        {
                            // Store initial value at drag start
                            paper.SetElementStorage(paper.CurrentParent, "drag_start", ConvertToDouble(value));
                        })
                        .OnDragging(e =>
                        {
                            float multiplier = 0.1f;
                            if (paper.IsKeyDown(PaperUI.PaperKey.LeftControl) || paper.IsKeyDown(PaperUI.PaperKey.RightControl))
                                multiplier *= 10f;
                            else if (paper.IsKeyDown(PaperUI.PaperKey.LeftShift) || paper.IsKeyDown(PaperUI.PaperKey.RightShift))
                                multiplier *= 0.01f;

                            double startVal = paper.GetElementStorage(paper.CurrentParent, "drag_start", ConvertToDouble(value));
                            double newVal = startVal + (double)e.TotalDelta.X * multiplier;
                            object? converted = ConvertFromDouble(newVal, fieldType);
                            if (converted != null) onChange(converted);
                        });
                }
                else
                {
                    lbl.IsNotInteractable();
                }
            }

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(m.RowHeight).Enter())
            {
                IsMixedField = isMixed;
                DrawFieldControl(paper, $"{id}_v", fieldType, value, config, onChange, depth);
                IsMixedField = false;
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

        // 1b. Host fallback (e.g. the editor's AssetRef / EngineObject editors). DrawField does this
        // for top-level fields; element controls must too, or list/array/dictionary entries of those
        // types render as a dead read-only label. The label is empty because the caller (collection /
        // dictionary row) already draws the index/key label.
        if (config.FallbackFieldDrawer != null &&
            config.FallbackFieldDrawer(paper, id, "", fieldType, value, onChange, depth))
            return;

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

    // Leaf element = drawn by a single control (primitive/enum/string, a registered drawer, or a host
    // single-control editor like AssetRef/Color) -> simple one-line list. Otherwise a nested type with
    // its own fields -> the per-element foldout list.
    private static bool IsLeafElementType(Type t, PropertyGridConfig config)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) return true;
        if (config.Drawers.GetDrawer(t) != null) return true;
        if (config.IsSimpleFieldType?.Invoke(t) == true) return true;
        return GetSerializableFields(t).Length == 0;
    }

    private static void DrawCollection(Paper paper, string id, Type collectionType,
        IList? list, PropertyGridConfig config, Action<object?> onChange, int depth, string label = "")
    {
        Type elementType = collectionType.IsArray
            ? collectionType.GetElementType()!
            : collectionType.GetGenericArguments()[0];

        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        if (font == null) return;
        var semi = theme.SemiBold ?? font;
        var mono = theme.Mono ?? font;

        UnitValue ST = UnitValue.Stretch();
        var bd = theme.BorderSoft;
        var glass = theme.Glass;
        var tHi = theme.Ink.C500; var tMid = theme.Ink.C300; var tLo = theme.Ink.C200; var tDim = theme.Ink.C100;
        var acc = theme.Primary.C700;
        var redBg = System.Drawing.Color.FromArgb(40, theme.Red.C500);

        // Null collection: a full-width Create button.
        if (list == null)
        {
            using (paper.Row($"{id}_null").Width(ST).Height(UnitValue.Auto).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
                Origami.Button(paper, $"{id}_create", "Create", () =>
                    onChange(collectionType.IsArray ? Array.CreateInstance(elementType, 0) : Activator.CreateInstance(collectionType))).Show();
            return;
        }

        // Stable ids keep Paper element identity across reorders/removals.
        var colEl = paper.CurrentParent;
        var stableIds = paper.GetElementStorage<List<string>>(colEl, "stableIds", null!) ?? new List<string>();
        while (stableIds.Count < list.Count) stableIds.Add(Guid.NewGuid().ToString("N")[..8]);
        while (stableIds.Count > list.Count) stableIds.RemoveAt(stableIds.Count - 1);
        paper.SetElementStorage(colEl, "stableIds", stableIds);

        void Commit() => paper.SetElementStorage(colEl, "stableIds", stableIds);

        void RemoveAt(int i)
        {
            if (i < 0 || i >= list.Count) return;
            stableIds.RemoveAt(i); Commit();
            if (collectionType.IsArray)
            {
                var a = Array.CreateInstance(elementType, list.Count - 1);
                for (int j = 0, k = 0; j < list.Count; j++) if (j != i) a.SetValue(list[j], k++);
                onChange(a);
            }
            else
            {
                var l = (IList)Activator.CreateInstance(list.GetType())!;
                for (int j = 0; j < list.Count; j++) if (j != i) l.Add(list[j]);
                onChange(l);
            }
        }

        void AddNew()
        {
            // Concrete reference types with a default ctor are instantiated; abstract/interface (or
            // ctor-less) element types are added as null and get a type picker to assign an instance.
            object? ne;
            if (elementType.IsValueType) ne = Activator.CreateInstance(elementType);
            else if (elementType == typeof(string)) ne = "";
            else if (!elementType.IsAbstract && !elementType.IsInterface && elementType.GetConstructor(Type.EmptyTypes) != null)
                ne = Activator.CreateInstance(elementType);
            else ne = null;
            stableIds.Add(Guid.NewGuid().ToString("N")[..8]); Commit();
            if (collectionType.IsArray)
            {
                var a = Array.CreateInstance(elementType, list.Count + 1);
                for (int j = 0; j < list.Count; j++) a.SetValue(list[j], j);
                a.SetValue(ne, list.Count); onChange(a);
            }
            else
            {
                var l = (IList)Activator.CreateInstance(list.GetType())!;
                for (int j = 0; j < list.Count; j++) l.Add(list[j]);
                l.Add(ne); onChange(l);
            }
        }

        void MoveTo(int from, int to)
        {
            if (from == to || from < 0 || from >= list.Count || to < 0 || to >= list.Count) return;
            var v = list[from]; var s = stableIds[from];
            if (from < to) for (int j = from; j < to; j++) { list[j] = list[j + 1]; stableIds[j] = stableIds[j + 1]; }
            else for (int j = from; j > to; j--) { list[j] = list[j - 1]; stableIds[j] = stableIds[j - 1]; }
            list[to] = v; stableIds[to] = s; Commit(); onChange(list);
        }

        // Drag reorder from the grip. Each visible row records its centre-Y via OnPostLayout; while
        // dragging, the row whose centre is nearest the pointer becomes the drop target. This is
        // accurate regardless of individual row heights (nested cards expand to different sizes).
        void CaptureRow(int i, ElementBuilder el) => el.OnPostLayout((h, r) =>
        {
            var cys = paper.GetElementStorage<List<float>>(colEl, "rowCys", null!);
            if (cys == null) { cys = new List<float>(); paper.SetElementStorage(colEl, "rowCys", cys); }
            while (cys.Count <= i) cys.Add(0f);
            cys[i] = (float)(r.Min.Y + r.Size.Y * 0.5f);
        });
        bool BeingDragged(string sk) => paper.GetElementStorage<string>(colEl, "dragSk", null!) == sk;
        void GripDrag(string sk, ElementBuilder grip)
        {
            grip.OnDragStart(sk, (k, _) => paper.SetElementStorage(colEl, "dragSk", k))
                .OnDragging(sk, (k, _) =>
                {
                    int cur = stableIds.IndexOf(k);
                    var cys = paper.GetElementStorage<List<float>>(colEl, "rowCys", null!);
                    if (cur < 0 || cys == null) return;
                    float py = (float)paper.PointerPos.Y;
                    int target = cur; float best = float.MaxValue;
                    for (int t = 0; t < cys.Count && t < list.Count; t++)
                    {
                        float d = MathF.Abs(cys[t] - py);
                        if (d < best) { best = d; target = t; }
                    }
                    MoveTo(cur, target);
                })
                .OnDragEnd(_ => paper.SetElementStorage(colEl, "dragSk", (string?)null));
        }

        System.Drawing.Color DepthColor(int d) => d switch
        {
            0 => System.Drawing.Color.FromArgb(128, theme.Primary.C500),
            1 => System.Drawing.Color.FromArgb(128, theme.Blue.C500),
            _ => System.Drawing.Color.FromArgb(128, 217, 107, 216),
        };

        // ── Simple (leaf element) list ─────────────────────────
        void DrawSimple()
        {
            const float rowH = 30f;
            using (paper.Column($"{id}_coll").Width(ST).Height(UnitValue.Auto)
                .Rounded(8).BorderColor(bd).BorderWidth(1).Clip().Enter())
            {
                var listEl = paper.CurrentParent;
                bool expanded = paper.GetElementStorage<bool>(listEl, "exp", config.ExpandByDefault);
                float anim = paper.AnimateBool(expanded, 0.18f, id: $"{id}_se");

                using (paper.Row($"{id}_ch").Width(ST).Height(26).RoundedTop(8).Padding(m.SpacingLarge, m.SpacingLarge, 0, 0).RowBetween(m.SpacingMedium).BackgroundColor(glass)
                    .Hovered.BackgroundColor(theme.Hover).End()
                    .OnClick(0, (_, _) => paper.SetElementStorage(listEl, "exp", !paper.GetElementStorage<bool>(listEl, "exp", config.ExpandByDefault)))
                    .Enter())
                {
                    paper.Box($"{id}_ci").Width(14).Height(26).Margin(0, 0, ST, ST).IsNotInteractable().Icon(paper, OrigamiIconSet.Layers, acc, size: 12f);
                    paper.Box($"{id}_cc").Width(ST).Height(26).IsNotInteractable()
                        .Text(string.IsNullOrEmpty(label) ? $"{list.Count} elements" : $"{label}  ({list.Count})", font)
                        .TextColor(tMid).FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);
                    paper.Box($"{id}_cv").Width(12).Height(26).Margin(0, 0, ST, ST).IsNotInteractable()
                        .Icon(paper, expanded ? OrigamiIconSet.ChevronDown : OrigamiIconSet.ChevronRight, tLo, size: 11f);
                }

                if (!expanded && anim <= 0.001f) return;

                using (paper.Column($"{id}_body").Width(ST).Height(UnitValue.Lerp(0, UnitValue.Auto, anim)).Clip().Enter())
                {
                    paper.Box($"{id}_chd").Width(ST).Height(1).BackgroundColor(bd).IsNotInteractable();

                    for (int i = 0; i < list.Count; i++)
                    {
                        int idx = i; string sk = stableIds[i];
                        var rowB = paper.Row($"{id}_r_{sk}").Width(ST).Height(UnitValue.Auto).MinHeight(rowH).Padding(m.PaddingSmall, m.Padding, m.PaddingSmall, m.PaddingSmall).RowBetween(m.SpacingLarge);
                        if (BeingDragged(sk)) rowB.BackgroundColor(theme.Selected);
                        CaptureRow(idx, rowB);
                        using (rowB.Enter())
                        {
                            var grip = paper.Box($"{id}_g_{sk}").Width(12).Height(rowH).Icon(paper, OrigamiIconSet.Grip, tDim, size: 13f);
                            GripDrag(sk, grip);

                            paper.Box($"{id}_i_{sk}").Width(14).Height(rowH).IsNotInteractable()
                                .Text(idx.ToString(), mono).TextColor(tDim).FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter);

                            using (paper.Box($"{id}_v_{sk}").Width(ST).Height(UnitValue.Auto).MinHeight(m.RowHeight).Enter())
                                DrawFieldControl(paper, $"{id}_el_{sk}", elementType, list[idx], config, v => { list[idx] = v; onChange(list); }, depth + 1);

                            paper.Box($"{id}_x_{sk}").Width(18).Height(18).Rounded(4).Margin(0, 0, ST, ST)
                                .Hovered.BackgroundColor(redBg).End()
                                .Icon(paper, OrigamiIconSet.Close, tLo, size: 11f)
                                .OnClick(idx, (j, _) => RemoveAt(j));
                        }
                        if (i < list.Count - 1)
                            paper.Box($"{id}_d_{sk}").Width(ST).Height(1).BackgroundColor(bd).IsNotInteractable();
                    }

                    paper.Box($"{id}_add").Width(ST).Height(26)
                        .Hovered.BackgroundColor(theme.Hover).End()
                        .Text("+ Add Element", font).TextColor(acc).FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(0, (_, _) => AddNew());
                }
            }
        }

        // ── One nested element card (foldout with its own property fields) ──
        void DrawElement(int i, string sk)
        {
            const float rowH = 28f;
            var cardB = paper.Column($"{id}_nel_{sk}").Width(ST).Height(UnitValue.Auto)
                .Rounded(8).BorderColor(bd).BorderWidth(1)
                .BackgroundColor(BeingDragged(sk) ? theme.Selected : System.Drawing.Color.FromArgb(6, 255, 255, 255)).Clip();
            CaptureRow(i, cardB);
            using (cardB.Enter())
            {
                var nelEl = paper.CurrentParent;
                bool exp = paper.GetElementStorage<bool>(nelEl, "exp", config.ExpandByDefault);

                var nh = paper.Row($"{id}_nh_{sk}").Width(ST).Height(UnitValue.Auto).MinHeight(rowH).Padding(m.SpacingLarge, m.SpacingLarge, 0, 0).RowBetween(m.SpacingMedium)
                    .Hovered.BackgroundColor(theme.Hover).End()
                    .OnClick(sk, (k, _) => paper.SetElementStorage(nelEl, "exp", !paper.GetElementStorage<bool>(nelEl, "exp", config.ExpandByDefault)));
                if (exp) nh.RoundedTop(8); else nh.Rounded(8);   // hover fill follows the card corners
                using (nh.Enter())
                {
                    var grip = paper.Box($"{id}_ng_{sk}").Width(12).Height(rowH).StopEventPropagation().Icon(paper, OrigamiIconSet.Grip, tDim, size: 12f);
                    GripDrag(sk, grip);

                    paper.Box($"{id}_nc_{sk}").Width(11).Height(rowH).IsNotInteractable()
                        .Icon(paper, exp ? OrigamiIconSet.ChevronDown : OrigamiIconSet.ChevronRight, tLo, size: 11f);

                    paper.Box($"{id}_ni_{sk}").Width(UnitValue.Auto).Height(rowH).IsNotInteractable()
                        .Text(i.ToString(), mono).TextColor(tDim).FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter);

                    // Show the element's actual concrete type, not the list's (base) element type.
                    paper.Box($"{id}_nt_{sk}").Width(ST).Height(rowH).IsNotInteractable()
                        .Text(list[i]?.GetType().Name ?? elementType.Name, semi).TextColor(tHi).FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                    paper.Box($"{id}_nx_{sk}").Width(18).Height(18).Rounded(4).Margin(0, 0, ST, ST).StopEventPropagation()
                        .Hovered.BackgroundColor(redBg).End()
                        .Icon(paper, OrigamiIconSet.Close, tLo, size: 11f)
                        .OnClick(i, (j, _) => RemoveAt(j));
                }

                if (exp)
                {
                    paper.Box($"{id}_nd_{sk}").Width(ST).Height(1).BackgroundColor(bd).IsNotInteractable();
                    using (paper.Column($"{id}_nbody_{sk}").Width(ST).Height(UnitValue.Auto).Padding(0, 0, m.SpacingSmall, m.PaddingSmall).Enter())
                    {
                        var elem = list[i];
                        int capIdx = i;
                        var customEd = elem != null ? config.CustomEditors.GetEditor(elem.GetType()) : null;
                        if (customEd != null)
                            customEd.OnGUI(paper, $"{id}_nce_{sk}", elem!);
                        else if (elem != null)
                            Draw(paper, $"{id}_nbf_{sk}", elem, config, changed => { list[capIdx] = changed; onChange(list); }, null, depth + 1);
                        else
                            // Null element: a type picker to assign / instantiate a value.
                            using (paper.Row($"{id}_npw_{sk}").Width(ST).Height(UnitValue.Auto).Padding(m.SpacingLarge, m.SpacingLarge, m.PaddingSmall, m.PaddingSmall).Enter())
                                config.DrawTypePicker?.Invoke(paper, $"{id}_np_{sk}", elementType, null, v => { list[capIdx] = v; onChange(list); });
                    }
                }
            }
        }

        // ── Nested (complex element) list ──────────────────────
        void DrawNested()
        {
            var color = DepthColor(depth);
            using (paper.Column($"{id}_nl").Width(ST).Height(UnitValue.Auto)
                .Rounded(9).BorderColor(bd).BorderWidth(1).BackgroundColor(System.Drawing.Color.FromArgb(36, 0, 0, 0)).Clip().Enter())
            {
                var listEl = paper.CurrentParent;
                bool expanded = paper.GetElementStorage<bool>(listEl, "exp", config.ExpandByDefault);
                float anim = paper.AnimateBool(expanded, 0.18f, id: $"{id}_ne");

                using (paper.Row($"{id}_nlh").Width(ST).Height(30).RoundedTop(9).Padding(m.SpacingLarge, m.SpacingLarge, 0, 0).RowBetween(m.SpacingLarge).BackgroundColor(glass)
                    .Hovered.BackgroundColor(theme.Hover).End()
                    .OnClick(0, (_, _) => paper.SetElementStorage(listEl, "exp", !paper.GetElementStorage<bool>(listEl, "exp", config.ExpandByDefault)))
                    .Enter())
                {
                    paper.Box($"{id}_nli").Width(14).Height(30).Margin(0, 0, ST, ST).IsNotInteractable().Icon(paper, OrigamiIconSet.Layers, color, size: 13f);
                    paper.Box($"{id}_nlt").Width(UnitValue.Auto).Height(30).IsNotInteractable()
                        .Text(string.IsNullOrEmpty(label) ? elementType.Name : label, semi).TextColor(tHi).FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);
                    paper.Box($"{id}_nlc").Width(ST).Height(30).Margin(4, 0, 0, 0).IsNotInteractable()
                        .Text($"[{list.Count}]", mono).TextColor(tLo).FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);
                    paper.Box($"{id}_nlv").Width(12).Height(30).Margin(0, 0, ST, ST).IsNotInteractable()
                        .Icon(paper, expanded ? OrigamiIconSet.ChevronDown : OrigamiIconSet.ChevronRight, tLo, size: 11f);
                }

                if (!expanded && anim <= 0.001f) return;

                using (paper.Column($"{id}_nlwrap").Width(ST).Height(UnitValue.Lerp(0, UnitValue.Auto, anim)).Clip().Enter())
                {
                    paper.Box($"{id}_nlhd").Width(ST).Height(1).BackgroundColor(bd).IsNotInteractable();

                    using (paper.Column($"{id}_nlb").Width(ST).Height(UnitValue.Auto).Padding(m.Padding, m.Padding, m.Padding, m.Padding).ColBetween(m.SpacingMedium).Enter())
                    {
                        if (list.Count == 0)
                            paper.Box($"{id}_nle").Width(ST).Height(24).IsNotInteractable()
                                .Text("Empty list", font).TextColor(tDim).FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter);
                        for (int i = 0; i < list.Count; i++)
                            DrawElement(i, stableIds[i]);

                        paper.Box($"{id}_nladd").Width(ST).Height(26).Rounded(6)
                            .Hovered.BackgroundColor(theme.Hover).End()
                            .Text($"+ Add {elementType.Name}", font).TextColor(acc).FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter)
                            .OnClick(0, (_, _) => AddNew());
                    }
                }
            }
        }

        if (IsLeafElementType(elementType, config))
            DrawSimple();
        else
            DrawNested();
    }

    // ── Dictionary ───────────────────────────────────────────

    private static void DrawDictionary(Paper paper, string id, Type dictType,
        IDictionary? dict, PropertyGridConfig config, Action<object?> onChange, int depth, string label = "")
    {
        var typeArgs = dictType.GetGenericArguments();
        Type keyType = typeArgs[0];
        Type valueType = typeArgs[1];

        int count = dict?.Count ?? 0;
        var theme = Origami.Current;
        var m = theme.Metrics;

        Origami.Foldout(paper, $"{id}_fold", string.IsNullOrEmpty(label) ? $"({count})" : label)
            .Badge(string.IsNullOrEmpty(label) ? null : count.ToString())
            .Body(() =>
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
                // Gather keys into a list for indexed access
                var keys = new List<object>();
                foreach (var key in dict.Keys) keys.Add(key);

                for (int i = 0; i < keys.Count; i++)
                {
                    int localIdx = i;
                    var keyObj = keys[i];

                    using (paper.Row($"{id}_entry_{localIdx}").Height(UnitValue.Auto).RowBetween(m.Spacing).Enter())
                    {
                        // Key display (read-only)
                        var font = theme.Font;
                        if (font != null)
                        {
                            paper.Box($"{id}_key_{localIdx}")
                                .Width(m.LabelWidth).Height(m.RowHeight)
                                .Padding(m.PaddingSmall, 0, 0, 0)
                                .IsNotInteractable()
                                .Text($"[{keyObj}]", font).TextColor(theme.Ink.C500)
                                .FontSize(m.FontSize);
                        }

                        // Value (editable)
                        using (paper.Column($"{id}_val_{localIdx}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            var capturedKey = keyObj;
                            DrawFieldControl(paper, $"{id}_vv_{localIdx}", valueType, dict[keyObj], config,
                                v => { dict[capturedKey] = v; onChange(dict); }, depth + 1);
                        }

                        // Remove button
                        var keyToRemove = keyObj;
                        Origami.IconButton(paper, $"{id}_rm_{localIdx}", theme.Icons.Close, () =>
                        {
                            dict.Remove(keyToRemove);
                            onChange(dict);
                        }).Show();
                    }
                }

                // Add entry row with key input
                using (paper.Row($"{id}_addrow").Height(m.RowHeight).RowBetween(m.Spacing).Enter())
                {
                    var addRowEl = paper.CurrentParent;
                    string pendingKey = paper.GetElementStorage(addRowEl, "pendingKey", "");

                    Origami.TextField(paper, $"{id}_newkey", pendingKey,
                            v => paper.SetElementStorage(addRowEl, "pendingKey", v))
                        .Placeholder("Key...").Width(UnitValue.Stretch()).SubmitOnEnter().Show();

                    Origami.Button(paper, $"{id}_addentry", "+ Add", () =>
                    {
                        string pk = paper.GetElementStorage(addRowEl, "pendingKey", "");
                        if (string.IsNullOrWhiteSpace(pk)) return;
                        try
                        {
                            object? typedKey = keyType == typeof(string) ? pk
                                : Convert.ChangeType(pk, keyType, System.Globalization.CultureInfo.InvariantCulture);
                            if (typedKey == null || dict.Contains(typedKey)) return;
                            object? newVal = valueType.IsValueType ? Activator.CreateInstance(valueType)
                                : valueType == typeof(string) ? "" : null;
                            dict.Add(typedKey, newVal);
                            onChange(dict);
                            paper.SetElementStorage(addRowEl, "pendingKey", "");
                        }
                        catch { }
                    }).Show();
                }
            }
        });
    }

    // ── Nested Object ────────────────────────────────────────

    private static void DrawNestedObject(Paper paper, string id, Type declaredType,
        object value, PropertyGridConfig config, Action<object?> onChange, int depth)
    {
        if (depth + 1 > config.MaxDepth) return;
        var actualType = value.GetType();

        Origami.Foldout(paper, $"{id}_fold", actualType.Name).DefaultExpanded(config.ExpandByDefault).Body(() =>
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

        Origami.Foldout(paper, $"{id}_fold", value.GetType().Name).DefaultExpanded(config.ExpandByDefault).Body(() =>
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

    // Apply an edited field value to every target (resolving the field per target type so it works even
    // when the selection is a mix of types that share the field), then notify each.
    private static void SetFieldOnAllAndNotify(PropertyGridConfig config, FieldInfo field,
        IReadOnlyList<object> targets, object? value, Action<object>? rootOnChange)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null) continue;
            var f = t.GetType() == field.DeclaringType ? field : ResolveField(t.GetType(), field.Name);
            if (f == null) continue;
            try { f.SetValue(t, value); } catch { continue; }
            config.OnFieldChanged?.Invoke(t);
        }
        rootOnChange?.Invoke(targets[0]);
    }

    /// <summary>The serializable fields shared (by name and type) across every target.</summary>
    public static FieldInfo[] CommonSerializableFields(IReadOnlyList<object> targets)
    {
        var rep = GetSerializableFields(targets[0].GetType());
        if (targets.Count == 1) return rep;

        var result = new List<FieldInfo>(rep.Length);
        foreach (var f in rep)
        {
            bool inAll = true;
            for (int i = 1; i < targets.Count; i++)
            {
                if (targets[i] == null) { inAll = false; break; }
                var tf = ResolveField(targets[i].GetType(), f.Name);
                if (tf == null || tf.FieldType != f.FieldType) { inAll = false; break; }
            }
            if (inAll) result.Add(f);
        }
        return result.ToArray();
    }

    private static FieldInfo? ResolveField(Type type, string name)
    {
        foreach (var f in GetSerializableFields(type))
            if (f.Name == name) return f;
        return null;
    }

    private static bool IsFieldMixed(IReadOnlyList<object> targets, FieldInfo repField)
    {
        object? first = null;
        bool got = false;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null) continue;
            var f = ResolveField(t.GetType(), repField.Name);
            if (f == null) continue;
            var v = f.GetValue(t);
            if (!got) { first = v; got = true; }
            else if (!ValuesEqual(first, v)) return true;
        }
        return false;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Equals(b);
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

    // ── Numeric drag helpers ────────────────────────────────

    private static readonly HashSet<Type> s_numericTypes = new()
    {
        typeof(float), typeof(double), typeof(decimal),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
    };

    private static bool IsNumericType(Type type) => s_numericTypes.Contains(type);

    private static double ConvertToDouble(object? value)
    {
        if (value == null) return 0;
        try { return Convert.ToDouble(value); }
        catch { return 0; }
    }

    private static object? ConvertFromDouble(double value, Type targetType)
    {
        try
        {
            if (targetType == typeof(float)) return (float)value;
            if (targetType == typeof(double)) return value;
            if (targetType == typeof(int)) return (int)Math.Round(value);
            if (targetType == typeof(uint)) return (uint)Math.Max(0, Math.Round(value));
            if (targetType == typeof(long)) return (long)Math.Round(value);
            if (targetType == typeof(ulong)) return (ulong)Math.Max(0, Math.Round(value));
            if (targetType == typeof(short)) return (short)Math.Round(value);
            if (targetType == typeof(ushort)) return (ushort)Math.Max(0, Math.Round(value));
            if (targetType == typeof(byte)) return (byte)Math.Clamp(Math.Round(value), 0, 255);
            if (targetType == typeof(sbyte)) return (sbyte)Math.Clamp(Math.Round(value), -128, 127);
            if (targetType == typeof(decimal)) return (decimal)value;
            return Convert.ChangeType(value, targetType);
        }
        catch { return null; }
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
