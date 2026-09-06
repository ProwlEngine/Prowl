using System;

namespace Prowl.Editor.GUI;

/// <summary> Attribute applied to a method that generates a script template. Provides metadata for the template entry displayed in the editor. </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScriptTemplateAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    /// <summary> Gets or sets the sort order of this template relative to others. </summary>
    public int Order { get; set; }

    public ScriptTemplateAttribute(string name, string description, string icon)
    {
        Name = name; Description = description; Icon = icon;
    }
}

/// <summary> Represents a registered script template with its metadata and generation function. </summary>
public sealed class ScriptTemplate
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    /// <summary> Gets the sort order of this template relative to others. </summary>
    public int Order { get; }
    /// <summary> Gets the function that generates the script content from a class name. </summary>
    public Func<string, string> Generate { get; }

    public ScriptTemplate(string name, string description, string icon, int order, Func<string, string> generate)
    {
        Name = name; Description = description; Icon = icon; Order = order; Generate = generate;
    }
}
