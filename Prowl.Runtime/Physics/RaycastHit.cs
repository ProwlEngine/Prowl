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
    public bool Hit;

    /// <summary>
    /// The distance from the ray's origin to the impact point.
    /// </summary>
    public float Distance;

    /// <summary>
    /// The normal of the surface the ray hit.
    /// </summary>
    public Float3 Normal;

    /// <summary>
    /// The point in world space where the ray hit the collider.
    /// </summary>
    public Float3 Point;

    /// <summary>
    /// The Rigidbody3D of the collider that was hit.
    /// </summary>
    public Rigidbody3D Rigidbody;

    /// <summary>
    /// The Shape that was hit.
    /// </summary>
    public RigidBodyShape Shape;

    /// <summary>
    /// The Collider that was hit. Null for hits that no Collider owns (e.g. terrain).
    /// </summary>
    public Collider Collider;

    /// <summary>
    /// The Transform of the rigidbody that was hit, or of the collider itself when the hit belongs to
    /// static geometry (which shares one body per layer and so cannot identify a GameObject).
    /// </summary>
    public Transform Transform;

    internal void SetFromJitterResult(PhysicsWorld world, DynamicTree.RayCastResult result, Float3 origin, Float3 direction)
    {
        Hit = true;
        Normal = new Float3(result.Normal.X, result.Normal.Y, result.Normal.Z);
        Distance = result.Lambda;
        Point = origin + (direction * Distance);

        Shape = result.Entity as RigidBodyShape;
        if (Shape == null)
        {
            // Terrain has no shape or body to report, only the GameObject it was authored on.
            Transform = world.GetTerrainTransform(result.Entity);
            return;
        }

        var userData = Shape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;

        Rigidbody = userData?.Rigidbody;
        Collider = world.GetShapeOwner(Shape);
        Transform = PhysicsWorld.ResolveHitTransform(Rigidbody, Collider);
    }
}
