// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Constrains the relative orientation between two rigidbodies, preventing all rotation.
/// Also known as a weld joint (when combined with position constraints).
/// </summary>
[AddComponentMenu("Physics/Constraints/Fixed Angle")]
public class FixedAngleConstraint : PhysicsConstraint
{
    [SerializeField] private float softness = 0.001f;
    [SerializeField] private float biasFactor = 0.2f;

    private FixedAngle constraint;

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
    /// Gets the accumulated impulse applied by this constraint.
    /// </summary>
    public Float3 Impulse
    {
        get
        {
            if (constraint == null) return Float3.Zero;
            Jitter2.LinearMath.JVector impulse = constraint.Impulse;
            return new Float3(impulse.X, impulse.Y, impulse.Z);
        }
    }

    protected override Constraint GetConstraint() => constraint;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        constraint = world.CreateConstraint<FixedAngle>(body1, body2);
        constraint.Initialize();
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
}
