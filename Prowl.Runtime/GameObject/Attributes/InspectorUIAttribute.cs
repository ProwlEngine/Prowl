// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using T = System.AttributeTargets;

namespace Prowl.Runtime;

/// <summary>
/// Enumerates the types of GUI attributes available for use in the Prowl Game Engine's inspector.
/// </summary>
public enum GuiAttribType { Space, Text, Separator, ShowIf, Tooltip, Button }

/// <summary>
/// Defines the interface for inspector UI attributes in the Prowl Game Engine.
/// </summary>
public interface InspectorUIAttribute
{
    /// <summary>
    /// Specifies the type of GUI attribute this instance represents.
    /// </summary>
    /// <returns>The GuiAttribType of this attribute.</returns>
    public GuiAttribType AttribType();
}


/// <summary>
/// Adds a text label in the inspector UI.
/// </summary>
[AttributeUsage(T.Field, AllowMultiple = true)]
public class TextAttribute : Attribute, InspectorUIAttribute
{
    /// <summary>
    /// The text to display in the inspector.
    /// </summary>
    public string text;

    /// <summary>
    /// Initializes a new instance of the TextAttribute class.
    /// </summary>
    /// <param name="text">The text to display in the inspector.</param>
    public TextAttribute(string text) => this.text = text;

    public GuiAttribType AttribType() => GuiAttribType.Text;
}

/// <summary>
/// Adds a tooltip to a field in the inspector UI.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class TooltipAttribute : Attribute, InspectorUIAttribute
{
    /// <summary>
    /// The tooltip text to display.
    /// </summary>
    public string tooltip;

    /// <summary>
    /// Initializes a new instance of the TooltipAttribute class.
    /// </summary>
    /// <param name="text">The tooltip text to display.</param>
    public TooltipAttribute(string text) => this.tooltip = text;

    public GuiAttribType AttribType() => GuiAttribType.Tooltip;
}

/// <summary>
/// Conditionally shows a field or property in the inspector based on another property's value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class ShowIfAttribute : Attribute, InspectorUIAttribute
{
    /// <summary>
    /// The name of the property to check.
    /// </summary>
    public string propertyName;

    /// <summary>
    /// If true, inverts the condition logic.
    /// </summary>
    public bool inverted;

    /// <summary>
    /// Initializes a new instance of the ShowIfAttribute class.
    /// </summary>
    /// <param name="propertyName">The name of the property to check.</param>
    /// <param name="inverted">If true, inverts the condition logic.</param>
    public ShowIfAttribute(string propertyName, bool inverted = false)
    {
        this.propertyName = propertyName;
        this.inverted = inverted;
    }

    public GuiAttribType AttribType() => GuiAttribType.ShowIf;
}

/// <summary>
/// Adds a button in the inspector UI that invokes the marked method when clicked.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class GUIButtonAttribute : Attribute
{
    /// <summary>
    /// The text to display on the button.
    /// </summary>
    public string buttonText;

    /// <summary>
    /// Initializes a new instance of the GUIButtonAttribute class.
    /// </summary>
    /// <param name="text">The text to display on the button.</param>
    public GUIButtonAttribute(string text) => this.buttonText = text;
}

/// <summary>
/// Hides a field from being displayed in the inspector.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class HideInInspectorAttribute : Attribute { }

/// <summary>
/// Shows a property in the inspector that would otherwise be hidden.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ShowInInspectorAttribute : Attribute { }

/// <summary>
/// A Numerical Value Clamping Attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class RangeAttribute(double min, double max, bool IsSlider = false) : Attribute
{
    /// <summary>
    /// The minimum value of the field.
    /// </summary>
    public double Min = min;

    /// <summary>
    /// The maximum value of the field.
    /// </summary>
    public double Max = max;

    /// <summary>
    /// Draws a slider for the field in the inspector.
    /// </summary>
    public bool IsSlider = true;
}

/// <summary>
/// A Numerical Value Clamping Attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class ListDrawerAttribute(bool allowReorder = true, bool allowResize = true, bool canCollapse = true) : Attribute
{
    public bool AllowReorder = allowReorder;
    public bool AllowResize = allowResize;
    public bool CanCollapse = canCollapse;
}
