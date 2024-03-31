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
using Jitter2.LinearMath;
using Jitter2.UnmanagedMemory;

namespace Jitter2.Dynamics.Constraints;

/// <summary>
/// Constrains the distance between a fixed point in the reference frame of one body and a fixed
/// point in the reference frame of another body. This constraint removes one translational degree
/// of freedom. For a distance of zero, use the <see cref="BallSocket"/> constraint.
/// </summary>
public unsafe class DistanceLimit : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DistanceLimitData
    {
        internal int _internal;
        public delegate*<ref ConstraintData, void> Iterate;
        public delegate*<ref ConstraintData, float, void> PrepareForIteration;

        public JHandle<RigidBodyData> Body1;
        public JHandle<RigidBodyData> Body2;

        public JVector LocalAnchor1;
        public JVector LocalAnchor2;

        public float BiasFactor;
        public float Softness;
        public float Distance;

        public float LimitMin;
        public float LimitMax;

        public float EffectiveMass;
        public float AccumulatedImpulse;
        public float Bias;

        public MemoryHelper.MemBlock48 J0;

        public short Clamp;
    }

    private JHandle<DistanceLimitData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(DistanceLimitData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<DistanceLimitData>(Handle);
    }

    public void Initialize(JVector anchor1, JVector anchor2)
    {
        Initialize(anchor1, anchor2, LinearLimit.Fixed);
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="anchor1">Anchor point on the first rigid body, in world space.</param>
    /// <param name="anchor2">Anchor point on the second rigid body, in world space.</param>
    /// <param name="limit">The allowed distance between the anchor points.</param>
    public void Initialize(JVector anchor1, JVector anchor2, LinearLimit limit)
    {
        ref DistanceLimitData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector.Subtract(anchor1, body1.Position, out data.LocalAnchor1);
        JVector.Subtract(anchor2, body2.Position, out data.LocalAnchor2);

        JVector.TransposedTransform(data.LocalAnchor1, body1.Orientation, out data.LocalAnchor1);
        JVector.TransposedTransform(data.LocalAnchor2, body2.Orientation, out data.LocalAnchor2);

        data.Softness = 0.001f;
        data.BiasFactor = 0.2f;
        data.Distance = (anchor2 - anchor1).Length();

        (data.LimitMin, data.LimitMax) = limit;
    }

    public JVector Anchor1
    {
        set
        {
            ref DistanceLimitData data = ref handle.Data;
            ref RigidBodyData body1 = ref data.Body1.Data;
            JVector.Subtract(value, body1.Position, out data.LocalAnchor1);
            JVector.TransposedTransform(data.LocalAnchor1, body1.Orientation, out data.LocalAnchor1);
        }
        get
        {
            ref DistanceLimitData data = ref handle.Data;
            ref RigidBodyData body1 = ref data.Body1.Data;
            JVector.Transform(data.LocalAnchor1, body1.Orientation, out JVector result);
            JVector.Add(result, body1.Position, out result);
            return result;
        }
    }

    public JVector Anchor2
    {
        set
        {
            ref DistanceLimitData data = ref handle.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;
            JVector.Subtract(value, body2.Position, out data.LocalAnchor2);
            JVector.TransposedTransform(data.LocalAnchor2, body2.Orientation, out data.LocalAnchor2);
        }
        get
        {
            ref DistanceLimitData data = ref handle.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;
            JVector.Transform(data.LocalAnchor2, body2.Orientation, out JVector result);
            JVector.Add(result, body2.Position, out result);
            return result;
        }
    }

    public float TargetDistance
    {
        set
        {
            ref DistanceLimitData data = ref handle.Data;
            data.Distance = value;
        }
        get => handle.Data.Distance;
    }

    public float Distance
    {
        get
        {
            ref DistanceLimitData data = ref handle.Data;
            ref RigidBodyData body1 = ref data.Body1.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;

            JVector.Transform(data.LocalAnchor1, body1.Orientation, out JVector r1);
            JVector.Transform(data.LocalAnchor2, body2.Orientation, out JVector r2);

            JVector.Add(body1.Position, r1, out JVector p1);
            JVector.Add(body2.Position, r2, out JVector p2);

            JVector.Subtract(p2, p1, out JVector dp);

            return dp.Length();
        }
    }

    public static void PrepareForIteration(ref ConstraintData constraint, float idt)
    {
        ref DistanceLimitData data = ref Unsafe.AsRef<DistanceLimitData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector.Transform(data.LocalAnchor1, body1.Orientation, out JVector r1);
        JVector.Transform(data.LocalAnchor2, body2.Orientation, out JVector r2);

        JVector.Add(body1.Position, r1, out JVector p1);
        JVector.Add(body2.Position, r2, out JVector p2);

        JVector.Subtract(p2, p1, out JVector dp);

        float error = dp.Length() - data.Distance;

        data.Clamp = 0;

        if (error >= data.LimitMax)
        {
            data.Clamp = 1;
            error -= data.LimitMax;
        }
        else if (error < data.LimitMin)
        {
            data.Clamp = 2;
            error -= data.LimitMin;
        }
        else
        {
            data.AccumulatedImpulse = 0.0f;
            return;
        }

        JVector n = p2 - p1;
        if (n.LengthSquared() != 0.0f) n.Normalize();

        var jacobian = new Span<JVector>(Unsafe.AsPointer(ref data.J0), 4);

        jacobian[0] = -1.0f * n;
        jacobian[1] = -1.0f * (r1 % n);
        jacobian[2] = 1.0f * n;
        jacobian[3] = r2 % n;

        data.EffectiveMass = body1.InverseMass +
                             body2.InverseMass +
                             JVector.Transform(jacobian[1], body1.InverseInertiaWorld) * jacobian[1] +
                             JVector.Transform(jacobian[3], body2.InverseInertiaWorld) * jacobian[3];

        data.EffectiveMass += data.Softness * idt;

        data.EffectiveMass = 1.0f / data.EffectiveMass;

        data.Bias = error * data.BiasFactor * idt;

        body1.Velocity += body1.InverseMass * data.AccumulatedImpulse * jacobian[0];
        body1.AngularVelocity += JVector.Transform(data.AccumulatedImpulse * jacobian[1], body1.InverseInertiaWorld);

        body2.Velocity += body2.InverseMass * data.AccumulatedImpulse * jacobian[2];
        body2.AngularVelocity += JVector.Transform(data.AccumulatedImpulse * jacobian[3], body2.InverseInertiaWorld);
    }

    public float Softness
    {
        get => handle.Data.Softness;
        set => handle.Data.Softness = value;
    }

    public float Bias
    {
        get => handle.Data.BiasFactor;
        set => handle.Data.BiasFactor = value;
    }

    public static void Iterate(ref ConstraintData constraint, float idt)
    {
        ref DistanceLimitData data = ref Unsafe.AsRef<DistanceLimitData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        if (data.Clamp == 0) return;

        var jacobian = new Span<JVector>(Unsafe.AsPointer(ref data.J0), 4);

        float jv =
            body1.Velocity * jacobian[0] +
            body1.AngularVelocity * jacobian[1] +
            body2.Velocity * jacobian[2] +
            body2.AngularVelocity * jacobian[3];

        float softnessScalar = data.AccumulatedImpulse * data.Softness * idt;

        float lambda = -data.EffectiveMass * (jv + data.Bias + softnessScalar);

        float oldacc = data.AccumulatedImpulse;

        data.AccumulatedImpulse += lambda;

        if (data.Clamp == 1)
        {
            data.AccumulatedImpulse = MathF.Min(data.AccumulatedImpulse, 0.0f);
        }
        else
        {
            data.AccumulatedImpulse = MathF.Max(data.AccumulatedImpulse, 0.0f);
        }

        lambda = data.AccumulatedImpulse - oldacc;

        body1.Velocity += body1.InverseMass * lambda * jacobian[0];
        body1.AngularVelocity += JVector.Transform(lambda * jacobian[1], body1.InverseInertiaWorld);

        body2.Velocity += body2.InverseMass * lambda * jacobian[2];
        body2.AngularVelocity += JVector.Transform(lambda * jacobian[3], body2.InverseInertiaWorld);
    }
}