// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Dynamics.Constraints;

namespace Prowl.Runtime;

/// <summary>
/// Base class for composite joints that are made up of multiple constraints.
/// </summary>
public abstract class PhysicsJoint : PhysicsConstraint
{
    protected Joint joint;

    protected override Constraint GetConstraint()
    {
        // Joints are composed of multiple constraints, return null for base implementation
        return null;
    }

    /// <summary>
    /// Gets the underlying Jitter2 joint.
    /// </summary>
    public Joint GetJoint() => joint;

    protected override void DestroyConstraint()
    {
        if (joint != null)
        {
            joint.Remove();
            joint = null;
        }
    }

    public override void DrawGizmos()
    {
        // TODO DrawGizmos
    }
}
