// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public class JitterGizmosDrawer : IDebugDrawer
{
    static JitterGizmosDrawer m_Instance;
    public static JitterGizmosDrawer Instance => m_Instance ??= new();
    public Color color { get; set; } = new Color(0, 255, 0, 128);

    public void DrawCube(in JVector p, in JQuaternion ori, in JVector size)
    {
        Vector3 center = new Vector3(p.X, p.Y, p.Z);
        Quaternion rotation = new Quaternion(ori.X, ori.Y, ori.Z, ori.W);
        Vector3 extents = new Vector3(size.X * 0.501f, size.Y * 0.501f, size.Z * 0.501f);

        Debug.PushMatrix(Matrix4x4.TRS(center, rotation, Vector3.one));
        Debug.DrawCube(center, extents, color);
        Debug.PopMatrix();
    }

    public void DrawPoint(in JVector p)
    {
        Vector3 center = new Vector3(p.X, p.Y, p.Z);
        Debug.DrawSphere(center, 0.1f, color, 8);
    }

    public void DrawSegment(in JVector pA, in JVector pB)
    {
        Vector3 a = new Vector3(pA.X, pA.Y, pA.Z);
        Vector3 b = new Vector3(pB.X, pB.Y, pB.Z);
        Debug.DrawLine(a, b, color);
    }

    public void DrawSphere(in JVector p, in JQuaternion ori, float radius)
    {
        Vector3 center = new Vector3(p.X, p.Y, p.Z);
        Quaternion rotation = new Quaternion(ori.X, ori.Y, ori.Z, ori.W);
        Debug.DrawWireSphere(center, radius, color);
    }

    public void DrawTriangle(in JVector pA, in JVector pB, in JVector pC)
    {
        Vector3 a = new Vector3(pA.X, pA.Y, pA.Z);
        Vector3 b = new Vector3(pB.X, pB.Y, pB.Z);
        Vector3 c = new Vector3(pC.X, pC.Y, pC.Z);
        //Debug.DrawTriangle(a, b, c, color);
        Debug.DrawLine(a, b, color);
        Debug.DrawLine(b, c, color);
        Debug.DrawLine(c, a, color);
    }
}

public class LayerFilter : IBroadPhaseFilter
{
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        if (proxyA is RigidBodyShape rbsA && proxyB is RigidBodyShape rbsB)
        {
            if (rbsA.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udA ||
                rbsB.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udB)
                return true;

            return Physics.GetLayerCollision(udA.Layer, udB.Layer);
        }

        return false;
    }
}

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
    public Vector3 normal;

    /// <summary>
    /// The point in world space where the ray hit the collider.
    /// </summary>
    public Vector3 point;

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

    internal void SetFromJitterResult(DynamicTree.RayCastResult result, Vector3 origin, Vector3 direction)
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
        normal = new Vector3(result.Normal.X, result.Normal.Y, result.Normal.Z);
        distance = result.Lambda;
        point = origin + direction * distance;
    }
}

public static class Physics
{
    static World _world;
    public static World World => _world ??= new()
    {
        BroadPhaseFilter = new LayerFilter()
    };

    private static double timer = 0;

    public static Vector3 Gravity = new Vector3(0, -9.81f, 0);
    public static int SolverIterations = 6;
    public static int RelaxIterations = 4;
    public static int Substep = 1;
    public static int TargetFrameRate = 50;
    public static bool AllowSleep = true;
    public static bool UseMultithreading = true;
    public static bool AutoSyncTransforms = true;

    public static Boolean32Matrix s_collisionMatrix = new(true);

    public static void Clear()
    {
        _world?.Clear();
    }

    public static void Update()
    {
        // Use World once to ensure its created
        World.SolverIterations = (SolverIterations, RelaxIterations);
        _world.SubstepCount = Substep;
        _world.AllowDeactivation = AllowSleep;

        _world.Gravity = new JVector(Gravity.x, Gravity.y, Gravity.z);

        _world.Step(Time.fixedDeltaTime, UseMultithreading);
    }

    /// <summary>
    /// Sets the collision matrix for two layers
    /// </summary>
    public static void SetLayerCollision(int layer1Index, int layer2Index, bool shouldCollide)
    {
        s_collisionMatrix.SetSymmetric(layer1Index, layer2Index, shouldCollide);
    }

    /// <summary>
    /// Makes sure the collision matrix is symmetric (if [a,b] collides, [b,a] should too)
    /// </summary>
    public static void EnsureSymmetric()
    {
        s_collisionMatrix.MakeSymmetric();
    }

    /// <summary>
    /// Sets all collisions for a specific layer
    /// </summary>
    public static void SetLayerCollisions(int layer, bool shouldCollide)
    {
        s_collisionMatrix.SetRow(layer, shouldCollide);
        // Make sure to maintain symmetry
        s_collisionMatrix.SetColumn(layer, shouldCollide);
    }

    /// <summary>
    /// Gets weather two layers should collide
    /// </summary>
    public static bool GetLayerCollision(int layer1, int layer2)
    {
        return s_collisionMatrix[layer1, layer2];
    }

    /// <summary>
    /// Gets all collisions for a specific layer
    /// </summary>
    public static bool[] GetLayerCollisions(int layer)
    {
        return s_collisionMatrix.GetRow(layer);
    }

    /// <summary>
    /// Sets all layers to collide or not collide
    /// </summary>
    public static void SetAllCollisions(bool shouldCollide)
    {
        s_collisionMatrix.SetAll(shouldCollide);
    }

    /// <summary>
    /// Casts a ray against all colliders in the scene.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction)
    {
        direction = direction.normalized;
        var jOrigin = new JVector(origin.x, origin.y, origin.z);
        var jDirection = new JVector(direction.x, direction.y, direction.z);

        return _world.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out _, out _, out _);
    }

    /// <summary>
    /// Casts a ray against all colliders in the scene and returns detailed information about the hit.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo)
    {
        direction = direction.normalized;
        var jOrigin = new JVector(origin.x, origin.y, origin.z);
        var jDirection = new JVector(direction.x, direction.y, direction.z);

        hitInfo = new RaycastHit();
        bool hit = _world.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out double lambda);

        if (hit)
        {
            var result = new DynamicTree.RayCastResult
            {
                Entity = shape,
                Lambda = lambda,
                Normal = normal
            };
            hitInfo.SetFromJitterResult(result, origin, direction);
        }

        return hit;
    }

    /// <summary>
    /// Casts a ray against all colliders in the scene within a maximum distance.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, double maxDistance)
    {
        direction = direction.normalized;
        var jOrigin = new JVector(origin.x, origin.y, origin.z);
        var jDirection = new JVector(direction.x, direction.y, direction.z);

        return _world.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out _, out _, out var dist) && dist <= maxDistance;
    }

    /// <summary>
    /// Casts a ray against all colliders in the scene within a maximum distance and returns detailed information about the hit.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, double maxDistance, out RaycastHit hitInfo)
    {
        direction = direction.normalized;
        var jOrigin = new JVector(origin.x, origin.y, origin.z);
        var jDirection = new JVector(direction.x, direction.y, direction.z);

        hitInfo = new RaycastHit();
        bool hit = _world.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out double lambda) && lambda <= maxDistance;

        if (hit)
        {
            var result = new DynamicTree.RayCastResult
            {
                Entity = shape,
                Lambda = lambda,
                Normal = normal,
            };
            hitInfo.SetFromJitterResult(result, origin, direction);
        }

        return hit;
    }

    /// <summary>
    /// Casts a ray against all colliders in the scene with the specified layer mask.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, double maxDistance, LayerMask layerMask)
    {
        direction = direction.normalized;
        var jOrigin = new JVector(origin.x, origin.y, origin.z);
        var jDirection = new JVector(direction.x, direction.y, direction.z);

        return _world.DynamicTree.RayCast(jOrigin, jDirection,
            shape => PreFilterWithLayer(shape, layerMask), PostFilter,
            out _, out _, out double lambda) && lambda <= maxDistance;
    }

    /// <summary>
    /// Casts a ray against all colliders in the scene with the specified layer mask and returns detailed information about the hit.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, double maxDistance, LayerMask layerMask)
    {
        direction = direction.normalized;
        var jOrigin = new JVector(origin.x, origin.y, origin.z);
        var jDirection = new JVector(direction.x, direction.y, direction.z);

        hitInfo = new RaycastHit();
        bool hit = _world.DynamicTree.RayCast(jOrigin, jDirection,
            shape => PreFilterWithLayer(shape, layerMask), PostFilter,
            out IDynamicTreeProxy shape, out JVector normal, out double lambda) && lambda <= maxDistance;

        if (hit)
        {
            var result = new DynamicTree.RayCastResult
            {
                Entity = shape,
                Lambda = lambda,
                Normal = normal,
            };
            hitInfo.SetFromJitterResult(result, origin, direction);
        }

        return hit;
    }

    private static bool PreFilter(IDynamicTreeProxy proxy)
    {
        return true;
    }

    private static bool PreFilterWithLayer(IDynamicTreeProxy proxy, LayerMask layerMask)
    {
        if (proxy is RigidBodyShape shape)
        {
            if (!PreFilter(proxy)) return false;

            var userData = shape.RigidBody.Tag as Rigidbody3D.RigidBodyUserData;

            return layerMask.HasLayer(userData.Layer);
        }

        return false;
    }

    private static bool PostFilter(DynamicTree.RayCastResult result)
    {
        return true;
    }
}
