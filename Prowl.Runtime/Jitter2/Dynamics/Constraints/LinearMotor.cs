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
/// A motor constraint that drives relative translational movement along two axes fixed
/// in the reference frames of the bodies.
/// </summary>
public unsafe class LinearMotor : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LinearMotorData
    {
        internal int _internal;
        public delegate*<ref ConstraintData, void> Iterate;
        public delegate*<ref ConstraintData, float, void> PrepareForIteration;

        public JHandle<RigidBodyData> Body1;
        public JHandle<RigidBodyData> Body2;

        public JVector LocalAxis1;
        public JVector LocalAxis2;

        public float Velocity;
        public float MaxForce;
        public float MaxLambda;

        public float EffectiveMass;

        public float AccumulatedImpulse;
    }

    private JHandle<LinearMotorData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(LinearMotorData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<LinearMotorData>(Handle);
    }

    public JVector LocalAxis1
    {
        get => handle.Data.LocalAxis1;
        set => handle.Data.LocalAxis1 = value;
    }

    public JVector LocalAxis2
    {
        get => handle.Data.LocalAxis2;
        set => handle.Data.LocalAxis2 = value;
    }

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="axis1">Axis on the first body in world space.</param>
    /// <param name="axis2">Axis on the second body in world space.</param>
    public void Initialize(JVector axis1, JVector axis2)
    {
        ref LinearMotorData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        axis1.Normalize();
        axis2.Normalize();

        JVector.TransposedTransform(axis1, body1.Orientation, out data.LocalAxis1);
        JVector.TransposedTransform(axis2, body2.Orientation, out data.LocalAxis2);

        data.MaxForce = 0;
        data.Velocity = 0;
    }

    public float TargetVelocity
    {
        get => handle.Data.Velocity;
        set => handle.Data.Velocity = value;
    }

    public float MaximumForce
    {
        get => handle.Data.MaxForce;
        set
        {
            if (value < 0.0f)
            {
                throw new ArgumentException("Maximum force must not be negative.");
            }

            handle.Data.MaxForce = value;
        }
    }

    public static void PrepareForIteration(ref ConstraintData constraint, float idt)
    {
        ref LinearMotorData data = ref Unsafe.AsRef<LinearMotorData>(Unsafe.AsPointer(ref constraint));

        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector.Transform(data.LocalAxis1, body1.Orientation, out JVector j1);
        JVector.Transform(data.LocalAxis2, body2.Orientation, out JVector j2);

        data.EffectiveMass = body1.InverseMass + body2.InverseMass;
        data.EffectiveMass = 1.0f / data.EffectiveMass;
        data.MaxLambda = (1.0f / idt) * data.MaxForce;

        body1.Velocity -= j1 * data.AccumulatedImpulse * body1.InverseMass;
        body2.Velocity += j2 * data.AccumulatedImpulse * body2.InverseMass;
    }

    public override void DebugDraw(IDebugDrawer drawer)
    {
        ref var data = ref handle.Data;

        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;
    }

    public static void Iterate(ref ConstraintData constraint, float idt)
    {
        ref LinearMotorData data = ref Unsafe.AsRef<LinearMotorData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        JVector.Transform(data.LocalAxis1, body1.Orientation, out JVector j1);
        JVector.Transform(data.LocalAxis2, body2.Orientation, out JVector j2);

        float jv = -j1 * body1.Velocity + j2 * body2.Velocity;

        float lambda = -(jv - data.Velocity) * data.EffectiveMass;

        float olda = data.AccumulatedImpulse;

        data.AccumulatedImpulse += lambda;

        data.AccumulatedImpulse = Math.Clamp(data.AccumulatedImpulse, -data.MaxLambda, data.MaxLambda);

        lambda = data.AccumulatedImpulse - olda;

        body1.Velocity -= j1 * lambda * body1.InverseMass;
        body2.Velocity += j2 * lambda * body2.InverseMass;
    }
}