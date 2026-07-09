// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Controls the initial properties of particles when they spawn.
/// </summary>
[Serializable]
public class InitialModule : ParticleSystemModule
{
    public MinMaxCurve StartLifetime = new(5.0f);
    public MinMaxCurve StartSpeed = new(5.0f);
    public MinMaxCurve StartSize = new(1.0f);
    public MinMaxCurve StartRotation = new(0.0f);
    public MinMaxGradient StartColor = new(Color.White);
    public float GravityModifier = 0.0f;

    public override void OnParticleSpawn(ref Particle particle, Random random)
    {
        if (!Enabled) return;

        particle.StartLifetime = StartLifetime.EvaluateInitial(random);
        particle.Lifetime = particle.StartLifetime;
        particle.Size = StartSize.EvaluateInitial(random);
        particle.StartSize = particle.Size;
        particle.Rotation = StartRotation.EvaluateInitial(random);
        particle.RotationalSpeed = 0;
        particle.Color = StartColor.EvaluateInitial(random);
        particle.StartColor = particle.Color;

        // Velocity is set by emission shape, but we apply start speed here
        float speed = StartSpeed.EvaluateInitial(random);
        if (particle.Velocity != Float3.Zero)
        {
            particle.Velocity = Float3.Normalize(particle.Velocity) * speed;
        }
    }

    public override void OnParticleUpdate(ref Particle particle, float deltaTime)
    {
        if (!Enabled) return;

        // Apply gravity
        if (GravityModifier != 0)
        {
            particle.Velocity += new Float3(0, -9.81f * GravityModifier * deltaTime, 0);
        }
    }
}
