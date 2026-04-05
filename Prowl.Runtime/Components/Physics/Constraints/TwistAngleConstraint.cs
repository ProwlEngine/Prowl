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
/// Constrains the relative twist of two bodies around specified axes.
/// This constraint removes one angular degree of freedom when the limit is enforced.
/// </summary>
[AddComponentMenu("Physics/Constraints/Twist Angle")]
public class TwistAngleConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 axis1 = Float3.UnitX;
    [SerializeField] private Float3 axis2 = Float3.UnitX;
    [SerializeField] private float minAngle = -45.0f;
    [SerializeField] private float maxAngle = 45.0f;
    [SerializeField] private float softness = 0.0001f;
    [SerializeField] private float biasFactor = 0.2f;

    private TwistAngle constraint;

    /// <summary>
    /// The first twist axis in local space of this rigidbody.
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
    /// The second twist axis in local space of the connected rigidbody.
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
    /// Minimum twist angle in degrees. Default is -45.
    /// </summary>
    public float MinAngle
    {
        get => minAngle;
        set
        {
            minAngle = value;
            UpdateLimits();
        }
    }

    /// <summary>
    /// Maximum twist angle in degrees. Default is 45.
    /// </summary>
    public float MaxAngle
    {
        get => maxAngle;
        set
        {
            maxAngle = value;
            UpdateLimits();
        }
    }

    /// <summary>
    /// Softness of the constraint. Higher values make the constraint softer.
    /// </summary>
    public float Softness
    {
        get => softness;
        set
        {
            softness = value;
            if (constraint != null) constraint.Softness = value;
        }
    }

    /// <summary>
    /// Bias factor for error correction. Higher values correct errors faster.
    /// </summary>
    public float BiasFactor
    {
        get => biasFactor;
        set
        {
            biasFactor = value;
            if (constraint != null) constraint.Bias = value;
        }
    }

    /// <summary>
    /// Gets the current twist angle in degrees.
    /// </summary>
    public float Angle
    {
        get
        {
            if (constraint == null) return 0.0f;
            return constraint.Angle.Degree;
        }
    }

    /// <summary>
    /// Gets the accumulated impulse applied by this constraint.
    /// </summary>
    public float Impulse => constraint?.Impulse ?? 0.0f;

    protected override Constraint GetConstraint() => constraint;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        JVector worldAxis1 = LocalDirToWorld(axis1, Body1.Transform);
        JVector worldAxis2 = connectedBody.IsValid()
            ? LocalDirToWorld(axis2, connectedBody.Transform)
            : new JVector(axis2.X, axis2.Y, axis2.Z);

        constraint = world.CreateConstraint<TwistAngle>(body1, body2);

        var limit = AngularLimit.FromDegree(minAngle, maxAngle);
        constraint.Initialize(worldAxis1, worldAxis2, limit);

        constraint.Softness = softness;
        constraint.Bias = biasFactor;
    }

    protected override void DestroyConstraint()
    {
        if (constraint != null && !constraint.Handle.IsZero)
        {
            Body1._body.World.Remove(constraint);
            constraint = null;
        }
    }

    private void UpdateLimits()
    {
        if (constraint != null)
        {
            var limit = AngularLimit.FromDegree(minAngle, maxAngle);
            constraint.Limit = limit;
        }
    }
}
