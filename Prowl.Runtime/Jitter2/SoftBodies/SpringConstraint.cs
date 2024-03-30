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

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;
using Jitter2.UnmanagedMemory;

namespace Jitter2.SoftBodies;

/// <summary>
/// Constrains the distance between a fixed point in the reference frame of one body and a fixed
/// point in the reference frame of another body. This constraint removes one translational degree
/// of freedom. For a distance of zero, use the <see cref="BallSocket"/> constraint.
/// </summary>
public unsafe class SpringConstraint : Constraint
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SpringData
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

        public float EffectiveMass;
        public float AccumulatedImpulse;
        public float Bias;

        public JVector Jacobian;
    }

    private JHandle<SpringData> handle;

    protected override void Create()
    {
        Trace.Assert(sizeof(SpringData) <= sizeof(ConstraintData));
        iterate = &Iterate;
        prepareForIteration = &PrepareForIteration;
        handle = JHandle<ConstraintData>.AsHandle<SpringData>(Handle);
    }

    public override bool IsSmallConstraint { get; } = sizeof(SpringData) <= SmallConstraintData.ConstraintSize;

    /// <summary>
    /// Initializes the constraint.
    /// </summary>
    /// <param name="anchor1">Anchor point on the first rigid body, in world space.</param>
    /// <param name="anchor2">Anchor point on the second rigid body, in world space.</param>
    public void Initialize(JVector anchor1, JVector anchor2)
    {
        ref SpringData data = ref handle.Data;
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector.Subtract(anchor1, body1.Position, out data.LocalAnchor1);
        JVector.Subtract(anchor2, body2.Position, out data.LocalAnchor2);

        data.Softness = 0.001f;
        data.BiasFactor = 0.2f;
        data.Distance = (anchor2 - anchor1).Length();
    }

    public JVector Anchor1
    {
        set
        {
            ref SpringData data = ref handle.Data;
            ref RigidBodyData body1 = ref data.Body1.Data;
            JVector.Subtract(value, body1.Position, out data.LocalAnchor1);
        }
        get
        {
            ref SpringData data = ref handle.Data;
            ref RigidBodyData body1 = ref data.Body1.Data;
            JVector.Add(data.LocalAnchor1, body1.Position, out JVector result);
            return result;
        }
    }

    public JVector Anchor2
    {
        set
        {
            ref SpringData data = ref handle.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;
            JVector.Subtract(value, body2.Position, out data.LocalAnchor2);
        }
        get
        {
            ref SpringData data = ref handle.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;
            JVector.Add(data.LocalAnchor2, body2.Position, out JVector result);
            return result;
        }
    }

    public float TargetDistance
    {
        set
        {
            ref SpringData data = ref handle.Data;
            data.Distance = value;
        }
        get => handle.Data.Distance;
    }

    public float Distance
    {
        get
        {
            ref SpringData data = ref handle.Data;
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
        ref SpringData data = ref Unsafe.AsRef<SpringData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref data.Body1.Data;
        ref RigidBodyData body2 = ref data.Body2.Data;

        JVector r1 = data.LocalAnchor1;
        JVector r2 = data.LocalAnchor2;

        JVector.Add(body1.Position, r1, out JVector p1);
        JVector.Add(body2.Position, r2, out JVector p2);

        JVector.Subtract(p2, p1, out JVector dp);

        float error = dp.Length() - data.Distance;

        JVector n = p2 - p1;
        if (n.LengthSquared() != 0.0f) n.Normalize();

        data.Jacobian = n;
        data.EffectiveMass = body1.InverseMass + body2.InverseMass;
        data.EffectiveMass += data.Softness * idt;
        data.EffectiveMass = 1.0f / data.EffectiveMass;

        data.Bias = error * data.BiasFactor * idt;

        body1.Velocity -= body1.InverseMass * data.AccumulatedImpulse * data.Jacobian;
        body2.Velocity += body2.InverseMass * data.AccumulatedImpulse * data.Jacobian;
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
        ref SpringData data = ref Unsafe.AsRef<SpringData>(Unsafe.AsPointer(ref constraint));
        ref RigidBodyData body1 = ref constraint.Body1.Data;
        ref RigidBodyData body2 = ref constraint.Body2.Data;

        float jv = (body2.Velocity - body1.Velocity) * data.Jacobian;

        float softnessScalar = data.AccumulatedImpulse * data.Softness * idt;

        float lambda = -data.EffectiveMass * (jv + data.Bias + softnessScalar);

        float oldacc = data.AccumulatedImpulse;
        data.AccumulatedImpulse += lambda;
        lambda = data.AccumulatedImpulse - oldacc;

        body1.Velocity -= body1.InverseMass * lambda * data.Jacobian;
        body2.Velocity += body2.InverseMass * lambda * data.Jacobian;
    }
}