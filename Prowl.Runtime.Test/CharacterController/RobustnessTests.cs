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
    [InlineData(ColliderShape.Capsule, 40f)]
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




}

public sealed class RobustnessUpright() : RobustnessTests(Gravity.Upright);
public sealed class RobustnessUpsideDown() : RobustnessTests(Gravity.UpsideDown);
public sealed class RobustnessSideways() : RobustnessTests(Gravity.Sideways);
public sealed class RobustnessDiagonal() : RobustnessTests(Gravity.Diagonal);
