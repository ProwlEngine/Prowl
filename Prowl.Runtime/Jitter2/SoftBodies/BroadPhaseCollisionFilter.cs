/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

namespace Jitter2.SoftBodies;

public class BroadPhaseCollisionFilter : IBroadPhaseFilter
{
    private readonly World world;

    public BroadPhaseCollisionFilter(World world)
    {
        this.world = world;
    }

    public bool Filter(Shape shapeA, Shape shapeB)
    {
        if (!world.Shapes.IsActive(shapeA) && !world.Shapes.IsActive(shapeB)) return false;

        ISoftBodyShape? i1 = shapeA as ISoftBodyShape;
        ISoftBodyShape? i2 = shapeB as ISoftBodyShape;

        if (i1 != null && i2 != null)
        {
            bool colliding = NarrowPhase.MPREPA(shapeA, shapeB,
                JMatrix.Identity, JVector.Zero,
                out JVector pA, out JVector pB, out JVector normal, out float penetration);

            if (!colliding) return false;

            var closestA = i1.GetClosest(pA);
            var closestB = i2.GetClosest(pB);

            world.RegisterContact(closestA.RigidBodyId, closestB.RigidBodyId, closestA, closestB,
                pA, pB, normal, penetration);

            return false;
        }

        if (i1 != null)
        {
            bool colliding = NarrowPhase.MPREPA(shapeA, shapeB, shapeB.RigidBody!.Orientation, shapeB.RigidBody.Position,
                out JVector pA, out JVector pB, out JVector normal, out float penetration);

            if (!colliding) return false;

            var closest = i1.GetClosest(pA);

            world.RegisterContact(closest.RigidBodyId, shapeB.RigidBody.RigidBodyId, closest, shapeB.RigidBody,
                pA, pB, normal, penetration);

            return false;
        }

        if (i2 != null)
        {
            bool colliding = NarrowPhase.MPREPA(shapeB, shapeA, shapeA.RigidBody!.Orientation, shapeA.RigidBody.Position,
                out JVector pA, out JVector pB, out JVector normal, out float penetration);

            if (!colliding) return false;

            var closest = i2.GetClosest(pA);

            world.RegisterContact(closest.RigidBodyId, shapeA.RigidBody.RigidBodyId, closest, shapeA.RigidBody,
                pA, pB, normal, penetration);

            return false;
        }

        return true;
    }
}