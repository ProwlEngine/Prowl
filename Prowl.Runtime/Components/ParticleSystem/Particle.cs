// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem;

/// <summary>
/// One live particle. Positions, velocities and rotations are in the system's simulation space.
/// </summary>
public struct Particle
{
    public Float3 Position;

    /// <summary>Simulated velocity. Gravity, forces, drag, wind and collisions act on this one.</summary>
    public Float3 Velocity;

    /// <summary>Velocity from Velocity over Lifetime and Inherit Velocity. Rebuilt every step, never accumulated.</summary>
    public Float3 AnimatedVelocity;

    /// <summary>Rotation in degrees. Billboards spin around Z, meshes use all three.</summary>
    public Float3 Rotation;

    /// <summary>Angular velocity in degrees per second, added on top of the rotation modules.</summary>
    public Float3 AngularVelocity;

    public Float3 StartSize;
    public Float3 Size;
    public Color StartColor;
    public Color Color;
    public float StartLifetime;

    /// <summary>Seconds left to live.</summary>
    public float Lifetime;

    /// <summary>Stable per particle seed. Every per particle random choice is derived from it.</summary>
    public uint RandomSeed;

    /// <summary>Texture sheet frame. The fraction is the blend toward the next frame.</summary>
    public float UVFrame;

    /// <summary>Values from the Custom Data module. X and Y reach the shader.</summary>
    public Float4 CustomData;

    /// <summary>One based trail slot, 0 when the particle has no trail.</summary>
    internal int TrailSlot;

    public readonly float Age => StartLifetime - Lifetime;

    /// <summary>0 at birth, 1 at death.</summary>
    public readonly float NormalizedAge => StartLifetime > 0f ? Maths.Saturate(1f - Lifetime / StartLifetime) : 1f;

    public readonly Float3 TotalVelocity => Velocity + AnimatedVelocity;

    public readonly bool IsAlive => Lifetime > 0f;

    /// <summary>A stable random value in [0, 1) for this particle. Each distinct <paramref name="salt"/> gives an independent value.</summary>
    public readonly float Random(uint salt) => ParticleRandom.Value(RandomSeed, salt);
}

/// <summary>Stateless hashing so a particle can reroll the same random value every frame.</summary>
public static class ParticleRandom
{
    public static float Value(uint seed, uint salt)
    {
        uint h = seed ^ (salt * 0x9E3779B9u);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h >> 8) * (1f / 16777216f);
    }

    public static Float3 OnUnitSphere(System.Random random)
    {
        float z = random.NextSingle() * 2f - 1f;
        float a = random.NextSingle() * Maths.PI * 2f;
        float r = Maths.Sqrt(Maths.Max(0f, 1f - z * z));
        return new Float3(r * Maths.Cos(a), r * Maths.Sin(a), z);
    }

    public static Float3 InUnitSphere(System.Random random)
        => OnUnitSphere(random) * Maths.Pow(random.NextSingle(), 1f / 3f);
}
