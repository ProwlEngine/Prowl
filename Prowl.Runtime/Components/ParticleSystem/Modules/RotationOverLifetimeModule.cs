// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Controls particle rotation over their lifetime.
/// </summary>
[Serializable]
public class RotationOverLifetimeModule : ParticleSystemModule
{
    public MinMaxCurve AngularVelocity = new(0.0f);

    public override void OnParticleSpawn(ref Particle particle, Random random)
    {
        if (!Enabled) return;

        particle.RotationalSpeed = AngularVelocity.EvaluateInitial(random);
    }

    public override void OnParticleUpdate(ref Particle particle, float deltaTime)
    {
        if (!Enabled) return;

        particle.Rotation += particle.RotationalSpeed * deltaTime;
    }
}
