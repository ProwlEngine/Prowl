// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
