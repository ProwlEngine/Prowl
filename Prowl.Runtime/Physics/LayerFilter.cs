// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Jitter2.Collision;
using Jitter2.Collision.Shapes;

namespace Prowl.Runtime;

public class LayerFilter : IBroadPhaseFilter
{
    private readonly struct Pair : IEquatable<Pair>
    {
        private readonly Rigidbody3D _a, _b;

        public Pair(Rigidbody3D shapeA, Rigidbody3D shapeB)
        {
            this._a = shapeA;
            this._b = shapeB;
        }

        public bool Equals(Pair other)
        {
            return _a.Equals(other._a) && _b.Equals(other._b);
        }

        public override bool Equals(object? obj)
        {
            return obj is Pair other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_a, _b);
        }
    }

    private static readonly HashSet<Pair> _ignore = [];

    internal static void IgnoreCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB)
    {
        if (bodyA.IsNotValid() || bodyB.IsNotValid()) return;
        if (bodyA == bodyB) return;

        if (bodyB.InstanceID < bodyA.InstanceID) (bodyA, bodyB) = (bodyB, bodyA);
        _ignore.Add(new Pair(bodyA, bodyB));
    }

    internal static void EnableCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB)
    {
        if (bodyA.IsNotValid() || bodyB.IsNotValid()) return;
        if (bodyA == bodyB) return;
        if (bodyB.InstanceID < bodyA.InstanceID) (bodyA, bodyB) = (bodyB, bodyA);
        _ignore.Remove(new Pair(bodyA, bodyB));
    }

    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        if (proxyA is RigidBodyShape rbsA && proxyB is RigidBodyShape rbsB)
        {
            // Things with constraints dont collide against eachother. (TODO: This should be toggleable)
            if (rbsA.RigidBody.Constraints.Any(conn => conn.Body1 == rbsB.RigidBody || conn.Body2 == rbsB.RigidBody))
                return false;
            if (rbsB.RigidBody.Constraints.Any(conn => conn.Body1 == rbsA.RigidBody || conn.Body2 == rbsA.RigidBody))
                return false;

            if (rbsA.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udA ||
                rbsB.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udB)
                return true;

            bool isIgnored = false;
            Rigidbody3D bodyA = udA.Rigidbody;
            Rigidbody3D bodyB = udB.Rigidbody;
            if (bodyA.IsValid() && bodyB.IsValid())
            {
                // Order by InstanceID to match how IgnoreCollisionBetween stores the pair.
                if (bodyB.InstanceID < bodyA.InstanceID) (bodyA, bodyB) = (bodyB, bodyA);
                isIgnored = _ignore.Contains(new Pair(bodyA, bodyB));
            }
            bool canCollide = CollisionMatrix.GetLayerCollision(udA.Layer, udB.Layer);

            return canCollide && !isIgnored;
        }

        // If not both RigidBodyShapes, let other filters handle it (e.g., terrain collision)
        return true;
    }
}
