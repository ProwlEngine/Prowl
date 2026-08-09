// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A motor constraint that drives relative angular movement between two axes fixed in the reference frames of the bodies.
/// Useful for creating powered hinges, rotating platforms, and wheels.
/// </summary>
[AddComponentMenu("Physics/Constraints/Angular Motor")]
public class AngularMotorConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 axis1 = Float3.UnitY;
    [SerializeField] private Float3 axis2 = Float3.UnitY;
    [SerializeField] private float targetVelocity = 0.0f;
    [SerializeField] private float maximumForce = 0.0f;

    private AngularMotor constraint;

    /// <summary>
    /// The motor axis in local space of the first rigidbody.
    /// </summary>
    public Float3 Axis1
    {
        get => axis1;
        set
        {
            axis1 = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// The motor axis in local space of the second rigidbody.
    /// If no connected body is specified, this is in world space.
    /// </summary>
    public Float3 Axis2
    {
        get => axis2;
        set
        {
            axis2 = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Target angular velocity for the motor in radians per second.
    /// Positive values rotate in the direction of the axis.
    /// </summary>
    public float TargetVelocity
    {
        get => targetVelocity;
        set
        {
            targetVelocity = value;
            if (IsLive(constraint)) constraint.TargetVelocity = value;
        }
    }

    /// <summary>
    /// Maximum torque the motor can apply. Set to 0 to disable the motor.
    /// </summary>
    public float MaximumForce
    {
        get => maximumForce;
        set
        {
            maximumForce = value;
            if (IsLive(constraint)) constraint.MaximumForce = value;
        }
    }

    /// <summary>
    /// Gets the local axis on the first body.
    /// </summary>
    public Float3 LocalAxis1
    {
        get
        {
            if (constraint == null) return axis1;
            JVector jaxis = constraint.LocalAxis1;
            return new Float3(jaxis.X, jaxis.Y, jaxis.Z);
        }
    }

    /// <summary>
    /// Gets the local axis on the second body.
    /// </summary>
    public Float3 LocalAxis2
    {
        get
        {
            if (constraint == null) return axis2;
            JVector jaxis = constraint.LocalAxis2;
            return new Float3(jaxis.X, jaxis.Y, jaxis.Z);
        }
    }

    protected override Constraint GetConstraint() => constraint;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        JVector worldAxis1 = LocalDirToWorld(axis1, Body1.Transform);
        JVector worldAxis2 = connectedBody.IsValid()
            ? LocalDirToWorld(axis2, connectedBody.Transform)
            : new JVector(axis2.X, axis2.Y, axis2.Z);

        constraint = world.CreateConstraint<AngularMotor>(body1, body2);
        constraint.Initialize(worldAxis1, worldAxis2);

        constraint.TargetVelocity = targetVelocity;
        constraint.MaximumForce = maximumForce;
    }

    protected override void DestroyConstraint()
    {
        RemoveConstraint(constraint);
        constraint = null;
    }

    public override void DrawGizmos() => DrawJointMarker(WorldPivot);

    // A motor that spins one body against another. The arc points the way it drives and grows with the
    // target speed; it greys out when the maximum force is zero, because then it cannot actually do it.
    public override void DrawGizmosSelected()
    {
        float scale = GizmoScale;
        Float3 pivot = WorldPivot;
        Float3 a = WorldAxis(axis1);
        Float3 b = WorldConnectedAxis(axis2);

        DrawJointMarker(pivot);
        Debug.DrawAxisLine(pivot, a, scale * 1.2f, AnchorColor);
        Debug.DrawAxisLine(pivot, b, scale * 1.0f, ConnectedColor);

        Debug.PerpendicularAxes(a, out Float3 zero, out _);
        Debug.DrawSpinArrow(pivot, a, zero, scale * 0.8f, DriveSweep(targetVelocity), DriveColor(maximumForce));
    }

    // Enough sweep to read as a direction even at rest, growing with speed but capped so a fast motor
    // does not wrap into an unreadable spiral.
    private static float DriveSweep(float velocity)
        => (velocity < 0.0f ? -1.0f : 1.0f) * Maths.Min(0.7f + Maths.Abs(velocity) * 0.3f, Maths.PI * 1.4f);

    private Color DriveColor(float force) => force > 0.0f ? MotorColor : InactiveColor;
}
