using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.PaperUI;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Attribute to register a custom editor for a specific type.
/// Works for any type: MonoBehaviours, ImageEffects, custom data classes, etc.
/// The editor replaces the default PropertyGrid when drawing objects of the target type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomEditorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomEditorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Base class for custom editors. Override OnGUI to completely replace
/// the default property grid for an object of the target type.
/// Call DrawDefaultInspector() to fall back to the default reflection-based editor.
/// </summary>
public abstract class CustomEditor
{
    /// <summary>
    /// Draw the custom editor for an object.
    /// </summary>
    /// <param name="paper">The Paper UI context.</param>
    /// <param name="id">Unique element ID prefix.</param>
    /// <param name="target">The object being edited.</param>
    public abstract void OnGUI(Paper paper, string id, object target);

    /// <summary>
    /// Draw the default PropertyGrid-based inspector for an object.
    /// Call this from OnGUI if you want the default + additions.
    /// </summary>
    protected void DrawDefaultInspector(Paper paper, string id, object target)
    {
        GUI.PropertyGrid.Draw(paper, id, target);
    }
}

/// <summary>
/// Registry that discovers and maps CustomEditor subclasses to their target types.
/// Scans all loaded assemblies for classes with [CustomEditor] attributes.
/// </summary>
public static class CustomEditorRegistry
{
    private static readonly Dictionary<Type, Type> _typeToEditor = new();
    private static readonly Dictionary<Type, CustomEditor> _editorCache = new();
    private static bool _initialized;

    public static void Reinitialize()
    {
        _initialized = false;
        _editorCache.Clear();
        Initialize();
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
                if (!typeof(CustomEditor).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<CustomEditorAttribute>();
                if (attr == null) continue;
                _typeToEditor[attr.TargetType] = type;
            }
        }

        Runtime.Debug.Log($"CustomEditorRegistry: {_typeToEditor.Count} custom editors registered.");
    }

    /// <summary>
    /// Gets a custom editor for the given type, checking the type itself and its base types.
    /// Returns null if no custom editor is registered.
    /// </summary>
    public static CustomEditor? GetEditor(Type targetType)
    {
        if (!_initialized) Initialize();

        if (_editorCache.TryGetValue(targetType, out var cached))
            return cached;

        // Direct match
        if (_typeToEditor.TryGetValue(targetType, out var editorType))
            return CacheEditor(targetType, editorType);

        // Walk base types
        Type? baseType = targetType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_typeToEditor.TryGetValue(baseType, out editorType))
                return CacheEditor(targetType, editorType);
            baseType = baseType.BaseType;
        }

        return null;
    }

    private static CustomEditor CacheEditor(Type targetType, Type editorType)
    {
        var editor = (CustomEditor)Activator.CreateInstance(editorType)!;
        _editorCache[targetType] = editor;
        return editor;
    }
}
