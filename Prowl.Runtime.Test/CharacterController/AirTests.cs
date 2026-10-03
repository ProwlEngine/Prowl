// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

public abstract class AirTests(Gravity gravity) : ControllerTestBase(gravity)
{
    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AJumpLeavesTheGroundOnTheFirstFrameAndLandsAgain(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Jump();
        Assert.False(walker.Grounded, "still grounded after the jump frame");

        float highest = 0f;
        walker.Run(Float3.Zero, 1.5f, () => highest = MathF.Max(highest, walker.Position.Y));

        float expected = walker.JumpSpeed * walker.JumpSpeed / (2f * walker.Gravity);
        Assert.True(MathF.Abs(highest - expected) < 0.15f, $"peaked at {highest}, the jump should reach {expected}");
        Assert.True(walker.Grounded);
        Assert.True(walker.Position.Y < 0.03f);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void LandingWhileRunningKeepsRunning(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Walker walker = Spawn(scene, new Float3(0f, 3f, 0f), shape);
        walker.Teleport(new Float3(0f, 3f, 0f));
        walker.Velocity = Float3.Zero;

        walker.Run(North * WalkSpeed, 1.5f);

        Assert.True(walker.Grounded);
        Assert.True(MathF.Abs(walker.Position.Z - WalkSpeed * 1.5f) < 0.1f, $"lost ground speed on landing, ended at {walker.Position}");
    }


    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void FallingOntoASteepSlopeSlidesDownItRatherThanSticking(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, -2f), 70f, 8f);
        Walker walker = Spawn(scene, new Float3(0f, 10f, 0f), shape);
        int groundedOnSlope = 0;

        walker.Run(Float3.Zero, 3f, () => { if (walker.Grounded && walker.Position.Y > 0.3f) groundedOnSlope++; });

        Assert.Equal(0, groundedOnSlope);
        Assert.True(walker.Position.Y < 0.05f && walker.Grounded, $"ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingOffALedgeTallerThanTheSnapFalls(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 1f, -3f), new Float3(4f, 2f, 6f));
        Walker walker = Spawn(scene, new Float3(0f, 2f, -1f), shape);
        bool leftTheGround = false;

        walker.Run(North * WalkSpeed, 1f, () => leftTheGround |= !walker.Grounded);

        Assert.True(leftTheGround, "never left the ground walking off a two metre ledge");
        Assert.True(walker.Position.Y < 0.03f && walker.Grounded, $"ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingOffALedgeWithinTheSnapStaysGrounded(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.2f, -3f), new Float3(4f, 0.4f, 6f));
        Walker walker = Spawn(scene, new Float3(0f, 0.4f, -1f), shape);
        walker.ResetCounts();

        walker.Run(North * WalkSpeed, 0.6f);

        Assert.Equal(walker.Frames, walker.GroundedFrames);
        Assert.True(walker.Position.Y < 0.03f, $"ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void TurningOffTheSnapLetsItFallOffEvenASmallLedge(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.25f, -3f), new Float3(4f, 0.5f, 6f));
        Walker walker = Spawn(scene, new Float3(0f, 0.5f, -1f), shape);
        walker.Controller.SnapDownDistance = 0f;
        bool leftTheGround = false;

        walker.Run(North * WalkSpeed, 0.6f, () => leftTheGround |= !walker.Grounded);

        Assert.True(leftTheGround);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingIntoAWallSlidesDownItAndLands(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 3f, 2f), new Float3(8f, 6f, 1f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Jump(North * WalkSpeed);
        walker.Run(North * WalkSpeed, 1.5f);

        Assert.True(walker.Grounded && walker.Position.Y < 0.03f, $"ended at {walker.Position}");
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingAlongAWallKeepsTheSidewaysSpeed(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(1f, 3f, 0f), new Float3(1f, 6f, 30f));
        Walker walker = Spawn(scene, Float3.Zero, shape);

        walker.Jump(Float3.Normalize(new Float3(1f, 0f, 1f)) * WalkSpeed);
        walker.Run(Float3.Normalize(new Float3(1f, 0f, 1f)) * WalkSpeed, 0.6f);

        float expected = WalkSpeed * MathF.Sqrt(0.5f) * (0.6f + Dt);
        Assert.True(walker.Position.Z > expected * 0.95f, $"lost speed along the wall, ended at {walker.Position}");
    }


    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void AHighFallOntoThinGroundDoesNotPassThrough(ColliderShape shape)
    {
        Scene scene = World();
        Box(scene, new Float3(0f, -0.05f, 0f), new Float3(10f, 0.1f, 10f));
        Walker walker = Spawn(scene, new Float3(0f, 40f, 0f), shape);
        walker.Gravity = 60f;

        walker.Settle(3f);

        Assert.True(walker.Grounded && walker.Position.Y > -0.01f, $"ended at {walker.Position}");
    }


    /// <summary>Resting in a V too steep to stand on is still resting, so it can jump and does not build up a fall.</summary>
    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void RestingInASteepVCountsAsGround(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 0.3f), 65f, 5f, width: 6f);
        Ramp(scene, new Float3(0f, 0f, -0.3f), 65f, 5f, width: 6f, yaw: 180f);
        Walker walker = Spawn(scene, new Float3(0f, 3f, 0f), shape);

        walker.Settle(1.5f);

        Assert.True(walker.Grounded, $"resting in the V at {walker.Position} is not grounded");
        Assert.True(walker.Velocity.Y > -1.5f, $"built up a fall of {walker.Velocity.Y} m/s while resting");
        AssertFinite(walker);
        AssertNotInside(walker);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingOutOfASteepVLeavesAtWalkingSpeed(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, 0.3f), 65f, 5f, width: 6f);
        Ramp(scene, new Float3(0f, 0f, -0.3f), 65f, 5f, width: 6f, yaw: 180f);
        Walker walker = Spawn(scene, new Float3(0f, 3f, 0f), shape);
        walker.KeepsMomentum = true;
        walker.Settle(3f);

        Float3 previous = walker.Position;
        float fastest = 0f;
        walker.Run(East * WalkSpeed, 1.5f, () =>
        {
            fastest = MathF.Max(fastest, Float3.Length(walker.Position - previous) / Dt);
            previous = walker.Position;
        });

        Assert.True(walker.Position.X > 3.5f, $"never got out, ended at {walker.Position}");
        Assert.True(fastest < WalkSpeed * 1.5f, $"was shot out at {fastest:0.0} m/s");
    }

    /// <summary>Walked out to an edge, right to it or with part of the shape over the drop, it is still standing on the edge, so it can still jump.</summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 0f)]
    [InlineData(ColliderShape.Capsule, 0.6f)]
    [InlineData(ColliderShape.Capsule, 0.9f)]
    [InlineData(ColliderShape.Cylinder, 0f)]
    [InlineData(ColliderShape.Cylinder, 0.6f)]
    [InlineData(ColliderShape.Cylinder, 0.9f)]
    public void HangingPartlyOverAnEdgeStillStandsAndCanJump(ColliderShape shape, float overhang)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 1f, -3f), new Float3(4f, 2f, 6f));
        Walker walker = Spawn(scene, new Float3(0f, 2f, -2f), shape);
        while (walker.Position.Z < walker.Controller.Radius * overhang && walker.Frames < 300) walker.Step(North * 0.5f);
        walker.Settle();

        // A rounded bottom far over the edge rests on the corner a little below the top.
        Assert.True(walker.Grounded && walker.Position.Y > 2f - walker.Controller.Radius, $"{overhang:0%} of its radius over the edge it was not standing, at {walker.Position}");

        walker.Jump();
        walker.Run(Float3.Zero, 0.2f);
        Assert.True(walker.Position.Y > 2.4f, $"could not jump from the edge, only rose to {walker.Position.Y}");
    }


    /// <summary>
    /// Jumping at a block about as tall as the jump, still walking into it, ends either back on the floor
    /// or up on top, and is never held up partway as if a corner caught at the side were ground.
    /// </summary>
    [Theory]
    [InlineData(ColliderShape.Capsule, 1.0f)]
    [InlineData(ColliderShape.Capsule, 1.15f)]
    [InlineData(ColliderShape.Capsule, 1.25f)]
    [InlineData(ColliderShape.Capsule, 1.4f)]
    [InlineData(ColliderShape.Cylinder, 1.0f)]
    [InlineData(ColliderShape.Cylinder, 1.15f)]
    [InlineData(ColliderShape.Cylinder, 1.25f)]
    [InlineData(ColliderShape.Cylinder, 1.4f)]
    public void JumpingAtABlockAboutAsTallAsTheJumpNeverHangsOnItsEdge(ColliderShape shape, float height)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, height * 0.5f, 12f), new Float3(4f, height, 20f));
        Walker walker = Spawn(scene, new Float3(0f, 0f, 1f), shape);
        walker.Run(North * WalkSpeed, 0.1f);
        int hanging = 0;

        walker.Jump(North * WalkSpeed);
        walker.Run(North * WalkSpeed, 1.5f, () =>
        {
            if (walker.Grounded && walker.Position.Y > 0.1f && walker.Position.Y < height - 0.1f) hanging++;
        });

        float apex = walker.JumpSpeed * walker.JumpSpeed / (2f * walker.Gravity);
        Assert.Equal(0, hanging);
        Assert.True(walker.Grounded, $"never settled, at {walker.Position}");
        if (height > apex + 0.1f) Assert.True(walker.Position.Y < 0.05f, $"got onto a {height} m block with a {apex:0.00} m jump, at {walker.Position}");
        else Assert.True(walker.Position.Y < 0.05f || walker.Position.Y > height - 0.05f, $"ended partway up at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingAtABlockJustLowEnoughLandsOnTop(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.4f, 3.5f), new Float3(4f, 0.8f, 3f));
        Walker walker = Spawn(scene, new Float3(0f, 0f, 1f), shape);
        walker.Run(North * WalkSpeed, 0.1f);

        walker.Jump(North * WalkSpeed);
        walker.Run(North * WalkSpeed, 0.6f);

        Assert.True(walker.Grounded && walker.Position.Y > 0.75f, $"did not land on top, ended at {walker.Position}");
    }
}

public sealed class AirUpright() : AirTests(Gravity.Upright);
public sealed class AirUpsideDown() : AirTests(Gravity.UpsideDown);
public sealed class AirSideways() : AirTests(Gravity.Sideways);
public sealed class AirDiagonal() : AirTests(Gravity.Diagonal);
