// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

using ColliderShape = Prowl.Runtime.CharacterController.ColliderShape;

namespace Prowl.Runtime.Test.Controller;

/// <summary>
/// The character controller's own movement, which is swept by hand rather than simulated: it never
/// becomes a rigidbody, so every guarantee here is one this component makes for itself.
/// </summary>
public class CoreTests : RuntimeTestBase
{
    private Scene CreatePhysicsScene()
    {
        var scene = CreateScene(enable: true);
        scene.Physics.UseMultithreading = false;
        return scene;
    }

    private GameObject AddStaticBox(Scene scene, Float3 position, Float3 size)
    {
        var go = CreateGameObject("StaticBox");
        go.Transform.Position = position;
        go.AddComponent<BoxCollider>().Size = size;
        scene.Add(go);
        return go;
    }

    private CharacterController AddController(Scene scene, Float3 position)
    {
        var go = CreateGameObject("Player");
        go.Transform.Position = position;
        var cc = go.AddComponent<CharacterController>();
        scene.Add(go);
        return cc;
    }

    /// <summary>
    /// The bug this exists for: a controller that has ended up even slightly inside geometry stops
    /// every shape cast at zero distance, so it can neither move nor slide out and is stuck for good.
    /// A move has to push it clear first.
    /// </summary>
    [Fact]
    public void AControllerStartingInsideGeometry_PushesItselfOutAndCanMove()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        // Sunk a little way into the floor, which is where a slope plus rounding leaves it.
        var cc = AddController(scene, new Float3(0, -0.05f, 0));

        var before = new List<ShapeCastHit>();
        Assert.True(cc.OverlapNow(before) > 0, "the controller was expected to start inside the floor");

        cc.Move(new Float3(1, 0, 0) * 0.1f);

        var after = new List<ShapeCastHit>();
        Assert.Equal(0, cc.OverlapNow(after));
        Assert.True(cc.GameObject.Transform.Position.X > 0.0f, "the controller did not move at all");
    }

    /// <summary>Being pushed out must not send it through the surface it was inside.</summary>
    [Fact]
    public void DepenetrationPushesOutOfTheFloorRatherThanThroughIt()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        var cc = AddController(scene, new Float3(0, -0.05f, 0));
        cc.Move(Float3.Zero);

        Assert.True(cc.GameObject.Transform.Position.Y >= -0.05f,
                    $"it was pushed down to {cc.GameObject.Transform.Position.Y} instead of up out of the floor");
    }

    [Fact]
    public void AControllerWithNothingAroundItMovesTheWholeWay()
    {
        var scene = CreatePhysicsScene();
        var cc = AddController(scene, new Float3(0, 10, 0));

        CharacterController.CollisionFlags flags = cc.Move(new Float3(0.5f, 0, 0));

        Assert.Equal(CharacterController.CollisionFlags.None, flags);
        Assert.Empty(cc.Hits);
        Assert.Equal(0.5f, cc.GameObject.Transform.Position.X, 3);
    }

    /// <summary>
    /// Walking into a wall has to report what was hit, so a caller can push it, read a tag off it,
    /// or decide to ignore it.
    /// </summary>
    [Fact]
    public void WalkingIntoAWallReportsTheWallItHit()
    {
        var scene = CreatePhysicsScene();
        GameObject wall = AddStaticBox(scene, new Float3(2, 0, 0), new Float3(1, 4, 8));

        var cc = AddController(scene, new Float3(0, 0, 0));
        CharacterController.CollisionFlags flags = cc.Move(new Float3(3, 0, 0));

        Assert.True(flags.HasFlag(CharacterController.CollisionFlags.Sides));
        Assert.NotEmpty(cc.Hits);

        bool sawTheWall = false;
        foreach (ShapeCastHit hit in cc.Hits)
            if (hit.Collider.IsValid() && ReferenceEquals(hit.Collider!.GameObject, wall)) sawTheWall = true;

        Assert.True(sawTheWall, "the wall was not among the reported hits");
    }

    [Fact]
    public void EachMoveReportsOnlyItsOwnCollisions()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(2, 0, 0), new Float3(1, 4, 8));

        var cc = AddController(scene, new Float3(0, 10, 0));
        cc.Move(new Float3(0.1f, 0, 0));
        Assert.Empty(cc.Hits);
    }

    [Fact]
    public void StandingOnTheGroundReportsBelowAndTheSurfaceUnderIt()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        var cc = AddController(scene, new Float3(0, 0, 0));
        CharacterController.CollisionFlags flags = cc.Move(new Float3(0, -0.01f, 0));

        Assert.True(cc.IsGrounded);
        Assert.True(flags.HasFlag(CharacterController.CollisionFlags.Below));
        Assert.True(cc.GroundNormal.Y > 0.9f, $"ground normal was {cc.GroundNormal}");
        Assert.True(cc.GroundSlopeAngle < 5.0f, $"flat ground reported a {cc.GroundSlopeAngle} degree slope");
    }

    [Fact]
    public void NotGroundedReportsUpAsTheGroundNormalAndNoCollider()
    {
        var scene = CreatePhysicsScene();
        var cc = AddController(scene, new Float3(0, 20, 0));
        cc.Move(Float3.Zero);

        Assert.False(cc.IsGrounded);
        Assert.Equal(1.0f, cc.GroundNormal.Y, 3);
        Assert.Equal(0.0f, cc.GroundSlopeAngle, 3);
        Assert.Null(cc.GroundCollider);
    }

    /// <summary>A teleport goes straight there, and still refuses to leave the controller inside anything.</summary>
    [Fact]
    public void TeleportingIntoTheFloorLeavesTheControllerOutsideIt()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(20, 1, 20));

        var cc = AddController(scene, new Float3(0, 10, 0));
        cc.Teleport(new Float3(5, -0.05f, 5));

        var overlaps = new List<ShapeCastHit>();
        Assert.Equal(0, cc.OverlapNow(overlaps));
        Assert.Equal(5.0f, cc.GameObject.Transform.Position.X, 3);
    }

    [Fact]
    public void CastFindsWhatIsAheadWithoutMoving()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(2, 0, 0), new Float3(1, 4, 8));

        var cc = AddController(scene, new Float3(0, 0, 0));
        Float3 before = cc.GameObject.Transform.Position;

        Assert.True(cc.Cast(new Float3(1, 0, 0), 5.0f, out ShapeCastHit hit));
        Assert.True(hit.Distance > 0.0f);
        Assert.Equal(before, cc.GameObject.Transform.Position);

        Assert.False(cc.Cast(new Float3(-1, 0, 0), 5.0f, out _));
    }

    [Fact]
    public void ACastWithNoDirectionIsRefusedRatherThanThrowing()
    {
        var scene = CreatePhysicsScene();
        var cc = AddController(scene, new Float3(0, 5, 0));

        Assert.False(cc.Cast(Float3.Zero, 1.0f, out _));
    }

    /// <summary>The velocity reported is what was achieved, not what was asked for.</summary>
    [Fact]
    public void BlockedMovementReportsTheDistanceActuallyTravelled()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(2, 0, 0), new Float3(1, 4, 8));

        var cc = AddController(scene, new Float3(0, 0, 0));
        cc.Move(new Float3(10, 0, 0));

        Assert.True(cc.GameObject.Transform.Position.X < 2.0f,
                    "the controller went through the wall");
    }

    /// <summary>
    /// A move angled down into flat ground, which is what walking under gravity asks for every frame,
    /// covers exactly its horizontal part. The floor is ground to slide along, not a step to climb,
    /// and climbing it used to turn the downward part into extra forward speed.
    /// </summary>
    [Fact]
    public void WalkingUnderGravityCoversExactlyTheDistanceAsked()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 40));
        var cc = AddController(scene, new Float3(0, 0.1f, 0));
        for (int i = 0; i < 10; i++) cc.Move(new Float3(0, -0.05f, 0));
        Assert.True(cc.IsGrounded);

        float start = cc.GameObject.Transform.Position.Z;
        for (int i = 0; i < 60; i++) cc.Move(new Float3(0, -0.033f, 0.028f));

        Assert.Equal(60 * 0.028f, cc.GameObject.Transform.Position.Z - start, 2);
    }

    /// <summary>
    /// Walking one way and then back the other, as a character turning round does, keeps moving. A
    /// controller that has settled a little closer to the floor than its skin used to read the floor
    /// as a wall on the way back and stop dead.
    /// </summary>
    [Fact]
    public void AControllerThatHasBeenWalkingCanWalkBack()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(80, 1, 80));
        var cc = AddController(scene, new Float3(0, 0.05f, 0));

        for (int i = 0; i < 200; i++) cc.Move(new Float3(0, -0.033f, 0.06f));
        float turnedAt = cc.GameObject.Transform.Position.Z;
        for (int i = 0; i < 60; i++) cc.Move(new Float3(0, -0.033f, -0.028f));

        Assert.Equal(-60 * 0.028f, cc.GameObject.Transform.Position.Z - turnedAt, 2);
        Assert.True(cc.GameObject.Transform.Position.Y > 0f, $"sank into the floor to {cc.GameObject.Transform.Position.Y}");
    }

    /// <summary>A controller placed exactly on the floor, as a scene built by hand puts it, can walk away.</summary>
    [Fact]
    public void AControllerPlacedExactlyOnTheFloorCanWalk()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 40));
        var cc = AddController(scene, Float3.Zero);

        for (int i = 0; i < 30; i++) cc.Move(new Float3(0.05f, -0.033f, 0));

        Assert.Equal(1.5, cc.GameObject.Transform.Position.X, 2);
        Assert.True(cc.GameObject.Transform.Position.Y >= 0f);
    }

    // ---------------------------------------------------------------------
    // Walking the way a game drives it: gravity every frame, jumping from the ground
    // ---------------------------------------------------------------------

    private const float Dt = 1f / 60f;

    /// <summary>A typical player loop: walk along <paramref name="direction"/>, fall under gravity, optionally jump on the first frame.</summary>
    private static void Walk(CharacterController cc, Float3 direction, float seconds, bool jump = false, float speed = 5f)
    {
        float vertical = 0f;
        for (int i = 0; i < (int)(seconds / Dt); i++)
        {
            if (cc.IsGrounded && vertical <= 0f) vertical = -1f;
            if (jump && i == 0) vertical = 7f;
            else if (!cc.IsGrounded) vertical -= 20f * Dt;

            cc.Move(direction * speed * Dt + new Float3(0f, vertical * Dt, 0f));
        }
    }

    private GameObject AddRamp(Scene scene, float degrees)
    {
        var go = CreateGameObject("Ramp");
        go.Transform.Position = new Float3(0, 0, 0);
        go.Transform.LocalEulerAngles = new Float3(degrees, 0, 0);
        go.AddComponent<BoxCollider>().Size = new Float3(10, 0.4f, 20);
        scene.Add(go);
        return go;
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void StandingStillOnAWalkableSlope_DoesNotSlideDown(ColliderShape shape)
    {
        var scene = CreatePhysicsScene();
        AddRamp(scene, -20f);
        var cc = AddController(scene, new Float3(0, 2, 0));
        cc.Shape = shape;
        Walk(cc, Float3.Zero, 1f);
        Float3 settled = cc.GameObject.Transform.Position;

        Walk(cc, Float3.Zero, 2f);

        Float3 moved = cc.GameObject.Transform.Position - settled;
        Assert.True(cc.IsGrounded);
        Assert.True(Float3.Length(moved) < 0.01f, $"slid {moved} in two seconds");
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void JumpingLeavesTheGround(ColliderShape shape)
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 40));
        var cc = AddController(scene, Float3.Zero);
        cc.Shape = shape;
        Walk(cc, Float3.Zero, 0.5f);

        Walk(cc, Float3.Zero, 0.3f, jump: true);

        Assert.False(cc.IsGrounded);
        Assert.True(cc.GameObject.Transform.Position.Y > 0.8f, $"only rose to {cc.GameObject.Transform.Position.Y}");

        Walk(cc, Float3.Zero, 1.5f);
        Assert.True(cc.IsGrounded);
        Assert.Equal(0.0, cc.GameObject.Transform.Position.Y, 1);
    }

    [Theory]
    [InlineData(ColliderShape.Capsule)]
    [InlineData(ColliderShape.Cylinder)]
    public void WalkingIntoStairs_ClimbsThem(ColliderShape shape)
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 40));
        for (int i = 0; i < 8; i++)
            AddStaticBox(scene, new Float3(0, 0.125f * (i + 1), 2f + i * 0.6f), new Float3(3, 0.25f * (i + 1), 0.6f));
        AddStaticBox(scene, new Float3(0, 1f, 8.6f), new Float3(3, 2, 3));
        var cc = AddController(scene, Float3.Zero);
        cc.Shape = shape;
        Walk(cc, Float3.Zero, 0.2f);

        Walk(cc, Float3.UnitZ, 1.6f);

        Assert.True(cc.GameObject.Transform.Position.Y > 1.9f, $"stopped at {cc.GameObject.Transform.Position}");
    }

    [Fact]
    public void WalkingUpAWalkableSlope_ReachesTheTop()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 60));
        AddRamp(scene, -20f);
        var cc = AddController(scene, new Float3(0, 0, -2));
        Walk(cc, Float3.Zero, 0.2f);

        Walk(cc, Float3.UnitZ, 2f);

        Assert.True(cc.GameObject.Transform.Position.Y > 2.5f, $"stopped at {cc.GameObject.Transform.Position}");
    }

    [Fact]
    public void ASlopeTooSteepToWalk_IsNotClimbed()
    {
        var scene = CreatePhysicsScene();
        AddStaticBox(scene, new Float3(0, -0.5f, 0), new Float3(40, 1, 60));
        AddRamp(scene, -65f);
        var cc = AddController(scene, new Float3(0, 0, -8));
        Walk(cc, Float3.Zero, 0.2f);

        Walk(cc, Float3.UnitZ, 3f);

        Assert.True(cc.GameObject.Transform.Position.Y < 0.6f, $"climbed to {cc.GameObject.Transform.Position}");
    }
}
