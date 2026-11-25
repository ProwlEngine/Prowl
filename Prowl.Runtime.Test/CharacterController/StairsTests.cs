// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

public abstract class StairsTests(Gravity gravity) : ControllerTestBase(gravity)
{
    [Theory]
    [InlineData(ColliderShape.Capsule, 0.25f, 0.35f)]
    [InlineData(ColliderShape.Cylinder, 0.25f, 0.35f)]
    public void WalksUpAStaircase(ColliderShape shape, float rise, float run)
    {
        Scene scene = WorldWithFloor();
        Stairs(scene, new Float3(0f, 0f, 2f), 10, rise, run);
        Box(scene, new Float3(0f, rise * 10f * 0.5f, 2f + run * 10f + 10f), new Float3(3f, rise * 10f, 20f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        float top = rise * 10f;
        float highest = 0f;
        walker.Run(North * WalkSpeed, 1.5f, () => highest = MathF.Max(highest, walker.Position.Y));
        Assert.True(walker.Position.Z > WalkSpeed * 1.5f * 0.7f, $"lost most of its speed on the stairs, only covered {walker.Position.Z:0.00} m");
        walker.Run(North * WalkSpeed, 1.5f, () => highest = MathF.Max(highest, walker.Position.Y));

        Assert.True(highest < top + 0.1f, $"rose to {highest:0.00} over a landing at {top}");
        Assert.True(walker.Position.Y > top - 0.05f, $"stopped at {walker.Position} below the top at {rise * 10f}");
        Assert.True(walker.Grounded);
        AssertNotInside(walker);
    }


    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingDownStairsStaysOnThem(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 1.25f, -3f), new Float3(3f, 2.5f, 6f));
        Stairs(scene, new Float3(0f, 0f, 3.3f), 10, 0.25f, 0.33f, yaw: 180f);
        Walker walker = Spawn(scene, new Float3(0f, 2.5f, -2f), shape);
        Assert.True(walker.Grounded, "did not start on the landing");
        walker.ResetCounts();

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Y < 0.1f, $"did not reach the bottom, ended at {walker.Position}");
        Assert.True(walker.GroundedFrames >= walker.Frames - 2, $"left the stairs for {walker.Frames - walker.GroundedFrames} frames on the way down");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ClimbsStairsApproachedAtAnAngle(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Stairs(scene, new Float3(0f, 0f, 2f), 8, 0.2f, 0.4f, width: 12f);
        Box(scene, new Float3(0f, 0.8f, 2f + 3.2f + 3f), new Float3(12f, 1.6f, 6f));
        Walker walker = Spawn(scene, new Float3(-3f, 0f, 0f), shape);

        walker.Run(Float3.Normalize(new Float3(1f, 0f, 1f)) * WalkSpeed, 2.5f);

        Assert.True(walker.Position.Y > 1.55f, $"stopped at {walker.Position}");
    }



    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ABlockTallerThanTheStepSizeIsAWall(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.225f, 4f), new Float3(6f, 0.45f, 4f));
        Walker walker = Spawn(scene, Float3.Zero, shape);
        walker.Controller.StepSize = 0.3f;

        walker.Run(North * WalkSpeed, 1.5f);

        Assert.True(walker.Position.Y < 0.1f, $"climbed onto it, ended at {walker.Position}");
        Assert.True(walker.Position.Z < 2f - 0.3f, $"went into it, ended at {walker.Position}");
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AZeroStepSizeClimbsNothing(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.1f, 4f), new Float3(6f, 0.2f, 4f));
        Walker walker = Spawn(scene, Float3.Zero, shape);
        walker.Controller.StepSize = 0f;

        walker.Run(North * WalkSpeed, 1f);

        Assert.True(walker.Position.Y < 0.12f, $"climbed it anyway, ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AStepUpDoesNotPushIntoALowCeilingAboveIt(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.125f, 4f), new Float3(6f, 0.25f, 4f));
        Box(scene, new Float3(0f, 0.25f + 1.6f + 0.5f, 4f), new Float3(6f, 1f, 4f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 1.5f);

        AssertNotInside(walker);
        Assert.True(walker.Position.Y < 0.1f, $"squeezed under a ceiling too low for it, ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AStepDoesNotLeadOntoASlopeTooSteepToStandOn(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0.2f, 3f), 70f, 6f);
        Box(scene, new Float3(0f, 0.1f, 2.5f), new Float3(6f, 0.2f, 1f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Y < 0.8f, $"climbed the steep slope to {walker.Position}");
        AssertNotInside(walker);
        AssertCanStillMove(walker);
    }



    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingAlongTheEdgeOfAStepStaysOnTheFloor(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(1.5f, 0.15f, 0f), new Float3(2f, 0.3f, 20f));
        // Its side just brushes the step's edge at x 0.5.
        Walker walker = Spawn(scene, new Float3(0.12f, 0f, -6f), shape);

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Z > 3.5f, $"stalled at {walker.Position}");
        Assert.True(walker.Position.Y < 0.05f, $"climbed onto the step it only brushed, ended at {walker.Position}");
    }


    /// <summary>A step right at the step size, met from several angles, is climbed every time.</summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 0.3f, 0f)]
    [InlineData(ColliderShape.Capsule, 0.295f, 30f)]
    [InlineData(ColliderShape.Capsule, 0.3f, 60f)]
    [InlineData(ColliderShape.Cylinder, 0.3f, 0f)]
    [InlineData(ColliderShape.Cylinder, 0.295f, 30f)]
    [InlineData(ColliderShape.Cylinder, 0.3f, 60f)]
    public void AStepRightAtTheStepSizeIsClimbed(ColliderShape shape, float height, float approach)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, height * 0.5f, 6f), new Float3(30f, height, 8f));
        float radians = approach * MathF.PI / 180f;
        Float3 direction = new(MathF.Sin(radians), 0f, MathF.Cos(radians));

        for (float start = 0f; start < 0.1f; start += 0.023f)
        {
            Walker walker = Spawn(scene, new Float3(0f, 0f, start), shape);
            walker.Controller.StepSize = 0.3f;

            walker.Run(direction * WalkSpeed, 1.2f);

            Assert.True(walker.Position.Y > height - 0.03f, $"starting {start:0.000} m back it stopped at {walker.Position}");
        }
    }
}

public sealed class StairsUpright() : StairsTests(Gravity.Upright);
public sealed class StairsUpsideDown() : StairsTests(Gravity.UpsideDown);
public sealed class StairsSideways() : StairsTests(Gravity.Sideways);
public sealed class StairsDiagonal() : StairsTests(Gravity.Diagonal);
