// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

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
        // A joint is several constraints, so no single one represents it. GetConstraints below is what
        // Active and enabledOnStart actually use.
        return null;
    }

    /// <summary>The constraints the underlying Jitter joint is composed of.</summary>
    protected override IEnumerable<Constraint> GetConstraints()
    {
        if (joint == null) yield break;

        foreach (Constraint constraint in joint.Constraints)
            yield return constraint;
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
}
