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

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace Jitter2;

public partial class World
{
    private struct ConvexHullIntersection
    {
        public JVector[] Left;
        public JVector[] Right;
        public JVector[] ManifoldA;
        public JVector[] ManifoldB;
        public int Lcount;
        public int Rcount;
        public int Mcount;

        public void Init()
        {
            if (Left == null)
            {
                Left = new JVector[6];
                Right = new JVector[6];
                ManifoldA = new JVector[6];
                ManifoldB = new JVector[6];
            }

            Lcount = 0;
            Rcount = 0;
            Mcount = 0;
        }

        public void PushLeft(in JVector v)
        {
            // for(int i = 0;i< lcount;i++)
            // {
            //     if((left[i] - v).LengthSquared() < 0.001f) return;
            // }
            const float Epsilon = 0.001f;

            if (Lcount > 0)
            {
                if ((Left[0] - v).LengthSquared() < Epsilon) return;
            }

            if (Lcount > 1)
            {
                if ((Left[Lcount - 1] - v).LengthSquared() < Epsilon) return;
            }

            Left[Lcount++] = v;
        }

        public void PushRight(in JVector v)
        {
            // for(int i = 0;i< rcount;i++)
            // {
            //     if((right[i] - v).LengthSquared() < 0.001f) return;
            // }
            const float Epsilon = 0.001f;

            if (Rcount > 0)
            {
                if ((Right[0] - v).LengthSquared() < Epsilon) return;
            }

            if (Rcount > 1)
            {
                if ((Right[Rcount - 1] - v).LengthSquared() < Epsilon) return;
            }

            Right[Rcount++] = v;
        }

        public void BuildManifold(in JVector pA, in JVector pB, in JVector normal, float pen)
        {
            if (Lcount > 2)
            {
                for (int e = 0; e < Rcount; e++)
                {
                    JVector p = Right[e];
                    JVector a = Left[Lcount - 1];
                    JVector b = Left[0];

                    JVector cr = (b - a) % (p - a);

                    bool sameSign = true;

                    for (int i = 0; i < Lcount - 1; i++)
                    {
                        a = Left[i];
                        b = Left[i + 1];

                        JVector cr2 = (b - a) % (p - a);

                        sameSign = JVector.Dot(cr, cr2) > 1e-3f;
                        if (!sameSign) break;
                    }

                    if (sameSign)
                    {
                        float diff = JVector.Dot(p - pB, normal);
                        ManifoldB[Mcount] = p;
                        ManifoldA[Mcount++] = p - (diff - pen) * normal;
                    }
                }
            }

            // ---
            if (Rcount > 2)
            {
                for (int e = 0; e < Lcount; e++)
                {
                    JVector p = Left[e];
                    JVector a = Right[Rcount - 1];
                    JVector b = Right[0];

                    JVector cr = (b - a) % (p - a);

                    bool sameSign = true;

                    for (int i = 0; i < Rcount - 1; i++)
                    {
                        a = Right[i];
                        b = Right[i + 1];

                        JVector cr2 = (b - a) % (p - a);

                        sameSign = JVector.Dot(cr, cr2) > 1e-3f;
                        if (!sameSign) break;
                    }

                    if (sameSign)
                    {
                        float diff = -JVector.Dot(p - pA, normal);
                        ManifoldA[Mcount] = p;
                        ManifoldB[Mcount++] = p - (diff - pen) * normal;
                    }
                }
            }

            /*
            if(lcount > 2 && rcount > 2)
            {
                for (int e = 0; e < rcount - 1; e++)
                {
                    JVector a = right[e];
                    JVector b = right[e + 1];

                    for (int i = 0; i < lcount - 1; i++)
                    {
                        JVector c = left[i];
                        JVector d = left[i + 1];

                        bool result = MathHelper.LineLineIntersect(a, b, c, d, 0.01f, out JVector ra, out JVector rb, out float mua, out float mub);

                        if(result)
                        {
                            if ((mua > 0 && mua < 1) && (mub > 0 && mub < 1))
                            {
                                manifoldA[mcount] = ra;
                                manifoldB[mcount++] = rb;
                            }
                        }
                    }

                }
            }
            */
        }
    }

    /// <summary>
    /// Specifies an implementation of the <see cref="INarrowPhaseFilter"/> to be used in collision detection.
    /// The default instance is of type <see cref="TriangleEdgeCollisionFilter"/>.
    /// </summary>
    public INarrowPhaseFilter? NarrowPhaseFilter { get; set; } = new TriangleEdgeCollisionFilter();

    /// <summary>
    /// Specifies an implementation of the <see cref="IBroadPhaseFilter"/> to be used in collision detection.
    /// The default value is null.
    /// </summary>
    public IBroadPhaseFilter? BroadPhaseFilter { get; set; }

    private const float Sqrt3Over2 = 0.8660254f;

    private readonly (float X, float Y)[] hexagonVertices =
    {
        (1f, 0f), (0.5f, Sqrt3Over2), (-0.5f, Sqrt3Over2), (-1f, 0f), (-0.5f, -Sqrt3Over2), (0.5f, -Sqrt3Over2)
    };

    /// <summary>
    /// Enables the generation of additional contacts for flat surfaces that are in contact.
    /// Traditionally, the collision system reports the deepest collision point between two objects.
    /// A full contact manifold is then generated over several time steps using contact caching, which
    /// can be unstable. This method attempts to build a fuller or complete contact manifold within a single time step.
    /// </summary>
    public bool EnableAuxiliaryContactPoints { set; get; } = true;

    /// <summary>
    /// A speculative contact slows a body down such that it does not penetrate or tunnel through
    /// an obstacle within one frame. The <see cref="SpeculativeRelaxationFactor"/> scales the
    /// slowdown, ranging from 0 (where the body stops immediately during this frame) to 1 (where the body and the
    /// obstacle just touch after the next velocity integration). A value below 1 is preferred, as the leftover velocity
    /// might be sufficient to trigger another speculative contact in the next frame.
    /// </summary>
    public float SpeculativeRelaxationFactor { get; set; } = 0.9f;

    /// <summary>
    /// Speculative contacts are generated when the velocity towards an obstacle exceeds
    /// the threshold value. To prevent bodies with a diameter of D from tunneling through thin walls, this
    /// threshold should be set to approximately D / timestep, e.g., 100 for a unit cube and a
    /// timestep of 0.01s.
    /// </summary>
    public float SpeculativeVelocityThreshold { get; set; } = 10f;

    public void RegisterContact(ulong id0, ulong id1, RigidBody body0, RigidBody body1,
        in JVector point1, in JVector point2, in JVector normal, float penetration, bool speculative = false)
    {
        GetArbiter(id0, id1, body0, body1, out Arbiter arbiter);

        lock (arbiter)
        {
            memContacts.ResizeLock.EnterReadLock();
            arbiter.Handle.Data.IsSpeculative = speculative;
            arbiter.Handle.Data.AddContact(point1, point2, normal, penetration);
            memContacts.ResizeLock.ExitReadLock();
        }
    }

    [ThreadStatic] private static ConvexHullIntersection cvh;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Detect(Shape sA, Shape sB)
    {
        if (sB.ShapeId < sA.ShapeId)
        {
            (sA, sB) = (sB, sA);
        }

        if (BroadPhaseFilter != null)
        {
            if (!BroadPhaseFilter.Filter(sA, sB))
            {
                return;
            }
        }

        bool colliding;
        Unsafe.SkipInit(out JVector normal);
        Unsafe.SkipInit(out JVector pA);
        Unsafe.SkipInit(out JVector pB);
        float penetration;

        Debug.Assert(sA.RigidBody != sB.RigidBody);

        if (sA.RigidBody == null || sB.RigidBody == null)
        {
            throw new InvalidOperationException(
                "Shapes passed to default narrow phase collision handler are not associated with a rigid body.");
        }

        if (!sA.RigidBody.Data.IsActive && !sB.RigidBody.Data.IsActive) return;
        if (sA.RigidBody.Data.IsStatic && sB.RigidBody.Data.IsStatic) return;

        ref RigidBodyData b1 = ref sA.RigidBody.Data;
        ref RigidBodyData b2 = ref sB.RigidBody.Data;

        bool speculative = sA.RigidBody.EnableSpeculativeContacts || sB.RigidBody.EnableSpeculativeContacts;

        if (UseFullEPASolver || speculative)
        {
            bool success = NarrowPhase.GJKEPA(sA, sB, b1.Orientation, b2.Orientation, b1.Position, b2.Position,
                out pA, out pB, out normal, out penetration);

            if (!success) return;

            colliding = penetration >= 0.0f;
        }
        else
        {
            colliding = NarrowPhase.MPREPA(sA, sB, b1.Orientation, b2.Orientation, b1.Position, b2.Position,
                out pA, out pB, out normal, out penetration);
        }

        Debug.Assert(!float.IsNaN(normal.X));

        if (!colliding)
        {
            if (speculative)
            {
                JVector dv = sB.RigidBody.Velocity - sA.RigidBody.Velocity;
                penetration = normal * (pA - pB) * SpeculativeRelaxationFactor;

                if (NarrowPhaseFilter != null)
                {
                    if (!NarrowPhaseFilter.Filter(sA, sB, ref pA, ref pB, ref normal, ref penetration))
                    {
                        return;
                    }
                }

                float dvn = -normal * dv;

                if (dvn > SpeculativeVelocityThreshold)
                {
                    GetArbiter(sA.ShapeId, sB.ShapeId, sA.RigidBody, sB.RigidBody, out Arbiter arbiter2);

                    lock (arbiter2)
                    {
                        // (see. 1)
                        arbiter2.Handle.Data.IsSpeculative = true;
                        memContacts.ResizeLock.EnterReadLock();
                        arbiter2.Handle.Data.AddContact(pA, pB, normal, penetration);
                        memContacts.ResizeLock.ExitReadLock();
                    }
                }
            }

            return;
        }

        if (NarrowPhaseFilter != null)
        {
            if (!NarrowPhaseFilter.Filter(sA, sB, ref pA, ref pB, ref normal, ref penetration))
            {
                return;
            }
        }

        GetArbiter(sA.ShapeId, sB.ShapeId, sA.RigidBody, sB.RigidBody, out Arbiter arbiter);

        // Auxiliary Flat Surface Contact Points
        //

        cvh.Init();

        if (EnableAuxiliaryContactPoints)
        {
            static void Support(Shape shape, in JVector direction, out JVector v)
            {
                JVector.TransposedTransform(direction, shape.RigidBody!.Data.Orientation, out JVector tmp);
                shape.SupportMap(tmp, out v);
                JVector.Transform(v, shape.RigidBody.Data.Orientation, out v);
                JVector.Add(v, shape.RigidBody.Data.Position, out v);
            }

            JVector crossVector1 = MathHelper.CreateOrthonormal(normal);
            JVector crossVector2 = normal % crossVector1;

            for (int e = 0; e < 6; e++)
            {
                JVector ptNormal = normal + hexagonVertices[e].X * 0.01f * crossVector1 +
                                   hexagonVertices[e].Y * 0.01f * crossVector2;

                Support(sA, ptNormal, out JVector np1);
                cvh.PushLeft(np1);

                ptNormal.Negate();
                Support(sB, ptNormal, out JVector np2);
                cvh.PushRight(np2);
            }

            cvh.BuildManifold(pA, pB, normal, penetration);
        }

        lock (arbiter)
        {
            // Using memContacts.Allocate and arbiter.Handle.Data.AddContact in parallel:
            //
            // 1. GetArbiter may trigger a resize in memContacts, invalidating memory which
            //    might be in use in AddContact. Protect from this by entering the critical
            //    section as a reader using memContacts.ResizeLock.
            //
            // 2. Apart from point (1), memContacts.Allocate(active: true, clear: false) does
            //    not move the memory position of an already existing active element.
            //
            memContacts.ResizeLock.EnterReadLock();

            arbiter.Handle.Data.IsSpeculative = false;

            for (int e = 0; e < cvh.Mcount; e++)
            {
                JVector mfA = cvh.ManifoldA[e];
                JVector mfB = cvh.ManifoldB[e];

                float nd = JVector.Dot(mfA - mfB, normal);
                arbiter.Handle.Data.AddContact(mfA, mfB, normal, nd);
            }

            arbiter.Handle.Data.AddContact(pA, pB, normal, penetration);

            memContacts.ResizeLock.ExitReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetArbiter(ulong id0, ulong id1, RigidBody b0, RigidBody b1, out Arbiter arbiter)
    {
        ArbiterKey arbiterKey = new(id0, id1);

        lock (arbiters)
        {
            ref Arbiter? arb = ref CollectionsMarshal.GetValueRefOrAddDefault(arbiters, arbiterKey, out bool exists);

            if (!exists)
            {
                if (!Arbiter.Pool.TryPop(out arb))
                {
                    arb = new Arbiter();
                }

                deferredArbiters.Push(arb);

                var h = memContacts.Allocate(true, false);
                arb.Handle = h;
                h.Data.Init(b0, b1);
                h.Data.Key = arbiterKey;
                arb.Body1 = b0;
                arb.Body2 = b1;
            }

            Debug.Assert(arb != null && memContacts.IsActive(arb.Handle));
            arbiter = arb;
        }
    }
}