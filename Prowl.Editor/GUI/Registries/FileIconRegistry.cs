using System;

namespace Prowl.Editor.GUI.Registries;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class FileIconAttribute : Attribute
{
    public string[] Extensions { get; }
    public FileIconAttribute(params string[] extensions) => Extensions = extensions;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FileIconProviderAttribute : Attribute { }
