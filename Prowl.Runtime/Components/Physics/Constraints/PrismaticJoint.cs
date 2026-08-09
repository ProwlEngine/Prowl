// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A prismatic (slider) joint that constrains two bodies to move along a single axis.
/// Like a piston or drawer slide. Composed of PointOnLine with optional angle constraints and motor.
/// </summary>
[AddComponentMenu("Physics/Joints/Prismatic Joint")]
public class PrismaticJoint : PhysicsJoint
{
    [SerializeField] private Float3 anchor = Float3.Zero;
    [SerializeField] private Float3 axis = Float3.UnitX;
    [SerializeField] private float minDistance = float.NegativeInfinity;
    [SerializeField] private float maxDistance = float.PositiveInfinity;
    [SerializeField] private bool pinned = true;
    [SerializeField] private bool hasMotor = false;
    [SerializeField] private float motorTargetVelocity = 0.0f;
    [SerializeField] private float motorMaxForce = 100.0f;

    private Jitter2.Dynamics.Constraints.PrismaticJoint prismaticJoint;

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
    /// The axis of movement in local space.
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
    /// Minimum distance along the axis. Use float.NegativeInfinity for no limit.
    /// </summary>
    public float MinDistance
    {
        get => minDistance;
        set
        {
            minDistance = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Maximum distance along the axis. Use float.PositiveInfinity for no limit.
    /// </summary>
    public float MaxDistance
    {
        get => maxDistance;
        set
        {
            maxDistance = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// If true, prevents all rotation. If false, allows rotation around the slider axis.
    /// </summary>
    public bool Pinned
    {
        get => pinned;
        set
        {
            if (pinned != value)
            {
                pinned = value;
                RecreateConstraint();
            }
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
            if (IsLive(prismaticJoint?.Motor))
                prismaticJoint.Motor.TargetVelocity = value;
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
            if (IsLive(prismaticJoint?.Motor))
                prismaticJoint.Motor.MaximumForce = value;
        }
    }

    /// <summary>
    /// Gets the current distance along the slider axis.
    /// </summary>
    public float CurrentDistance
    {
        get
        {
            if (!IsLive(prismaticJoint?.Slider)) return 0.0f;
            return prismaticJoint.Slider.Distance;
        }
    }

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        Jitter2.LinearMath.JVector worldAnchor = LocalToWorld(anchor, Body1.Transform);
        Jitter2.LinearMath.JVector worldAxis = LocalDirToWorld(axis, Body1.Transform);

        var limit = new LinearLimit(minDistance, maxDistance);

        prismaticJoint = new Jitter2.Dynamics.Constraints.PrismaticJoint(
            world, body1, body2, worldAnchor, worldAxis, limit, pinned, hasMotor);

        joint = prismaticJoint;

        if (hasMotor && prismaticJoint.Motor != null)
        {
            prismaticJoint.Motor.TargetVelocity = motorTargetVelocity;
            prismaticJoint.Motor.MaximumForce = motorMaxForce;
        }
    }

    protected override void DestroyConstraint()
    {
        prismaticJoint = null;
        base.DestroyConstraint();
    }

    public override void DrawGizmos() => DrawJointMarker(WorldAnchor(anchor));

    // A slider: the rail, how far along it may travel, and which way the motor drives. Pinned means the
    // orientation is welded too, which the frame at the anchor stands for.
    public override void DrawGizmosSelected()
    {
        float scale = GizmoScale;
        Float3 pivot = WorldAnchor(anchor);
        Float3 rail = WorldAxis(axis);

        DrawJointMarker(pivot);
        Debug.DrawAxisLine(pivot, rail, scale * 1.5f, AxisColor);
        Debug.DrawLinearRange(pivot, rail, minDistance, maxDistance, scale * 2.5f, scale * 0.35f, RangeColor, LimitColor);

        if (pinned) Debug.DrawAxes(pivot, BodyFrame.Rotation, scale * 0.5f, LinkColor);

        if (hasMotor)
        {
            float sign = motorTargetVelocity < 0.0f ? -1.0f : 1.0f;
            Debug.DrawArrow(pivot, Float3.Normalize(rail) * (sign * scale * 1.6f), motorMaxForce > 0.0f ? MotorColor : InactiveColor);
        }
    }
}
