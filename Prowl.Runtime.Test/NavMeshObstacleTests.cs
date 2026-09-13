// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshObstacle"/>: enabling, moving and disabling one carves (and un-carves) a
/// real hole into the live, already-baked navmesh via <c>DtTileCache</c> - not a re-bake of anything, and
/// not something that ever needs a walking agent to be removed and rejoined to its crowd. A carve is
/// queued when the obstacle changes and applied the next few times the scene ticks (see
/// <see cref="NavMeshQuery.TickTileCache"/>), so every test here ticks a little after touching one before
/// checking its effect.
/// </summary>
public class NavMeshObstacleTests : RuntimeTestBase
{
    private static Mesh CreateFlatGround(float width, float depth)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        return new Mesh
        {
            Vertices = [new(-hw, 0, -hd), new(hw, 0, -hd), new(hw, 0, hd), new(-hw, 0, hd)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private NavMeshSurface BuildBakedSurface(Scene scene, float groundSize = 30f)
    {
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateFlatGround(groundSize, groundSize);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface.LayerMask, surface.AgentTypeId);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();

        return surface;
    }

    private NavMeshObstacle AddObstacle(Scene scene, Float3 position, Float3 size)
    {
        GameObject go = CreateGameObject("Obstacle");
        go.Transform.Position = position;
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.Size = size;
        scene.Add(go);
        return obstacle;
    }

    [Fact]
    public void EnablingAnObstacle_CarvesARealHoleOnceTicked()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        // Reachable before the obstacle exists.
        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 beforeSnap));
        Assert.True(Float3.Distance(beforeSnap, Float3.Zero) < 0.5f);

        AddObstacle(scene, Float3.Zero, new Float3(6, 4, 6));
        Tick(scene, 10);

        // The exact center is now carved out - the nearest walkable point has to be well outside the
        // obstacle's own footprint (half-size 3, so anything snapping back inside ~3 units means the
        // hole didn't actually take).
        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 afterSnap));
        Assert.True(Float3.Distance(afterSnap, Float3.Zero) > 2.5f, $"Snapped to {afterSnap}, still inside the obstacle's own footprint.");
    }

    [Fact]
    public void MovingAnObstacle_MovesTheHoleWithIt()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        NavMeshObstacle obstacle = AddObstacle(scene, new Float3(-8, 0, 0), new Float3(6, 4, 6));
        Tick(scene, 10);
        Assert.True(surface.Query!.SamplePosition(new Float3(-8, 0, 0), out Float3 snapNearOldSpot));
        Assert.True(Float3.Distance(snapNearOldSpot, new Float3(-8, 0, 0)) > 2.5f, "Expected the old spot to still be carved right after enabling.");

        obstacle.Transform.Position = new Float3(8, 0, 0);
        Tick(scene, 10);

        // The old spot healed...
        Assert.True(surface.Query!.SamplePosition(new Float3(-8, 0, 0), out Float3 snapOldSpotAfterMove));
        Assert.True(Float3.Distance(snapOldSpotAfterMove, new Float3(-8, 0, 0)) < 0.5f, $"Old spot should be walkable again, snapped to {snapOldSpotAfterMove}.");

        // ...and the new one is now carved.
        Assert.True(surface.Query!.SamplePosition(new Float3(8, 0, 0), out Float3 snapNewSpot));
        Assert.True(Float3.Distance(snapNewSpot, new Float3(8, 0, 0)) > 2.5f, $"New spot should be carved, snapped to {snapNewSpot}.");
    }

    [Fact]
    public void DisablingAnObstacle_RestoresTheHole()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        NavMeshObstacle obstacle = AddObstacle(scene, Float3.Zero, new Float3(6, 4, 6));
        Tick(scene, 10);
        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 snapWhileCarved));
        Assert.True(Float3.Distance(snapWhileCarved, Float3.Zero) > 2.5f);

        obstacle.Enabled = false;
        Tick(scene, 10);

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 snapAfterDisable));
        Assert.True(Float3.Distance(snapAfterDisable, Float3.Zero) < 0.5f, $"Expected the hole to heal, snapped to {snapAfterDisable}.");
    }

    [Fact]
    public void Obstacle_ScopedAwayFromTheOnlyBakedType_HasNoEffect()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        // Scoped away *before* OnEnable ever fires (scene.Add is what enables it) - otherwise the
        // very first, unscoped carve would already have happened against every registered type.
        GameObject go = CreateGameObject("Obstacle");
        go.Transform.Position = Float3.Zero;
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(6, 4, 6);
        obstacle.AffectAllAgentTypes = false;
        obstacle.AgentTypeIds.Add(surface.AgentTypeId + 999); // some other type, not the one baked here
        scene.Add(go);
        Tick(scene, 10);

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 snap));
        Assert.True(Float3.Distance(snap, Float3.Zero) < 0.5f, "An obstacle scoped away from this type should never carve it.");
    }

    [Fact]
    public void MovingObstacleMidTransit_DoesNotFreezeAWalkingAgent()
    {
        Scene scene = CreateScene(enable: true);
        BuildBakedSurface(scene, groundSize: 40f);

        NavMeshAgent agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = new Float3(-15, 0, 0);
        scene.Add(agent.GameObject);

        Assert.True(agent.SetDestination(new Float3(15, 0, 0)));
        Tick(scene, 60); // 1 second - well underway

        Float3 positionBeforeCarve = agent.Position;

        // Drop an obstacle nearby (not necessarily exactly on top of the agent) and let it carve -
        // the agent is never removed from its crowd or rejoined for this, unlike a full surface rebake.
        AddObstacle(scene, new Float3(0, 0, 0), new Float3(4, 4, 4));
        Tick(scene, 30);

        Assert.True(Float3.Distance(agent.Position, positionBeforeCarve) > 0.01f, "Agent should still be moving right after the carve, not frozen.");

        Tick(scene, 900); // plenty of time to finish the ~30-unit crossing even with a detour
        Assert.True(agent.HasArrived, $"Agent ended at {agent.Position}");
    }

    [Fact]
    public void ObstacleEnabledBeforeAnyBakeExists_StillCarvesOnceOneDoes()
    {
        // The order a level is normally built in: obstacles placed (and enabled) as part of scene
        // setup, baked afterward - not the other way around. OnEnable's own carve finds no usable
        // query yet and must not just give up forever; it has to keep retrying until a bake actually
        // produces one.
        Scene scene = CreateScene(enable: true);

        NavMeshObstacle obstacle = AddObstacle(scene, Float3.Zero, new Float3(6, 4, 6));

        NavMeshSurface surface = BuildBakedSurface(scene);
        Tick(scene, 10);

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 snap));
        Assert.True(Float3.Distance(snap, Float3.Zero) > 2.5f, $"Expected the obstacle to carve in once a bake existed; snapped to {snap}.");
        _ = obstacle; // keep the reference alive for clarity - nothing further to do with it here
    }

    [Fact]
    public void CapsuleShape_CarvesACylindricalHole()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        GameObject go = CreateGameObject("Obstacle");
        go.Transform.Position = Float3.Zero;
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.Shape = NavMeshObstacleShape.Capsule;
        obstacle.Radius = 3f;
        obstacle.Height = 4f;
        scene.Add(go);
        Tick(scene, 10);

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 snap));
        Assert.True(Float3.Distance(snap, Float3.Zero) > 2.5f, $"Expected the capsule to carve a hole; snapped to {snap}.");
    }

    [Fact]
    public void CarveOff_LeavesTheMeshIntactButStillBlocksACrowdAgent()
    {
        Scene scene = CreateScene(enable: true);
        BuildBakedSurface(scene, groundSize: 40f);

        // Carve off: the mesh itself must stay walkable straight through the obstacle's footprint...
        NavMeshObstacle obstacle = AddObstacle(scene, Float3.Zero, new Float3(6, 4, 6));
        obstacle.Carve = false;
        Tick(scene, 10);

        NavMeshQuery query = NavMeshSystem.GetOrCreate(scene).GetQuery(NavMeshAgentTypes.HumanoidId)!;
        Assert.True(query.SamplePosition(Float3.Zero, out Float3 snap));
        Assert.True(Float3.Distance(snap, Float3.Zero) < 0.5f, $"Carve-off must never touch the mesh; snapped to {snap}.");

        // ...but a crowd agent walking straight through it must still arrive without the simulation
        // breaking - the static crowd-avoidance slot the obstacle occupies is a real neighbour to it.
        NavMeshAgent agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = new Float3(-15, 0, 0);
        scene.Add(agent.GameObject);
        Assert.True(agent.SetDestination(new Float3(15, 0, 0)));
        Tick(scene, 900); // plenty of time to finish the ~30-unit crossing even with an avoidance detour

        Assert.True(agent.HasArrived, $"Agent ended at {agent.Position}");
    }

    [Fact]
    public void CarveOnlyStationary_LiftsTheHoleWhileMovingAndRestoresItOnceSettled()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        NavMeshObstacle obstacle = AddObstacle(scene, Float3.Zero, new Float3(6, 4, 6));
        obstacle.CarveOnlyStationary = true;
        obstacle.CarvingTimeToStationary = 0.2f; // 12 ticks at 1/60
        Tick(scene, 20); // long enough to settle once

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 settledSnap));
        Assert.True(Float3.Distance(settledSnap, Float3.Zero) > 2.5f, $"Expected a real carve once settled; snapped to {settledSnap}.");

        // Move it every tick - never stationary long enough to re-settle - and the hole should heal.
        for (int i = 0; i < 10; i++)
        {
            obstacle.Transform.Position = new Float3(i * 0.5f, 0, 0);
            Tick(scene, 1);
        }

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 movingSnap));
        Assert.True(Float3.Distance(movingSnap, Float3.Zero) < 0.5f, $"Expected the hole to heal while moving; snapped to {movingSnap}.");
    }
}
