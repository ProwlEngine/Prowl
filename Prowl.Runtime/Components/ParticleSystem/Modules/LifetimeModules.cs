// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Velocity layered on top of the simulated one over each particle's life: straight velocity,
/// orbiting around the emitter, and moving toward or away from it. Curves are sampled at the
/// particle's normalized age and any random pick stays fixed for that particle.
/// </summary>
[Serializable]
public class VelocityOverLifetimeModule : ParticleSystemModule
{
    public ParticleSpace Space = ParticleSpace.Local;
    public MinMaxCurve X = new(0f);
    public MinMaxCurve Y = new(0f);
    public MinMaxCurve Z = new(0f);

    [Header("Orbital")]
    [Tooltip("Degrees per second around each axis of the emitter.")]
    public MinMaxCurve OrbitalX = new(0f);
    public MinMaxCurve OrbitalY = new(0f);
    public MinMaxCurve OrbitalZ = new(0f);
    [Tooltip("Center of the orbit and the radial push, relative to the emitter.")]
    public Float3 OrbitalOffset = Float3.Zero;
    [Tooltip("Speed away from the orbit center, negative pulls in.")]
    public MinMaxCurve Radial = new(0f);

    [Tooltip("Multiplies the particle's total speed when it moves.")]
    public MinMaxCurve SpeedModifier = new(1f);

    internal void Apply(ParticleSystemComponent system, ref Particle p, float age, float deltaTime)
    {
        Float3 linear = new(X.Evaluate(age, p.Random(0xA1)), Y.Evaluate(age, p.Random(0xA2)), Z.Evaluate(age, p.Random(0xA3)));
        if (linear != Float3.Zero)
            p.AnimatedVelocity += Space == ParticleSpace.World ? system.WorldVectorToSim(linear) : system.LocalVectorToSim(linear);

        Float3 orbital = new Float3(OrbitalX.Evaluate(age, p.Random(0xA4)), OrbitalY.Evaluate(age, p.Random(0xA5)), OrbitalZ.Evaluate(age, p.Random(0xA6))) * Maths.Deg2Rad;
        float radial = Radial.Evaluate(age, p.Random(0xA7));
        if (orbital == Float3.Zero && radial == 0f) return;

        Float3 center = system.EmitterOriginSim + system.LocalVectorToSim(OrbitalOffset);
        Float3 offset = p.Position - center;
        float rate = Float3.Length(orbital);
        if (rate > 0f && deltaTime > 0f)
        {
            // The chord of the arc this step rather than the tangent, so orbits keep their radius
            // instead of spiralling outward.
            Float3 axis = Float3.NormalizeSafe(system.LocalVectorToSim(orbital / rate), Float3.UnitY);
            Float3 turned = Quaternion.AxisAngle(axis, rate * deltaTime) * offset;
            p.AnimatedVelocity += (turned - offset) / deltaTime;
        }
        if (radial != 0f)
            p.AnimatedVelocity += Float3.NormalizeSafe(offset, Float3.Zero) * radial;
    }

    internal float Speed(in Particle p, float age) => SpeedModifier.Evaluate(age, p.Random(0xA8));
}

/// <summary>
/// Slows the simulated velocity (start speed, gravity, forces, wind and bounces) when it goes faster than
/// a limit, and applies drag to it. Velocity over Lifetime is added on top and is not limited.
/// </summary>
[Serializable]
public class LimitVelocityOverLifetimeModule : ParticleSystemModule
{
    [Tooltip("Speed particles are pulled back to.")]
    public MinMaxCurve Limit = new(1f);
    [Range(0f, 1f), Tooltip("How much of the speed over the limit is removed, per thirtieth of a second.")]
    public float Dampen = 0.5f;
    [Tooltip("Linear drag, per second.")]
    public MinMaxCurve Drag = new(0f);
    [Tooltip("Bigger particles get more drag.")]
    public bool MultiplyDragBySize = true;
    [Tooltip("Faster particles get more drag, like air resistance.")]
    public bool MultiplyDragByVelocity = true;

    internal void Apply(ref Particle p, float age, float deltaTime)
    {
        float drag = Drag.Evaluate(age, p.Random(0xB1));
        if (drag > 0f)
        {
            if (MultiplyDragBySize)
            {
                float size = (MathF.Abs(p.Size.X) + MathF.Abs(p.Size.Y)) * 0.5f;
                drag *= size * size;
            }
            if (MultiplyDragByVelocity)
                drag *= Float3.Length(p.Velocity);
            p.Velocity *= MathF.Exp(-drag * deltaTime);
        }

        float limit = MathF.Max(0f, Limit.Evaluate(age, p.Random(0xB2)));
        float speed = Float3.Length(p.Velocity);
        if (speed > limit && speed > 0f)
        {
            float keep = MathF.Pow(1f - Maths.Saturate(Dampen), deltaTime * 30f);
            float target = limit + (speed - limit) * keep;
            p.Velocity *= target / speed;
        }
    }
}

public enum InheritVelocityMode
{
    /// <summary>Particles pick up the emitter's velocity once, when born.</summary>
    Initial,
    /// <summary>Particles move along with the emitter's velocity for their whole life.</summary>
    Current
}

/// <summary>
/// Hands the emitter's movement on to its particles. Only has an effect when simulating in World or
/// Custom space, a Local space system already carries its particles along. Particles emitted by a sub
/// emitter inherit the velocity of the parent particle instead.
/// </summary>
[Serializable]
public class InheritVelocityModule : ParticleSystemModule
{
    public InheritVelocityMode Mode = InheritVelocityMode.Initial;
    public MinMaxCurve Multiplier = new(1f);

    internal void Apply(ParticleSystemComponent system, ref Particle p, float age)
    {
        if (Mode != InheritVelocityMode.Current) return;

        // Particles from a sub emitter follow the parent particle they came from, not this emitter.
        Float3 source = p.HasInheritedVelocity ? p.InheritedVelocity : system.EmitterVelocitySim;
        p.AnimatedVelocity += source * Multiplier.Evaluate(age, p.Random(0xC1));
    }
}

/// <summary>A constant push, an acceleration over each particle's life.</summary>
[Serializable]
public class ForceOverLifetimeModule : ParticleSystemModule
{
    public ParticleSpace Space = ParticleSpace.Local;
    public MinMaxCurve X = new(0f);
    public MinMaxCurve Y = new(0f);
    public MinMaxCurve Z = new(0f);
    [Tooltip("Pick a new random force every frame instead of one per particle.")]
    public bool Randomized = false;

    internal void Apply(ParticleSystemComponent system, ref Particle p, float age, float deltaTime, Random random)
    {
        Float3 force = Randomized
            ? new Float3(X.Evaluate(age, random), Y.Evaluate(age, random), Z.Evaluate(age, random))
            : new Float3(X.Evaluate(age, p.Random(0xD1)), Y.Evaluate(age, p.Random(0xD2)), Z.Evaluate(age, p.Random(0xD3)));
        if (force == Float3.Zero) return;
        p.Velocity += (Space == ParticleSpace.World ? system.WorldVectorToSim(force) : system.LocalVectorToSim(force)) * deltaTime;
    }
}

/// <summary>Tints particles over their life. Multiplies the start color.</summary>
[Serializable]
public class ColorOverLifetimeModule : ParticleSystemModule
{
    public MinMaxGradient Color = new(new Gradient());

    internal Color Evaluate(in Particle p, float age) => Color.Evaluate(age, p.Random(0xE1));
}

/// <summary>Tints particles by how fast they move. Multiplies the start color.</summary>
[Serializable]
public class ColorBySpeedModule : ParticleSystemModule
{
    public MinMaxGradient Color = new(new Gradient());
    [Tooltip("Speeds that map to the start and end of the gradient.")]
    public Float2 Range = new(0f, 1f);

    internal Color Evaluate(in Particle p, float speed) => Color.Evaluate(SpeedTime(Range, speed), p.Random(0xE2));

    internal static float SpeedTime(Float2 range, float speed)
        => range.Y > range.X ? Maths.Saturate((speed - range.X) / (range.Y - range.X)) : 0f;
}

/// <summary>Scales particles over their life. Multiplies the start size.</summary>
[Serializable]
public class SizeOverLifetimeModule : ParticleSystemModule
{
    public bool SeparateAxes = false;
    [Tooltip("Uniform size, or the X size when the axes are separate.")]
    public MinMaxCurve Size = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve SizeY = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve SizeZ = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));

    internal Float3 Evaluate(in Particle p, float age)
    {
        float x = Size.Evaluate(age, p.Random(0xF1));
        return SeparateAxes ? new Float3(x, SizeY.Evaluate(age, p.Random(0xF2)), SizeZ.Evaluate(age, p.Random(0xF3))) : new Float3(x);
    }
}

/// <summary>Scales particles by how fast they move. Multiplies the start size.</summary>
[Serializable]
public class SizeBySpeedModule : ParticleSystemModule
{
    public bool SeparateAxes = false;
    public MinMaxCurve Size = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve SizeY = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve SizeZ = new(new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f)));
    [Tooltip("Speeds that map to the start and end of the curves.")]
    public Float2 Range = new(0f, 1f);

    internal Float3 Evaluate(in Particle p, float speed)
    {
        float t = ColorBySpeedModule.SpeedTime(Range, speed);
        float x = Size.Evaluate(t, p.Random(0xF4));
        return SeparateAxes ? new Float3(x, SizeY.Evaluate(t, p.Random(0xF5)), SizeZ.Evaluate(t, p.Random(0xF6))) : new Float3(x);
    }
}

/// <summary>Spins particles over their life, in degrees per second.</summary>
[Serializable]
public class RotationOverLifetimeModule : ParticleSystemModule
{
    public bool SeparateAxes = false;
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve X = new(0f);
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve Y = new(0f);
    [Tooltip("Degrees per second around Z, the axis billboards spin on.")]
    public MinMaxCurve Z = new(45f);

    internal Float3 Evaluate(in Particle p, float age)
    {
        float z = Z.Evaluate(age, p.Random(0x111));
        return SeparateAxes ? new Float3(X.Evaluate(age, p.Random(0x112)), Y.Evaluate(age, p.Random(0x113)), z) : new Float3(0f, 0f, z);
    }
}

/// <summary>Spins particles by how fast they move, in degrees per second.</summary>
[Serializable]
public class RotationBySpeedModule : ParticleSystemModule
{
    public bool SeparateAxes = false;
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve X = new(0f);
    [ShowIf(nameof(SeparateAxes))]
    public MinMaxCurve Y = new(0f);
    public MinMaxCurve Z = new(45f);
    [Tooltip("Speeds that map to the start and end of the curves.")]
    public Float2 Range = new(0f, 1f);

    internal Float3 Evaluate(in Particle p, float speed)
    {
        float t = ColorBySpeedModule.SpeedTime(Range, speed);
        float z = Z.Evaluate(t, p.Random(0x114));
        return SeparateAxes ? new Float3(X.Evaluate(t, p.Random(0x115)), Y.Evaluate(t, p.Random(0x116)), z) : new Float3(0f, 0f, z);
    }
}

/// <summary>
/// Two free values per particle over its life, for custom shaders. They reach the particle shader as
/// the Z and W of the per instance custom data.
/// </summary>
[Serializable]
public class CustomDataModule : ParticleSystemModule
{
    public MinMaxCurve X = new(0f);
    public MinMaxCurve Y = new(0f);

    internal void Apply(ref Particle p, float age)
    {
        p.CustomData.X = X.Evaluate(age, p.Random(0x121));
        p.CustomData.Y = Y.Evaluate(age, p.Random(0x122));
    }
}
