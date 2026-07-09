// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Controls particle size over their lifetime.
/// </summary>
[Serializable]
public class SizeOverLifetimeModule : ParticleSystemModule
{
    public AnimationCurve SizeCurve = new([new KeyFrame(0f, 1f), new KeyFrame(1f, 1f)]);

    public override void OnParticleUpdate(ref Particle particle, float deltaTime)
    {
        if (!Enabled) return;

        float normalizedTime = particle.NormalizedLifetime;
        float sizeMultiplier = (float)SizeCurve.Evaluate(normalizedTime);

        particle.Size = particle.StartSize * sizeMultiplier;
    }
}
