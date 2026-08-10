// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Cloning;

/// <summary>
/// Settings for a clone, and the map from source objects to their counterparts in the copy.
/// <para/>
/// A reference to something in the map is rewritten to that thing's counterpart. A reference to
/// anything else is shared with the original.
/// </summary>
public class CloneContext
{
    /// <summary>
    /// When true, fields marked <see cref="CloneFieldFlags.IdentityRelevant"/> are not written, so a
    /// target that already existed keeps the identity it had.
    /// </summary>
    public bool PreserveIdentity { get; init; } = true;

    internal readonly Dictionary<object, object?> Targets = new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<object> TargetSet = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Declares that references to <paramref name="source"/> resolve to <paramref name="target"/>.
    /// Call before cloning to supply a correspondence the cloner could not work out on its own, such
    /// as objects matched by an identifier rather than by position.
    /// </summary>
    public void AddTarget(object source, object target)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));

        SetTarget(source, target);
    }

    /// <summary>
    /// Records a mapping, including one to null. That is distinct from having no mapping: an unmapped
    /// object is shared with the original, one mapped to null becomes null in the copy.
    /// </summary>
    internal void SetTarget(object source, object? target)
    {
        if (target == null)
        {
            Targets[source] = null;
            return;
        }

        if (TargetSet.Add(target))
            Targets[source] = target;
    }

    /// <summary>The counterpart of a source object, if it has one.</summary>
    public bool TryGetTarget(object source, out object? target) => Targets.TryGetValue(source, out target);

    /// <summary>Forgets every mapping, so the context can be used for an unrelated clone.</summary>
    public void ClearTargets()
    {
        Targets.Clear();
        TargetSet.Clear();
    }
}
