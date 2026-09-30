// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Simulation rules of the particle system: timing, emission, spaces, collisions, sub emitters and the
/// edit mode preview.
/// </summary>
public class ParticleSystemTests : RuntimeTestBase
{
    private const float Frame = 1f / 60f;

    /// <summary>A system that emits nothing by default, from a point, moving straight up at no speed.</summary>
    private ParticleSystemComponent CreateSystem(Scene scene, Action<ParticleSystemComponent>? configure = null, GameObject? parent = null)
    {
        var go = CreateGameObject("Particles");
        var system = go.AddComponent<ParticleSystemComponent>();
        system.Emission.RateOverTime = new MinMaxCurve(0f);
        system.Initial.StartSpeed = new MinMaxCurve(0f);
        system.Initial.StartLifetime = new MinMaxCurve(100f);
        system.Shape.Enabled = false;
        configure?.Invoke(system);

        if (parent != null) go.SetParent(parent);
        else scene.Add(go);
        return system;
    }

    private void Run(Scene scene, float seconds, float step = Frame)
    {
        Time.CurrentTime.DeltaTime = step;
        int frames = (int)MathF.Round(seconds / step);
        Update(scene, frames);
        Time.CurrentTime.DeltaTime = Frame;
    }

    [Fact]
    public void NonLoopingSystemKeepsSimulatingAfterEmissionEnds()
    {
        var scene = CreateScene(enable: true);
        int stopped = 0;
        var system = CreateSystem(scene, s =>
        {
            s.Duration = 1f;
            s.Looping = false;
            s.Initial.StartLifetime = new MinMaxCurve(3f);
            s.Initial.StartSpeed = new MinMaxCurve(1f);
            s.Emission.Bursts.Add(new ParticleBurst(0f, 5));
        });
        system.Stopped += _ => stopped++;

        Run(scene, 1.5f);
        Assert.False(system.IsEmitting);
        Assert.True(system.IsPlaying);
        Assert.Equal(5, system.ParticleCount);

        float height = system.Particles[0].Position.Y;
        Run(scene, 0.5f);
        Assert.True(system.Particles[0].Position.Y > height + 0.4f);

        Run(scene, 1.5f);
        Assert.Equal(0, system.ParticleCount);
        Assert.True(system.IsStopped);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void BurstsFireAtTheirTimeInSeconds()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.Duration = 5f;
            s.Looping = false;
            s.Emission.Bursts.Add(new ParticleBurst(2f, 7));
        });

        Run(scene, 1.9f);
        Assert.Equal(0, system.ParticleCount);

        Run(scene, 0.2f);
        Assert.Equal(7, system.ParticleCount);
    }

    [Fact]
    public void LoopingBurstsFireEveryLoop()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.Duration = 1f;
            s.Emission.Bursts.Add(new ParticleBurst(0.5f, 3));
        });

        Run(scene, 3.2f);

        Assert.Equal(9, system.ParticleCount);
    }

    [Fact]
    public void BurstCyclesAreCounted()
    {
        var scene = CreateScene(enable: true);
        var limited = CreateSystem(scene, s =>
        {
            s.Duration = 5f;
            s.Looping = false;
            s.Emission.Bursts.Add(new ParticleBurst(0f, 2, 2, 3, 0.5f));
        });
        var endless = CreateSystem(scene, s =>
        {
            s.Duration = 5f;
            s.Looping = false;
            s.Emission.Bursts.Add(new ParticleBurst(0f, 2, 2, 0, 1f));
        });

        Run(scene, 6f);

        Assert.Equal(6, limited.ParticleCount);
        Assert.Equal(10, endless.ParticleCount);
    }

    [Fact]
    public void RateOverTimeEmitsAtItsRate()
    {
        var scene = CreateScene(enable: true);
        var constant = CreateSystem(scene, s => s.Emission.RateOverTime = new MinMaxCurve(10f));
        var random = CreateSystem(scene, s => s.Emission.RateOverTime = new MinMaxCurve(20f, 20f));

        Run(scene, 2f);

        Assert.InRange(constant.ParticleCount, 19, 20);
        Assert.InRange(random.ParticleCount, 39, 40);
    }

    [Fact]
    public void RateCurvesFollowEveryLoop()
    {
        var scene = CreateScene(enable: true);
        // No emission in the first half of each loop, 20 per second in the second.
        var curve = new AnimationCurve(new Keyframe(0f, 0f) { Interpolation = CurveInterpolation.Step }, new Keyframe(0.5f, 20f) { Interpolation = CurveInterpolation.Step }, new Keyframe(1f, 20f));
        var system = CreateSystem(scene, s =>
        {
            s.Duration = 1f;
            s.Emission.RateOverTime = new MinMaxCurve(curve);
        });

        Run(scene, 2.4f);
        int twoLoops = system.ParticleCount;
        Run(scene, 0.05f);

        Assert.InRange(twoLoops, 19, 21);
        Assert.Equal(twoLoops, system.ParticleCount);
    }

    [Fact]
    public void LongFramesSpreadParticlesInsteadOfClumping()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.SimulationSpace = SimulationSpace.World;
            s.Emission.RateOverTime = new MinMaxCurve(100f);
            s.Initial.StartSpeed = new MinMaxCurve(10f);
        });

        Run(scene, Frame);
        system.Transform.Position = new Float3(10f, 0f, 0f);
        Run(scene, 0.5f, 0.5f);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Particle p in system.Particles)
        {
            minX = MathF.Min(minX, p.Position.X);
            maxX = MathF.Max(maxX, p.Position.X);
            minY = MathF.Min(minY, p.Position.Y);
            maxY = MathF.Max(maxY, p.Position.Y);
        }

        Assert.True(maxX - minX > 8f, $"x spread {maxX - minX}");
        Assert.True(maxY - minY > 4f, $"y spread {maxY - minY}");
    }

    [Fact]
    public void LocalSpaceGravityPullsDownInTheWorld()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s => s.Initial.GravityModifier = 1f);
        system.Transform.Rotation = Quaternion.AxisAngle(Float3.UnitZ, MathF.PI * 0.5f);

        system.Emit(new EmitParams { Position = Float3.Zero, Velocity = Float3.Zero }, 1);
        Run(scene, 0.5f);

        Float3 world = system.Transform.TransformPoint(system.Particles[0].Position);
        Assert.True(world.Y < -0.5f, $"y {world.Y}");
        Assert.True(MathF.Abs(world.X) < 0.01f, $"x {world.X}");
    }

    [Fact]
    public void VelocityOverLifetimeIsAVelocityNotAnAcceleration()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.SimulationSpace = SimulationSpace.World;
            s.VelocityOverLifetime.Enabled = true;
            s.VelocityOverLifetime.X = new MinMaxCurve(2f);
        });

        system.Emit(new EmitParams { Position = Float3.Zero, Velocity = Float3.Zero }, 1);
        Run(scene, 1f);

        Particle p = system.Particles[0];
        Assert.Equal(2f, p.Position.X, 1);
        Assert.Equal(Float3.Zero, p.Velocity);
    }

    [Fact]
    public void LimitVelocityPullsSpeedToTheLimit()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.LimitVelocityOverLifetime.Enabled = true;
            s.LimitVelocityOverLifetime.Limit = new MinMaxCurve(2f);
            s.LimitVelocityOverLifetime.Dampen = 1f;
        });

        system.Emit(new EmitParams { Velocity = new Float3(10f, 0f, 0f) }, 1);
        Run(scene, Frame);

        Assert.Equal(2f, Float3.Length(system.Particles[0].Velocity), 3);
    }

    [Fact]
    public void InheritVelocityPicksUpTheEmittersMovement()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.SimulationSpace = SimulationSpace.World;
            s.Emission.RateOverTime = new MinMaxCurve(60f);
            s.InheritVelocity.Enabled = true;
        });

        for (int i = 0; i < 30; i++)
        {
            system.Transform.Position = new Float3(i * 10f * Frame, 0f, 0f);
            Run(scene, Frame);
        }

        Particle newest = system.Particles[0];
        foreach (Particle p in system.Particles)
            if (p.Age < newest.Age) newest = p;
        Assert.Equal(10f, newest.Velocity.X, 1);
    }

    [Fact]
    public void ParticlesBounceOffCollisionPlanes()
    {
        var scene = CreateScene(enable: true);
        var plane = CreateGameObject("Plane");
        scene.Add(plane);

        ParticleSystemComponent MakeSystem(float bounce) => CreateSystem(scene, s =>
        {
            s.SimulationSpace = SimulationSpace.World;
            s.Initial.StartSize = new MinMaxCurve(0.2f);
            s.Collision.Enabled = true;
            s.Collision.Type = ParticleCollisionType.Planes;
            s.Collision.Planes.Add(plane);
            s.Collision.Bounce = bounce;
        });

        var bouncy = MakeSystem(1f);
        var dead = MakeSystem(0f);
        var start = new EmitParams { Position = new Float3(0f, 2f, 0f), Velocity = new Float3(0f, -10f, 0f) };
        bouncy.Emit(start, 1);
        dead.Emit(start, 1);

        Run(scene, 0.3f);

        Assert.True(bouncy.Particles[0].Velocity.Y > 9f, $"vy {bouncy.Particles[0].Velocity.Y}");
        Assert.True(bouncy.Particles[0].Position.Y > 0.1f);
        Assert.Equal(0.1f, dead.Particles[0].Position.Y, 2);
        Assert.True(MathF.Abs(dead.Particles[0].Velocity.Y) < 1e-3f);
    }

    [Theory]
    [InlineData(ParticleCollisionQuality.High)]
    [InlineData(ParticleCollisionQuality.Medium)]
    [InlineData(ParticleCollisionQuality.Low)]
    public void ParticlesRestOnTheWorldInsteadOfSinking(ParticleCollisionQuality quality)
    {
        var scene = CreateScene(enable: true);
        var floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(20f, 1f, 20f);
        floor.Transform.Position = new Float3(0f, -0.5f, 0f);
        Tick(scene);

        var system = CreateSystem(scene, s =>
        {
            s.SimulationSpace = SimulationSpace.World;
            s.Initial.GravityModifier = 1f;
            s.Initial.StartSize = new MinMaxCurve(0.2f);
            s.Collision.Enabled = true;
            s.Collision.Quality = quality;
            s.Collision.Bounce = 0.3f;
        });
        system.Emit(new EmitParams { Position = new Float3(0f, 2f, 0f), Velocity = Float3.Zero }, 1);

        Run(scene, 3f);

        Assert.True(system.Particles[0].Position.Y > 0.05f, $"y {system.Particles[0].Position.Y}");
        Assert.True(system.Particles[0].Position.Y < 0.2f, $"y {system.Particles[0].Position.Y}");
    }

    [Fact]
    public void MaxParticlesCapsEmission()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.MaxParticles = 5;
            s.Emission.Bursts.Add(new ParticleBurst(0f, 20));
        });

        Run(scene, 0.1f);

        Assert.Equal(5, system.ParticleCount);
    }

    [Fact]
    public void DeadParticlesAreRemovedWithoutDisturbingTheRest()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene);
        for (int i = 0; i < 10; i++)
            system.Emit(new EmitParams { StartLifetime = i % 2 == 0 ? 0.1f : 10f, Position = new Float3(i, 0f, 0f) }, 1);

        Run(scene, 0.2f);

        Assert.Equal(5, system.ParticleCount);
        var xs = new HashSet<float>();
        foreach (Particle p in system.Particles)
        {
            Assert.True(p.Lifetime > 9f);
            xs.Add(p.Position.X);
        }
        Assert.True(xs.SetEquals(new[] { 1f, 3f, 5f, 7f, 9f }));
    }

    [Fact]
    public void ChangingSimulationSpaceKeepsParticlesInPlace()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene);
        system.Transform.Position = new Float3(10f, 0f, 0f);
        system.Transform.Rotation = Quaternion.AxisAngle(Float3.UnitY, 1f);
        system.Transform.LocalScale = new Float3(2f);

        system.Emit(new EmitParams { Position = new Float3(1f, 2f, 3f), Velocity = Float3.Zero }, 1);
        Float3 before = system.Transform.TransformPoint(system.Particles[0].Position);

        system.SimulationSpace = SimulationSpace.World;
        system.RefreshSpaces();

        Float3 after = system.Particles[0].Position;
        Assert.True(Float3.Distance(before, after) < 1e-3f, $"{before} vs {after}");
    }

    [Fact]
    public void StopEmittingLetsLiveParticlesFinish()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.Emission.RateOverTime = new MinMaxCurve(10f);
            s.Initial.StartLifetime = new MinMaxCurve(1f);
        });

        Run(scene, 1f);
        system.Stop();
        int alive = system.ParticleCount;

        Assert.True(alive > 5);
        Assert.False(system.IsEmitting);

        Run(scene, 1.1f);
        Assert.Equal(0, system.ParticleCount);
        Assert.True(system.IsStopped);
    }

    [Fact]
    public void StopActionDestroyRemovesTheGameObjectWhenFinished()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.Duration = 0.1f;
            s.Looping = false;
            s.StopAction = ParticleStopAction.Destroy;
            s.Initial.StartLifetime = new MinMaxCurve(0.2f);
            s.Emission.Bursts.Add(new ParticleBurst(0f, 1));
        });
        GameObject go = system.GameObject;

        Run(scene, 0.5f);

        Assert.True(go.IsNotValid());
    }

    [Fact]
    public void PrewarmStartsWithAFullLoop()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.Duration = 2f;
            s.Prewarm = true;
            s.Emission.RateOverTime = new MinMaxCurve(10f);
            s.Initial.StartLifetime = new MinMaxCurve(2f);
        });

        Assert.InRange(system.ParticleCount, 18, 20);
    }

    [Fact]
    public void StartDelayHoldsEmissionBack()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.StartDelay = new MinMaxCurve(1f);
            s.Emission.Bursts.Add(new ParticleBurst(0f, 4));
        });

        Run(scene, 0.9f);
        Assert.Equal(0, system.ParticleCount);

        Run(scene, 0.2f);
        Assert.Equal(4, system.ParticleCount);
    }

    [Fact]
    public void FixedSeedReplaysTheSameParticles()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.AutoRandomSeed = false;
            s.RandomSeed = 42;
            s.Shape.Enabled = true;
            s.Shape.Type = ParticleShapeType.Sphere;
            s.Initial.StartSpeed = new MinMaxCurve(1f, 5f);
            s.Emission.RateOverTime = new MinMaxCurve(30f);
        });

        Run(scene, 0.5f);
        var first = system.Particles.ToArray();

        system.Stop(true, ParticleStopBehavior.StopEmittingAndClear);
        system.Play();
        Run(scene, 0.5f);
        var second = system.Particles.ToArray();

        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
            Assert.True(Float3.Distance(first[i].Position, second[i].Position) < 1e-4f);
    }

    [Fact]
    public void SimulateFastForwardsAndPauses()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s => s.Emission.RateOverTime = new MinMaxCurve(10f));

        system.Simulate(1.5f);

        Assert.InRange(system.ParticleCount, 14, 15);
        Assert.True(system.IsPaused);

        Run(scene, 1f);
        Assert.InRange(system.ParticleCount, 14, 15);
    }

    [Fact]
    public void CullingPauseStopsSimulatingWhileUnseen()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.CullingMode = ParticleCullingMode.Pause;
            s.Emission.RateOverTime = new MinMaxCurve(10f);
        });

        Run(scene, 1f);

        Assert.Equal(0, system.ParticleCount);
    }

    [Fact]
    public void DeathSubEmittersEmitTheChildsBurstsAndSilenceIt()
    {
        var scene = CreateScene(enable: true);
        var parent = CreateSystem(scene, s =>
        {
            s.Initial.StartLifetime = new MinMaxCurve(0.1f);
            s.Emission.Bursts.Add(new ParticleBurst(0f, 3));
        });
        var child = CreateSystem(scene, s => s.Emission.Bursts.Add(new ParticleBurst(0f, 4)), parent.GameObject);
        parent.SubEmitters.Enabled = true;
        parent.SubEmitters.Emitters.Add(new SubEmitter { System = child, Type = SubEmitterType.Death });
        parent.Stop(false, ParticleStopBehavior.StopEmittingAndClear);
        child.Stop(false, ParticleStopBehavior.StopEmittingAndClear);
        parent.Play();

        Run(scene, 0.5f);

        Assert.Equal(0, parent.ParticleCount);
        Assert.Equal(12, child.ParticleCount);
    }

    [Fact]
    public void BirthSubEmittersEmitAtTheChildsRate()
    {
        var scene = CreateScene(enable: true);
        var parent = CreateSystem(scene, s => s.Initial.StartLifetime = new MinMaxCurve(10f));
        var child = CreateSystem(scene, s => s.Emission.RateOverTime = new MinMaxCurve(20f), parent.GameObject);
        parent.SubEmitters.Enabled = true;
        parent.SubEmitters.Emitters.Add(new SubEmitter { System = child, Type = SubEmitterType.Birth });

        parent.Emit(1);
        Run(scene, 1f);

        Assert.InRange(child.ParticleCount, 12, 28);
    }

    [Fact]
    public void EditModeOnlySimulatesWhileSelected()
    {
        using var _ = EditMode();
        var scene = CreateScene(enable: true);
        var parent = CreateSystem(scene, s => s.Emission.RateOverTime = new MinMaxCurve(10f));
        var child = CreateSystem(scene, s => s.Emission.RateOverTime = new MinMaxCurve(10f), parent.GameObject);

        Run(scene, 0.5f);
        Assert.Equal(0, parent.ParticleCount);

        // Selecting the child previews the whole tree from its topmost system.
        child.DrawGizmosSelected();
        Run(scene, 1f);
        Assert.True(parent.ParticleCount > 5);
        Assert.True(child.ParticleCount > 5);

        Time.CurrentTime.FrameCount += 5;
        Run(scene, Frame);
        Assert.Equal(0, parent.ParticleCount);
        Assert.Equal(0, child.ParticleCount);
    }

    [Fact]
    public void TextureSheetReachesTheLastFrameAtTheEndOfLife()
    {
        var sheet = new TextureSheetAnimationModule { Enabled = true, TilesX = 4, TilesY = 2 };
        var p = new Particle { StartLifetime = 1f, Lifetime = 0.001f, RandomSeed = 7 };

        sheet.Apply(ref p, p.NormalizedAge, 0f);
        Assert.InRange(p.UVFrame, 7f, 8f);

        sheet.Animation = TextureSheetAnimation.SingleRow;
        sheet.RandomRow = false;
        sheet.RowIndex = 1;
        p.Lifetime = 0.75f;
        sheet.Apply(ref p, p.NormalizedAge, 0f);
        Assert.InRange(p.UVFrame, 4f, 8f);
        Assert.Equal(new Float4(4f, 2f, 4f, 0f), sheet.ShaderParams);
    }

    [Fact]
    public void TrailsFadeOutAfterTheirParticleDies()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.SimulationSpace = SimulationSpace.World;
            s.Initial.StartLifetime = new MinMaxCurve(0.5f);
            s.Initial.StartSpeed = new MinMaxCurve(5f);
            s.Trails.Enabled = true;
            s.Trails.DieWithParticles = false;
            s.Trails.MinVertexDistance = 0.1f;
        });
        system.Emit(1);

        Run(scene, 0.4f);
        var buffer = Array.Empty<InstanceData>();
        AABB bounds = default;
        bool hasBounds = false;
        int segments = system.Trails.BuildSegments(system, system.TotalTime, ref buffer, ref bounds, ref hasBounds);
        Assert.True(segments > 5, $"segments {segments}");

        Run(scene, 0.2f);
        Assert.Equal(0, system.ParticleCount);
        Assert.True(system.Trails.HasOrphans);
        Assert.True(system.IsAlive());

        Run(scene, 0.6f);
        Assert.False(system.Trails.HasOrphans);
    }

    [Fact]
    public void LightsRespectTheCap()
    {
        var scene = CreateScene(enable: true);
        var system = CreateSystem(scene, s =>
        {
            s.Light.Enabled = true;
            s.Light.MaxLights = 3;
        });
        system.Emit(10);

        var lights = new List<IRenderableLight>();
        system.Light.Collect(system, lights);

        Assert.Equal(3, lights.Count);
        Assert.Equal(3, new HashSet<int> { lights[0].GetLightID(), lights[1].GetLightID(), lights[2].GetLightID() }.Count);
    }

    [Fact]
    public void ConeShapeStaysNearTheEmitterAtWideAngles()
    {
        var shape = new ShapeModule { Type = ParticleShapeType.Cone, Angle = 90f, Radius = 1f };
        var random = new Random(3);
        for (int i = 0; i < 500; i++)
        {
            shape.Sample(random, out Float3 position, out Float3 direction);
            Assert.True(Float3.Length(position) <= 1.0001f);
            Assert.Equal(1f, Float3.Length(direction), 3);
            Assert.True(direction.Y >= -1e-4f);
        }
    }
}
