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
/// Constrains the relative twist of two bodies. This constraint removes one angular
/// degree of freedom when the limit is enforced.
/// </summary>
public unsafe class TwistAngle : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TwistLimitData
    {
        internal int _internal;
        public delegate*<ref ConstraintData, void> Iterate;
        public delegate*<ref ConstraintData, float, void> PrepareForIteration;

        public JHandle<RigidBodyData> Body1;
        public JHandle<RigidBodyData> Body2;

        public JVector B;

        public JQuaternion Q0;

        public float Angle1, Angle2;
        public ushort Clamp;

        public float BiasFactor;
        public float Softness;

        public float EffectiveMass;
        public float AccumulatedImpulse;
        public float Bias;

        public JVector Jacobian;
    }

    private JHandle<TwistLimitData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(TwistLimitData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<TwistLimitData>(Handle);
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="axis1">Axis fixed in the local reference frame of the first body, represented in world space.</param>
    /// <param name="axis2">Axis fixed in the local reference frame of the second body, represented in world space.</param>
    /// <param name="angle">The permissible relative twist between the bodies along the specified axes.</param>
    public void Initialize(JVector axis1, JVector axis2, AngularLimit angle)
    {
        ref TwistLimitData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        data.Softness = 0.0001f;
        data.BiasFactor = 0.2f;

        axis1.Normalize();
        axis2.Normalize();

        data.Angle1 = MathF.Sin((float)angle.From / 2.0f);
        data.Angle2 = MathF.Sin((float)angle.To / 2.0f);

        data.B = JVector.TransposedTransform(axis2, body2.Orientation);

        JQuaternion q1 = JQuaternion.CreateFromMatrix(body1.Orientation);
        JQuaternion q2 = JQuaternion.CreateFromMatrix(body2.Orientation);

        data.Q0 = q2.Conj() * q1;
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="axis1">Axis fixed in the local reference frame of the first body, defined in world space.</param>
    /// <param name="axis2">Axis fixed in the local reference frame of the second body, defined in world space.</param>
    public void Initialize(JVector axis1, JVector axis2)
    {
        Initialize(axis1, axis2, AngularLimit.Fixed);
    }

    public static void PrepareForIteration(ref ConstraintData constraint, float idt)
    {
        ref TwistLimitData data = ref Unsafe.AsRef<TwistLimitData>(Unsafe.AsPointer(ref constraint));

        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JQuaternion q1 = JQuaternion.CreateFromMatrix(body1.Orientation);
        JQuaternion q2 = JQuaternion.CreateFromMatrix(body2.Orientation);

        JMatrix m = (-1.0f / 2.0f) * QMatrix.ProjectMultiplyLeftRight(data.Q0 * q1.Conj(), q2);

        JQuaternion q = data.Q0 * q1.Conj() * q2;

        data.Jacobian = JVector.TransposedTransform(data.B, m);

        data.EffectiveMass = JVector.Transform(data.Jacobian, body1.InverseInertiaWorld + body2.InverseInertiaWorld) * data.Jacobian;

        data.EffectiveMass += (data.Softness * idt);

        data.EffectiveMass = 1.0f / data.EffectiveMass;

        float error = JVector.Dot(data.B, new JVector(q.X, q.Y, q.Z));

        if (q.W < 0.0f)
        {
            error *= -1.0f;
            data.Jacobian *= -1;
        }

        data.Clamp = 0;

        if (error >= data.Angle2)
        {
            data.Clamp = 1;
            error -= data.Angle2;
        }
        else if (error < data.Angle1)
        {
            data.Clamp = 2;
            error -= data.Angle1;
        }
        else
        {
            data.AccumulatedImpulse = 0.0f;
            return;
        }

        data.Bias = error * data.BiasFactor * idt;

        body1.AngularVelocity += JVector.Transform(data.AccumulatedImpulse * data.Jacobian, body1.InverseInertiaWorld);
        body2.AngularVelocity -= JVector.Transform(data.AccumulatedImpulse * data.Jacobian, body2.InverseInertiaWorld);
    }

    public JAngle Angle
    {
        get
        {
            ref var data = ref handle.Data;
            JQuaternion q1 = JQuaternion.CreateFromMatrix(data.Body1.Data.Orientation);
            JQuaternion q2 = JQuaternion.CreateFromMatrix(data.Body2.Data.Orientation);

            JQuaternion quat0 = data.Q0 * q1.Conj() * q2;

            if (quat0.W < 0.0f)
            {
                quat0 *= -1.0f;
            }

            float error = JVector.Dot(data.B, new JVector(quat0.X, quat0.Y, quat0.Z));
            return (JAngle)(2.0f * MathF.Asin(error));
        }
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

    public override void DebugDraw(IDebugDrawer drawer)
    {
        ref var data = ref handle.Data;

        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;
    }

    public static void Iterate(ref ConstraintData constraint, float idt)
    {
        ref TwistLimitData data = ref Unsafe.AsRef<TwistLimitData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        if (data.Clamp == 0) return;

        float jv = (body1.AngularVelocity - body2.AngularVelocity) * data.Jacobian;

        float softnessScalar = data.AccumulatedImpulse * (data.Softness * idt);

        float lambda = -data.EffectiveMass * (jv + data.Bias + softnessScalar);

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

        body1.AngularVelocity += JVector.Transform(lambda * data.Jacobian, body1.InverseInertiaWorld);
        body2.AngularVelocity -= JVector.Transform(lambda * data.Jacobian, body2.InverseInertiaWorld);
    }
}