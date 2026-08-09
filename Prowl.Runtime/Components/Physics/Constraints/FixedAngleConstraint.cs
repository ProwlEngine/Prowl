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
            if (IsLive(constraint)) constraint.Softness = value;
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
            if (IsLive(constraint)) constraint.Bias = value;
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
        RemoveConstraint(constraint);
        constraint = null;
    }

    public override void DrawGizmos() => DrawJointMarker(WorldPivot);

    // Nothing about this joint is positional: it welds two orientations together. So the gizmo is the
    // two frames it is holding aligned, and the tie between them.
    public override void DrawGizmosSelected()
    {
        float scale = GizmoScale;
        Float3 pivot = WorldPivot;

        DrawJointMarker(pivot);
        Debug.DrawAxes(pivot, BodyFrame.Rotation, scale, AnchorColor);

        if (connectedBody.IsValid())
        {
            Float3 other = connectedBody.Transform.Position;
            Debug.DrawAxes(other, connectedBody.Transform.Rotation, scale, ConnectedColor);
        }
    }
}
