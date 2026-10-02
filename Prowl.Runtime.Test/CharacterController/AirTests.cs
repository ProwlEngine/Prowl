// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

public class AirTests : ControllerTestBase
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
        walker.Controller.Teleport(new Float3(0f, 3f, 0f));
        walker.Velocity = Float3.Zero;

        walker.Run(North * WalkSpeed, 1.5f);

        Assert.True(walker.Grounded);
        Assert.True(MathF.Abs(walker.Position.Z - WalkSpeed * 1.5f) < 0.1f, $"lost ground speed on landing, ended at {walker.Position}");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule, 30f)]
    [InlineData(ColliderShape.Cylinder, 30f)]
    [InlineData(ColliderShape.Capsule, 50f)]
    [InlineData(ColliderShape.Cylinder, 50f)]
    public void FallingOntoAWalkableSlopeLandsAndStays(ColliderShape shape, float degrees)
    {
        Scene scene = WorldWithFloor();
        Ramp(scene, new Float3(0f, 0f, -6f), degrees, 14f);
        Walker walker = Spawn(scene, new Float3(0f, 10f, 0f), shape);
        walker.Settle(1.5f);
        Float3 landed = walker.Position;

        walker.Settle(1f);

        Assert.True(walker.Grounded);
        Assert.True(Float3.Length(walker.Position - landed) < 0.01f, $"slid {walker.Position - landed} after landing");
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
    public void FallingIntoAVBetweenTwoSteepSlopesNeverTrapsIt(ColliderShape shape)
    {
        Scene scene = World();
        Ramp(scene, new Float3(0f, 0f, 0.3f), 65f, 6f);
        Ramp(scene, new Float3(0f, 0f, -0.3f), 65f, 6f, yaw: 180f);
        Box(scene, new Float3(0f, -2f, 0f), new Float3(6f, 1f, 6f));
        Walker walker = Spawn(scene, new Float3(0f, 6f, 0f), shape);

        walker.Settle(2f);

        AssertFinite(walker);
        AssertNotInside(walker);
        float before = walker.Position.Y;
        walker.Jump();
        walker.Run(Float3.Zero, 0.15f);
        Assert.True(walker.Position.Y > before + 0.3f || !walker.Grounded, $"could not even jump out of the V at {walker.Position}");
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

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingFromALedgeEdgeStillJumps(ColliderShape shape)
    {
        Scene scene = WorldWithFloor();
        Box(scene, new Float3(0f, 0.5f, -3f), new Float3(4f, 1f, 6f));
        Walker walker = Spawn(scene, new Float3(0f, 1f, 0f), shape);
        Assert.True(walker.Grounded, "a controller over the very edge of a ledge should still stand on it");

        walker.Jump();
        walker.Run(Float3.Zero, 0.2f);

        Assert.True(walker.Position.Y > 1.5f, $"only rose to {walker.Position.Y}");
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
}
