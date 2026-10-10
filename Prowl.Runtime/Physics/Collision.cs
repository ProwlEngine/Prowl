// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Describes a contact between one of this GameObject's colliders and something else. Both sides of a
/// contact receive their own copy, each describing the other side.
/// <para/>
/// Static geometry has no rigidbody of its own, so <see cref="Rigidbody"/> is null for it and
/// <see cref="Collider"/> is what names the surface. Terrain has neither, and only fills in
/// <see cref="GameObject"/>.
/// </summary>
public readonly struct Collision
{
    /// <summary>The rigidbody that was hit. Null for static geometry and terrain.</summary>
    public readonly Rigidbody3D Rigidbody;

    /// <summary>The collider that was hit. Null for terrain.</summary>
    public readonly Collider Collider;

    /// <summary>The collider on this side of the contact. Null for terrain.</summary>
    public readonly Collider ThisCollider;

    /// <summary>The GameObject that was hit: the rigidbody's when there is one, otherwise the collider's or terrain's.</summary>
    public readonly GameObject GameObject;

    /// <summary>Average contact point in world space, weighted by impulse. Zero on <see cref="Component.OnCollisionEnd"/>.</summary>
    public readonly Float3 Point;

    /// <summary>Contact normal in world space, pointing from the other side toward this one. Zero on <see cref="Component.OnCollisionEnd"/>.</summary>
    public readonly Float3 Normal;

    /// <summary>Total normal impulse the solver applied across the contact in the last step. Zero on <see cref="Component.OnCollisionEnd"/>.</summary>
    public readonly float ImpulseMagnitude;

    /// <summary>The Transform of whatever was hit, or null when nothing identifiable was involved.</summary>
    public Transform Transform => GameObject.IsValid() ? GameObject.Transform : null;

    internal Collision(Rigidbody3D rigidbody, Collider collider, Collider thisCollider, GameObject gameObject,
        Float3 point, Float3 normal, float impulseMagnitude)
    {
        Rigidbody = rigidbody.IsValid() ? rigidbody : null;
        Collider = collider.IsValid() ? collider : null;
        ThisCollider = thisCollider.IsValid() ? thisCollider : null;
        GameObject = gameObject.IsValid() ? gameObject : null;
        Point = point;
        Normal = normal;
        ImpulseMagnitude = impulseMagnitude;
    }
}
