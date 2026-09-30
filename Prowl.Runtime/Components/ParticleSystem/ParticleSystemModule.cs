// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.ParticleSystem;

/// <summary>
/// Base class for particle system modules. A module is a group of settings the system reads while it
/// simulates or renders. The inspector draws <see cref="Enabled"/> as the section toggle.
/// </summary>
[Serializable]
public abstract class ParticleSystemModule
{
    [HideInInspector]
    public bool Enabled;
}

/// <summary>Which axes a module's vectors are expressed in.</summary>
public enum ParticleSpace
{
    /// <summary>The emitter's own axes.</summary>
    Local,
    World
}
