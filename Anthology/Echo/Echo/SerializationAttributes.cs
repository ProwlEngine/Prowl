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

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class SerializeIfAttribute : Attribute
{
    public string ConditionMemberName { get; }
    public SerializeIfAttribute(string conditionMemberName) => ConditionMemberName = conditionMemberName;
}

/// <summary>
/// Preserves a member's or type's previous serialized name across a rename.
///
/// On a <b>field</b>: old serialized data stored under <see cref="oldName"/> is read into the field.
/// On a <b>class/struct</b>: old serialized <c>$type</c> data referencing <see cref="oldName"/> (the
/// former type name) resolves to this type, so renaming a serialized type (e.g. a component script)
/// doesn't orphan existing data. The name may be the old namespace-qualified name or the old short name.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class FormerlySerializedAsAttribute : Attribute
{
    public string oldName { get; set; }
    public FormerlySerializedAsAttribute(string name) => oldName = name;
}


/// <summary>
/// Indicates that a type's structure is fixed and will not change,
/// allowing for more efficient ordinal-based serialization.
/// This doesn't require that memory layout is fixed, only that the order of fields is fixed.
/// 
/// This works great for types like Vector3, Quaternion, etc.
/// Since those types are always the same size and layout.
/// It also works great for network packets and other fixed-size structures, where you know the Writer and Reader will always be in sync.
/// 
/// This is highly recommended to be used when possible.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class FixedEchoStructureAttribute : Attribute { }
