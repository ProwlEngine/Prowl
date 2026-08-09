// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// How a value passed to <see cref="Rigidbody3D.AddForce(Prowl.Vector.Float3, ForceMode)"/> and friends
/// is interpreted. The continuous modes accumulate for the next step; the instant ones change velocity
/// straight away.
/// </summary>
public enum ForceMode
{
    /// <summary>A continuous force in newtons. Heavier bodies accelerate less.</summary>
    Force,

    /// <summary>A continuous acceleration. Mass is ignored, so every body accelerates the same.</summary>
    Acceleration,

    /// <summary>An instant change in momentum, in newton-seconds. Heavier bodies change velocity less.</summary>
    Impulse,

    /// <summary>An instant change in velocity. Mass is ignored.</summary>
    VelocityChange
}
