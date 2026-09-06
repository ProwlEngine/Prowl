using System;

namespace Prowl.Editor.GUI.Registries;

/// <summary> Specifies file extensions that a static parameterless method returning a string maps to an icon. The method's return value is used as the icon identifier for each extension. </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class FileIconAttribute : Attribute
{
    /// <summary> Gets the file extensions that this attribute maps to an icon. </summary>
    public string[] Extensions { get; }
    /// <summary> Creates a new FileIconAttribute that maps the specified file extensions to an icon. </summary>
    public FileIconAttribute(params string[] extensions) => Extensions = extensions;
}

/// <summary> Marks a static parameterless void method that is invoked to register file icons at startup. </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FileIconProviderAttribute : Attribute { }
