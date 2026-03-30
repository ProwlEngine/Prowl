using System;

namespace Prowl.Editor.Docking;

/// <summary>
/// Register a DockPanel subclass so it appears in the Window menu.
/// The path determines menu placement, e.g. "General/Scene" → Window > General > Scene.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class EditorWindowAttribute : Attribute
{
    public string Path { get; }

    public EditorWindowAttribute(string path)
    {
        Path = path;
    }
}
