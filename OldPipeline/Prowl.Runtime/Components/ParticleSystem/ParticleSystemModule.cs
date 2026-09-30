// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.ParticleSystem;

/// <summary>
/// Base class for all particle system modules.
/// Modules control different aspects of particle behavior.
/// </summary>
[Serializable]
public abstract class ParticleSystemModule
{
    public bool Enabled = false;

    /// <summary>
    /// Called when particles are spawned to initialize their values.
    /// </summary>
    public virtual void OnParticleSpawn(ref Particle particle, Random random) { }

    /// <summary>
    /// Called every frame to update particle values.
    /// </summary>
    public virtual void OnParticleUpdate(ref Particle particle, float deltaTime) { }
}
