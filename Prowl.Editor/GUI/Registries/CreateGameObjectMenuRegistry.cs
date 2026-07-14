using System;

namespace Prowl.Editor;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class CreateGameObjectMenuAttribute : Attribute
{
    public string Path { get; }
    public string Icon { get; set; } = "";
    public int Order { get; set; } = 100;
    public bool Separator { get; set; } = false;

    public CreateGameObjectMenuAttribute(string path) => Path = path;
}
