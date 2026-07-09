// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Controls particle color over their lifetime.
/// </summary>
[Serializable]
public class ColorOverLifetimeModule : ParticleSystemModule
{
    public Gradient ColorGradient = new();

    public override void OnParticleUpdate(ref Particle particle, float deltaTime)
    {
        if (!Enabled) return;

        float normalizedTime = particle.NormalizedLifetime;
        var color = ColorGradient.Evaluate(normalizedTime);
        particle.Color = particle.StartColor * color;
    }
}
