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
    /// <summary>Zone with the time-varying terms off, so only the profile is left.</summary>
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
    public void OutflowIsStrongestBetweenTheEyeAndTheRim()
    {
        // Downwash, not an explosion: the middle is where the air arrives, so it has no outward
        // push of its own. The flow builds as it spreads and dies at the rim.
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 2f);

        float eye = zone.SampleWind(new Float3(0.2f, 0f, 0f), 0f).X;
        float spread = zone.SampleWind(new Float3(4.5f, 0f, 0f), 0f).X;
        float rim = zone.SampleWind(new Float3(9.5f, 0f, 0f), 0f).X;

        Assert.True(spread > eye, "the spread should outrun the eye");
        Assert.True(spread > rim, "the spread should outrun the rim");
        Assert.Equal(Float3.Zero, zone.SampleWind(new Float3(11f, 0f, 0f), 0f));
    }

    [Fact]
    public void TheEyeBlowsDownwardInstead()
    {
        // What the middle gets is the column coming down, which is what pins particles to the
        // ground under a hovering craft rather than blasting them sideways.
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 2f);

        Float3 eye = zone.SampleWind(Float3.Zero, 0f);

        Assert.True(eye.Y < 0f);
        Assert.True(MathF.Abs(eye.X) < 1e-4f);
        Assert.True(MathF.Abs(eye.Z) < 1e-4f);
    }

    [Fact]
    public void OutflowIsRadial()
    {
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, new Float3(5f, 0f, 5f), 10f, 1f);

        Float3 wind = zone.SampleWind(new Float3(5f, 0f, 9f), 0f);

        Assert.True(wind.Z > 0f);
        Assert.True(MathF.Abs(wind.X) < 1e-4f);
    }

    [Fact]
    public void GustsKeepTheFieldMoving()
    {
        // Turbulence has to animate. A field that only varies in space reads as noise painted on
        // the ground rather than as air moving over it.
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 2f);
        zone.Turbulence = 0.6f;

        var sample = new Float3(3.5f, 0f, 0f);
        Float3 now = zone.SampleWind(sample, 0f);
        Float3 later = zone.SampleWind(sample, 0.15f);

        Assert.True(Float3.Length(now - later) > 1e-3f);
    }

    [Fact]
    public void StrongerWindDrivesGustsFaster()
    {
        // Turning the strength up past the point where grass lies flat has to keep doing something,
        // and what it does is drive the gust fronts outward faster.
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 1f);
        zone.Turbulence = 0.6f;

        float ChangeRate(float strength)
        {
            zone.WindMain = strength;
            var sample = new Float3(3.5f, 0f, 0f);
            // Divided through by strength, so this measures how fast the pattern moves, not how hard it blows
            return Float3.Length(zone.SampleWind(sample, 0f) - zone.SampleWind(sample, 0.02f)) / strength;
        }

        Assert.True(ChangeRate(10f) > ChangeRate(1f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(60f)]
    [InlineData(600f)]
    public void MovingTheZoneDoesNotScrambleTheField(float time)
    {
        // Anything that multiplies the zone's own position by elapsed time makes a small move
        // decorrelate the whole gust pattern, and worse the longer the game has been running. That
        // reads as the field flickering whenever the zone is dragged, so a nudge has to stay a nudge
        // no matter how late it happens.
        var scene = CreateScene(enable: true);
        var zone = CreateZone(scene, Float3.Zero, 10f, 2f);
        zone.Turbulence = 1f;

        var sample = new Float3(4f, 0f, 0f);
        Float3 before = zone.SampleWind(sample, time);

        // Sideways, which is the move that turns the outward direction the most
        zone.Transform.Position = new Float3(0f, 0f, 0.05f);
        Float3 after = zone.SampleWind(sample, time);

        float change = Float3.Length(after - before);
        Assert.True(change < Float3.Length(before) * 0.1f,
            $"a 5cm nudge at t={time} changed the wind by {change}");
    }

    [Fact]
    public void HeightFadesTheGroundEffect()
    {
        // A zone parked well overhead should barely stir the ground under it
        var scene = CreateScene(enable: true);
        var low = CreateZone(scene, Float3.Zero, 10f, 2f);
        var high = CreateZone(scene, new Float3(0f, 8f, 0f), 10f, 2f);

        var ground = new Float3(4.5f, 0f, 0f);

        Assert.True(low.SampleWind(ground, 0f).X > high.SampleWind(ground, 0f).X);
        Assert.Equal(Float3.Zero, CreateZone(scene, new Float3(0f, 20f, 0f), 10f, 2f).SampleWind(ground, 0f));
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
