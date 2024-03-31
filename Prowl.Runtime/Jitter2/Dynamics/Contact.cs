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
using Jitter2.LinearMath;
using Jitter2.UnmanagedMemory;

namespace Jitter2.Dynamics;

/// <summary>
/// Holds four <see cref="Contact"/> structs. The <see cref="ContactData.UsageMask"/>
/// indicates which contacts are actually in use. Every shape-to-shape collision in Jitter is managed
/// by one of these structs.
/// </summary>
public struct ContactData
{
#pragma warning disable CS0649
    // Accessed in unsafe code.
    internal int _internal;
#pragma warning restore CS0649

    public int UsageMask;

    public JHandle<RigidBodyData> Body1;
    public JHandle<RigidBodyData> Body2;

    public ArbiterKey Key;

    private float Friction;
    private float Restitution;

    public bool IsSpeculative;

    public Contact Contact0;
    public Contact Contact1;
    public Contact Contact2;
    public Contact Contact3;

    public void PrepareForIteration(float dt)
    {
        if ((UsageMask & 0b0001) != 0) Contact0.PrepareForIteration(ref Body1.Data, ref Body2.Data, dt, IsSpeculative);
        if ((UsageMask & 0b0010) != 0) Contact1.PrepareForIteration(ref Body1.Data, ref Body2.Data, dt, IsSpeculative);
        if ((UsageMask & 0b0100) != 0) Contact2.PrepareForIteration(ref Body1.Data, ref Body2.Data, dt, IsSpeculative);
        if ((UsageMask & 0b1000) != 0) Contact3.PrepareForIteration(ref Body1.Data, ref Body2.Data, dt, IsSpeculative);
    }

    public void Iterate()
    {
        if ((UsageMask & 0b0001) != 0) Contact0.Iterate(ref Body1.Data, ref Body2.Data);
        if ((UsageMask & 0b0010) != 0) Contact1.Iterate(ref Body1.Data, ref Body2.Data);
        if ((UsageMask & 0b0100) != 0) Contact2.Iterate(ref Body1.Data, ref Body2.Data);
        if ((UsageMask & 0b1000) != 0) Contact3.Iterate(ref Body1.Data, ref Body2.Data);
    }

    public int UpdatePosition()
    {
        int delc = 0;
        if ((UsageMask & 0b0001) != 0)
        {
            if (!Contact0.UpdatePosition(ref Body1.Data, ref Body2.Data))
            {
                delc++;
                UsageMask &= ~(1 << 0);
            }
        }

        if ((UsageMask & 0b0010) != 0)
        {
            if (!Contact1.UpdatePosition(ref Body1.Data, ref Body2.Data))
            {
                delc++;
                UsageMask &= ~(1 << 1);
            }
        }

        if ((UsageMask & 0b0100) != 0)
        {
            if (!Contact2.UpdatePosition(ref Body1.Data, ref Body2.Data))
            {
                delc++;
                UsageMask &= ~(1 << 2);
            }
        }

        if ((UsageMask & 0b1000) != 0)
        {
            if (!Contact3.UpdatePosition(ref Body1.Data, ref Body2.Data))
            {
                delc++;
                UsageMask &= ~(1 << 3);
            }
        }

        return delc;
    }

    public void Init(RigidBody body1, RigidBody body2)
    {
        Body1 = body1.handle;
        Body2 = body2.handle;

        Friction = MathF.Max(body1.Friction, body2.Friction);
        Restitution = MathF.Max(body1.Restitution, body2.Restitution);

        UsageMask = 0;
    }

    // ---------------------------------------------------------------------------------------------------------
    //
    // The following contact caching code is heavily influenced / a direct copy of the great
    // Bullet Physics Engine:
    //
    // Bullet Continuous Collision Detection and Physics Library Copyright (c) 2003-2006 Erwin
    // Coumans  https://bulletphysics.org

    // This software is provided 'as-is', without any express or implied warranty. In no event will
    // the authors be held liable for any damages arising from the use of this software. Permission
    // is granted to anyone to use this software for any purpose, including commercial applications,
    // and to alter it and redistribute it freely, subject to the following restrictions:

    // 1. The origin of this software must not be misrepresented; you must not claim that you wrote
    //    the original software. If you use this software in a product, an acknowledgment in the
    //    product documentation would be appreciated but is not required.
    // 2. Altered source versions must be plainly marked as such, and must not be misrepresented as
    //    being the original software.
    // 3. This notice may not be removed or altered from any source distribution.
    //
    // https://github.com/bulletphysics/bullet3/blob/39b8de74df93721add193e5b3d9ebee579faebf8/
    // src/Bullet3OpenCL/NarrowphaseCollision/b3ContactCache.cpp

    /// <summary>
    /// Adds a new collision result to the contact manifold. Keeps at most four points.
    /// </summary>
    public void AddContact(in JVector point1, in JVector point2, in JVector normal, float penetration)
    {
        // if (GetCacheEntry(point1, point2, normal, penetration)) return;
        if (UsageMask == 0b1111)
        {
            // All four contacts are in use. Find one candidate to be replaced by the new one.
            SortCachedPoints(point1, point2, normal, penetration);
            return;
        }

        // Not all contacts are in use, but the new contact point is close enough
        // to an already existing point. Replace this point by the new one.

        // Neither of the above.
        if ((UsageMask & 0b0001) == 0)
        {
            Contact0.Initialize(ref Body1.Data, ref Body2.Data, point1, point2, normal, penetration, true, Restitution,
                Friction);
            UsageMask |= 1 << 0;
        }
        else if ((UsageMask & 0b0010) == 0)
        {
            Contact1.Initialize(ref Body1.Data, ref Body2.Data, point1, point2, normal, penetration, true, Restitution,
                Friction);
            UsageMask |= 1 << 1;
        }
        else if ((UsageMask & 0b0100) == 0)
        {
            Contact2.Initialize(ref Body1.Data, ref Body2.Data, point1, point2, normal, penetration, true, Restitution,
                Friction);
            UsageMask |= 1 << 2;
        }
        else if ((UsageMask & 0b1000) == 0)
        {
            Contact3.Initialize(ref Body1.Data, ref Body2.Data, point1, point2, normal, penetration, true, Restitution,
                Friction);
            UsageMask |= 1 << 3;
        }
    }

    private static float CalcArea4Points(in JVector p0, in JVector p1, in JVector p2, in JVector p3)
    {
        JVector a0 = p0 - p1;
        JVector a1 = p0 - p2;
        JVector a2 = p0 - p3;
        JVector b0 = p2 - p3;
        JVector b1 = p1 - p3;
        JVector b2 = p1 - p2;

        JVector tmp0 = a0 % b0;
        JVector tmp1 = a1 % b1;
        JVector tmp2 = a2 % b2;

        return MathF.Max(MathF.Max(tmp0.LengthSquared(), tmp1.LengthSquared()), tmp2.LengthSquared());
    }

    private void SortCachedPoints(in JVector point1, in JVector point2, in JVector normal, float penetration)
    {
        JVector.Subtract(point1, Body1.Data.Position, out JVector rp1);

        // calculate 4 possible cases areas, and take biggest area
        // int maxPenetrationIndex = -1;
        // float maxPenetration = penetration;

        // always prefer the new point
        const float epsilon = -0.0001f;

        float biggestArea = 0;

        ref Contact cref = ref Contact0;
        int index = -1;

        // if (maxPenetrationIndex != 0)
        {
            float clsq = CalcArea4Points(rp1,
                Contact1.RelativePos1,
                Contact2.RelativePos1,
                Contact3.RelativePos1);

            if (clsq > biggestArea + epsilon)
            {
                biggestArea = clsq;
                cref = ref Contact0;
                index = 0;
            }
        }

        // if (maxPenetrationIndex != 1)
        {
            float clsq = CalcArea4Points(rp1,
                Contact0.RelativePos1,
                Contact2.RelativePos1,
                Contact3.RelativePos1);

            if (clsq > biggestArea + epsilon)
            {
                biggestArea = clsq;
                cref = ref Contact1;
                index = 1;
            }
        }

        // if (maxPenetrationIndex != 2)
        {
            float clsq = CalcArea4Points(rp1,
                Contact0.RelativePos1,
                Contact1.RelativePos1,
                Contact3.RelativePos1);

            if (clsq > biggestArea + epsilon)
            {
                biggestArea = clsq;
                cref = ref Contact2;
                index = 2;
            }
        }

        // if (maxPenetrationIndex != 3)
        {
            float clsq = CalcArea4Points(rp1,
                Contact0.RelativePos1,
                Contact1.RelativePos1,
                Contact2.RelativePos1);

            if (clsq > biggestArea + epsilon)
            {
                // not necessary: biggestArea = clsq;
                cref = ref Contact3;
                index = 3;
            }
        }

        if (index != -1)
        {
            cref.Initialize(ref Body1.Data, ref Body2.Data, point1, point2, normal, penetration, false, Restitution,
                Friction);
            UsageMask |= 1 << index;
        }
    }

    /*
    private bool GetCacheEntry(in JVector point1, in JVector point2, in JVector normal, float penetration)
    {
        JVector.Subtract(point1, Key1.Data.position, out JVector realRelPos1);

        float shortestDist = Contact.BreakThreshold * Contact.BreakThreshold;

        ref Contact cref = ref Contact0;
        int index = -1;

        if ((UsageMask & 0b0001) != 0)
        {
            float distToManiPoint = (Contact0.relativePos1 - realRelPos1).LengthSquared();
            if (distToManiPoint < shortestDist)
            {
                shortestDist = distToManiPoint;
                cref = ref Contact0;
                index = 0;
            }
        }

        if ((UsageMask & 0b0010) != 0)
        {
            float distToManiPoint = (Contact1.relativePos1 - realRelPos1).LengthSquared();
            if (distToManiPoint < shortestDist)
            {
                shortestDist = distToManiPoint;
                cref = ref Contact1;
                index = 1;
            }
        }

        if ((UsageMask & 0b0100) != 0)
        {
            float distToManiPoint = (Contact2.relativePos1 - realRelPos1).LengthSquared();
            if (distToManiPoint < shortestDist)
            {
                shortestDist = distToManiPoint;
                cref = ref Contact2;
                index = 2;
            }
        }

        if ((UsageMask & 0b1000) != 0)
        {
            float distToManiPoint = (Contact3.relativePos1 - realRelPos1).LengthSquared();
            if (distToManiPoint < shortestDist)
            {
                shortestDist = distToManiPoint;
                cref = ref Contact3;
                index = 3;
            }
        }

        if (index != -1)
        {
            cref.Initialize(ref Key1.Data, ref Key2.Data, point1, point2, normal, penetration, false, Restitution,
                Friction);
            return true;
        }

        return false;
    }
    */

    // ---------------------------------------------------------------------------------------------------------
    public struct Contact
    {
        public const float MaximumBias = 100.0f;
        public const float BiasFactor = 0.2f;
        public const float AllowedPenetration = 0.01f;
        public const float BreakThreshold = 0.02f;

        [Flags]
        public enum Flags
        {
            NewContact = 1 << 1,
            Body1Static = 1 << 2,
            Body2Static = 1 << 3
        }

        public Flags Flag;

        public float Bias;
        public float MassNormal;

        public float AccumulatedTangentImpulse1;
        public float AccumulatedTangentImpulse2;
        public float AccumulatedNormalImpulse;

        public float MassTangent1;
        public float MassTangent2;
        public float MaxTangentImpulse;

        public float Friction;
        public float Penetration;
        public float RestitutionBias;

        internal JVector Normal;
        internal JVector Tangent1;
        internal JVector Tangent2;

        private JVector M_n1;
        private JVector M_t1;
        private JVector M_tt1;

        private JVector M_n2;
        private JVector M_t2;
        private JVector M_tt2;

        internal JVector RealRelPos1;
        internal JVector RealRelPos2;

        public JVector RelativePos1;
        public JVector RelativePos2;

        public void Initialize(ref RigidBodyData b1, ref RigidBodyData b2, in JVector point1, in JVector point2, in JVector n,
            float penetration, bool newContact, float restitution, float friction)
        {
            Normal = n;
            Debug.Assert(Math.Abs(n.LengthSquared() - 1.0f) < 1e-3);

            JVector.Subtract(point1, b1.Position, out RelativePos1);
            JVector.Subtract(point2, b2.Position, out RelativePos2);
            JVector.TransposedTransform(RelativePos1, b1.Orientation, out RealRelPos1);
            JVector.TransposedTransform(RelativePos2, b2.Orientation, out RealRelPos2);

            Penetration = penetration;

            // Material Properties
            if (newContact)
            {
                Flag = Flags.NewContact;
                if (b1.IsStatic || !b1.IsActive) Flag |= Flags.Body1Static;
                if (b2.IsStatic || !b2.IsActive) Flag |= Flags.Body2Static;

                AccumulatedNormalImpulse = 0;
                AccumulatedTangentImpulse1 = 0;
                AccumulatedTangentImpulse2 = 0;

                JVector dv = b2.Velocity + b2.AngularVelocity % RelativePos2;
                dv -= b1.Velocity + b1.AngularVelocity % RelativePos1;

                float relNormalVel = JVector.Dot(dv, Normal);

                // Fake restitution
                if (relNormalVel < -1.0f && (Flag & Flags.NewContact) != 0)
                {
                    Bias = Math.Max(-restitution * relNormalVel, Bias);
                }

                Tangent1 = dv - Normal * relNormalVel;

                float num = Tangent1.LengthSquared();

                if (num > 1e-12f)
                {
                    num = 1.0f / (float)Math.Sqrt(num);
                    Tangent1 *= num;
                }
                else
                {
                    Tangent1 = MathHelper.CreateOrthonormal(Normal);
                }

                Tangent2 = Tangent1 % Normal;

                RestitutionBias = 0.0f;

                // Fake restitution
                if (relNormalVel < -1.0f)
                {
                    RestitutionBias = -restitution * relNormalVel;
                }

                Friction = friction;
            }
        }

        public bool UpdatePosition(ref RigidBodyData b1, ref RigidBodyData b2)
        {
            JVector.Transform(RealRelPos1, b1.Orientation, out RelativePos1);
            JVector.Add(RelativePos1, b1.Position, out JVector p1);

            JVector.Transform(RealRelPos2, b2.Orientation, out RelativePos2);
            JVector.Add(RelativePos2, b2.Position, out JVector p2);

            JVector.Subtract(p1, p2, out JVector dist);

            Penetration = JVector.Dot(dist, Normal);

            if (Penetration < -BreakThreshold * 0.1f)
            {
                return false;
            }

            dist -= Penetration * Normal;
            float tangentialOffsetSq = dist.LengthSquared();

            if (tangentialOffsetSq > BreakThreshold * BreakThreshold)
            {
                return false;
            }

            return true;
        }

        public void PrepareForIteration(ref RigidBodyData b1, ref RigidBodyData b2,
            float idt, bool speculative = false)
        {
            if ((Flag & Flags.NewContact) != 0) Flag = Flags.NewContact;
            else Flag = 0;

            // redundant if contact has just been initialized or updateposition was called
            // before <=> it is redundant the first time it is called in world.step <=> it is
            // redundant if no sub-stepping is used. but it does not seem to slow anything down,
            // since we are memory bound anyway.
            JVector.Transform(RealRelPos1, b1.Orientation, out RelativePos1);
            JVector.Transform(RealRelPos2, b2.Orientation, out RelativePos2);
            JVector.Add(RelativePos1, b1.Position, out JVector p1);
            JVector.Add(RelativePos2, b2.Position, out JVector p2);
            JVector.Subtract(p1, p2, out JVector dist);
            // If this is a speculative contact we should not
            // tinker with the penetration which got scaled
            // by the speculative relaxation factor before.
            if (!speculative)
                Penetration = JVector.Dot(dist, Normal);

            // prepare
            JVector.Cross(RelativePos1, Normal, out JVector tt);
            JVector.Transform(tt, b1.InverseInertiaWorld, out M_n1);

            JVector.Cross(RelativePos2, Normal, out tt);
            JVector.Transform(tt, b2.InverseInertiaWorld, out M_n2);

            // --- tangent
            JVector.Cross(RelativePos1, Tangent1, out tt);
            JVector.Transform(tt, b1.InverseInertiaWorld, out M_t1);

            JVector.Cross(RelativePos2, Tangent1, out tt);
            JVector.Transform(tt, b2.InverseInertiaWorld, out M_t2);

            JVector.Cross(RelativePos1, Tangent2, out tt);
            JVector.Transform(tt, b1.InverseInertiaWorld, out M_tt1);

            JVector.Cross(RelativePos2, Tangent2, out tt);
            JVector.Transform(tt, b2.InverseInertiaWorld, out M_tt2);

            if (speculative)
            {
                M_n1 = M_n2 = JVector.Zero;
                M_t1 = M_t2 = JVector.Zero;
                M_tt1 = M_tt2 = JVector.Zero;
            }

            float kTangent1 = 0.0f;
            float kTangent2 = 0.0f;
            float kNormal = 0.0f;

            kTangent1 += b1.InverseMass;
            kTangent2 += b1.InverseMass;
            kNormal += b1.InverseMass;

            JVector.Cross(M_t1, RelativePos1, out JVector rantra);
            kTangent1 += JVector.Dot(rantra, Tangent1);

            JVector.Cross(M_tt1, RelativePos1, out rantra);
            kTangent2 += JVector.Dot(rantra, Tangent2);

            JVector.Cross(M_n1, RelativePos1, out rantra);
            kNormal += JVector.Dot(rantra, Normal);

            kTangent1 += b2.InverseMass;
            kTangent2 += b2.InverseMass;
            kNormal += b2.InverseMass;

            JVector.Cross(M_t2, RelativePos2, out JVector rbntrb);
            kTangent1 += JVector.Dot(rbntrb, Tangent1);

            JVector.Cross(M_tt2, RelativePos2, out rbntrb);
            kTangent2 += JVector.Dot(rbntrb, Tangent2);

            JVector.Cross(M_n2, RelativePos2, out rbntrb);
            kNormal += JVector.Dot(rbntrb, Normal);

            MassTangent1 = 1.0f / kTangent1;
            MassTangent2 = 1.0f / kTangent2;
            MassNormal = 1.0f / kNormal;

            Bias = RestitutionBias;
            RestitutionBias = 0;

            // Speculative Contacts!
            if (Penetration < -BreakThreshold)
            {
                // no restitution for speculative contacts
                Bias = Penetration * idt;
            }

            if (Penetration > AllowedPenetration)
            {
                Bias = Math.Max(Bias,
                    BiasFactor * idt * Math.Max(0.0f, Penetration - AllowedPenetration));
                Bias = Math.Clamp(Bias, 0.0f, MaximumBias);
            }

            // warmstarting
            JVector impulse = Normal * AccumulatedNormalImpulse + Tangent1 * AccumulatedTangentImpulse1 +
                              Tangent2 * AccumulatedTangentImpulse2;

            b1.Velocity -= impulse * b1.InverseMass;
            b1.AngularVelocity -= AccumulatedNormalImpulse * M_n1 + AccumulatedTangentImpulse1 * M_t1 +
                                  AccumulatedTangentImpulse2 * M_tt1;

            b2.Velocity += impulse * b2.InverseMass;
            b2.AngularVelocity += AccumulatedNormalImpulse * M_n2 + AccumulatedTangentImpulse1 * M_t2 +
                                  AccumulatedTangentImpulse2 * M_tt2;

            Flag &= ~Flags.NewContact;
        }

        public void Iterate(ref RigidBodyData b1, ref RigidBodyData b2)
        {
            JVector dv = b2.Velocity + b2.AngularVelocity % RelativePos2;
            dv -= b1.Velocity + b1.AngularVelocity % RelativePos1;

            float vn = JVector.Dot(Normal, dv);
            float vt1 = JVector.Dot(Tangent1, dv);
            float vt2 = JVector.Dot(Tangent2, dv);

            float normalImpulse = Bias - vn;
            normalImpulse *= MassNormal;

            float oldNormalImpulse = AccumulatedNormalImpulse;
            AccumulatedNormalImpulse = MathF.Max(oldNormalImpulse + normalImpulse, 0.0f);
            normalImpulse = AccumulatedNormalImpulse - oldNormalImpulse;

            float maxTangentImpulse = Friction * AccumulatedNormalImpulse;
            float tangentImpulse1 = MassTangent1 * -vt1;
            float tangentImpulse2 = MassTangent2 * -vt2;

            float oldTangentImpulse = AccumulatedTangentImpulse1;
            AccumulatedTangentImpulse1 = oldTangentImpulse + tangentImpulse1;
            AccumulatedTangentImpulse1 = Math.Clamp(AccumulatedTangentImpulse1, -maxTangentImpulse, maxTangentImpulse);
            tangentImpulse1 = AccumulatedTangentImpulse1 - oldTangentImpulse;

            float oldTangentImpulse2 = AccumulatedTangentImpulse2;
            AccumulatedTangentImpulse2 = oldTangentImpulse2 + tangentImpulse2;
            AccumulatedTangentImpulse2 = Math.Clamp(AccumulatedTangentImpulse2, -maxTangentImpulse, maxTangentImpulse);
            tangentImpulse2 = AccumulatedTangentImpulse2 - oldTangentImpulse2;

            // Apply contact impulse
            JVector impulse = normalImpulse * Normal + tangentImpulse1 * Tangent1 + tangentImpulse2 * Tangent2;

            b1.Velocity -= b1.InverseMass * impulse;
            b1.AngularVelocity -= normalImpulse * M_n1 + tangentImpulse1 * M_t1 + tangentImpulse2 * M_tt1;

            b2.Velocity += b2.InverseMass * impulse;
            b2.AngularVelocity += normalImpulse * M_n2 + tangentImpulse1 * M_t2 + tangentImpulse2 * M_tt2;
        }
    }
}