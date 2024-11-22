// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CustomEditorAttribute : Attribute
{
    public Type Type { get; private set; }
    public bool Inheritable { get; private set; }

    public CustomEditorAttribute(Type type, bool isInheritable = true)
    {
        Type = type;
        Inheritable = isInheritable;
    }
}
