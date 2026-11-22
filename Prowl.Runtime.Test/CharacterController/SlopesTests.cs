// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

public abstract class SlopesTests(Gravity gravity) : ControllerTestBase(gravity)
{
    [Theory]
    [InlineData(ColliderShape.Capsule, 30f)]
    [InlineData(ColliderShape.Capsule, 50f)]
    [InlineData(ColliderShape.Cylinder, 30f)]
    [InlineData(ColliderShape.Cylinder, 50f)]
    public void WalksUpAWalkableSlope(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 1f), degrees, 6f);
        Walker walker = Spawn(scene, Float3.Zero, shape);
        Float3 start = walker.Position;

        walker.Run(North * WalkSpeed, 0.9f);

        float expected = RampHeight(degrees, walker.Position.Z - 1f);
        if (shape == ColliderShape.Cylinder) expected += walker.Controller.Radius * MathF.Tan(degrees * MathF.PI / 180f);
        Assert.True(walker.Grounded, $"lost the ground at {walker.Position}");
        float alongSlope = WalkSpeed * 0.9f - 0.62f;
        Assert.True(walker.Position.Z > 0.62f + alongSlope * MathF.Cos(degrees * MathF.PI / 180f) * 0.9f, $"slowed to a crawl, ended at {walker.Position}");
        Assert.True(MathF.Abs(walker.Position.Y - expected) < 0.25f, $"at {walker.Position} the slope is {expected:0.00} high");
        float travelled = Float3.Length(walker.Position - start);
        Assert.True(travelled < WalkSpeed * 0.9f * 1.05f, $"covered {travelled:0.00} m, faster up the slope than walking on the flat");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 45f)]
    [InlineData(ColliderShape.Cylinder, 45f)]
    public void WalkingDownASlopeNeverLeavesIt(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 0f), degrees, 8f);
        float top = RampHeight(degrees, 8f * MathF.Cos(degrees * MathF.PI / 180f));
        Walker walker = Spawn(scene, new Float3(0f, top - 0.6f, 8f * MathF.Cos(degrees * MathF.PI / 180f) - 0.6f), shape);
        Assert.True(walker.Grounded, $"did not settle on the slope, at {walker.Position}");
        walker.ResetCounts();

        walker.Run(South * WalkSpeed, 1f);

        Assert.True(walker.GroundedFrames >= walker.Frames - 1, $"bounced off the slope for {walker.Frames - walker.GroundedFrames} frames");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 50f)]
    [InlineData(ColliderShape.Cylinder, 50f)]
    public void StandingStillOnAWalkableSlopeDoesNotSlide(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, -5f), degrees, 12f);
        Walker walker = Spawn(scene, new Float3(0f, 5f, 0f), shape);
        walker.Settle(1f);
        Float3 settled = walker.Position;

        walker.Settle(3f);

        Assert.True(walker.Grounded);
        Assert.True(Float3.Length(walker.Position - settled) < 0.01f, $"slid {walker.Position - settled} in three seconds");
    }



    [Fact]
    public void LoweringTheSteepestWalkableSlopeMakesAShallowRampUnclimbable()
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 1f), 40f, 6f);
        Walker walker = Spawn(scene, Float3.Zero);
        walker.Controller.MaxSlopeAngle = 35f;

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Y < 0.35f, $"climbed to {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void RunningOverACrestStaysOnTheGround(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 0f), 25f, 4f);
        float run = 4f * MathF.Cos(25f * MathF.PI / 180f);
        float top = RampHeight(25f, run);
        Box(scene, new Float3(0f, top * 0.5f, run + 4f), new Float3(6f, top, 8f));
        Walker walker = Spawn(scene, new Float3(0f, 0f, -1f), shape);
        walker.ResetCounts();

        walker.Run(North * 8f, 1.2f);

        Assert.True(walker.Position.Z > run + 1f, $"did not get over the crest, ended at {walker.Position}");
        Assert.True(walker.GroundedFrames >= walker.Frames - 1, $"took off over the crest for {walker.Frames - walker.GroundedFrames} frames");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingAcrossASlopeKeepsItsHeight(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, -6f), 30f, 14f, width: 30f);
        Walker walker = Spawn(scene, new Float3(-8f, 4f, 0f), shape);
        Float3 start = walker.Position;

        walker.Run(East * WalkSpeed, 2f);

        Assert.True(walker.Position.X - start.X > 8f, $"stalled at {walker.Position}");
        Assert.True(MathF.Abs(walker.Position.Y - start.Y) < 0.1f, $"drifted from {start.Y} to {walker.Position.Y}");
        Assert.True(walker.Grounded);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingIntoASteepSlopeAtAnAngleSlidesAlongItWithoutRising(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 2f), 70f, 6f, width: 40f);
        Walker walker = Spawn(scene, new Float3(-8f, 0f, 0f), shape);

        walker.Run(Float3.Normalize(new Float3(1f, 0f, 1f)) * WalkSpeed, 3f);

        Assert.True(walker.Position.X > 0f, $"stopped against the slope at {walker.Position}");
        Assert.True(walker.Position.Y < 0.35f, $"rose up the slope to {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AValleyBetweenTwoWalkableSlopesIsCrossed(ColliderShape shape)
    {
        Scene scene = World();
        Box(scene, new Float3(0f, -0.5f, 0f), new Float3(10f, 1f, 2f));
        Ramp(scene, new Float3(0f, 0f, 1f), 30f, 6f);
        Ramp(scene, new Float3(0f, 0f, -1f), 30f, 6f, yaw: 180f);
        Walker walker = Spawn(scene, new Float3(0f, 2.5f, -4f), shape);

        walker.Run(North * WalkSpeed, 2f);

        Assert.True(walker.Position.Z > 3f, $"stuck in the valley at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void ARoofShapedRidgeCanBeWalkedOver(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 0f), 20f, 4f);
        float run = 4f * MathF.Cos(20f * MathF.PI / 180f);
        Ramp(scene, new Float3(0f, 0f, 2f * run), 20f, 4f, yaw: 180f);
        Walker walker = Spawn(scene, new Float3(0f, 0f, -1f), shape);

        walker.Run(North * WalkSpeed, 3f);

        Assert.True(walker.Position.Z > 2f * run + 1f, $"stuck on the ridge at {walker.Position}");
        Assert.True(walker.Position.Y < 0.1f);
    }


    /// <summary>
    /// Running into a slope too steep to stand on carries some of the speed up it, the way a quarter
    /// pipe does, then gravity brings the controller back down, rather than it stopping dead.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 60f)]
    [InlineData(ColliderShape.Cylinder, 60f)]
    public void RunningIntoASteepSlopeCarriesUpItThenSlidesBack(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 2f), 40f, 1.5f);
        float run = 1.5f * MathF.Cos(40f * MathF.PI / 180f);
        float rise = RampHeight(40f, run);
        Ramp(scene, new Float3(0f, rise, 2f + run), degrees, 8f);
        Box(scene, new Float3(0f, rise * 0.5f - 0.2f, 2f + run + 0.5f), new Float3(6f, rise, 1f));
        Walker walker = Spawn(scene, Float3.Zero, shape);
        walker.KeepsMomentum = true;
        float highest = 0f;

        walker.Run(North * 7f, 0.6f, () => highest = MathF.Max(highest, walker.Position.Y));
        walker.Run(Float3.Zero, 2.5f, () => highest = MathF.Max(highest, walker.Position.Y));

        Assert.True(highest > rise + 0.1f, $"stopped dead at the steep part, it only rose to {highest:0.00} with the steep part starting at {rise:0.00}");
        Assert.True(walker.Grounded && walker.Position.Y < rise + 0.15f, $"stayed up the steep slope at {walker.Position}");
    }

    /// <summary>
    /// Holding forward into a steep slope, with a game loop that keeps the upward speed the controller
    /// manages, must not pump it higher and higher: pushing into the face adds no height.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 60f)]
    [InlineData(ColliderShape.Capsule, 80f)]
    [InlineData(ColliderShape.Cylinder, 60f)]
    [InlineData(ColliderShape.Cylinder, 80f)]
    public void HoldingForwardIntoASteepSlopeGainsNoHeight(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 2f), degrees, 10f);
        Walker walker = Spawn(scene, Float3.Zero, shape);
        walker.KeepsMomentum = true;
        float highestEarly = 0f;
        float highestLate = 0f;

        walker.Run(North * 7f, 1f, () => highestEarly = MathF.Max(highestEarly, walker.Position.Y));
        walker.Run(North * 7f, 3f, () => highestLate = MathF.Max(highestLate, walker.Position.Y));

        Assert.True(highestLate <= highestEarly + 0.02f, $"kept climbing, from {highestEarly:0.00} in the first second to {highestLate:0.00} later");
        Assert.True(highestLate < 0.6f, $"was pumped up to {highestLate:0.00}");
        AssertNotInside(walker);
        AssertCanStillMove(walker);
    }

    /// <summary>
    /// Walking up a curve until it is too steep and holding walk there settles at the top of the
    /// walkable part, rather than hopping up the steep part and sliding back over and over.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void HoldingWalkWhereACurveGetsTooSteepSettlesWithoutJitter(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        float[] angles = [10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f];
        Float3 foot = new(0f, 0f, 2f);
        foreach (float angle in angles)
        {
            Ramp(scene, foot, angle, 0.8f, thickness: 2f);
            float radians = angle * MathF.PI / 180f;
            foot += new Float3(0f, MathF.Sin(radians), MathF.Cos(radians)) * 0.8f;
        }
        Walker walker = Spawn(scene, Float3.Zero, shape);
        walker.KeepsMomentum = true;
        walker.Run(North * WalkSpeed, 2f);

        Float3 previous = walker.Position;
        float bob = 0f;
        walker.Run(North * WalkSpeed, 1.5f, () =>
        {
            bob = MathF.Max(bob, Float3.Length(walker.Position - previous));
            previous = walker.Position;
        });

        Assert.True(bob < 0.01f, $"jittered up to {bob:0.000} m a frame while holding walk at the top, at {walker.Position}");
    }

    /// <summary>
    /// Walking up a ramp along its side and drifting off it keeps walking smoothly along the edge and
    /// drops to the floor once the shape is past it, rather than hanging on or being pulled sideways.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 20f)]
    [InlineData(ColliderShape.Cylinder, 20f)]
    public void DriftingOffTheSideOfARampDropsOffCleanly(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 1f), degrees, 12f, width: 3f);
        Walker walker = Spawn(scene, new Float3(1.1f, 0f, 0f), shape);
        walker.KeepsMomentum = true;
        Float3 walk = Float3.Normalize(new Float3(0.12f, 0f, 1f)) * WalkSpeed;
        float holdsUntil = 1.5f + walker.Controller.Radius + 0.05f;
        int hanging = 0;
        float slowest = float.MaxValue;
        Float3 previous = walker.Position;
        bool first = true;

        // Each frame's check sees the move the previous frame made.
        walker.Run(walk, 3f, () =>
        {
            if (walker.Grounded && walker.Position.X > holdsUntil && walker.Position.Y > 0.3f) hanging++;
            if (!first) slowest = MathF.Min(slowest, Horizontal(walker.Position - previous));
            first = false;
            previous = walker.Position;
        });

        Assert.Equal(0, hanging);
        Assert.True(slowest > Float3.Length(walk) * Dt * 0.4f, $"stalled to {slowest:0.000} m in a frame on the way off");
        Assert.True(walker.Grounded && walker.Position.Y < 0.05f, $"never got down to the floor, ended at {walker.Position}");
    }

    private static float Horizontal(Float3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}

public sealed class SlopesUpright() : SlopesTests(Gravity.Upright);
public sealed class SlopesUpsideDown() : SlopesTests(Gravity.UpsideDown);
public sealed class SlopesSideways() : SlopesTests(Gravity.Sideways);
public sealed class SlopesDiagonal() : SlopesTests(Gravity.Diagonal);
