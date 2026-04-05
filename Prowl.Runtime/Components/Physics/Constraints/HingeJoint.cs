// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A hinge joint that constrains two bodies to rotate around a shared axis.
/// Similar to a door hinge. Composed of HingeAngle and BallSocket constraints.
/// </summary>
[AddComponentMenu("Physics/Joints/Hinge Joint")]
public class HingeJoint : PhysicsJoint
{
    [SerializeField] private Float3 anchor = Float3.Zero;
    [SerializeField] private Float3 axis = Float3.UnitY;
    [SerializeField] private float minAngleDegrees = -180.0f;
    [SerializeField] private float maxAngleDegrees = 180.0f;
    [SerializeField] private bool hasMotor = false;
    [SerializeField] private float motorTargetVelocity = 0.0f;
    [SerializeField] private float motorMaxForce = 100.0f;

    private Jitter2.Dynamics.Constraints.HingeJoint hingeJoint;

    /// <summary>
    /// The anchor point in local space where the joint connects.
    /// </summary>
    public Float3 Anchor
    {
        get => anchor;
        set
        {
            anchor = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// The axis of rotation in local space.
    /// </summary>
    public Float3 Axis
    {
        get => axis;
        set
        {
            axis = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Minimum angle limit in degrees.
    /// </summary>
    public float MinAngleDegrees
    {
        get => minAngleDegrees;
        set
        {
            minAngleDegrees = value;
            UpdateAngleLimits();
        }
    }

    /// <summary>
    /// Maximum angle limit in degrees.
    /// </summary>
    public float MaxAngleDegrees
    {
        get => maxAngleDegrees;
        set
        {
            maxAngleDegrees = value;
            UpdateAngleLimits();
        }
    }

    /// <summary>
    /// Whether this joint has a motor attached.
    /// </summary>
    public bool HasMotor
    {
        get => hasMotor;
        set
        {
            if (hasMotor != value)
            {
                hasMotor = value;
                RecreateConstraint();
            }
        }
    }

    /// <summary>
    /// Target velocity for the motor (if enabled).
    /// </summary>
    public float MotorTargetVelocity
    {
        get => motorTargetVelocity;
        set
        {
            motorTargetVelocity = value;
            if (hingeJoint?.Motor != null)
                hingeJoint.Motor.TargetVelocity = value;
        }
    }

    /// <summary>
    /// Maximum force the motor can apply (if enabled).
    /// </summary>
    public float MotorMaxForce
    {
        get => motorMaxForce;
        set
        {
            motorMaxForce = value;
            if (hingeJoint?.Motor != null)
                hingeJoint.Motor.MaximumForce = value;
        }
    }

    /// <summary>
    /// Gets the current angle of the hinge in degrees.
    /// </summary>
    public float CurrentAngleDegrees
    {
        get
        {
            if (hingeJoint?.HingeAngle == null) return 0.0f;
            return (float)hingeJoint.HingeAngle.Angle * (180.0f / Maths.PI);
        }
    }

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        Jitter2.LinearMath.JVector worldAnchor = LocalToWorld(anchor, Body1.Transform);
        Jitter2.LinearMath.JVector worldAxis = LocalDirToWorld(axis, Body1.Transform);

        var angleLimit = AngularLimit.FromDegree(minAngleDegrees, maxAngleDegrees);

        hingeJoint = new Jitter2.Dynamics.Constraints.HingeJoint(
            world, body1, body2, worldAnchor, worldAxis, angleLimit, hasMotor);

        joint = hingeJoint;

        if (hasMotor && hingeJoint.Motor != null)
        {
            hingeJoint.Motor.TargetVelocity = motorTargetVelocity;
            hingeJoint.Motor.MaximumForce = motorMaxForce;
        }
    }

    protected override void DestroyConstraint()
    {
        hingeJoint = null;
        base.DestroyConstraint();
    }

    private void UpdateAngleLimits()
    {
        if (hingeJoint?.HingeAngle != null)
        {
            var angleLimit = AngularLimit.FromDegree(minAngleDegrees, maxAngleDegrees);
            hingeJoint.HingeAngle.Limit = angleLimit;
        }
    }
}
