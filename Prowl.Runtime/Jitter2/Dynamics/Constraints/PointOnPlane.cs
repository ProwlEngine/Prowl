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
/// Constrains a fixed point in the reference frame of one body to a plane that is fixed in
/// the reference frame of another body. This constraint removes one degree of translational
/// freedom if the limit is enforced.
/// </summary>
public unsafe class PointOnPlane : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SliderData
    {
        internal int _internal;

        public delegate*<ref ConstraintData, void> Iterate;
        public delegate*<ref ConstraintData, float, void> PrepareForIteration;

        public JHandle<RigidBodyData> Body1;
        public JHandle<RigidBodyData> Body2;

        public JVector LocalAxis;

        public JVector LocalAnchor1;
        public JVector LocalAnchor2;

        public float BiasFactor;
        public float Softness;

        public float EffectiveMass;
        public float AccumulatedImpulse;
        public float Bias;

        public float Min;
        public float Max;

        public ushort Clamp;

        public MemoryHelper.MemBlock48 J0;
    }

    private JHandle<SliderData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(SliderData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<SliderData>(Handle);
    }

    public void Initialize(JVector axis, JVector anchor1, JVector anchor2)
    {
        Initialize(axis, anchor1, anchor2, LinearLimit.Fixed);
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="axis">Axis fixed in the reference frame of the first body in world space.</param>
    /// <param name="anchor1">Anchor point on the first body. Together with the axis this defines a plane in the reference
    /// frame of body1.</param>
    /// <param name="anchor2">Anchor point on the second body in world space.</param>
    /// <param name="limit">A limit for the distance between the plane and the anchor point on the second body.</param>
    public void Initialize(JVector axis, JVector anchor1, JVector anchor2, LinearLimit limit)
    {
        ref SliderData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        axis.Normalize();

        JVector.Subtract(anchor1, body1.Position, out data.LocalAnchor1);
        JVector.Subtract(anchor2, body2.Position, out data.LocalAnchor2);

        JVector.TransposedTransform(data.LocalAnchor1, body1.Orientation, out data.LocalAnchor1);
        JVector.TransposedTransform(data.LocalAnchor2, body2.Orientation, out data.LocalAnchor2);

        JVector.TransposedTransform(axis, body1.Orientation, out data.LocalAxis);

        data.BiasFactor = 0.01f;
        data.Softness = 0.00001f;

        (data.Min, data.Max) = limit;
    }

    public static void PrepareForIteration(ref ConstraintData constraint, float idt)
    {
        ref SliderData data = ref Unsafe.AsRef<SliderData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector.Transform(data.LocalAxis, body1.Orientation, out JVector axis);

        JVector.Transform(data.LocalAnchor1, body1.Orientation, out JVector R1);
        JVector.Transform(data.LocalAnchor2, body2.Orientation, out JVector R2);

        JVector.Add(body1.Position, R1, out JVector p1);
        JVector.Add(body2.Position, R2, out JVector p2);

        data.Clamp = 0;

        JVector U = p2 - p1;

        var jacobian = new Span<JVector>(Unsafe.AsPointer(ref data.J0), 4);

        jacobian[0] = -axis;
        jacobian[1] = -((R1 + U) % axis);
        jacobian[2] = axis;
        jacobian[3] = R2 % axis;

        float error = JVector.Dot(U, axis);

        data.EffectiveMass = 1.0f;

        if (error > data.Max)
        {
            error -= data.Max;
            data.Clamp = 1;
        }
        else if (error < data.Min)
        {
            error -= data.Min;
            data.Clamp = 2;
        }
        else
        {
            data.AccumulatedImpulse = 0;
            return;
        }

        data.EffectiveMass = body1.InverseMass + body2.InverseMass +
                             JVector.Transform(jacobian[1], body1.InverseInertiaWorld) * jacobian[1] +
                             JVector.Transform(jacobian[3], body2.InverseInertiaWorld) * jacobian[3];

        data.EffectiveMass += (data.Softness * idt);
        data.EffectiveMass = 1.0f / data.EffectiveMass;

        data.Bias = error * data.BiasFactor * idt;

        float acc = data.AccumulatedImpulse;

        body1.Velocity += body1.InverseMass * (jacobian[0] * acc);
        body1.AngularVelocity += JVector.Transform(jacobian[1] * acc, body1.InverseInertiaWorld);

        body2.Velocity += body2.InverseMass * (jacobian[2] * acc);
        body2.AngularVelocity += JVector.Transform(jacobian[3] * acc, body2.InverseInertiaWorld);
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
        ref SliderData data = ref Unsafe.AsRef<SliderData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        if (data.Clamp == 0) return;

        var jacobian = new Span<JVector>(Unsafe.AsPointer(ref data.J0), 4);

        float jv = jacobian[0] * body1.Velocity + jacobian[1] * body1.AngularVelocity + jacobian[2] * body2.Velocity +
                   jacobian[3] * body2.AngularVelocity;

        float softness = data.AccumulatedImpulse * data.Softness * idt;

        float lambda = -1.0f * (jv + data.Bias + softness) * data.EffectiveMass;

        float origAcc = data.AccumulatedImpulse;

        data.AccumulatedImpulse += lambda;

        if (data.Clamp == 1)
        {
            data.AccumulatedImpulse = MathF.Min(data.AccumulatedImpulse, 0.0f);
        }
        else
        {
            data.AccumulatedImpulse = MathF.Max(data.AccumulatedImpulse, 0.0f);
        }

        lambda = data.AccumulatedImpulse - origAcc;

        body1.Velocity += body1.InverseMass * (jacobian[0] * lambda);
        body1.AngularVelocity += JVector.Transform(jacobian[1] * lambda, body1.InverseInertiaWorld);

        body2.Velocity += body2.InverseMass * (jacobian[2] * lambda);
        body2.AngularVelocity += JVector.Transform(jacobian[3] * lambda, body2.InverseInertiaWorld);
    }
}