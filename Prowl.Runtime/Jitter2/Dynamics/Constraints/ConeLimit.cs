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
/// Implements the ConeLimit constraint, which restricts the tilt of one body relative to
/// another body.
/// </summary>
public unsafe class ConeLimit : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ConeLimitData
    {
        internal int _internal;
        public delegate*<ref ConstraintData, void> Iterate;
        public delegate*<ref ConstraintData, float, void> PrepareForIteration;

        public JHandle<RigidBodyData> Body1;
        public JHandle<RigidBodyData> Body2;

        public JVector LocalAxis1, LocalAxis2;

        public float BiasFactor;
        public float Softness;

        public float EffectiveMass;
        public float AccumulatedImpulse;
        public float Bias;

        public float LimitLow;
        public float LimitHigh;

        public short Clamp;

        public MemoryHelper.MemBlock48 J0;
    }

    private JHandle<ConeLimitData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(ConeLimitData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<ConeLimitData>(Handle);
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="axis">The axis in world space.</param>
    public void Initialize(JVector axis, AngularLimit limit)
    {
        ref ConeLimitData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        axis.Normalize();

        JVector.TransposedTransform(axis, body1.Orientation, out data.LocalAxis1);
        JVector.TransposedTransform(axis, body2.Orientation, out data.LocalAxis2);

        data.Softness = 0.001f;
        data.BiasFactor = 0.2f;

        float lower = (float)limit.From;
        float upper = (float)limit.To;

        data.LimitLow = MathF.Cos(lower);
        data.LimitHigh = MathF.Cos(upper);
    }

    public JAngle Angle
    {
        get
        {
            ref ConeLimitData data = ref handle.Data;

            ref RigidBodyData body1 = ref data.Body1.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;

            JVector.Transform(data.LocalAxis1, body1.Orientation, out JVector a1);
            JVector.Transform(data.LocalAxis2, body2.Orientation, out JVector a2);

            return (JAngle)MathF.Acos(JVector.Dot(a1, a2));
        }
    }

    public static void PrepareForIteration(ref ConstraintData constraint, float idt)
    {
        ref ConeLimitData data = ref Unsafe.AsRef<ConeLimitData>(Unsafe.AsPointer(ref constraint));

        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector.Transform(data.LocalAxis1, body1.Orientation, out JVector a1);
        JVector.Transform(data.LocalAxis2, body2.Orientation, out JVector a2);

        var jacobian = new Span<JVector>(Unsafe.AsPointer(ref data.J0), 2);

        jacobian[0] = JVector.Cross(a2, a1);
        jacobian[1] = JVector.Cross(a1, a2);

        data.Clamp = 0;

        float error = JVector.Dot(a1, a2);

        if (error < data.LimitHigh)
        {
            data.Clamp = 1;
            error -= data.LimitHigh;
        }
        else if (error > data.LimitLow)
        {
            data.Clamp = 2;
            error -= data.LimitLow;
        }
        else
        {
            data.AccumulatedImpulse = 0.0f;
            return;
        }

        data.EffectiveMass = JVector.Transform(jacobian[0], body1.InverseInertiaWorld) * jacobian[0] +
                             JVector.Transform(jacobian[1], body2.InverseInertiaWorld) * jacobian[1];

        data.EffectiveMass += data.Softness * idt;

        data.EffectiveMass = 1.0f / data.EffectiveMass;

        data.Bias = -error * data.BiasFactor * idt;

        body1.AngularVelocity +=
            JVector.Transform(data.AccumulatedImpulse * jacobian[0], body1.InverseInertiaWorld);

        body2.AngularVelocity +=
            JVector.Transform(data.AccumulatedImpulse * jacobian[1], body2.InverseInertiaWorld);
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
        ref ConeLimitData data = ref Unsafe.AsRef<ConeLimitData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        if (data.Clamp == 0) return;

        var jacobian = new Span<JVector>(Unsafe.AsPointer(ref data.J0), 2);

        float jv =
            body1.AngularVelocity * jacobian[0] +
            body2.AngularVelocity * jacobian[1];

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

        body1.AngularVelocity += JVector.Transform(lambda * jacobian[0], body1.InverseInertiaWorld);
        body2.AngularVelocity += JVector.Transform(lambda * jacobian[1], body2.InverseInertiaWorld);
    }
}