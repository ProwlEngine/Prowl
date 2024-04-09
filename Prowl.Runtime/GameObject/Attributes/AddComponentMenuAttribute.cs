using System;

namespace Prowl.Runtime;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AddComponentMenuAttribute : Attribute
{
    public string Path { get; }
    public AddComponentMenuAttribute(string path)
    {
        Path = path;
    }
}
