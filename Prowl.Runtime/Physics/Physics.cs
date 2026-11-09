// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Jitter2.Collision.Shapes;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Physics queries and collision filtering against <see cref="Resources.SceneInstance.Active"/>, the scene whose code is running.
/// Every call forwards to that scene's <see cref="PhysicsWorld"/>, so it never drifts from the instance API. Main thread only.
/// </summary>
public static class Physics
{
    private static PhysicsWorld World => Resources.SceneInstance.Active.Physics;

    /// <summary>The active scene's gravity.</summary>
    public static Float3 Gravity
    {
        get => World.Gravity;
        set => World.Gravity = value;
    }

    public static void IgnoreCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB) => World.IgnoreCollisionBetween(bodyA, bodyB);
    public static void EnableCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB) => World.EnableCollisionBetween(bodyA, bodyB);
    public static void IgnoreCollisionsBetween(IEnumerable<(Rigidbody3D A, Rigidbody3D B)> pairs) => World.IgnoreCollisionsBetween(pairs);
    public static void EnableCollisionsBetween(IEnumerable<(Rigidbody3D A, Rigidbody3D B)> pairs) => World.EnableCollisionsBetween(pairs);
    public static void ClearIgnoredCollisions() => World.ClearIgnoredCollisions();
    public static bool Raycast(Float3 origin, Float3 direction) => World.Raycast(origin, direction);
    public static bool Raycast(Float3 origin, Float3 direction, float maxDistance) => World.Raycast(origin, direction, maxDistance);
    public static bool Raycast(Float3 origin, Float3 direction, float maxDistance, QueryFilter filter) => World.Raycast(origin, direction, maxDistance, filter);
    public static bool Raycast(Float3 origin, Float3 direction, out RaycastHit hitInfo) => World.Raycast(origin, direction, out hitInfo);
    public static bool Raycast(Float3 origin, Float3 direction, float maxDistance, out RaycastHit hitInfo) => World.Raycast(origin, direction, maxDistance, out hitInfo);
    public static bool Raycast(Float3 origin, Float3 direction, float maxDistance, out RaycastHit hitInfo, QueryFilter filter) => World.Raycast(origin, direction, maxDistance, out hitInfo, filter);
    public static bool Linecast(Float3 from, Float3 to, out RaycastHit hitInfo) => World.Linecast(from, to, out hitInfo);
    public static bool Linecast(Float3 from, Float3 to, out RaycastHit hitInfo, QueryFilter filter) => World.Linecast(from, to, out hitInfo, filter);
    public static bool Linecast(Float3 from, Float3 to) => World.Linecast(from, to);
    public static bool Linecast(Float3 from, Float3 to, QueryFilter filter) => World.Linecast(from, to, filter);
    public static int ShapeCastAll(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, List<ShapeCastHit> hits) => World.ShapeCastAll(shape, orientation, origin, direction, maxDistance, hits);
    public static int ShapeCastAll(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter) => World.ShapeCastAll(shape, orientation, origin, direction, maxDistance, hits, filter);
    public static bool ShapeCast(RigidBodyShape shape, Quaternion orientation, Float3 origin, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter) => World.ShapeCast(shape, orientation, origin, direction, maxDistance, out hitInfo, filter);
    public static bool ShapeCast(RigidBodyShape shape, Float3 origin, Float3 direction, float maxDistance, out ShapeCastHit hitInfo) => World.ShapeCast(shape, origin, direction, maxDistance, out hitInfo);
    public static bool SphereCast(Float3 origin, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo) => World.SphereCast(origin, radius, direction, maxDistance, out hitInfo);
    public static bool SphereCast(Float3 origin, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter) => World.SphereCast(origin, radius, direction, maxDistance, out hitInfo, filter);
    public static int SphereCastAll(Float3 origin, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits) => World.SphereCastAll(origin, radius, direction, maxDistance, hits);
    public static int SphereCastAll(Float3 origin, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter) => World.SphereCastAll(origin, radius, direction, maxDistance, hits, filter);
    public static bool CapsuleCast(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo) => World.CapsuleCast(point1, point2, radius, direction, maxDistance, out hitInfo);
    public static bool CapsuleCast(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter) => World.CapsuleCast(point1, point2, radius, direction, maxDistance, out hitInfo, filter);
    public static int CapsuleCastAll(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits) => World.CapsuleCastAll(point1, point2, radius, direction, maxDistance, hits);
    public static int CapsuleCastAll(Float3 point1, Float3 point2, float radius, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter) => World.CapsuleCastAll(point1, point2, radius, direction, maxDistance, hits, filter);
    public static bool BoxCast(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo) => World.BoxCast(origin, size, orientation, direction, maxDistance, out hitInfo);
    public static bool BoxCast(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter) => World.BoxCast(origin, size, orientation, direction, maxDistance, out hitInfo, filter);
    public static int BoxCastAll(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits) => World.BoxCastAll(origin, size, orientation, direction, maxDistance, hits);
    public static int BoxCastAll(Float3 origin, Float3 size, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter) => World.BoxCastAll(origin, size, orientation, direction, maxDistance, hits, filter);
    public static bool CylinderCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo) => World.CylinderCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo);
    public static bool CylinderCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter) => World.CylinderCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo, filter);
    public static int CylinderCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits) => World.CylinderCastAll(origin, radius, height, orientation, direction, maxDistance, hits);
    public static int CylinderCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter) => World.CylinderCastAll(origin, radius, height, orientation, direction, maxDistance, hits, filter);
    public static bool ConeCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo) => World.ConeCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo);
    public static bool ConeCast(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, out ShapeCastHit hitInfo, QueryFilter filter) => World.ConeCast(origin, radius, height, orientation, direction, maxDistance, out hitInfo, filter);
    public static int ConeCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits) => World.ConeCastAll(origin, radius, height, orientation, direction, maxDistance, hits);
    public static int ConeCastAll(Float3 origin, float radius, float height, Quaternion orientation, Float3 direction, float maxDistance, List<ShapeCastHit> hits, QueryFilter filter) => World.ConeCastAll(origin, radius, height, orientation, direction, maxDistance, hits, filter);
    public static int Overlap(RigidBodyShape shape, Quaternion orientation, Float3 position, List<ShapeCastHit> hits, QueryFilter filter) => World.Overlap(shape, orientation, position, hits, filter);
    public static int Overlap(RigidBodyShape shape, Quaternion orientation, Float3 position, List<ShapeCastHit> hits) => World.Overlap(shape, orientation, position, hits);
    public static int OverlapSphere(Float3 position, float radius, List<ShapeCastHit> hits) => World.OverlapSphere(position, radius, hits);
    public static int OverlapSphere(Float3 position, float radius, List<ShapeCastHit> hits, QueryFilter filter) => World.OverlapSphere(position, radius, hits, filter);
    public static int OverlapCapsule(Float3 point1, Float3 point2, float radius, List<ShapeCastHit> hits) => World.OverlapCapsule(point1, point2, radius, hits);
    public static int OverlapCapsule(Float3 point1, Float3 point2, float radius, List<ShapeCastHit> hits, QueryFilter filter) => World.OverlapCapsule(point1, point2, radius, hits, filter);
    public static int OverlapBox(Float3 position, Float3 size, Quaternion orientation, List<ShapeCastHit> hits) => World.OverlapBox(position, size, orientation, hits);
    public static int OverlapBox(Float3 position, Float3 size, Quaternion orientation, List<ShapeCastHit> hits, QueryFilter filter) => World.OverlapBox(position, size, orientation, hits, filter);
    public static int OverlapCylinder(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits) => World.OverlapCylinder(position, radius, height, orientation, hits);
    public static int OverlapCylinder(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits, QueryFilter filter) => World.OverlapCylinder(position, radius, height, orientation, hits, filter);
    public static int OverlapCone(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits) => World.OverlapCone(position, radius, height, orientation, hits);
    public static int OverlapCone(Float3 position, float radius, float height, Quaternion orientation, List<ShapeCastHit> hits, QueryFilter filter) => World.OverlapCone(position, radius, height, orientation, hits, filter);
    public static bool CheckSphere(Float3 position, float radius) => World.CheckSphere(position, radius);
    public static bool CheckSphere(Float3 position, float radius, QueryFilter filter) => World.CheckSphere(position, radius, filter);
    public static bool CheckCapsule(Float3 point1, Float3 point2, float radius) => World.CheckCapsule(point1, point2, radius);
    public static bool CheckCapsule(Float3 point1, Float3 point2, float radius, QueryFilter filter) => World.CheckCapsule(point1, point2, radius, filter);
    public static bool CheckBox(Float3 position, Float3 size, Quaternion orientation) => World.CheckBox(position, size, orientation);
    public static bool CheckBox(Float3 position, Float3 size, Quaternion orientation, QueryFilter filter) => World.CheckBox(position, size, orientation, filter);
    public static bool CheckCylinder(Float3 position, float radius, float height, Quaternion orientation) => World.CheckCylinder(position, radius, height, orientation);
    public static bool CheckCylinder(Float3 position, float radius, float height, Quaternion orientation, QueryFilter filter) => World.CheckCylinder(position, radius, height, orientation, filter);
    public static bool CheckCone(Float3 position, float radius, float height, Quaternion orientation) => World.CheckCone(position, radius, height, orientation);
    public static bool CheckCone(Float3 position, float radius, float height, Quaternion orientation, QueryFilter filter) => World.CheckCone(position, radius, height, orientation, filter);
}
