// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Blows particles around. The wind a particle feels is a steady <see cref="AmbientWind"/>, plus the
/// nearest <see cref="WindZone"/>, plus a swirling turbulence layer. The zone is picked once per
/// frame from the system's position, everything else is sampled per particle.
/// </summary>
[Serializable]
public class WindModule : ParticleSystemModule
{
    /// <summary>Steady world space wind that blows everywhere, with or without a wind zone.</summary>
    public Float3 AmbientWind = Float3.Zero;

    /// <summary>Scales the whole wind velocity this system feels.</summary>
    public float Multiplier = 1f;

    /// <summary>
    /// How quickly particles are carried along by the wind, per second. Higher values make light
    /// debris that rides the air, 0 makes particles ignore it. Since still air is still wind, this
    /// also settles particles that are moving through calm space.
    /// </summary>
    public float Drag = 1f;

    /// <summary>Extra straight acceleration along the wind. Use for gusty, arcade feeling pushes.</summary>
    public float Force = 0f;

    /// <summary>Speed of the swirls layered on top of the wind, in units per second.</summary>
    public float Turbulence = 0.5f;

    /// <summary>World size of one swirl. Larger values give broader, lazier eddies.</summary>
    public float TurbulenceScale = 8f;

    /// <summary>How fast the swirl pattern drifts through the world.</summary>
    public float TurbulenceSpeed = 0.5f;

    [NonSerialized] private WindZone? _zone;
    [NonSerialized] private float _time;
    [NonSerialized] private float _turbulenceScale;
    [NonSerialized] private float _turbulenceDrift;

    /// <summary>The zone this module is currently pulling wind from, if any.</summary>
    public WindZone? CurrentZone => _zone;

    /// <summary>Picks the zone for this step from the emitter position. Called once per simulation step.</summary>
    internal void BeginStep(ParticleSystemComponent system)
    {
        _time = Time.TimeSinceStartup;
        _turbulenceScale = 1f / MathF.Max(TurbulenceScale, 1e-3f);
        // Zones follow global time so gusts stay in step with grass, the swirls follow the system's own
        // clock so pausing, speed and scrubbing affect them. Wrapped to keep float precision.
        _turbulenceDrift = (float)(system.SimulationTime * TurbulenceSpeed % 65536.0);
        _zone = WindZone.GetNearest(system.Transform.Position);
    }

    internal void Apply(ParticleSystemComponent system, ref Particle particle, float deltaTime)
    {
        Float3 worldPosition = system.SimPointToWorld(particle.Position);

        Float3 wind = AmbientWind;
        if (_zone.IsValid())
            wind += _zone.SampleWind(worldPosition, _time);
        if (Turbulence != 0f)
            wind += SampleTurbulence(worldPosition, particle.RandomSeed) * Turbulence;

        wind = system.WorldVectorToSim(wind * Multiplier);

        if (Force != 0f)
            particle.Velocity += wind * (Force * deltaTime);

        // Exponential approach so the pull is framerate independent even at large steps
        if (Drag > 0f)
            particle.Velocity += (wind - particle.Velocity) * (1f - MathF.Exp(-Drag * deltaTime));
    }

    /// <summary>Turbulence velocity at a world position. Three decorrelated noise lanes, offset per particle.</summary>
    private Float3 SampleTurbulence(Float3 worldPosition, uint seed)
    {
        float x = worldPosition.X * _turbulenceScale + _turbulenceDrift;
        float y = worldPosition.Y * _turbulenceScale + _turbulenceDrift;
        float z = worldPosition.Z * _turbulenceScale + _turbulenceDrift;
        float offset = (seed & 0xFFFF) * (32f / 65535f);

        return new Float3(
            ValueNoise(x + offset, y, z),
            ValueNoise(x, y + offset + 17.3f, z),
            ValueNoise(x, y, z + offset + 41.7f));
    }

    /// <summary>Trilinear value noise in -1..1.</summary>
    private static float ValueNoise(float x, float y, float z)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;

        float sx = fx * fx * (3f - 2f * fx);
        float sy = fy * fy * (3f - 2f * fy);
        float sz = fz * fz * (3f - 2f * fz);

        float n000 = Hash(ix, iy, iz), n100 = Hash(ix + 1, iy, iz);
        float n010 = Hash(ix, iy + 1, iz), n110 = Hash(ix + 1, iy + 1, iz);
        float n001 = Hash(ix, iy, iz + 1), n101 = Hash(ix + 1, iy, iz + 1);
        float n011 = Hash(ix, iy + 1, iz + 1), n111 = Hash(ix + 1, iy + 1, iz + 1);

        float x00 = n000 + (n100 - n000) * sx;
        float x10 = n010 + (n110 - n010) * sx;
        float x01 = n001 + (n101 - n001) * sx;
        float x11 = n011 + (n111 - n011) * sx;

        float y0 = x00 + (x10 - x00) * sy;
        float y1 = x01 + (x11 - x01) * sy;
        return y0 + (y1 - y0) * sz;
    }

    private static float Hash(int x, int y, int z)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
        h ^= h >> 16;
        h *= 0x45d9f3b;
        h ^= h >> 16;
        return (h & 0xFFFF) * (2f / 65535f) - 1f;
    }
}
