using System;

namespace Prowl.Editor.GUI;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScriptTemplateAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public int Order { get; set; }

    public ScriptTemplateAttribute(string name, string description, string icon)
    {
        Name = name; Description = description; Icon = icon;
    }
}

public sealed class ScriptTemplate
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public int Order { get; }
    public Func<string, string> Generate { get; }

    public ScriptTemplate(string name, string description, string icon, int order, Func<string, string> generate)
    {
        Name = name; Description = description; Icon = icon; Order = order; Generate = generate;
    }
}
