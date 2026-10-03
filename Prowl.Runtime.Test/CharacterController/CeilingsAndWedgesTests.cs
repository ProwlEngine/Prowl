// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;
using CollisionFlags = Prowl.Runtime.CharacterController.CollisionFlags;

namespace Prowl.Runtime.Test.Controller;

public abstract class CeilingsAndWedgesTests(Gravity gravity) : ControllerTestBase(gravity)
{
    /// <summary>
    /// Pressing into a ceiling that slopes down to the floor while walking at an angle to it has to
    /// slide along the line where the head meets it, not pin the controller in place.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 15f, 30f)]
    [InlineData(ColliderShape.Capsule, 15f, 60f)]
    [InlineData(ColliderShape.Capsule, 30f, 30f)]
    [InlineData(ColliderShape.Capsule, 30f, 60f)]
    [InlineData(ColliderShape.Capsule, 45f, 45f)]
    [InlineData(ColliderShape.Capsule, 60f, 30f)]
    [InlineData(ColliderShape.Capsule, 60f, 75f)]
    [InlineData(ColliderShape.Cylinder, 15f, 30f)]
    [InlineData(ColliderShape.Cylinder, 30f, 60f)]
    [InlineData(ColliderShape.Cylinder, 45f, 45f)]
    [InlineData(ColliderShape.Cylinder, 60f, 75f)]
    public void WalkingIntoASlopedCeilingAtAnAngleSlidesAlongIt(ColliderShape shape, float ceilingDegrees, float approachFromSide)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(0f, 0f, 2f), 2.4f, ceilingDegrees);
        Walker walker = Spawn(scene, new Float3(-6f, 0f, 0f), shape);

        float radians = approachFromSide * MathF.PI / 180f;
        Float3 direction = new(MathF.Sin(radians), 0f, MathF.Cos(radians));
        walker.Run(direction * WalkSpeed, 3f);

        float along = walker.Position.X + 6f;
        float expected = WalkSpeed * MathF.Sin(radians) * 3f;
        Assert.True(along > expected * 0.6f, $"only slid {along:0.00} m of the {expected:0.00} m its sideways motion asked for, ended at {walker.Position}");
        Assert.True(walker.Grounded, "was lifted off the floor");
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 20f)]
    [InlineData(ColliderShape.Capsule, 45f)]
    [InlineData(ColliderShape.Cylinder, 20f)]
    [InlineData(ColliderShape.Cylinder, 45f)]
    public void WalkingStraightIntoASlopedCeilingStopsAndCanBackOut(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(0f, 0f, 2f), 2.4f, degrees);
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 2f);
        float pressedAt = walker.Position.Z;
        Assert.True(walker.Position.Y < 0.05f, $"was pushed up or down to {walker.Position}");
        AssertNotInside(walker);

        walker.Run(South * WalkSpeed, 0.5f);
        Assert.True(walker.Position.Z < pressedAt - 2f, $"could not back away, ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingAlongUnderALeanToCeilingKeepsGoing(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(-1f, 0f, 0f), 2.3f, 35f, yaw: 90f);
        Walker walker = Spawn(scene, new Float3(0f, 0f, -10f), shape);

        walker.Run(Float3.Normalize(new Float3(0.4f, 0f, 1f)) * WalkSpeed, 3f);

        Assert.True(walker.Position.Z > 0f, $"got stuck under the lean-to at {walker.Position}");
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingIntoASlopedCeilingSlidesOffItAndLands(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(0f, 0f, -1f), 3.2f, 30f);
        Walker walker = Spawn(scene, new Float3(0f, 0f, 1f), shape);

        walker.Jump();
        walker.Run(Float3.Zero, 2f);

        Assert.True(walker.Grounded, $"never came back down, at {walker.Position}");
        Assert.True(walker.Position.Y < 0.05f);
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingIntoAFlatCeilingStopsTheClimbAndReportsIt(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 2.5f + 0.25f, 0f), new Float3(6f, 0.5f, 6f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        bool sawAbove = walker.Jump().HasFlag(CollisionFlags.Above);
        float highest = 0f;
        for (int i = 0; i < 90; i++)
        {
            sawAbove |= walker.Step(Float3.Zero).HasFlag(CollisionFlags.Above);
            highest = MathF.Max(highest, walker.Position.Y);
        }

        Assert.True(sawAbove, "hitting the ceiling was not reported");
        Assert.True(highest < 2.5f - 1.8f + 0.03f, $"rose to {highest}, into the ceiling");
        Assert.True(walker.Grounded);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ABeamLowerThanTheControllerBlocksIt(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 1.5f, 3f), new Float3(8f, 0.6f, 1f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 1.5f);

        Assert.True(walker.Position.Z < 2.5f - 0.3f, $"walked through the beam to {walker.Position}");
        Assert.True(walker.Position.Y < 0.05f, $"climbed to {walker.Position}");
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void CrouchingFitsUnderALowCeilingAndCannotStandUpUnderIt(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 1.3f + 0.5f, 5f), new Float3(8f, 1f, 4f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        Assert.True(walker.Controller.TrySetHeight(1.1f));
        walker.Run(North * WalkSpeed, 1f);
        Assert.True(walker.Position.Z > 4f, $"did not get under, ended at {walker.Position}");

        Assert.False(walker.Controller.TrySetHeight(1.8f), "stood up into the ceiling");
        Assert.Equal(1.1f, walker.Controller.Height, 3);

        walker.Run(North * WalkSpeed, 1f);
        Assert.True(walker.Controller.TrySetHeight(1.8f), "could not stand up once clear");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingUpARampIntoACeilingStopsWithoutSinkingIn(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 1f), 25f, 10f);
        Box(scene, new Float3(0f, 3.2f + 0.25f, 8f), new Float3(8f, 0.5f, 10f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 3f);

        AssertNotInside(walker);
        Assert.True(walker.Position.Y + 1.8f < 3.2f + 0.02f, $"head went into the ceiling at {walker.Position}");
        AssertCanStillMove(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 20f)]
    [InlineData(ColliderShape.Capsule, 50f)]
    [InlineData(ColliderShape.Cylinder, 20f)]
    [InlineData(ColliderShape.Cylinder, 50f)]
    public void WalkingIntoTheTipOfAVShapedWedgeStopsAndCanLeave(ColliderShape shape, float halfAngle)
    {
        Scene scene = WorldWithFloor();
        float length = 8f;
        Float3 tip = new(0f, 0f, 6f);
        foreach (float side in new[] { -1f, 1f })
        {
            float yaw = -side * halfAngle;
            Quaternion turn = Quaternion.FromEuler(0f, yaw, 0f);
            Float3 wallCenter = tip + turn * new Float3(side * 0.25f, 1.5f, -length * 0.5f);
            Box(scene, wallCenter, new Float3(0.5f, 3f, length), new Float3(0f, yaw, 0f), "Wedge Wall");
        }
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Run(North * WalkSpeed, 2f);

        AssertNotInside(walker);
        Assert.True(walker.Position.Z < tip.Z, $"went through the tip to {walker.Position}");
        AssertCanStillMove(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 0f)]
    [InlineData(ColliderShape.Capsule, 35f)]
    [InlineData(ColliderShape.Capsule, 80f)]
    [InlineData(ColliderShape.Cylinder, 0f)]
    [InlineData(ColliderShape.Cylinder, 35f)]
    [InlineData(ColliderShape.Cylinder, 80f)]
    public void AGapClosingToNothingNeverTrapsTheController(ColliderShape shape, float approach)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(0f, 0f, 2f), 2.2f, 12f);
        Walker walker = Spawn(scene, new Float3(-3f, 0f, 0f), shape);

        float radians = approach * MathF.PI / 180f;
        walker.Run(new Float3(MathF.Sin(radians), 0f, MathF.Cos(radians)) * WalkSpeed, 3f);

        AssertNotInside(walker);
        AssertFinite(walker);
        AssertCanStillMove(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ARoofOfTwoSlopesMeetingOverheadCanBeWalkedUnder(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        SlopedCeiling(scene, new Float3(0f, 0f, 0f), 2.6f, 30f, width: 40f, yaw: 90f);
        SlopedCeiling(scene, new Float3(0f, 0f, 0f), 2.6f, 30f, width: 40f, yaw: -90f);
        Walker walker = Spawn(scene, new Float3(0f, 0f, -12f), shape);

        walker.Run(Float3.Normalize(new Float3(0.3f, 0f, 1f)) * WalkSpeed, 4f);

        Assert.True(walker.Position.Z > 0f, $"stuck under the roof at {walker.Position}");
        AssertNotInside(walker);
    }
}

public sealed class CeilingsAndWedgesUpright() : CeilingsAndWedgesTests(Gravity.Upright);
public sealed class CeilingsAndWedgesUpsideDown() : CeilingsAndWedgesTests(Gravity.UpsideDown);
public sealed class CeilingsAndWedgesSideways() : CeilingsAndWedgesTests(Gravity.Sideways);
public sealed class CeilingsAndWedgesDiagonal() : CeilingsAndWedgesTests(Gravity.Diagonal);
