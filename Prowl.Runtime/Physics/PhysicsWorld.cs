// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

public class PhysicsWorld
{
    public World World { get; private set; }

    public Double3 Gravity = new Double3(0, -9.81f, 0);
    public int SolverIterations = 6;
    public int RelaxIterations = 4;
    public int Substep = 1;
    public bool AllowSleep = true;
    public bool UseMultithreading = true;
    public bool AutoSyncTransforms = true;

    public PhysicsWorld()
    {
        World = new World()
        {
            BroadPhaseFilter = new LayerFilter()
        };
    }

    public void Clear()
    {
        World?.Clear();
    }

    public void Update()
    {
        // Configure world settings
        World.SolverIterations = (SolverIterations, RelaxIterations);
        World.SubstepCount = Substep;
        World.AllowDeactivation = AllowSleep;

        World.Gravity = new JVector(Gravity.X, Gravity.Y, Gravity.Z);

        World.Step(Time.fixedDeltaTime, UseMultithreading);
    }

    /// <summary>
    /// Casts a ray against all colliders in this physics world.
    /// </summary>
    public bool Raycast(Double3 origin, Double3 direction)
    {
        direction = direction.Normalized;
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        return World.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out _, out _, out _);
    }

    /// <summary>
    /// Casts a ray against all colliders and returns detailed information about the hit.
    /// </summary>
    public bool Raycast(Double3 origin, Double3 direction, out RaycastHit hitInfo)
    {
        direction = direction.Normalized;
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        hitInfo = new RaycastHit();
        bool hit = World.DynamicTree.RayCast(jOrigin, jDirection,
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
    /// Casts a ray within a maximum distance.
    /// </summary>
    public bool Raycast(Double3 origin, Double3 direction, double maxDistance)
    {
        direction = direction.Normalized;
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        return World.DynamicTree.RayCast(jOrigin, jDirection,
            PreFilter, PostFilter,
            out _, out _, out var dist) && dist <= maxDistance;
    }

    /// <summary>
    /// Casts a ray within a maximum distance and returns detailed information.
    /// </summary>
    public bool Raycast(Double3 origin, Double3 direction, double maxDistance, out RaycastHit hitInfo)
    {
        direction = direction.Normalized;
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        hitInfo = new RaycastHit();
        bool hit = World.DynamicTree.RayCast(jOrigin, jDirection,
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
    /// Casts a ray with layer mask filtering.
    /// </summary>
    public bool Raycast(Double3 origin, Double3 direction, double maxDistance, LayerMask layerMask)
    {
        direction = direction.Normalized;
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        return World.DynamicTree.RayCast(jOrigin, jDirection,
            shape => PreFilterWithLayer(shape, layerMask), PostFilter,
            out _, out _, out double lambda) && lambda <= maxDistance;
    }

    /// <summary>
    /// Casts a ray with layer mask filtering and returns detailed information.
    /// </summary>
    public bool Raycast(Double3 origin, Double3 direction, out RaycastHit hitInfo, double maxDistance, LayerMask layerMask)
    {
        direction = direction.Normalized;
        var jOrigin = new JVector(origin.X, origin.Y, origin.Z);
        var jDirection = new JVector(direction.X, direction.Y, direction.Z);

        hitInfo = new RaycastHit();
        bool hit = World.DynamicTree.RayCast(jOrigin, jDirection,
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
