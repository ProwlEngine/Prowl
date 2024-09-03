// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

[AttributeUsage(AttributeTargets.Class)]
public class RequireComponentAttribute : Attribute
{
    public Type[] types { get; }

    public RequireComponentAttribute(params Type[] types) { this.types = types; }
}
