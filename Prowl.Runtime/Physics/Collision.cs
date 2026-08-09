// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Describes a contact between this GameObject's <see cref="Rigidbody3D"/> and something else.
/// <para/>
/// Static geometry has no rigidbody of its own - every static collider on a layer shares one body - so
/// <see cref="Rigidbody"/> is null for it and <see cref="Collider"/> is what names the surface. Terrain
/// has neither, and reports both as null.
/// </summary>
public readonly struct Collision
{
    /// <summary>The rigidbody that was hit. Null for static geometry and terrain.</summary>
    public readonly Rigidbody3D Rigidbody;

    /// <summary>The collider that was hit. Null for terrain, which has no collider component per contact.</summary>
    public readonly Collider Collider;

    /// <summary>Contact point in world space. Zero on <see cref="MonoBehaviour.OnCollisionEnd"/>.</summary>
    public readonly Float3 Point;

    /// <summary>Contact normal in world space. Zero on <see cref="MonoBehaviour.OnCollisionEnd"/>.</summary>
    public readonly Float3 Normal;

    /// <summary>Impulse the solver applied at this contact. Zero on <see cref="MonoBehaviour.OnCollisionEnd"/>.</summary>
    public readonly float ImpulseMagnitude;

    /// <summary>The GameObject that was hit, or null when nothing identifiable was involved.</summary>
    public GameObject GameObject
    {
        get
        {
            if (Rigidbody.IsValid() && Rigidbody.GameObject.IsValid()) return Rigidbody.GameObject;
            if (Collider.IsValid() && Collider.GameObject.IsValid()) return Collider.GameObject;
            return null;
        }
    }

    /// <summary>The Transform of whatever was hit, or null when nothing identifiable was involved.</summary>
    public Transform Transform
    {
        get
        {
            GameObject go = GameObject;
            return go.IsValid() ? go.Transform : null;
        }
    }

    internal Collision(Rigidbody3D rigidbody, Collider collider, Float3 point, Float3 normal, float impulseMagnitude)
    {
        Rigidbody = rigidbody;
        Collider = collider;
        Point = point;
        Normal = normal;
        ImpulseMagnitude = impulseMagnitude;
    }
}
