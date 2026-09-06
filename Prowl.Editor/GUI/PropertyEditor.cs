using System;

using Prowl.PaperUI;

namespace Prowl.Editor.GUI;

/// <summary>
/// Attribute to register a custom property editor for a specific type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomPropertyEditorAttribute : Attribute
{
    /// <summary> Gets the type for which this custom property editor is registered. </summary>
    public Type TargetType { get; }
    /// <summary> Initializes a new instance with the type that this editor handles. </summary>
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

