// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class IgnoreOnNullAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class SerializeIgnoreAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class SerializeFieldAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class FormerlySerializedAsAttribute : Attribute
{
    public string oldName { get; set; }
    public FormerlySerializedAsAttribute(string name) => oldName = name;
}
