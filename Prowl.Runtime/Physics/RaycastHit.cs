// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision;
using Jitter2.Collision.Shapes;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Contains information about a raycast hit.
/// </summary>
public struct RaycastHit
{
    /// <summary>
    /// If the ray hit something.
    /// </summary>
    public bool hit;

    /// <summary>
    /// The distance from the ray's origin to the impact point.
    /// </summary>
    public double distance;

    /// <summary>
    /// The normal of the surface the ray hit.
    /// </summary>
    public Double3 normal;

    /// <summary>
    /// The point in world space where the ray hit the collider.
    /// </summary>
    public Double3 point;

    /// <summary>
    /// The Rigidbody3D of the collider that was hit.
    /// </summary>
    public Rigidbody3D rigidbody;

    /// <summary>
    /// The Shape that was hit.
    /// </summary>
    public RigidBodyShape shape;

    /// <summary>
    /// The Transform of the rigidbody that was hit.
    /// </summary>
    public Transform transform;

    internal void SetFromJitterResult(DynamicTree.RayCastResult result, Double3 origin, Double3 direction)
    {
        shape = result.Entity as RigidBodyShape;
        if(shape == null)
        {
            hit = false;
            return;
        }

        var userData = shape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;

        hit = true;
        rigidbody = userData.Rigidbody;
        transform = rigidbody?.GameObject?.Transform;
        normal = new Double3(result.Normal.X, result.Normal.Y, result.Normal.Z);
        distance = result.Lambda;
        point = origin + direction * distance;
    }
}
