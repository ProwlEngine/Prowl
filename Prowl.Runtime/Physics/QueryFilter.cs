// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// Which colliders a physics query is allowed to hit. Every query overload that used to take a bare
/// <see cref="Runtime.LayerMask"/> takes one of these instead; a LayerMask converts implicitly, so
/// layer-only call sites read the same as before.
/// <para/>
/// The exclusions are what a bare layer mask cannot express: casting from a character or a vehicle
/// almost always has to skip the caster's own colliders, and layers are a blunt instrument for that.
/// </summary>
public struct QueryFilter
{
    /// <summary>Layers the query may hit.</summary>
    public LayerMask LayerMask;

    /// <summary>Skip everything attached to this rigidbody, so a cast cannot hit its own caster.</summary>
    public Rigidbody3D IgnoreRigidbody;

    /// <summary>Skip this one collider.</summary>
    public Collider IgnoreCollider;

    /// <summary>Hits anything on any layer.</summary>
    public static QueryFilter Default => new(LayerMask.Everything);

    public QueryFilter(LayerMask layerMask)
    {
        LayerMask = layerMask;
    }

    /// <summary>This filter, additionally skipping everything attached to <paramref name="rigidbody"/>.</summary>
    public readonly QueryFilter Ignoring(Rigidbody3D rigidbody)
    {
        QueryFilter filter = this;
        filter.IgnoreRigidbody = rigidbody;
        return filter;
    }

    /// <summary>This filter, additionally skipping <paramref name="collider"/>.</summary>
    public readonly QueryFilter Ignoring(Collider collider)
    {
        QueryFilter filter = this;
        filter.IgnoreCollider = collider;
        return filter;
    }

    /// <summary>Whether anything is excluded beyond the layer mask. Lets queries skip the owner lookup.</summary>
    internal readonly bool HasExclusions => IgnoreRigidbody.IsValid() || IgnoreCollider.IsValid();

    public static implicit operator QueryFilter(LayerMask layerMask) => new(layerMask);
}
