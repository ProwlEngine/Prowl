// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;
using Jitter2.Unmanaged;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Pulls this body toward a target pose relative to the connected body, or to the world without one, with a spring and
/// a damper on its position and another pair on its rotation, each with a limit on how hard it may push. The spring
/// closes the gap to the target pose and the damper matches the target velocity, both solved inside the physics
/// solver, so a drive stays stable however stiff it is set and whatever else is jointed to the body. The position drive
/// acts between this body's origin and the target point on the connected body, so each body is pushed at that point
/// and turned by the push as a real joint would be. Directions a body cannot move or turn in are left out of the drive.
/// </summary>
[AddComponentMenu("Physics/Constraints/Drive")]
public class DriveConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 targetPosition;
    [SerializeField] private Quaternion targetRotation = Quaternion.Identity;
    [SerializeField] private Float3 targetVelocity;
    [SerializeField] private Float3 targetAngularVelocity;
    [SerializeField] private float positionSpring = 1000f;
    [SerializeField] private float positionDamper = 100f;
    [SerializeField] private float maximumForce = 1000f;
    [SerializeField] private float rotationSpring = 100f;
    [SerializeField] private float rotationDamper = 10f;
    [SerializeField] private float maximumTorque = 100f;

    private SpringDrive? constraint;

    /// <summary>Where this body's origin is driven to, from the connected body's origin in its space, or in the world without one.</summary>
    public Float3 TargetPosition { get => targetPosition; set { targetPosition = value; Push(); } }

    /// <summary>The rotation this body is driven to, relative to the connected body's, or in the world without one.</summary>
    public Quaternion TargetRotation { get => targetRotation; set { targetRotation = value; Push(); } }

    /// <summary>The velocity relative to the connected body the damper drives toward, in the connected body's space.</summary>
    public Float3 TargetVelocity { get => targetVelocity; set { targetVelocity = value; Push(); } }

    /// <summary>The angular velocity relative to the connected body the rotation damper drives toward, in the connected body's space.</summary>
    public Float3 TargetAngularVelocity { get => targetAngularVelocity; set { targetAngularVelocity = value; Push(); } }

    /// <summary>The position spring, in newtons per metre of gap.</summary>
    public float PositionSpring { get => positionSpring; set { positionSpring = MathF.Max(0f, value); Push(); } }

    /// <summary>The position damper, in newtons per metre per second of speed off the target velocity.</summary>
    public float PositionDamper { get => positionDamper; set { positionDamper = MathF.Max(0f, value); Push(); } }

    /// <summary>The most the position drive pushes with, in newtons.</summary>
    public float MaximumForce { get => maximumForce; set { maximumForce = MathF.Max(0f, value); Push(); } }

    /// <summary>The rotation spring, in newton metres per radian of turn.</summary>
    public float RotationSpring { get => rotationSpring; set { rotationSpring = MathF.Max(0f, value); Push(); } }

    /// <summary>The rotation damper, in newton metres per radian per second of spin off the target.</summary>
    public float RotationDamper { get => rotationDamper; set { rotationDamper = MathF.Max(0f, value); Push(); } }

    /// <summary>The most the rotation drive twists with, in newton metres.</summary>
    public float MaximumTorque { get => maximumTorque; set { maximumTorque = MathF.Max(0f, value); Push(); } }

    /// <summary>The push the position drive gave on the last substep, in newton seconds.</summary>
    public Float3 Impulse => IsLive(constraint) ? constraint!.Data.Impulse.ToProwl() : Float3.Zero;

    protected override Constraint GetConstraint() => constraint!;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        constraint = world.CreateConstraint<SpringDrive>(body1, body2);
        Push();
    }

    internal override void Refresh() => Push();

    protected override void DestroyConstraint()
    {
        RemoveConstraint(constraint!);
        constraint = null;
    }

    /// <summary>Copies the settings into the live constraint, which is cheap enough to do every step.</summary>
    private void Push()
    {
        if (!IsLive(constraint)) return;
        ref SpringDrive.DriveData data = ref constraint!.Data;
        Rigidbody3D body1 = Body1;
        Float3 center1 = body1.IsValid() ? body1.AppliedCenterOfMass : Float3.Zero;
        Float3 center2 = connectedBody.IsValid() && connectedBody.IsSimulated ? connectedBody.AppliedCenterOfMass : Float3.Zero;
        data.LocalAnchor1 = (-center1).ToJitter();
        data.TargetPosition = (targetPosition - center2).ToJitter();
        data.TargetRotation = targetRotation.ToJitter();
        data.TargetVelocity = targetVelocity.ToJitter();
        data.TargetAngularVelocity = targetAngularVelocity.ToJitter();
        data.PositionSpring = positionSpring;
        data.PositionDamper = positionDamper;
        data.MaxForce = maximumForce;
        data.RotationSpring = rotationSpring;
        data.RotationDamper = rotationDamper;
        data.MaxTorque = maximumTorque;
        WakeBodies();
    }

    public override void DrawGizmos() => DrawJointMarker(WorldPivot);


    /// <summary>
    /// The solver side of <see cref="DriveConstraint"/>. Each drive is a soft constraint: the spring and damper are folded
    /// into a bias and a softness, so the spring closes the error and the damper pulls toward the target velocity with
    /// exactly the force the pair would give, solved together with every other constraint on the bodies. The impulse is
    /// limited on each substep by the force times the substep's own time.
    /// </summary>
    public unsafe class SpringDrive : Constraint<SpringDrive.DriveData>
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DriveData
        {
            internal int _internal;
            internal uint DispatchId;
            internal ulong ConstraintId;

            public JHandle<RigidBodyData> Body1;
            public JHandle<RigidBodyData> Body2;

            /// <summary>Where body1's origin is driven to, from body2's centre of mass in body2's space.</summary>
            public JVector TargetPosition;
            public JQuaternion TargetRotation;
            public JVector TargetVelocity;
            public JVector TargetAngularVelocity;
            public float PositionSpring, PositionDamper, MaxForce;
            public float RotationSpring, RotationDamper, MaxTorque;

            /// <summary>Body1's origin from its centre of mass, in body1's space.</summary>
            public JVector LocalAnchor1;

            public JVector Impulse, AngularImpulse;
            public JVector R1, R2;
            public JVector Bias, AngularBias;
            public JSymmetricMatrix Mass, AngularMass;
        }

        private static readonly uint RegisteredDispatchId = RegisterFullConstraint(&Prepare, &Iterate);

        protected override void Create()
        {
            if (sizeof(DriveData) > Precision.ConstraintSizeFull)
                throw new InvalidOperationException($"{nameof(DriveData)} is {sizeof(DriveData)} bytes, more than a constraint can hold.");

            DispatchId = RegisteredDispatchId;
            base.Create();
            ref DriveData data = ref Data;
            data.TargetRotation = JQuaternion.Identity;
        }

        public override void ResetWarmStart()
        {
            Data.Impulse = JVector.Zero;
            Data.AngularImpulse = JVector.Zero;
        }

        /// <summary>
        /// The softness and bias of a spring and damper solved implicitly over <paramref name="h"/>: the bias closes the
        /// error at the spring's rate and the softness lets the impulse give as the damper would.
        /// </summary>
        private static void Soften(float spring, float damper, float h, out float softness, out float biasRate)
        {
            float stiffness = damper + h * spring;
            if (stiffness <= (float)1e-9)
            {
                softness = (float)1e9;
                biasRate = 0;
                return;
            }
            softness = (float)1.0 / (h * stiffness);
            biasRate = spring / stiffness;
        }

        /// <summary>How a point <paramref name="r"/> from a body's centre of mass gives under a push, as an inverse mass.</summary>
        private static JSymmetricMatrix PointResponse(in RigidBodyData body, in JVector r)
        {
            JMatrix cross = JMatrix.CreateCrossProduct(r);
            JMatrix turn = JMatrix.Transpose(cross) * (JMatrix)body.InverseInertiaWorld * cross;
            JVector linear = body.InverseMassVector;
            JSymmetricMatrix response = JSymmetricMatrix.Zero;
            response.M11 = linear.X + turn.M11;
            response.M12 = turn.M12;
            response.M13 = turn.M13;
            response.M22 = linear.Y + turn.M22;
            response.M23 = turn.M23;
            response.M33 = linear.Z + turn.M33;
            return response;
        }

        /// <summary>
        /// The softened inverse of a response, with every direction neither body can move in taken out, so a locked
        /// axis builds no impulse and leaves the force limit to the axes that can move.
        /// </summary>
        private static JSymmetricMatrix SoftInverse(JSymmetricMatrix response, float softness)
        {
            const float locked = (float)1e-9;
            bool x = response.M11 > locked, y = response.M22 > locked, z = response.M33 > locked;
            response.M11 += softness;
            response.M22 += softness;
            response.M33 += softness;
            if (!JSymmetricMatrix.Inverse(response, out JSymmetricMatrix mass)) return JSymmetricMatrix.Zero;
            if (!x) mass.M11 = mass.M12 = mass.M13 = 0;
            if (!y) mass.M12 = mass.M22 = mass.M23 = 0;
            if (!z) mass.M13 = mass.M23 = mass.M33 = 0;
            return mass;
        }

        private static void Push(ref DriveData data, ref RigidBodyData body1, ref RigidBodyData body2, in JVector impulse)
        {
            body1.Velocity += JVector.Multiply(impulse, body1.InverseMassVector);
            body1.AngularVelocity += JVector.Transform(JVector.Cross(data.R1, impulse), body1.InverseInertiaWorld);
            body2.Velocity -= JVector.Multiply(impulse, body2.InverseMassVector);
            body2.AngularVelocity -= JVector.Transform(JVector.Cross(data.R2, impulse), body2.InverseInertiaWorld);
        }

        private static void Twist(ref RigidBodyData body1, ref RigidBodyData body2, in JVector impulse)
        {
            body1.AngularVelocity += JVector.Transform(impulse, body1.InverseInertiaWorld);
            body2.AngularVelocity -= JVector.Transform(impulse, body2.InverseInertiaWorld);
        }

        public static void Prepare(ref ConstraintData constraint, in TimeStep timeStep)
        {
            ref var data = ref Unsafe.As<ConstraintData, DriveData>(ref constraint);
            ref RigidBodyData body1 = ref data.Body1.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;
            float h = timeStep.SubstepDt;

            // Body1's origin is pulled to the target point on body2. Body2 takes the push back where body1 takes it, as
            // an arm reaching out from body2 would hand it on, so the pair never gains a spin from the drive.
            data.R1 = JVector.Transform(data.LocalAnchor1, body1.Orientation);
            JVector anchor = body1.Position + data.R1;
            JVector error = anchor - (body2.Position + JVector.Transform(data.TargetPosition, body2.Orientation));
            data.R2 = anchor - body2.Position;
            Soften(data.PositionSpring, data.PositionDamper, h, out float softness, out float biasRate);
            data.Bias = error * biasRate - JVector.Transform(data.TargetVelocity, body2.Orientation);
            data.Mass = SoftInverse(PointResponse(body1, data.R1) + PointResponse(body2, data.R2), softness);

            // The turn from where body1 should face to where it does face, as an axis scaled by its angle.
            JQuaternion goalRotation = body2.Orientation * data.TargetRotation;
            JQuaternion turn = JQuaternion.MultiplyConjugate(body1.Orientation, goalRotation);
            if (turn.W < 0) turn = JQuaternion.Multiply(turn, (float)(-1.0));
            JVector axis = new(turn.X, turn.Y, turn.Z);
            float sine = axis.Length();
            JVector angleError = sine > (float)1e-6 ? axis * ((float)2.0 * MathF.Atan2(sine, turn.W) / sine) : axis * (float)2.0;

            Soften(data.RotationSpring, data.RotationDamper, h, out float angularSoftness, out float angularBiasRate);
            data.AngularBias = angleError * angularBiasRate - JVector.Transform(data.TargetAngularVelocity, body2.Orientation);
            data.AngularMass = SoftInverse(body1.InverseInertiaWorld + body2.InverseInertiaWorld, angularSoftness);

            // Warm start with what the drive pushed last time, kept within its limits and off any locked axis.
            data.Impulse = Limit(JVector.Transform(data.Impulse, Free(data.Mass)), data.MaxForce * h);
            data.AngularImpulse = Limit(JVector.Transform(data.AngularImpulse, Free(data.AngularMass)), data.MaxTorque * h);
            Push(ref data, ref body1, ref body2, data.Impulse);
            Twist(ref body1, ref body2, data.AngularImpulse);
        }

        public static void Iterate(ref ConstraintData constraint, in TimeStep timeStep)
        {
            ref var data = ref Unsafe.As<ConstraintData, DriveData>(ref constraint);
            ref RigidBodyData body1 = ref data.Body1.Data;
            ref RigidBodyData body2 = ref data.Body2.Data;
            float h = timeStep.SubstepDt;

            Soften(data.PositionSpring, data.PositionDamper, h, out float softness, out _);
            JVector relative = body1.Velocity + JVector.Cross(body1.AngularVelocity, data.R1)
                - body2.Velocity - JVector.Cross(body2.AngularVelocity, data.R2);
            JVector lambda = -JVector.Transform(relative + data.Bias + data.Impulse * softness, data.Mass);
            JVector old = data.Impulse;
            data.Impulse = Limit(old + lambda, data.MaxForce * h);
            Push(ref data, ref body1, ref body2, data.Impulse - old);

            Soften(data.RotationSpring, data.RotationDamper, h, out float angularSoftness, out _);
            JVector spin = body1.AngularVelocity - body2.AngularVelocity;
            JVector angularLambda = -JVector.Transform(spin + data.AngularBias + data.AngularImpulse * angularSoftness, data.AngularMass);
            JVector oldAngular = data.AngularImpulse;
            data.AngularImpulse = Limit(oldAngular + angularLambda, data.MaxTorque * h);
            Twist(ref body1, ref body2, data.AngularImpulse - oldAngular);
        }

        /// <summary>A projection keeping only the axes a softened inverse left free.</summary>
        private static JSymmetricMatrix Free(in JSymmetricMatrix mass)
        {
            JSymmetricMatrix keep = JSymmetricMatrix.Zero;
            keep.M11 = mass.M11 != 0 ? 1 : 0;
            keep.M22 = mass.M22 != 0 ? 1 : 0;
            keep.M33 = mass.M33 != 0 ? 1 : 0;
            return keep;
        }

        private static JVector Limit(JVector impulse, float max)
        {
            float length = impulse.Length();
            return length > max ? impulse * (max / length) : impulse;
        }
    }
}
