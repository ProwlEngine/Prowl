// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A universal joint (U-joint or Cardan joint) that allows rotation around two perpendicular axes.
/// Like the joint in a drive shaft. Composed of TwistAngle and BallSocket constraints.
/// </summary>
[AddComponentMenu("Physics/Joints/Universal Joint")]
public class UniversalJoint : PhysicsJoint
{
    [SerializeField] private Float3 anchor = Float3.Zero;
    [SerializeField] private Float3 axis1 = Float3.UnitX;
    [SerializeField] private Float3 axis2 = Float3.UnitZ;
    [SerializeField] private bool hasMotor = false;
    [SerializeField] private float motorTargetVelocity = 0.0f;
    [SerializeField] private float motorMaxForce = 100.0f;

    private Jitter2.Dynamics.Constraints.UniversalJoint universalJoint;

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
    /// The first rotation axis in local space.
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
    /// The second rotation axis in local space (should be perpendicular to Axis1).
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
            if (universalJoint?.Motor != null)
                universalJoint.Motor.TargetVelocity = value;
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
            if (universalJoint?.Motor != null)
                universalJoint.Motor.MaximumForce = value;
        }
    }

    /// <summary>
    /// Gets the current twist angle in degrees.
    /// </summary>
    public float CurrentAngleDegrees
    {
        get
        {
            if (universalJoint?.TwistAngle == null) return 0.0f;
            return (float)universalJoint.TwistAngle.Angle * (180.0f / Maths.PI);
        }
    }

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        JVector worldAnchor = LocalToWorld(anchor, Body1.Transform);
        JVector worldAxis1 = LocalDirToWorld(axis1, Body1.Transform);
        JVector worldAxis2 = connectedBody.IsValid()
            ? LocalDirToWorld(axis2, connectedBody.Transform)
            : new JVector(axis2.X, axis2.Y, axis2.Z);

        universalJoint = new Jitter2.Dynamics.Constraints.UniversalJoint(
            world, body1, body2, worldAnchor, worldAxis1, worldAxis2, hasMotor);

        joint = universalJoint;

        if (hasMotor && universalJoint.Motor != null)
        {
            universalJoint.Motor.TargetVelocity = motorTargetVelocity;
            universalJoint.Motor.MaximumForce = motorMaxForce;
        }
    }

    protected override void DestroyConstraint()
    {
        universalJoint = null;
        base.DestroyConstraint();
    }
}
