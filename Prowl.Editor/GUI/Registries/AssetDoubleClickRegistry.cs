using System;

namespace Prowl.Editor;

/// <summary> Specifies that the decorated method handles double-click on assets with the given file extensions. </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AssetDoubleClickHandlerAttribute : Attribute
{
    public string[] Extensions { get; }
    public AssetDoubleClickHandlerAttribute(params string[] extensions) => Extensions = extensions;
}
