using System;

namespace Prowl.Ember;

/// <summary>
/// Marks a field, auto property, or type that hot reload should not carry over. The field is left at its
/// default on the replacement instance; a marked type is never migrated and its statics are never walked.
/// On an auto property this also covers the compiler generated backing field.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ReloadIgnoreAttribute : Attribute { }

/// <summary>
/// A field added since the previous build is initialized by the named parameterless instance method instead
/// of by replaying its field initializer. A null name leaves the field at its default deliberately.
/// </summary>
/// <remarks>
/// Initializer methods run once every field of the instance has been populated, so an initializer may read
/// any sibling field regardless of declaration order.
/// </remarks>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ReloadInitializerAttribute : Attribute
{
    public string? MethodName { get; }

    public ReloadInitializerAttribute(string? methodName) => MethodName = methodName;
}
