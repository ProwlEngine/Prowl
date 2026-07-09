// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Controls particle velocity over their lifetime.
/// </summary>
[Serializable]
public class VelocityOverLifetimeModule : ParticleSystemModule
{
    public AnimationCurve VelocityX = new([new KeyFrame(0f, 0f), new KeyFrame(1f, 0f)]);
    public AnimationCurve VelocityY = new([new KeyFrame(0f, 0f), new KeyFrame(1f, 0f)]);
    public AnimationCurve VelocityZ = new([new KeyFrame(0f, 0f), new KeyFrame(1f, 0f)]);

    public override void OnParticleUpdate(ref Particle particle, float deltaTime)
    {
        if (!Enabled) return;

        float normalizedTime = particle.NormalizedLifetime;
        float vx = (float)VelocityX.Evaluate(normalizedTime);
        float vy = (float)VelocityY.Evaluate(normalizedTime);
        float vz = (float)VelocityZ.Evaluate(normalizedTime);

        Float3 velocityChange = new Float3(vx, vy, vz);
        particle.Velocity += velocityChange * deltaTime;
    }
}
