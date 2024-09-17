// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using T = System.AttributeTargets;

namespace Prowl.Runtime;

public enum GuiAttribType { Space, Text, Separator, ShowIf, Tooltip, Button }

public interface InspectorUIAttribute
{
    public GuiAttribType AttribType();
}

[AttributeUsage(T.Field, AllowMultiple = true)]
public class SpaceAttribute : Attribute, InspectorUIAttribute
{
    public GuiAttribType AttribType() => GuiAttribType.Space;
}

[AttributeUsage(T.Field, AllowMultiple = true)]
public class TextAttribute(string text) : Attribute, InspectorUIAttribute
{
    public readonly string text = text;
    public GuiAttribType AttribType() => GuiAttribType.Text;
}

[AttributeUsage(T.Field, AllowMultiple = true)]
public class SeparatorAttribute : Attribute, InspectorUIAttribute
{
    public GuiAttribType AttribType() => GuiAttribType.Separator;
}

[AttributeUsage(T.Field, AllowMultiple = false)]
public class TooltipAttribute(string text) : Attribute, InspectorUIAttribute
{
    public readonly string tooltip = text;
    public GuiAttribType AttribType() => GuiAttribType.Tooltip;
}

[AttributeUsage(T.Field | T.Property, AllowMultiple = false)]
public class ShowIfAttribute(string propertyName, bool inverted = false) : Attribute, InspectorUIAttribute
{
    public readonly string propertyName = propertyName;
    public readonly bool inverted = inverted;
    public GuiAttribType AttribType() => GuiAttribType.ShowIf;
}

[AttributeUsage(T.Method, AllowMultiple = false)]
public class GUIButtonAttribute(string text) : Attribute
{
    public readonly string buttonText = text;
}

[AttributeUsage(T.Field, AllowMultiple = false)]
public class HideInInspectorAttribute : Attribute { }

[AttributeUsage(T.Property, AllowMultiple = false)]
public class ShowInInspectorAttribute : Attribute { }
