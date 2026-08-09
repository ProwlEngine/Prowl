// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

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
    /// Whether this constraint is currently solving. False while it holds no live constraints, and for
    /// a joint, false unless every constraint it is composed of is enabled.
    /// </summary>
    public bool Active
    {
        get
        {
            // GetConstraints only yields live ones, so anything reached here is safe to read.
            bool any = false;
            foreach (Constraint constraint in GetConstraints())
            {
                if (!constraint.IsEnabled) return false;
                any = true;
            }

            return any;
        }
        set
        {
            foreach (Constraint constraint in GetConstraints())
                constraint.IsEnabled = value;
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
        if (GameObject.IsNotValid() || GameObject.Scene.IsNotValid()) return;
        RecreateConstraint();
    }

    /// <summary>
    /// Gets the underlying Jitter2 constraint. Composite joints own several and return null here;
    /// anything that has to act on all of them goes through <see cref="GetConstraints"/>.
    /// </summary>
    protected abstract Constraint GetConstraint();

    /// <summary>
    /// Every Jitter constraint this component owns. A simple constraint owns one, a joint owns several,
    /// and enabling or disabling has to reach all of them.
    /// </summary>
    protected virtual IEnumerable<Constraint> GetConstraints()
    {
        Constraint constraint = GetConstraint();
        if (IsLive(constraint)) yield return constraint;
    }

    /// <summary>
    /// Creates the constraint in the physics world.
    /// </summary>
    protected abstract void CreateConstraint(World world, RigidBody body1, RigidBody body2);

    /// <summary>
    /// Destroys the constraint.
    /// </summary>
    protected abstract void DestroyConstraint();

    /// <summary>
    /// Whether a constraint is live enough to read or write its properties.
    /// <para/>
    /// A non-null check is not sufficient. Jitter's constraint properties are views onto unmanaged
    /// memory reached through <c>Handle</c>, and removing a body removes its constraints, which zeroes
    /// their handles while this component still holds the managed object. Disabling a rigidbody that has
    /// a joint on it is enough to get there, and writing through it afterwards writes to freed memory.
    /// </summary>
    protected static bool IsLive(Constraint constraint) => constraint != null && !constraint.Handle.IsZero;

    /// <summary>
    /// Removes a constraint from the world that owns it. The constraint names its own bodies, so this
    /// still works during teardown, when the owning Rigidbody3D component may already be gone and
    /// reaching back through it would throw.
    /// </summary>
    protected static void RemoveConstraint(Constraint constraint)
    {
        if (constraint == null || constraint.Handle.IsZero) return;

        World world = constraint.Body1?.World;
        world?.Remove(constraint);
    }

    /// <summary>
    /// Recreates the constraint with current settings.
    /// </summary>
    protected void RecreateConstraint()
    {
        DestroyConstraint();

        Rigidbody3D body1 = Body1;
        if (body1.IsNotValid() || body1._body == null || body1._body.Handle.IsZero)
            return;

        // Reached from property setters as well as the lifecycle, so the scene can be mid-teardown
        // here; Scene.Physics throws once the scene is disposed.
        Resources.Scene scene = GameObject.IsValid() ? GameObject.Scene : null;
        World world = scene.IsValid() ? scene.Physics?.World : null;
        if (world == null) return;

        // No connected body means "anchor to the world". Jitter keeps a pinned static NullBody for
        // exactly that; creating a fresh static body here would leak one into the world on every
        // recreate, and this runs from OnEnable, OnValidate and every property setter.
        RigidBody body2 = connectedBody.IsNotValid() || connectedBody._body == null || connectedBody._body.Handle.IsZero
            ? world.NullBody
            : connectedBody._body;

        CreateConstraint(world, body1._body, body2);

        // Set initial enabled state. Through Active so a joint's constraints all get it, not just the
        // single one GetConstraint can name.
        Active = enabledOnStart;
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
