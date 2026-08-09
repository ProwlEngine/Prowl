// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Restricts the tilt of one body relative to another body within a cone shape.
/// Useful for creating ball-and-socket joints with angular limits (ragdoll joints).
/// </summary>
[AddComponentMenu("Physics/Constraints/Cone Limit")]
public class ConeLimitConstraint : PhysicsConstraint
{
    [SerializeField] private Float3 axis = Float3.UnitY;
    [SerializeField] private float minAngle = 0.0f;
    [SerializeField] private float maxAngle = 45.0f;
    [SerializeField] private float softness = 0.001f;
    [SerializeField] private float biasFactor = 0.2f;

    private ConeLimit constraint;

    /// <summary>
    /// The cone axis in local space of this rigidbody.
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
    /// Minimum cone angle in degrees. Default is 0.
    /// </summary>
    public float MinAngle
    {
        get => minAngle;
        set
        {
            minAngle = value;
            RecreateConstraint();
        }
    }

    /// <summary>
    /// Maximum cone angle in degrees. Default is 45.
    /// This defines the cone's opening angle from the axis.
    /// </summary>
    public float MaxAngle
    {
        get => maxAngle;
        set
        {
            maxAngle = value;
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
    /// Gets the current angle between the axes in degrees.
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

    // ConeLimit measures Acos(dot(axis1, axis2)), so a tilt only ever spans 0 to 180 degrees, and Jitter
    // throws on anything outside that or on an inverted range. Clamping keeps a stray inspector value
    // from taking the constraint out entirely.
    private float ClampedMinAngle => Maths.Clamp(minAngle, 0.0f, 180.0f);
    private float ClampedMaxAngle => Maths.Clamp(maxAngle, ClampedMinAngle, 180.0f);

    protected override Constraint GetConstraint() => constraint;

    protected override void CreateConstraint(World world, RigidBody body1, RigidBody body2)
    {
        Jitter2.LinearMath.JVector worldAxis = LocalDirToWorld(axis, Body1.Transform);

        constraint = world.CreateConstraint<ConeLimit>(body1, body2);

        var limit = AngularLimit.FromDegree(ClampedMinAngle, ClampedMaxAngle);
        constraint.Initialize(worldAxis, limit);

        constraint.Softness = softness;
        constraint.Bias = biasFactor;
    }

    protected override void DestroyConstraint()
    {
        RemoveConstraint(constraint);
        constraint = null;
    }

    public override void DrawGizmos() => DrawJointMarker(WorldPivot);

    // A swing limit: the axis the tilt is measured from, and the cone it may lean out to. Both ends of
    // the range are stops, so both are red; the green band between them is the part that is allowed.
    public override void DrawGizmosSelected()
    {
        float scale = GizmoScale;
        float length = scale * 1.3f;
        Float3 apex = WorldPivot;
        Float3 dir = WorldAxis(axis);

        DrawJointMarker(apex);
        Debug.DrawAxisLine(apex, dir, scale * 1.5f, AxisColor);

        float min = ClampedMinAngle;
        float max = ClampedMaxAngle;

        Debug.DrawWireConeAngle(apex, dir, max, length, LimitColor);
        if (min > 0.0f) Debug.DrawWireConeAngle(apex, dir, min, length, LimitColor);

        // With a zero minimum the outer cone already is the allowed region, so the band would only
        // retrace it. It earns its place once an inner cone carves the middle out.
        if (min > 0.0f)
        {
            Debug.PerpendicularAxes(dir, out Float3 u, out Float3 v);
            Float3 from = Quaternion.AxisAngle(v, min * Maths.Deg2Rad) * dir;
            Debug.DrawWireArc(apex, v, from, length, (max - min) * Maths.Deg2Rad, RangeColor, 16);
        }
    }

}
