using System;
using System.Reflection;

using Prowl.Editor.Utils;
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
    private static readonly EditorTypeRegistry<PropertyEditor> _reg = new(
        "PropertyEditorRegistry",
        t => t.GetCustomAttribute<CustomPropertyEditorAttribute>()?.TargetType,
        checkInterfaces: true);

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() => _reg.Reinitialize();

    [Runtime.OnAssemblyUnload]
    public static void ClearCache() => _reg.ClearCache();

    public static void Initialize() => _reg.Initialize();

    public static PropertyEditor? GetEditor(Type type) => _reg.GetEditor(type);
}
