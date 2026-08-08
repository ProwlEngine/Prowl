// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

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

        public bool IsAlive => _a.IsValid() && _b.IsValid();

        public bool Equals(Pair other) => ReferenceEquals(_a, other._a) && ReferenceEquals(_b, other._b);

        public override bool Equals(object? obj)
        {
            return obj is Pair other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(_a), RuntimeHelpers.GetHashCode(_b));
        }
    }

    private HashSet<Pair> _ignore = [];

    internal void IgnoreCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB)
    {
        if (!TryOrderPair(ref bodyA, ref bodyB)) return;

        HashSet<Pair> next = LiveCopy();
        next.Add(new Pair(bodyA, bodyB));
        Volatile.Write(ref _ignore, next);
    }

    internal void EnableCollisionBetween(Rigidbody3D bodyA, Rigidbody3D bodyB)
    {
        if (!TryOrderPair(ref bodyA, ref bodyB)) return;

        HashSet<Pair> next = LiveCopy();
        next.Remove(new Pair(bodyA, bodyB));
        Volatile.Write(ref _ignore, next);
    }

    internal void ClearIgnoredCollisions() => Volatile.Write(ref _ignore, []);

    private static bool TryOrderPair(ref Rigidbody3D bodyA, ref Rigidbody3D bodyB)
    {
        if (bodyA.IsNotValid() || bodyB.IsNotValid()) return false;
        if (bodyA == bodyB) return false;

        if (bodyB.InstanceID < bodyA.InstanceID) (bodyA, bodyB) = (bodyB, bodyA);
        return true;
    }

    // Rebuilding drops pairs whose bodies have been destroyed, so the set does not pin them forever.
    private HashSet<Pair> LiveCopy()
    {
        HashSet<Pair> copy = [];
        foreach (Pair pair in Volatile.Read(ref _ignore))
            if (pair.IsAlive) copy.Add(pair);

        return copy;
    }

    private static bool AreConstrainedTogether(RigidBody a, RigidBody b)
    {
        if (a.Constraints.Count == 0 || b.Constraints.Count == 0) return false;

        if (b.Constraints.Count < a.Constraints.Count) (a, b) = (b, a);

        foreach (Constraint constraint in a.Constraints)
            if (constraint.Body1 == b || constraint.Body2 == b) return true;

        return false;
    }

    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        if (proxyA is RigidBodyShape rbsA && proxyB is RigidBodyShape rbsB)
        {
            // Things with constraints dont collide against eachother. (TODO: This should be toggleable)
            if (AreConstrainedTogether(rbsA.RigidBody, rbsB.RigidBody))
                return false;

            if (rbsA.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udA ||
                rbsB.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udB)
                return true;

            bool isIgnored = false;
            HashSet<Pair> ignore = Volatile.Read(ref _ignore);
            Rigidbody3D bodyA = udA.Rigidbody;
            Rigidbody3D bodyB = udB.Rigidbody;
            if (ignore.Count > 0 && TryOrderPair(ref bodyA, ref bodyB))
                isIgnored = ignore.Contains(new Pair(bodyA, bodyB));

            bool canCollide = CollisionMatrix.GetLayerCollision(udA.Layer, udB.Layer);

            return canCollide && !isIgnored;
        }

        // If not both RigidBodyShapes, let other filters handle it (e.g., terrain collision)
        return true;
    }
}
