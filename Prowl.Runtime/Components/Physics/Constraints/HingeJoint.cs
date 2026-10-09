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
            axis = RequireAxis(value);
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Minimum angle limit in degrees.
    /// </summary>
    public float MinAngle
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
    public float MaxAngle
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
            if (motorTargetVelocity == value) return;
            motorTargetVelocity = value;
            if (IsLive(hingeJoint?.Motor))
                hingeJoint.Motor.TargetVelocity = value;
            WakeBodies();
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
            if (motorMaxForce == value) return;
            motorMaxForce = value;
            if (IsLive(hingeJoint?.Motor))
                hingeJoint.Motor.MaximumForce = value;
            WakeBodies();
        }
    }

    /// <summary>
    /// Gets the current angle of the hinge in degrees.
    /// </summary>
    public float CurrentAngleDegrees
    {
        get
        {
            if (!IsLive(hingeJoint?.HingeAngle)) return 0.0f;
            return (float)hingeJoint.HingeAngle.Angle * (180.0f / Maths.PI);
        }
    }

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        Jitter2.LinearMath.JVector worldAnchor = LocalToWorld(anchor, Body1.Transform);
        // Jitter drives and measures the connected side relative to this body, so the axis is reversed to
        // make a positive motor, angle or distance mean this body moving along +axis.
        Jitter2.LinearMath.JVector worldAxis = -LocalDirToWorld(axis, Body1.Transform);

        var angleLimit = AngularLimit.FromDegree(minAngleDegrees, maxAngleDegrees);

        hingeJoint = new Jitter2.Dynamics.Constraints.HingeJoint(
            world, body1, body2, worldAnchor, worldAxis, angleLimit, hasMotor);

        joint = hingeJoint;

        if (hasMotor && IsLive(hingeJoint.Motor))
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
        if (IsLive(hingeJoint?.HingeAngle))
        {
            var angleLimit = AngularLimit.FromDegree(minAngleDegrees, maxAngleDegrees);
            hingeJoint.HingeAngle.Limit = angleLimit;
        }
    }

    public override void DrawGizmos() => DrawJointMarker(WorldAnchor(anchor));

    // Hinge plus socket: the pin, the swing range about it, and the motor when one is fitted.
    public override void DrawGizmosSelected()
    {
        float scale = GizmoScale;
        Float3 pivot = WorldAnchor(anchor);
        Float3 hinge = WorldAxis(axis);

        DrawJointMarker(pivot);
        Debug.DrawAxisLine(pivot, hinge, scale * 1.2f, AxisColor);
        Debug.DrawWireCircle(pivot, hinge, scale, AxisColor, 28);

        Debug.PerpendicularAxes(hinge, out Float3 zero, out _);
        Debug.DrawArcRange(pivot, hinge, zero, scale, minAngleDegrees, maxAngleDegrees, RangeColor, LimitColor);

        if (!hasMotor) return;

        float sweep = (motorTargetVelocity < 0.0f ? -1.0f : 1.0f) * Maths.Min(0.7f + Maths.Abs(motorTargetVelocity) * 0.3f, Maths.PI * 1.4f);
        Debug.DrawSpinArrow(pivot, hinge, zero, scale * 0.6f, sweep, motorMaxForce > 0.0f ? MotorColor : InactiveColor);
    }
}
