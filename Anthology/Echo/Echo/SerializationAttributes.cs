// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class IgnoreOnNullAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class SerializeIgnoreAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class SerializeFieldAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class FormerlySerializedAsAttribute : Attribute
{
    public string oldName { get; set; }
    public FormerlySerializedAsAttribute(string name) => oldName = name;
}


/// <summary>
/// Indicates that a struct's structure is fixed and will not change,
/// allowing for more efficient ordinal-based serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public class FixedStructureAttribute : Attribute { }
