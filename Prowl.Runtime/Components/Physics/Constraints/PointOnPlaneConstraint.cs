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
/// Constrains a fixed point on one body to a plane that is fixed on another body.
/// This constraint removes one degree of translational freedom if the limit is enforced.
/// Useful for creating sliding surfaces or limiting movement to a plane.
/// </summary>
[AddComponentMenu("Physics/Constraints/Point On Plane")]
public class PointOnPlaneConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 planeNormal = Float3.UnitY;
    [SerializeField] private Float3 anchor1 = Float3.Zero;
    [SerializeField] private Float3 anchor2 = Float3.Zero;
    [SerializeField] private float minDistance = float.NegativeInfinity;
    [SerializeField] private float maxDistance = float.PositiveInfinity;
    [SerializeField] private float softness = 0.00001f;
    [SerializeField] private float biasFactor = 0.01f;

    private PointOnPlane constraint;

    /// <summary>
    /// The plane normal in local space of the first rigidbody.
    /// </summary>
    public Float3 PlaneNormal
    {
        get => planeNormal;
        set
        {
            planeNormal = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Anchor point on the first body that defines the plane position.
    /// </summary>
    public Float3 Anchor1
    {
        get => anchor1;
        set
        {
            anchor1 = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Anchor point on the second body that is constrained to the plane.
    /// </summary>
    public Float3 Anchor2
    {
        get => anchor2;
        set
        {
            anchor2 = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Minimum allowed distance from the plane. Use float.NegativeInfinity for no minimum.
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
    /// Maximum allowed distance from the plane. Use float.PositiveInfinity for no maximum.
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
    public float Impulse => constraint?.Impulse ?? 0.0f;

    protected override Constraint GetConstraint() => constraint;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        JVector worldNormal = LocalDirToWorld(planeNormal, Body1.Transform);
        JVector worldAnchor1 = LocalToWorld(anchor1, Body1.Transform);
        JVector worldAnchor2 = connectedBody.IsValid()
            ? LocalToWorld(anchor2, connectedBody.Transform)
            : new JVector(anchor2.X, anchor2.Y, anchor2.Z);

        constraint = world.CreateConstraint<PointOnPlane>(body1, body2);

        var limit = new LinearLimit(minDistance, maxDistance);
        constraint.Initialize(worldNormal, worldAnchor1, worldAnchor2, limit);

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
