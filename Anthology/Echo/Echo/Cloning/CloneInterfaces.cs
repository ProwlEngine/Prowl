// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Cloning;

/// <summary>
/// The setup step of a clone, in which the target graph is created and mapped to the source graph.
/// No values are copied yet.
/// </summary>
public interface ICloneSetup
{
    CloneContext Context { get; }

    /// <summary>
    /// Declares that references to <paramref name="source"/> resolve to <paramref name="target"/>,
    /// without walking into the source.
    /// </summary>
    void AddTarget(object source, object target);

    /// <summary>
    /// Walks an object from the source graph, mapping it onto an existing target where one is given.
    /// <paramref name="behaviorTarget"/> scopes the behaviour override to values assignable to that type.
    /// </summary>
    void HandleObject(object? source, object? target, CloneBehavior behavior = CloneBehavior.Default, Type? behaviorTarget = null);
}

/// <summary>
/// The copy step of a clone, in which values move from source to target. Nothing is created here.
/// </summary>
public interface ICloneOperation
{
    CloneContext Context { get; }

    /// <summary>True when this object belongs to the target graph.</summary>
    bool IsTarget(object? target);

    /// <summary>The target counterpart, or the source itself when it is not part of the copy.</summary>
    object? GetTarget(object? source);

    /// <summary>The target counterpart, or null when it is not part of the copy.</summary>
    object? GetMappedTarget(object? source);

    /// <summary>Copies one object's values onto its counterpart.</summary>
    void HandleObject(object? source, object? target);
}

/// <summary>
/// Implemented by a type that describes its own clone, instead of having every field walked.
/// Children whose creation has requirements beyond allocating an instance are created here.
/// </summary>
public interface ICloneExplicit
{
    /// <summary>Create or claim the target's child objects and map them. Copy nothing.</summary>
    void SetupCloneTargets(object target, ICloneSetup setup);

    /// <summary>Copy values onto the target. Create nothing.</summary>
    void CopyCloneTo(object target, ICloneOperation operation);
}

/// <summary>
/// Clone handling for a type whose structure cannot be reached by copying its fields, such as a
/// dictionary whose buckets depend on hash codes the copy will not share.
/// </summary>
public interface ICloneFormat
{
    bool CanClone(Type type);

    /// <summary>
    /// Produces the target, reusing <paramref name="existingTarget"/> when it is suitable. Anything
    /// that has to be supplied at construction, such as a comparer, is supplied here.
    /// </summary>
    object CreateCloneTarget(object source, object? existingTarget);

    void SetupCloneTargets(object source, object target, ICloneSetup setup);
    void CopyCloneTo(object source, object target, ICloneOperation operation);
}

/// <summary>
/// Clone handling for a type that is immutable and whose contents depend on the mapping, so its
/// target cannot exist until the rest of the graph does.
/// </summary>
public interface ICloneLateFormat : ICloneFormat
{
    /// <summary>
    /// When true, this format is consulted even where the source value is null but the target still
    /// holds one, so the two can be merged rather than the target simply being cleared.
    /// </summary>
    bool RequiresMerge { get; }

    /// <summary>Produces the target once the whole map exists.</summary>
    object? CreateTargetLate(object? source, object? target, ICloneOperation operation);
}

/// <summary>Notified around being cloned.</summary>
public interface ICloneCallbackReceiver
{
    void OnBeforeClone(CloneContext context);
    void OnAfterClone(CloneContext context);
}
