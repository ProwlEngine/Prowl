using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.PaperUI;

namespace Prowl.Editor.GUI;

/// <summary>
/// Attribute to register a custom property editor for a specific type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomPropertyEditorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomPropertyEditorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Base class for custom property editors. Override OnGUI to draw a custom UI for a type.
/// </summary>
public abstract class PropertyEditor
{
    /// <summary>
    /// Draw the custom editor for a value.
    /// </summary>
    /// <param name="paper">The Paper UI context.</param>
    /// <param name="id">Unique element ID.</param>
    /// <param name="label">Field label.</param>
    /// <param name="value">Current value (may be null).</param>
    /// <param name="onChange">Callback to set the new value.</param>
    /// <param name="depth">Nesting depth for recursive editors.</param>
    public abstract void OnGUI(Paper paper, string id, string label,
        object? value, Action<object?> onChange, int depth);
}

/// <summary>
/// Registry that discovers and maps PropertyEditor subclasses to their target types.
/// </summary>
public static class PropertyEditorRegistry
{
    private static readonly Dictionary<Type, Type> _typeToEditor = new();
    private static readonly Dictionary<Type, PropertyEditor> _editorCache = new();
    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>
    /// Drop all cached <see cref="Type"/> references and editor instances so the script
    /// AssemblyLoadContext can be collected. Caches rebuild on the next <see cref="Initialize"/>.
    /// </summary>
    [Runtime.OnAssemblyUnload]
    public static void ClearCache()
    {
        _initialized = false;
        _typeToEditor.Clear();
        _editorCache.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _typeToEditor.Clear();
        _editorCache.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(PropertyEditor).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<CustomPropertyEditorAttribute>();
                if (attr == null) continue;
                _typeToEditor[attr.TargetType] = type;
            }
        }

        Runtime.Debug.Log($"PropertyEditorRegistry: {_typeToEditor.Count} custom editors registered.");
    }

    /// <summary>
    /// Get a PropertyEditor instance for the given type, or null if none registered.
    /// Checks exact type first, then walks base types and interfaces.
    /// </summary>
    public static PropertyEditor? GetEditor(Type type)
    {
        // Check cache
        if (_editorCache.TryGetValue(type, out var cached))
            return cached;

        // Exact match
        if (_typeToEditor.TryGetValue(type, out var editorType))
            return CacheAndReturn(type, editorType);

        // Walk base types
        Type? baseType = type.BaseType;
        while (baseType != null)
        {
            if (_typeToEditor.TryGetValue(baseType, out editorType))
                return CacheAndReturn(type, editorType);
            baseType = baseType.BaseType;
        }

        // Check interfaces
        foreach (var iface in type.GetInterfaces())
        {
            if (_typeToEditor.TryGetValue(iface, out editorType))
                return CacheAndReturn(type, editorType);
        }

        return null;
    }

    private static PropertyEditor CacheAndReturn(Type valueType, Type editorType)
    {
        var editor = (PropertyEditor)Activator.CreateInstance(editorType)!;
        _editorCache[valueType] = editor;
        return editor;
    }
}
