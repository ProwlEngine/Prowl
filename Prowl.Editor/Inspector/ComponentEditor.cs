using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Attribute to register a custom component editor for a specific MonoBehaviour type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomComponentEditorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomComponentEditorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Base class for custom component editors. Override OnGUI to completely replace
/// the default property grid for a component.
/// Call DrawDefaultInspector() to fall back to the default reflection-based editor.
/// </summary>
public abstract class ComponentEditor
{
    /// <summary>
    /// Draw the custom editor for a component.
    /// </summary>
    public abstract void OnGUI(Paper paper, string id, MonoBehaviour component);

    /// <summary>
    /// Draw the default PropertyGrid-based inspector for a component.
    /// Call this from OnGUI if you want the default + additions.
    /// </summary>
    protected void DrawDefaultInspector(Paper paper, string id, MonoBehaviour component)
    {
        Widgets.PropertyGrid.Draw(paper, id, component);
    }
}

/// <summary>
/// Registry that discovers and maps ComponentEditor subclasses to their target types.
/// </summary>
public static class ComponentEditorRegistry
{
    private static readonly Dictionary<Type, Type> _typeToEditor = new();
    private static readonly Dictionary<Type, ComponentEditor> _editorCache = new();
    private static bool _initialized;

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
                if (!typeof(ComponentEditor).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<CustomComponentEditorAttribute>();
                if (attr == null) continue;
                _typeToEditor[attr.TargetType] = type;
            }
        }

        Runtime.Debug.Log($"ComponentEditorRegistry: {_typeToEditor.Count} custom editors registered.");
    }

    public static ComponentEditor? GetEditor(Type componentType)
    {
        if (_editorCache.TryGetValue(componentType, out var cached))
            return cached;

        if (_typeToEditor.TryGetValue(componentType, out var editorType))
        {
            var editor = (ComponentEditor)Activator.CreateInstance(editorType)!;
            _editorCache[componentType] = editor;
            return editor;
        }

        // Check base types
        Type? baseType = componentType.BaseType;
        while (baseType != null && baseType != typeof(MonoBehaviour))
        {
            if (_typeToEditor.TryGetValue(baseType, out editorType))
            {
                var editor = (ComponentEditor)Activator.CreateInstance(editorType)!;
                _editorCache[componentType] = editor;
                return editor;
            }
            baseType = baseType.BaseType;
        }

        return null;
    }
}
