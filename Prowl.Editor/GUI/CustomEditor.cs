using System;

using Prowl.PaperUI;

namespace Prowl.Editor.GUI;

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
        GUI.PropertyGridUtils.Draw(paper, id, target);
    }
}

