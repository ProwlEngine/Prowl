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
/// Constrains the distance between two anchor points on two rigidbodies.
/// Can be used to create rope-like connections or maintain a specific distance.
/// </summary>
[AddComponentMenu("Physics/Constraints/Distance Limit")]
public class DistanceLimitConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 anchor = Float3.Zero;
    [SerializeField] private Float3 connectedAnchor = Float3.Zero;
    [SerializeField] private float targetDistance = 1.0f;
    [SerializeField] private float minDistance = float.NegativeInfinity;
    [SerializeField] private float maxDistance = float.PositiveInfinity;
    [SerializeField] private float softness = 0.001f;
    [SerializeField] private float biasFactor = 0.2f;

    private DistanceLimit constraint;

    /// <summary>
    /// The anchor point in local space of this rigidbody.
    /// </summary>
    public Float3 Anchor
    {
        get => anchor;
        set
        {
            anchor = value;
            UpdateAnchors();
        }
    }

    /// <summary>
    /// The anchor point in local space of the connected rigidbody.
    /// </summary>
    public Float3 ConnectedAnchor
    {
        get => connectedAnchor;
        set
        {
            connectedAnchor = value;
            UpdateAnchors();
        }
    }

    /// <summary>
    /// The target distance to maintain between the anchors.
    /// </summary>
    public float TargetDistance
    {
        get => targetDistance;
        set
        {
            targetDistance = value;
            if (constraint != null) constraint.TargetDistance = value;
        }
    }

    /// <summary>
    /// Minimum allowed distance. Use float.NegativeInfinity for no minimum.
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
    /// Maximum allowed distance. Use float.PositiveInfinity for no maximum.
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
    /// Gets the current distance between the anchors.
    /// </summary>
    public float CurrentDistance => constraint?.Distance ?? 0.0f;

    /// <summary>
    /// Gets the accumulated impulse applied by this constraint.
    /// </summary>
    public float Impulse => constraint?.Impulse ?? 0.0f;

    protected override Constraint GetConstraint() => constraint;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        JVector worldAnchor1 = LocalToWorld(anchor, Body1.Transform);
        JVector worldAnchor2 = connectedBody.IsValid()
            ? LocalToWorld(connectedAnchor, connectedBody.Transform)
            : new JVector(connectedAnchor.X, connectedAnchor.Y, connectedAnchor.Z);

        constraint = world.CreateConstraint<DistanceLimit>(body1, body2);

        var limit = new LinearLimit(minDistance, maxDistance);
        constraint.Initialize(worldAnchor1, worldAnchor2, limit);
        constraint.TargetDistance = targetDistance;
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

    private void UpdateAnchors()
    {
        if (constraint != null && !constraint.Handle.IsZero)
        {
            JVector worldAnchor1 = LocalToWorld(anchor, Body1.Transform);
            JVector worldAnchor2 = connectedBody.IsValid()
                ? LocalToWorld(connectedAnchor, connectedBody.Transform)
                : new JVector(connectedAnchor.X, connectedAnchor.Y, connectedAnchor.Z);

            constraint.Anchor1 = worldAnchor1;
            constraint.Anchor2 = worldAnchor2;
        }
    }
}
