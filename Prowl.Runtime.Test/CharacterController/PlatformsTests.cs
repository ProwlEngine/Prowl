// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

/// <summary>Platforms here are plain colliders moved by their Transform between frames, the way an animated or scripted platform moves.</summary>
public abstract class PlatformsTests(Gravity gravity) : ControllerTestBase(gravity)
{
    private const float PlatformTop = 1f;

    private (Scene Scene, GameObject Platform, Walker Walker) StandOnPlatform(ColliderShape shape, Float3 offset = default, float size = 4f)
    {
        Scene scene = WorldWithFloor();
        GameObject platform = Box(scene, new Float3(0f, PlatformTop - 0.25f, 0f), new Float3(size, 0.5f, size), name: "Platform");
        Walker walker = Spawn(scene, new Float3(offset.X, PlatformTop, offset.Z), shape);
        Assert.True(walker.Grounded, "did not settle on the platform");
        return (scene, platform, walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ASlidingPlatformCarriesWhatStandsOnIt(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape);
        walker.ResetCounts();

        walker.Run(Float3.Zero, 2f, () => MoveBy(platform, East * 3f * Dt));

        Assert.True(MathF.Abs(walker.Position.X - LocalPosition(platform).X) < 0.05f, $"fell behind to {walker.Position}, the platform is at {LocalPosition(platform)}");
        Assert.Equal(walker.Frames, walker.GroundedFrames);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ARisingLiftLiftsWithoutTheControllerSinkingIn(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape);
        walker.ResetCounts();

        walker.Run(Float3.Zero, 1.5f, () => MoveBy(platform, Float3.UnitY * 2f * Dt));

        float top = LocalPosition(platform).Y + 0.25f;
        Assert.True(MathF.Abs(walker.Position.Y - top) < 0.05f, $"stood at {walker.Position.Y} on a lift whose top is {top}");
        Assert.Equal(walker.Frames, walker.GroundedFrames);
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AFallingLiftKeepsTheControllerOnIt(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape);
        MoveBy(platform, Float3.UnitY * 4f);
        walker.Teleport(walker.Position + Float3.UnitY * 4f);
        walker.Settle();
        walker.ResetCounts();

        walker.Run(Float3.Zero, 1f, () => MoveBy(platform, -(Float3.UnitY * 3f * Dt)));

        float top = LocalPosition(platform).Y + 0.25f;
        Assert.True(MathF.Abs(walker.Position.Y - top) < 0.05f, $"hovered at {walker.Position.Y} over a lift whose top is {top}");
        Assert.Equal(walker.Frames, walker.GroundedFrames);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ASpinningPlatformCarriesTheControllerRoundAndReportsTheTurn(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape, new Float3(2f, 0f, 0f), size: 6f);
        float yaw = 0f;
        float reported = 0f;

        walker.Run(Float3.Zero, 1f, () =>
        {
            yaw += 90f * Dt;
            SetRotation(platform, Quaternion.FromEuler(0f, yaw, 0f));
            reported += walker.Controller.GroundYawDelta;
        });
        reported += walker.Controller.GroundYawDelta;

        Float3 expected = Quaternion.FromEuler(0f, yaw, 0f) * new Float3(2f, 0f, 0f);
        Assert.True(MathF.Abs(walker.Position.X - expected.X) < 0.1f && MathF.Abs(walker.Position.Z - expected.Z) < 0.1f,
                    $"ended at {walker.Position}, the spot it stood on is now at {expected}");
        Assert.True(MathF.Abs(reported - yaw) < 2f, $"reported {reported} degrees of turn for a platform that turned {yaw}");
    }

    /// <summary>The controller only reports the turn. Turning its own Transform left a character facing the wrong way after stepping off.</summary>
    [Fact]
    public void TheControllerNeverTurnsItselfOnASpinningPlatform()
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(ColliderShape.Capsule, new Float3(1f, 0f, 0f), size: 6f);
        float yaw = 0f;

        walker.Run(Float3.Zero, 1f, () =>
        {
            yaw += 90f * Dt;
            SetRotation(platform, Quaternion.FromEuler(0f, yaw, 0f));
        });

        Float3 facing = walker.Controller.GameObject.Transform.Rotation * North;
        Assert.True(Float3.Dot(facing, North) > 0.999f, $"turned to {facing}");
    }

    /// <summary>
    /// Two rollers lying side by side and touching, both turning so their tops run the same way. The
    /// controller is carried into the crease between them and has to be handed over to the second.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void TouchingRollersHandTheControllerFromOneToTheNext(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        var rollers = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            rollers[i] = Cylinder(scene, new Float3(0f, 0.6f, i * 1.2f), 0.6f, 6f, Quaternion.FromEuler(0f, 0f, 90f), "Roller");
        }
        Walker walker = Spawn(scene, new Float3(0f, 1.2f, 0f), shape);
        Assert.True(walker.Grounded, "did not settle on the first roller");
        float spin = 0f;

        walker.Run(Float3.Zero, 3f, () =>
        {
            spin += 150f * Dt;
            foreach (GameObject roller in rollers)
                SetRotation(roller, Quaternion.FromEuler(spin, 0f, 0f) * Quaternion.FromEuler(0f, 0f, 90f));
        });

        Assert.True(walker.Position.Z > 1.2f, $"stuck between the rollers at {walker.Position}");
        AssertNotInside(walker);
    }

    [Fact]
    public void RidingCanBeSwitchedOff()
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(ColliderShape.Capsule);
        walker.Controller.RideMovingPlatforms = false;

        walker.Run(Float3.Zero, 0.3f, () => MoveBy(platform, East * 3f * Dt));

        Assert.True(MathF.Abs(walker.Position.X) < 0.05f, $"was carried to {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void APlatformCarryingTheControllerIntoAWallLeavesItAgainstTheWall(ColliderShape shape)
    {
        (Scene scene, GameObject platform, Walker walker) = StandOnPlatform(shape);
        Box(scene, new Float3(3f, PlatformTop + 1.5f, 0f), new Float3(0.5f, 2f, 8f), name: "Wall");

        walker.Run(Float3.Zero, 1.5f, () =>
        {
            MoveBy(platform, East * 2f * Dt);
            Assert.True(DeepestOverlap(walker.Controller) < 0.01f, $"was dragged into the wall at {walker.Position}");
        });

        Assert.True(walker.Position.X < 2.75f - 0.35f, $"went through the wall to {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void GroundVelocityIsThePlatformsAndVelocityIsTheControllersOwn(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape);

        walker.Run(Float3.Zero, 0.5f, () => MoveBy(platform, East * 3f * Dt));

        Assert.Equal(3f, walker.GroundVelocity.X, 1);
        Assert.True(Float3.Length(walker.Controller.Velocity) < 0.05f, $"reported its own velocity as {walker.Controller.Velocity}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingOnAMovingPlatformAddsToItsMotion(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape, new Float3(0f, 0f, -1.5f), size: 8f);

        walker.Run(North * 1.5f, 1f, () => MoveBy(platform, East * 2f * Dt));

        Assert.True(MathF.Abs(walker.Position.X - 2f) < 0.1f, $"ended at {walker.Position}");
        Assert.True(MathF.Abs(walker.Position.Z - 0f) < 0.1f, $"ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AKinematicBodyDrivenByVelocityCarriesTheController(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        GameObject platform = Box(scene, new Float3(0f, PlatformTop - 0.25f, 0f), new Float3(4f, 0.5f, 4f), name: "Platform");
        Rigidbody3D body = platform.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        Walker walker = Spawn(scene, new Float3(0f, PlatformTop, 0f), shape);

        body.LinearVelocity = ToWorld(new Float3(2f, 0f, 0f));
        for (int i = 0; i < 60; i++)
        {
            Tick(scene);
            walker.Step(Float3.Zero);
        }

        Assert.True(walker.Grounded);
        Assert.True(MathF.Abs(walker.Position.X - LocalPosition(platform).X) < 0.1f, $"stood at {walker.Position} on a platform at {LocalPosition(platform)}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void LeavingThePlatformInAJumpStopsTheRide(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape, size: 30f);
        walker.Jump();
        float tookOffAt = walker.Position.X;

        for (int i = 0; i < 20; i++)
        {
            MoveBy(platform, East * 3f * Dt);
            walker.Step(Float3.Zero);
        }

        Assert.True(MathF.Abs(walker.Position.X - tookOffAt) < 0.06f, $"was carried {walker.Position.X - tookOffAt} m while in the air");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AWallMovingIntoTheControllerPushesItAlong(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        GameObject wall = Box(scene, new Float3(-2f, 1.5f, 0f), new Float3(0.5f, 3f, 6f), name: "Pusher");
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(Float3.Zero, 1.5f, () => MoveBy(wall, East * 2f * Dt));

        AssertNotInside(walker);
        Assert.True(walker.Position.X > LocalPosition(wall).X + 0.25f + 0.3f, $"was left at {walker.Position} with the wall at {LocalPosition(wall)}");
        Assert.True(walker.Grounded);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingOffTheEdgeOfAMovingPlatformDropsToTheFloor(ColliderShape shape)
    {
        (_, GameObject platform, Walker walker) = StandOnPlatform(shape);

        walker.Run(North * WalkSpeed, 1.5f, () => MoveBy(platform, East * 1f * Dt));

        Assert.True(walker.Position.Y < 0.05f && walker.Grounded, $"ended at {walker.Position}");
        Assert.True(walker.Position.Z > 3f);
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
    }
}

public sealed class PlatformsUpright() : PlatformsTests(Gravity.Upright);
public sealed class PlatformsUpsideDown() : PlatformsTests(Gravity.UpsideDown);
public sealed class PlatformsSideways() : PlatformsTests(Gravity.Sideways);
public sealed class PlatformsDiagonal() : PlatformsTests(Gravity.Diagonal);
