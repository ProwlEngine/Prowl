// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// The character controller's own movement, which is swept by hand rather than simulated: it never
/// becomes a rigidbody, so every guarantee here is one this component makes for itself.
/// </summary>
public class CharacterControllerTests : RuntimeTestBase
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
}
