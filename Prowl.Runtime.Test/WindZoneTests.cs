// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Covers the wind zone registry, its spherical falloff, and the particle module that reads it.
/// The grass shader mirrors <see cref="WindZone.SampleWind"/>, so the math pinned here is the same
/// math the blades run.
/// </summary>
public class WindZoneTests : RuntimeTestBase
{
    /// <summary>Zone with the time-varying terms switched off, so strength is purely the falloff.</summary>
    private WindZone CreateZone(Scene scene, Float3 position, float radius, float strength)
    {
        var go = CreateGameObject("WindZone");
        go.Transform.Position = position;
        var zone = go.AddComponent<WindZone>();
        zone.Radius = radius;
        zone.WindMain = strength;
        zone.Turbulence = 0f;
        zone.PulseMagnitude = 0f;
        scene.Add(go);
        return zone;
    }

    [Fact]
    public void EnabledZonesRegisterAndUnregister()
    {
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 1f);

        Assert.Contains(zone, WindZone.Active);

        zone.Enabled = false;

        Assert.DoesNotContain(zone, WindZone.Active);
    }

    [Fact]
    public void WindPushesOutwardAndFadesToTheRadius()
    {
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 2f);

        Float3 near = zone.SampleWind(new Float3(1f, 0f, 0f), 0f);
        Float3 far = zone.SampleWind(new Float3(8f, 0f, 0f), 0f);
        Float3 outside = zone.SampleWind(new Float3(11f, 0f, 0f), 0f);

        Assert.True(near.X > 0f);
        Assert.True(far.X > 0f);
        Assert.True(near.X > far.X);
        Assert.Equal(Float3.Zero, outside);
    }

    [Fact]
    public void WindDirectionIsRadial()
    {
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, new Float3(5f, 0f, 5f), 10f, 1f);

        Float3 wind = zone.SampleWind(new Float3(5f, 0f, 9f), 0f);

        Assert.True(wind.Z > 0f);
        Assert.True(MathF.Abs(wind.X) < 1e-4f);
    }

    [Fact]
    public void NearestZoneWinsForParticles()
    {
        var scene = CreateScene(enable: true);
        CreateZone(scene, new Float3(100f, 0f, 0f), 10f, 1f);
        var close = CreateZone(scene, new Float3(2f, 0f, 0f), 10f, 1f);

        Assert.Equal(close, WindZone.GetNearest(Float3.Zero));
    }

    [Fact]
    public void GatherReturnsTheFourNearestZonesInOrder()
    {
        var scene = CreateScene(enable: true);
        for (int i = 5; i >= 1; i--)
            CreateZone(scene, new Float3(i * 10f, 0f, 0f), 1f, 1f);

        var zones = new WindZone?[WindZone.kMaxShaderZones];
        int count = WindZone.GetNearest(Float3.Zero, zones);

        Assert.Equal(WindZone.kMaxShaderZones, count);
        for (int i = 0; i < count; i++)
            Assert.Equal((i + 1) * 10f, zones[i]!.Transform.Position.X, 3);
    }

    /// <summary>Strips drag and turbulence so a test sees only the zone's own push.</summary>
    private static WindModule ForceOnly(ParticleSystemComponent system)
    {
        system.Wind.Enabled = true;
        system.Wind.Drag = 0f;
        system.Wind.Force = 1f;
        system.Wind.Turbulence = 0f;
        return system.Wind;
    }

    [Fact]
    public void ParticlesAccelerateAlongTheWind()
    {
        var scene = CreateScene(enable: true);
        CreateZone(scene, Float3.Zero, 10f, 5f);

        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        var wind = ForceOnly(system);
        wind.BeginFrame(go.Transform, SimulationSpace.World);

        var particle = new Particle { Position = new Float3(2f, 0f, 0f), StartLifetime = 1f, Lifetime = 1f };
        system.Wind.OnParticleUpdate(ref particle, 0.5f);

        Assert.True(particle.Velocity.X > 0f);
        Assert.True(MathF.Abs(particle.Velocity.Z) < 1e-4f);
    }

    [Fact]
    public void ParticlesIgnoreWindWhenTheModuleIsOff()
    {
        var scene = CreateScene(enable: true);
        CreateZone(scene, Float3.Zero, 10f, 5f);

        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        system.Wind.BeginFrame(go.Transform, SimulationSpace.World);

        var particle = new Particle { Position = new Float3(2f, 0f, 0f), StartLifetime = 1f, Lifetime = 1f };
        system.Wind.OnParticleUpdate(ref particle, 0.5f);

        Assert.Equal(Float3.Zero, particle.Velocity);
    }

    [Fact]
    public void LocalSpaceParticlesGetWindInTheirOwnSpace()
    {
        var scene = CreateScene(enable: true);
        CreateZone(scene, Float3.Zero, 100f, 5f);

        var go = CreateGameObject("Particles");
        // Yawed 90 degrees: world +X wind has to arrive as local -Z (or +Z, sign depends on the turn).
        go.Transform.Rotation = Quaternion.AxisAngle(Float3.UnitY, MathF.PI * 0.5f);
        go.Transform.Position = new Float3(10f, 0f, 0f);
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        var wind = ForceOnly(system);
        wind.BeginFrame(go.Transform, SimulationSpace.Local);

        var particle = new Particle { StartLifetime = 1f, Lifetime = 1f };
        system.Wind.OnParticleUpdate(ref particle, 0.5f);

        Assert.True(MathF.Abs(particle.Velocity.Z) > 1e-3f);
        Assert.True(MathF.Abs(particle.Velocity.X) < 1e-3f);
    }

    [Fact]
    public void DragCarriesParticlesTowardTheWindVelocity()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        system.Wind.Enabled = true;
        system.Wind.AmbientWind = new Float3(10f, 0f, 0f);
        system.Wind.Turbulence = 0f;
        system.Wind.Drag = 5f;
        system.Wind.Force = 0f;
        system.Wind.BeginFrame(go.Transform, SimulationSpace.World);

        var particle = new Particle { StartLifetime = 1f, Lifetime = 1f };
        for (int i = 0; i < 100; i++)
            system.Wind.OnParticleUpdate(ref particle, 1f / 60f);

        Assert.Equal(10f, particle.Velocity.X, 2);
    }

    [Fact]
    public void DragIsStableAtAnyTimestep()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        system.Wind.Enabled = true;
        system.Wind.AmbientWind = new Float3(4f, 0f, 0f);
        system.Wind.Turbulence = 0f;
        system.Wind.Drag = 20f;
        system.Wind.Force = 0f;
        system.Wind.BeginFrame(go.Transform, SimulationSpace.World);

        // A huge step would overshoot and oscillate with a naive lerp
        var particle = new Particle { StartLifetime = 1f, Lifetime = 1f };
        system.Wind.OnParticleUpdate(ref particle, 10f);

        Assert.True(particle.Velocity.X <= 4f);
        Assert.Equal(4f, particle.Velocity.X, 3);
    }

    [Fact]
    public void AmbientWindBlowsWithoutAnyZone()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        system.Wind.Enabled = true;
        system.Wind.AmbientWind = new Float3(0f, 0f, 3f);
        system.Wind.Turbulence = 0f;
        system.Wind.BeginFrame(go.Transform, SimulationSpace.World);

        Assert.Null(system.Wind.CurrentZone);

        var particle = new Particle { StartLifetime = 1f, Lifetime = 1f };
        system.Wind.OnParticleUpdate(ref particle, 0.1f);

        Assert.True(particle.Velocity.Z > 0f);
    }

    [Fact]
    public void TurbulenceDecorrelatesNeighbouringParticles()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        system.Wind.Enabled = true;
        system.Wind.Turbulence = 2f;
        system.Wind.Drag = 1f;
        system.Wind.BeginFrame(go.Transform, SimulationSpace.World);

        var a = new Particle { StartLifetime = 1f, Lifetime = 1f, RandomSeed = 12345 };
        var b = new Particle { StartLifetime = 1f, Lifetime = 1f, RandomSeed = 999 };
        system.Wind.OnParticleUpdate(ref a, 0.1f);
        system.Wind.OnParticleUpdate(ref b, 0.1f);

        Assert.NotEqual(a.Velocity.X, b.Velocity.X, 5);
        Assert.NotEqual(Float3.Zero, a.Velocity);
    }

    [Fact]
    public void TurbulenceStaysWithinItsSpeed()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        scene.Add(go);

        system.Wind.Enabled = true;
        system.Wind.Turbulence = 3f;
        system.Wind.Drag = 0f;
        system.Wind.Force = 1f;
        system.Wind.BeginFrame(go.Transform, SimulationSpace.World);

        // Noise is bounded to -1..1 per axis, so one second of push cannot exceed the swirl speed
        for (uint seed = 1; seed < 40; seed++)
        {
            var particle = new Particle { Position = new Float3(seed, seed * 2f, seed * 3f), StartLifetime = 1f, Lifetime = 1f, RandomSeed = seed };
            system.Wind.OnParticleUpdate(ref particle, 1f);

            Assert.True(MathF.Abs(particle.Velocity.X) <= 3f);
            Assert.True(MathF.Abs(particle.Velocity.Y) <= 3f);
            Assert.True(MathF.Abs(particle.Velocity.Z) <= 3f);
        }
    }
}
