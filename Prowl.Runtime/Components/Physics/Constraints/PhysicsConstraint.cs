// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Base class for all physics constraints that connect two rigidbodies.
/// </summary>
[ComponentIcon("\uf0c1")] // Link inherited by all joints/constraints
public abstract class PhysicsConstraint : MonoBehaviour
{
    [SerializeField] protected Rigidbody3D connectedBody;
    [SerializeField] protected bool enabledOnStart = true;

    /// <summary>
    /// The rigidbody connected by this constraint. If null, the constraint connects to the world.
    /// </summary>
    public Rigidbody3D ConnectedBody
    {
        get => connectedBody;
        set
        {
            if (connectedBody != value)
            {
                connectedBody = value;
                RecreateConstraint();
            }
        }
    }

    /// <summary>
    /// The first rigidbody (owner of this component).
    /// </summary>
    protected Rigidbody3D Body1 => GetComponentInParent<Rigidbody3D>();

    /// <summary>
    /// Gets or sets whether this constraint is active.
    /// </summary>
    public bool Active
    {
        get => GetConstraint()?.IsEnabled ?? false;
        set
        {
            Constraint constraint = GetConstraint();
            if (constraint != null) constraint.IsEnabled = value;
        }
    }

    public override void OnEnable()
    {
        RecreateConstraint();
    }

    public override void OnDisable()
    {
        DestroyConstraint();
    }

    public override void OnValidate()
    {
        if (GameObject?.Scene?.IsNotValid() ?? true) return;
        RecreateConstraint();
    }

    public override void DrawGizmos()
    {
        // TODO DrawGizmos
    }

    /// <summary>
    /// Gets the underlying Jitter2 constraint.
    /// </summary>
    protected abstract Constraint GetConstraint();

    /// <summary>
    /// Creates the constraint in the physics world.
    /// </summary>
    protected abstract void CreateConstraint(World world, RigidBody body1, RigidBody body2);

    /// <summary>
    /// Destroys the constraint.
    /// </summary>
    protected abstract void DestroyConstraint();

    /// <summary>
    /// Recreates the constraint with current settings.
    /// </summary>
    protected void RecreateConstraint()
    {
        DestroyConstraint();

        Rigidbody3D body1 = Body1;
        if (body1.IsNotValid() || body1._body == null || body1._body.Handle.IsZero)
            return;

        World world = GameObject.Scene.Physics.World;
        if (world == null) return;

        // If no connected body is specified, create a static body at the world origin
        RigidBody body2;
        if (connectedBody.IsNotValid() || connectedBody._body == null || connectedBody._body.Handle.IsZero)
        {
            // Create a temporary static body for world-space constraints
            body2 = world.CreateRigidBody();
            body2.MotionType = MotionType.Static;
        }
        else
        {
            body2 = connectedBody._body;
        }

        CreateConstraint(world, body1._body, body2);

        // Set initial enabled state
        Constraint constraint = GetConstraint();
        if (constraint != null)
        {
            constraint.IsEnabled = enabledOnStart;
        }
    }

    /// <summary>
    /// Converts a local position to world space.
    /// </summary>
    protected Jitter2.LinearMath.JVector LocalToWorld(Float3 localPos, Transform transform)
    {
        Float3 worldPos = transform.TransformPoint(localPos);
        return new Jitter2.LinearMath.JVector(worldPos.X, worldPos.Y, worldPos.Z);
    }

    /// <summary>
    /// Converts a local direction to world space.
    /// </summary>
    protected Jitter2.LinearMath.JVector LocalDirToWorld(Float3 localDir, Transform transform)
    {
        Float3 worldDir = transform.TransformDirection(localDir);
        return new Jitter2.LinearMath.JVector(worldDir.X, worldDir.Y, worldDir.Z);
    }
}
