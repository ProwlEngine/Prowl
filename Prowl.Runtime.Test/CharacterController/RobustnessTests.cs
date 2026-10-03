// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

/// <summary>Corners, seams, bad input and cluttered worlds: the places a controller gets stuck or ends up inside things.</summary>
public abstract class RobustnessTests(Gravity gravity) : ControllerTestBase(gravity)
{
    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void PressingIntoACornerHoldsStillAndCanLeave(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 1.5f, 3f), new Float3(8f, 3f, 0.5f));
        Box(scene, new Float3(3f, 1.5f, 0f), new Float3(0.5f, 3f, 8f));
        Walker walker = Spawn(scene, Float3.Zero, shape);
        Float3 corner = Float3.Normalize(new Float3(1f, 0f, 1f)) * WalkSpeed;

        walker.Run(corner, 1.5f);
        Float3 pressed = walker.Position;
        float drift = 0f;
        walker.Run(corner, 0.5f, () => drift = MathF.Max(drift, Float3.Length(walker.Position - pressed)));

        Assert.True(drift < 0.002f, $"jittered {drift} m in the corner");
        AssertNotInside(walker);
        AssertCanStillMove(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingIntoARoundPillarSlipsAroundIt(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Pillar(scene, new Float3(0f, 0f, 3f), 0.5f, 3f);
        Walker walker = Spawn(scene, new Float3(0.15f, 0f, 0f), shape);

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Z > 4.5f, $"stuck on the pillar at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ACorridorJustWideEnoughIsWalkedThrough(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        const float Gap = 0.86f;
        Box(scene, new Float3(-Gap * 0.5f - 0.5f, 1.5f, 6f), new Float3(1f, 3f, 6f));
        Box(scene, new Float3(Gap * 0.5f + 0.5f, 1.5f, 6f), new Float3(1f, 3f, 6f));
        Walker walker = Spawn(scene, new Float3(0.1f, 0f, 0f), shape);

        walker.Run(Float3.Normalize(new Float3(0.03f, 0f, 1f)) * WalkSpeed, 3f);

        Assert.True(walker.Position.Z > 10f, $"did not get through, ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AGapNarrowerThanTheControllerIsNotSqueezedThrough(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        const float Gap = 0.6f;
        Box(scene, new Float3(-Gap * 0.5f - 0.5f, 1.5f, 3f), new Float3(1f, 3f, 1f));
        Box(scene, new Float3(Gap * 0.5f + 0.5f, 1.5f, 3f), new Float3(1f, 3f, 1f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Z < 3f, $"squeezed through to {walker.Position}");
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ABrokenMotionIsIgnored(float bad)
    {
        Scene scene = WorldWithFloor();
        Walker walker = Spawn(scene, Float3.Zero);
        Float3 before = walker.Position;

        walker.Move(new Float3(bad, 0f, 1f));

        AssertFinite(walker);
        Assert.True(Float3.Length(walker.Position - before) < 0.01f, $"moved to {walker.Position}");
        Assert.True(walker.Grounded);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AHugeMoveDoesNotTunnelThroughAThinWall(ColliderShape shape)
    {
        Scene scene = WorldWithFloor(400f);
        Box(scene, new Float3(0f, 1.5f, 5f), new Float3(8f, 3f, 0.05f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Move(new Float3(0f, -0.1f, 150f));

        Assert.True(walker.Position.Z < 5f, $"went through to {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void StartingInsideACrateIsPushedOut(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0.3f, 0.5f, 0f), new Float3(1f, 1f, 1f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(Float3.Zero, 0.3f);

        AssertNotInside(walker);
        AssertCanStillMove(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void TeleportedIntoTheMouthOfAWedgeItWorksItsWayOut(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(0f, 0f, 0f), 2f, 25f);
        Walker walker = Spawn(scene, new Float3(0f, 0f, -3f), shape);

        walker.Teleport(new Float3(0f, 0f, 1.2f));
        walker.Run(South * WalkSpeed, 1f);

        AssertFinite(walker);
        AssertNotInside(walker);
        Assert.True(walker.Position.Z < -1f, $"never got out, ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 10f)]
    [InlineData(ColliderShape.Capsule, 40f)]
    [InlineData(ColliderShape.Cylinder, 10f)]
    [InlineData(ColliderShape.Cylinder, 40f)]
    public void WalkingIntoAWallAtAShallowAngleKeepsTheSpeedAlongIt(ColliderShape shape, float angle)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(1f, 1.5f, 0f), new Float3(0.5f, 3f, 60f));
        Walker walker = Spawn(scene, new Float3(0f, 0f, -20f), shape);

        float radians = angle * MathF.PI / 180f;
        walker.Run(new Float3(MathF.Sin(radians), 0f, MathF.Cos(radians)) * WalkSpeed, 2f);

        float along = walker.Position.Z + 20f;
        Assert.True(along > WalkSpeed * MathF.Cos(radians) * 2f * 0.97f, $"only covered {along:0.00} m along the wall");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AWallBuiltFromSeparateBlocksDoesNotSnag(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        for (int i = 0; i < 30; i++)
            Box(scene, new Float3(1f, 1.5f, -15f + i + 0.5f), new Float3(0.5f, 3f, 1f), name: "Block");
        Walker walker = Spawn(scene, new Float3(0f, 0f, -12f), shape);

        walker.Run(Float3.Normalize(new Float3(0.5f, 0f, 1f)) * WalkSpeed, 3f);

        float expected = WalkSpeed * (1f / MathF.Sqrt(1.25f)) * 3f;
        Assert.True(walker.Position.Z + 12f > expected * 0.95f, $"snagged on the seams, covered {walker.Position.Z + 12f:0.00} m of {expected:0.00}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 0f)]
    [InlineData(ColliderShape.Capsule, 0.01f)]
    [InlineData(ColliderShape.Cylinder, 0f)]
    [InlineData(ColliderShape.Cylinder, 0.01f)]
    public void AFloorOfTilesIsCrossedSmoothly(ColliderShape shape, float unevenness)
    {
        Scene scene = World();
        for (int x = -3; x <= 6; x++)
            for (int z = -2; z < 20; z++)
                Box(scene, new Float3(x, -0.5f + ((x + z) % 2 == 0 ? unevenness : 0f), z + 0.5f), new Float3(1f, 1f, 1f), name: "Tile");
        Walker walker = Spawn(scene, new Float3(0.5f, 0.05f, 0f), shape);
        walker.ResetCounts();

        walker.Run(Float3.Normalize(new Float3(0.2f, 0f, 1f)) * WalkSpeed, 2.5f);

        Assert.True(walker.Position.Z > WalkSpeed * 2.5f * 0.95f, $"stumbled, ended at {walker.Position}");
        Assert.Equal(walker.Frames, walker.GroundedFrames);
    }

    [Fact]
    public void TheControllersOwnRotationDoesNotChangeHowItMoves()
    {
        Scene scene = WorldWithFloor();
        Walker walker = Spawn(scene, Float3.Zero);
        walker.Controller.GameObject.Transform.Rotation = Quaternion.FromEuler(0f, 133f, 0f);

        walker.Run(North * WalkSpeed, 1f);

        Assert.True(MathF.Abs(walker.Position.X) < 0.01f && MathF.Abs(walker.Position.Z - WalkSpeed) < 0.05f, $"ended at {walker.Position}");
    }

    [Fact]
    public void StandingStillInTheOpenTouchesNothingButTheFloor()
    {
        Scene scene = WorldWithFloor();
        Walker walker = Spawn(scene, Float3.Zero);

        CharacterController.CollisionFlags flags = walker.Move(new Float3(0f, -0.01f, 0f));

        Assert.Equal(CharacterController.CollisionFlags.Below, flags);
        Assert.Single(walker.Controller.Hits);
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
    public void IgnoredBodiesAreWalkedThrough()
    {
        Scene scene = WorldWithFloor();
        GameObject crate = Box(scene, new Float3(0f, 1f, 2f), new Float3(2f, 2f, 1f));
        Rigidbody3D body = crate.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Static;
        Walker walker = Spawn(scene, Float3.Zero);
        walker.Controller.IgnoreCollisionWith(body);

        walker.Run(North * WalkSpeed, 1f);

        Assert.True(walker.Position.Z > 4f, $"was blocked by an ignored body at {walker.Position}");
    }

    /// <summary>
    /// A cluttered world of tilted blocks, ramps, ceilings and pillars, walked through at random with
    /// random jumps. Every frame it has to stay finite and out of everything, and whenever the way it
    /// is asked to walk is clear it has to actually go that way.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 1)]
    [InlineData(ColliderShape.Capsule, 2)]
    [InlineData(ColliderShape.Capsule, 3)]
    [InlineData(ColliderShape.Capsule, 4)]
    [InlineData(ColliderShape.Cylinder, 1)]
    [InlineData(ColliderShape.Cylinder, 2)]
    [InlineData(ColliderShape.Cylinder, 3)]
    [InlineData(ColliderShape.Cylinder, 4)]
    public void WanderingAClutteredWorldNeverGetsStuckOrInside(ColliderShape shape, int seed)
    {
        var random = new Random(seed);
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        Scene scene = WorldWithFloor(60f);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(scene, new Float3(side * 20f, 2f, 0f), new Float3(1f, 4f, 41f));
            Box(scene, new Float3(0f, 2f, side * 20f), new Float3(41f, 4f, 1f));
        }

        for (int i = 0; i < 18; i++)
        {
            Float3 at = new(Range(-17f, 17f), 0f, Range(-17f, 17f));
            if (Float3.Length(at) < 3f) continue;
            Float3 size = new(Range(0.3f, 3f), Range(0.1f, 2.5f), Range(0.3f, 3f));
            Box(scene, at + new Float3(0f, size.Y * 0.5f - Range(0f, 0.3f), 0f), size, new Float3(Range(-25f, 25f), Range(0f, 360f), Range(-25f, 25f)), "Block");
        }
        for (int i = 0; i < 5; i++)
            Ramp(scene, new Float3(Range(-15f, 15f), 0f, Range(-15f, 15f)), Range(10f, 70f), Range(2f, 6f), Range(1.5f, 4f), yaw: Range(0f, 360f));
        for (int i = 0; i < 4; i++)
            SlopedCeiling(scene, new Float3(Range(-15f, 15f), 0f, Range(-15f, 15f)), Range(1.6f, 2.8f), Range(10f, 60f), Range(2f, 6f), Range(0f, 360f));
        for (int i = 0; i < 5; i++)
            Pillar(scene, new Float3(Range(-16f, 16f), 0f, Range(-16f, 16f)), Range(0.2f, 1f), Range(1f, 4f));

        Walker walker = Spawn(scene, Float3.Zero, shape);
        Float3 walk = Float3.Zero;
        int stalls = 0;
        int checks = 0;

        for (int frame = 0; frame < 60 * 25; frame++)
        {
            if (frame % 30 == 0)
            {
                float angle = Range(0f, MathF.PI * 2f);
                walk = new Float3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * Range(2f, 7f);
            }

            Float3 direction = Float3.Normalize(walk);
            bool clear = walker.Grounded && !walker.Cast(direction, Float3.Length(walk) * Dt + 0.05f);
            Float3 before = walker.Position;

            walker.Step(walk, jump: random.NextDouble() < 0.01);

            AssertFinite(walker);
            float deepest = DeepestOverlap(walker.Controller);
            Assert.True(deepest < 0.02f, $"frame {frame}: {deepest:0.000} m inside something at {walker.Position}");
            Assert.True(walker.Position.Y > -0.05f, $"frame {frame}: fell through the floor to {walker.Position}");

            if (clear)
            {
                checks++;
                Float3 moved = walker.Position - before;
                if (Float3.Dot(moved, direction) < Float3.Length(walk) * Dt * 0.5f) stalls++;
            }
        }

        Assert.True(checks > 100, $"only {checks} frames had a clear path, the world is too cluttered to test anything");
        Assert.True(stalls <= checks / 50, $"stalled on {stalls} of {checks} frames where the way ahead was clear");
    }
}

public sealed class RobustnessUpright() : RobustnessTests(Gravity.Upright);
public sealed class RobustnessUpsideDown() : RobustnessTests(Gravity.UpsideDown);
public sealed class RobustnessSideways() : RobustnessTests(Gravity.Sideways);
public sealed class RobustnessDiagonal() : RobustnessTests(Gravity.Diagonal);
