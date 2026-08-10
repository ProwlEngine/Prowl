// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Cloning;

/// <summary>
/// Whether an object reached during a clone is part of the copy or merely referenced by it.
/// </summary>
public enum CloneBehavior
{
    /// <summary>Decide from the type's own attributes, falling back to <see cref="ChildObject"/>.</summary>
    Default,
    /// <summary>Owned elsewhere. The copy shares the same instance.</summary>
    Reference,
    /// <summary>Owned here. The copy gets its own instance.</summary>
    ChildObject
}

/// <summary>
/// Declares how a type, or a value reached through a field, participates in a clone.
/// <para/>
/// On a field, an optional <see cref="TargetType"/> restricts the declaration to values assignable to
/// that type, leaving values of other types to their own behaviour.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Field, AllowMultiple = false)]
public class CloneBehaviorAttribute : Attribute
{
    public Type? TargetType { get; }
    public CloneBehavior Behavior { get; }

    public CloneBehaviorAttribute(CloneBehavior behavior) : this(null, behavior) { }

    public CloneBehaviorAttribute(Type? targetType, CloneBehavior behavior)
    {
        TargetType = targetType;
        Behavior = behavior;
    }
}

[Flags]
public enum CloneFieldFlags
{
    None = 0,
    /// <summary>
    /// Not written when copying onto an existing target, which keeps whatever value it already had.
    /// See <see cref="CloneContext.PreserveIdentity"/>.
    /// </summary>
    IdentityRelevant = 1,
    /// <summary>Never copied.</summary>
    Skip = 2,
    /// <summary>Copied even though it is excluded from serialization.</summary>
    DontSkip = 4
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class CloneFieldAttribute : Attribute
{
    public CloneFieldFlags Flags { get; }
    public CloneFieldAttribute(CloneFieldFlags flags) => Flags = flags;
}

/// <summary>
/// Handled by an <see cref="ICloneExplicit"/> implementation, so the automatic walker skips it.
/// On a type it applies to that type's own declared fields, without inheritance.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ManuallyClonedAttribute : Attribute { }
