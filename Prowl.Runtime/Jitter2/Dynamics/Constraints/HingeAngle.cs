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
/// Constrains two bodies to only allow rotation around a specified axis, removing two angular degrees of freedom, or three if a limit is enforced.
/// </summary>
public unsafe class HingeAngle : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct HingeAngleData
    {
        internal int _internal;
        public delegate*<ref ConstraintData, void> Iterate;
        public delegate*<ref ConstraintData, float, void> PrepareForIteration;

        public JHandle<RigidBodyData> Body1;
        public JHandle<RigidBodyData> Body2;

        public float MinAngle;
        public float MaxAngle;

        public float BiasFactor;
        public float LimitBias;

        public float LimitSoftness;
        public float Softness;

        public JVector Axis;
        public JQuaternion Q0;

        public JVector AccumulatedImpulse;
        public JVector Bias;

        public JMatrix EffectiveMass;
        public JMatrix Jacobian;

        public ushort Clamp;
    }

    private JHandle<HingeAngleData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(HingeAngleData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<HingeAngleData>(Handle);
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="axis">Axis in world space for which relative angular movement is allowed.</param>
    public void Initialize(JVector axis, AngularLimit angle)
    {
        ref HingeAngleData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        data.Softness = 0.001f;
        data.LimitSoftness = 0.001f;
        data.BiasFactor = 0.2f;
        data.LimitBias = 0.1f;

        data.MinAngle = MathF.Sin((float)angle.From / 2.0f);
        data.MaxAngle = MathF.Sin((float)angle.To / 2.0f);

        data.Axis = JVector.TransposedTransform(axis, body2.Orientation);

        JQuaternion q1 = JQuaternion.CreateFromMatrix(body1.Orientation);
        JQuaternion q2 = JQuaternion.CreateFromMatrix(body2.Orientation);

        data.Q0 = q2.Conj() * q1;
    }

    public AngularLimit Limit
    {
        set
        {
            ref HingeAngleData data = ref handle.Data;
            data.MinAngle = MathF.Sin((float)value.From / 2.0f);
            data.MaxAngle = MathF.Sin((float)value.To / 2.0f);
        }
    }

    public static void PrepareForIteration(ref ConstraintData constraint, float idt)
    {
        ref HingeAngleData data = ref Unsafe.AsRef<HingeAngleData>(Unsafe.AsPointer(ref constraint));

        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JQuaternion q1 = JQuaternion.CreateFromMatrix(body1.Orientation);
        JQuaternion q2 = JQuaternion.CreateFromMatrix(body2.Orientation);

        JVector p0 = MathHelper.CreateOrthonormal(data.Axis);
        JVector p1 = data.Axis % p0;

        JQuaternion quat0 = data.Q0 * q1.Conj() * q2;

        JVector error;
        error.X = JVector.Dot(p0, new JVector(quat0.X, quat0.Y, quat0.Z));
        error.Y = JVector.Dot(p1, new JVector(quat0.X, quat0.Y, quat0.Z));
        error.Z = JVector.Dot(data.Axis, new JVector(quat0.X, quat0.Y, quat0.Z));

        data.Clamp = 0;

        JMatrix m0 = (-1.0f / 2.0f) * QMatrix.ProjectMultiplyLeftRight(data.Q0 * q1.Conj(), q2);

        if (quat0.W < 0.0f)
        {
            error *= -1.0f;
            m0 *= -1.0f;
        }

        data.Jacobian.UnsafeGet(0) = JVector.TransposedTransform(p0, m0);
        data.Jacobian.UnsafeGet(1) = JVector.TransposedTransform(p1, m0);
        data.Jacobian.UnsafeGet(2) = JVector.TransposedTransform(data.Axis, m0);

        data.EffectiveMass = JMatrix.TransposedMultiply(data.Jacobian, JMatrix.Multiply(body1.InverseInertiaWorld + body2.InverseInertiaWorld, data.Jacobian));

        data.EffectiveMass.M11 += data.Softness * idt;
        data.EffectiveMass.M22 += data.Softness * idt;
        data.EffectiveMass.M33 += data.LimitSoftness * idt;

        float maxa = data.MaxAngle;
        float mina = data.MinAngle;

        if (error.Z > maxa)
        {
            data.Clamp = 1;
            error.Z -= maxa;
        }
        else if (error.Z < mina)
        {
            data.Clamp = 2;
            error.Z -= mina;
        }
        else
        {
            data.AccumulatedImpulse.Z = 0;
            data.EffectiveMass.M33 = 1;
            data.EffectiveMass.M31 = data.EffectiveMass.M13 = 0;
            data.EffectiveMass.M32 = data.EffectiveMass.M23 = 0;

            // TODO: do he have to set them to zero here, explicitly?
            //       does this also has to be done in PointOnLine?
            data.Jacobian.M13 = data.Jacobian.M23 = data.Jacobian.M33 = 0;
        }

        JMatrix.Inverse(data.EffectiveMass, out data.EffectiveMass);

        data.Bias = error * idt;
        data.Bias.X *= data.BiasFactor;
        data.Bias.Y *= data.BiasFactor;
        data.Bias.Z *= data.LimitBias;

        body1.AngularVelocity += JVector.Transform(JVector.Transform(data.AccumulatedImpulse, data.Jacobian), body1.InverseInertiaWorld);
        body2.AngularVelocity -= JVector.Transform(JVector.Transform(data.AccumulatedImpulse, data.Jacobian), body2.InverseInertiaWorld);
    }

    public JAngle Angle
    {
        get
        {
            ref HingeAngleData data = ref handle.Data;
            JQuaternion q1 = JQuaternion.CreateFromMatrix(data.Body1.Data.Orientation);
            JQuaternion q2 = JQuaternion.CreateFromMatrix(data.Body2.Data.Orientation);

            JQuaternion quat0 = data.Q0 * q1.Conj() * q2;

            if (quat0.W < 0.0f)
            {
                quat0 *= -1.0f;
            }

            float error = JVector.Dot(data.Axis, new JVector(quat0.X, quat0.Y, quat0.Z));
            return (JAngle)(2.0f * MathF.Asin(error));
        }
    }

    public float Softness
    {
        get => handle.Data.Softness;
        set => handle.Data.Softness = value;
    }

    public float LimitSoftness
    {
        get => handle.Data.LimitSoftness;
        set => handle.Data.LimitSoftness = value;
    }

    public float Bias
    {
        get => handle.Data.BiasFactor;
        set => handle.Data.BiasFactor = value;
    }

    public float LimitBias
    {
        get => handle.Data.LimitBias;
        set => handle.Data.LimitBias = value;
    }

    public static void Iterate(ref ConstraintData constraint, float idt)
    {
        ref HingeAngleData data = ref Unsafe.AsRef<HingeAngleData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        JVector jv = JVector.TransposedTransform(body1.AngularVelocity - body2.AngularVelocity, data.Jacobian);

        JVector softness = data.AccumulatedImpulse * idt;
        softness.X *= data.Softness;
        softness.Y *= data.Softness;
        softness.Z *= data.LimitSoftness;

        JVector lambda = -1.0f * JVector.Transform(jv + data.Bias + softness, data.EffectiveMass);

        JVector origAcc = data.AccumulatedImpulse;

        data.AccumulatedImpulse += lambda;

        if (data.Clamp == 1)
        {
            data.AccumulatedImpulse.Z = MathF.Min(0, data.AccumulatedImpulse.Z);
        }
        else if (data.Clamp == 2)
        {
            data.AccumulatedImpulse.Z = MathF.Max(0, data.AccumulatedImpulse.Z);
        }
        else
        {
            origAcc.Z = 0;
            data.AccumulatedImpulse.Z = 0;
        }

        lambda = data.AccumulatedImpulse - origAcc;

        body1.AngularVelocity += JVector.Transform(JVector.Transform(lambda, data.Jacobian), body1.InverseInertiaWorld);
        body2.AngularVelocity -= JVector.Transform(JVector.Transform(lambda, data.Jacobian), body2.InverseInertiaWorld);
    }
}