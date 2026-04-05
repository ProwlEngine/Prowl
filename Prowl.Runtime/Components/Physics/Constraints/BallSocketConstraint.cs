// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A ball-and-socket constraint that connects two rigidbodies at a point,
/// allowing free rotation but constraining translation.
/// Also known as a point-to-point constraint or spherical joint.
/// </summary>
[AddComponentMenu("Physics/Constraints/Ball Socket")]
public class BallSocketConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 anchor = Float3.Zero;
    [SerializeField] private float softness = 0.0f;
    [SerializeField] private float biasFactor = 0.2f;

    private BallSocket constraint;

    /// <summary>
    /// The anchor point in local space of this rigidbody.
    /// </summary>
    public Float3 Anchor
    {
        get => anchor;
        set
        {
            anchor = value;
            UpdateAnchor();
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
        Jitter2.LinearMath.JVector worldAnchor = LocalToWorld(anchor, Body1.Transform);

        constraint = world.CreateConstraint<BallSocket>(body1, body2);
        constraint.Initialize(worldAnchor);
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

    private void UpdateAnchor()
    {
        if (constraint != null && !constraint.Handle.IsZero)
        {
            Jitter2.LinearMath.JVector worldAnchor = LocalToWorld(anchor, Body1.Transform);
            constraint.Anchor1 = worldAnchor;
        }
    }
}
