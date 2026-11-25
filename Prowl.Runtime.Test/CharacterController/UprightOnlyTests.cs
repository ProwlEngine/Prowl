// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test.Controller;

/// <summary>Guards, flags and bookkeeping that do not depend on which way gravity pulls, so they run once rather than in every frame.</summary>
public sealed class UprightOnlyTests() : ControllerTestBase(Gravity.Upright)
{
    /// <summary>A controller with the component's own defaults, placed without being settled.</summary>
    private Walker Place(Scene scene, Float3 at)
    {
        GameObject go = CreateGameObject("Player");
        CharacterController controller = go.AddComponent<CharacterController>();
        go.Transform.Position = at;
        scene.Add(go);
        return new Walker(controller, Frame);
    }

    [Fact]
    public void AControllerWithNothingAroundItMovesTheWholeWay()
    {
        Scene scene = World();
        Walker cc = Place(scene, new Float3(0, 10, 0));

        CharacterController.CollisionFlags flags = cc.Move(new Float3(0.5f, 0, 0));

        Assert.Equal(CharacterController.CollisionFlags.None, flags);
        Assert.Empty(cc.Controller.Hits);
        Assert.Equal(0.5f, cc.Position.X, 3);
    }
    [Fact]
    public void CastFindsWhatIsAheadWithoutMoving()
    {
        Scene scene = World();
        Box(scene, new Float3(2, 0, 0), new Float3(1, 4, 8));

        Walker cc = Place(scene, new Float3(0, 0, 0));
        Float3 before = cc.Position;

        Assert.True(cc.Controller.Cast(ToWorld(new Float3(1, 0, 0)), 5.0f, out ShapeCastHit hit));
        Assert.True(hit.Distance > 0.0f);
        Assert.Equal(before, cc.Position);

        Assert.False(cc.Controller.Cast(ToWorld(new Float3(-1, 0, 0)), 5.0f, out _));
        Assert.False(cc.Controller.Cast(Float3.Zero, 1.0f, out _), "a cast with no direction should be refused");
    }
    [Fact]
    public void RidingCanBeSwitchedOff()
    {
        Scene scene = WorldWithFloor();
        GameObject platform = Box(scene, new Float3(0f, 0.75f, 0f), new Float3(4f, 0.5f, 4f), name: "Platform");
        Walker walker = Spawn(scene, new Float3(0f, 1f, 0f));
        walker.Controller.RideMovingPlatforms = false;

        walker.Run(Float3.Zero, 0.3f, () => MoveBy(platform, East * 3f * Dt));

        Assert.True(MathF.Abs(walker.Position.X) < 0.05f, $"was carried to {walker.Position}");
    }
    /// <summary>A crate stood on is reported as the ground body, which is what lets a game leave it alone when pushing things.</summary>
    [Fact]
    public void ACrateStoodOnIsTheGroundBody()
    {
        Scene scene = WorldWithFloor();
        GameObject crate = Box(scene, new Float3(0f, 0.5f, 0f), new Float3(1.5f, 1f, 1.5f), name: "Crate");
        Rigidbody3D body = crate.AddComponent<Rigidbody3D>();
        Walker walker = Spawn(scene, new Float3(0.6f, 1f, 0f));

        Assert.True(walker.Grounded);
        Assert.Same(body, walker.Controller.GroundBody);
    }    /// <summary>A motion or an up that is not a number is ignored rather than spreading through the controller.</summary>
    [Fact]
    public void ABrokenMotionOrUpIsIgnored()
    {
        Scene scene = WorldWithFloor();
        Walker walker = Spawn(scene, Float3.Zero);
        Float3 before = walker.Position;

        walker.Move(new Float3(float.NaN, 0f, 1f));
        walker.Controller.Up = new Float3(float.NaN, 1f, 0f);
        walker.Move(new Float3(0f, -0.01f, 0f));

        AssertFinite(walker);
        Assert.True(Float3.Length(walker.Position - before) < 0.01f, $"moved to {walker.Position}");
        Assert.True(walker.Grounded);
        Assert.Equal(Float3.UnitY, walker.Controller.Up);
    }
    [Fact]
    public void ATeleportForgetsThePlatformItStoodOn()
    {
        Scene scene = WorldWithFloor();
        GameObject platform = Box(scene, new Float3(0f, 0.75f, 0f), new Float3(3f, 0.5f, 3f));
        Walker walker = Spawn(scene, new Float3(0f, 1f, 0f));
        walker.Teleport(new Float3(10f, 0f, 0f));

        MoveBy(platform, East * 2f);
        walker.Step(Float3.Zero);

        Assert.True(MathF.Abs(walker.Position.X - 10f) < 0.01f, $"was dragged to {walker.Position}");
    }
    [Fact]
    public void IgnoredBodiesAreWalkedThroughUntilEnabledAgain()
    {
        Scene scene = WorldWithFloor();
        GameObject crate = Box(scene, new Float3(0f, 1f, 2f), new Float3(2f, 2f, 1f));
        Rigidbody3D body = crate.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Static;
        Walker walker = Spawn(scene, Float3.Zero);
        walker.Controller.IgnoreCollisionWith(body);

        walker.Run(North * WalkSpeed, 1f);

        Assert.True(walker.Position.Z > 4f, $"was blocked by an ignored body at {walker.Position}");

        walker.Controller.EnableCollisionWith(body);
        walker.Run(South * WalkSpeed, 1f);
        Assert.True(walker.Position.Z > 2.5f + 0.3f, $"walked back through the body once it was enabled again, to {walker.Position}");
    }
}
