// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class NavMeshComponentTests : RuntimeTestBase
{
    private (Scene scene, NavMeshSurface surface) CreateBakedFloorScene(float size = 20f)
        => CreateFloorScene(size, bake: true);

    /// <summary>
    /// Agent-type resolution end to end: two types with different radii baked from the same
    /// scene produce two navmeshes, and a corridor passable for the small type is eroded shut
    /// for the large one. Also locks the agent-table → surface composition
    /// (ResolveBuildSettings) and per-type instance lookup.
    /// </summary>
    [Fact]
    public void Surfaces_TwoAgentTypes_ProduceDifferentWalkability()
    {
        try
        {
            NavMeshAgentTypes.ApplyTable(
            [
                new NavMeshAgentType { Id = 0, Name = "Humanoid", Radius = 0.5f, Height = 2f, MaxSlope = 45f, MaxClimb = 0.4f },
                new NavMeshAgentType { Id = 7, Name = "Tank", Radius = 1.4f, Height = 2f, MaxSlope = 45f, MaxClimb = 0.4f },
            ]);

            Scene scene = CreateScene(enable: true);
            GameObject floor = CreateGameObject("Corridor");
            scene.Add(floor);
            // 3-unit-wide corridor: walkable inset survives radius 0.5, vanishes at radius 1.4.
            floor.AddComponent<BoxCollider>().Size = new Float3(30, 1, 3);
            floor.Transform.Position = new Float3(15, -0.5f, 1.5f);

            GameObject smallGo = CreateGameObject("SmallSurface");
            scene.Add(smallGo);
            var small = smallGo.AddComponent<NavMeshSurface>();
            ApplyFastBakeSettings(small);
            small.AgentTypeId = 0;
            Assert.True(small.BuildNavMesh());

            GameObject largeGo = CreateGameObject("LargeSurface");
            scene.Add(largeGo);
            var large = largeGo.AddComponent<NavMeshSurface>();
            ApplyFastBakeSettings(large);
            large.AgentTypeId = 7;
            // The large type's bake legitimately produces nothing walkable in this corridor.
            bool largeBaked = large.BuildNavMesh();

            // Resolution pulled the right envelopes from the table.
            Assert.Equal(0.5f, small.ResolveBuildSettings().AgentRadius);
            Assert.Equal(1.4f, large.ResolveBuildSettings().AgentRadius);

            // Small type walks the corridor; large type has no mesh there (either its bake was
            // empty or its instance has nothing at the sample point).
            Assert.True(scene.Navigation.HasNavMesh(0));
            Assert.True(scene.Navigation.SamplePosition(new Float3(15, 0.2f, 1.5f), out _, 0.5f,
                new NavMeshQueryFilter { AgentTypeId = 0 }));

            bool largeWalkable = largeBaked && scene.Navigation.SamplePosition(new Float3(15, 0.2f, 1.5f), out _, 0.5f,
                new NavMeshQueryFilter { AgentTypeId = 7 });
            Assert.False(largeWalkable, "A 1.4-radius agent type must not fit a 3-unit corridor.");
        }
        finally
        {
            // Agent-type table is global static state; restore defaults for other tests.
            NavMeshAgentTypes.ApplyTable([new NavMeshAgentType { Id = 0, Name = "Humanoid" }]);
        }
    }

    [Fact]
    public void Surface_BakeRegistersWithSceneWorld()
    {
        (Scene scene, NavMeshSurface surface) = CreateBakedFloorScene();

        Assert.True(scene.Navigation.HasNavMesh());
        Assert.NotNull(surface.Instance);

        // The static facade reaches it when the scene is current.
        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-8, 0, -8), new Float3(8, 0, 8), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    [Fact]
    public void Surface_DisableUnregisters()
    {
        (Scene scene, NavMeshSurface surface) = CreateBakedFloorScene();
        Assert.True(scene.Navigation.HasNavMesh());

        surface.GameObject.Enabled = false;
        Assert.False(scene.Navigation.HasNavMesh());

        surface.GameObject.Enabled = true;
        Assert.True(scene.Navigation.HasNavMesh());
    }

    [Fact]
    public void Agent_WalksTowardDestination()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, -8);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 10f;
        agent.Acceleration = 100f;

        Assert.True(agent.SetDestination(new Float3(8, 0, 8)) || agent.PathPending || !agent.IsOnNavMesh);

        double startDistance = Float3.Distance(agentGo.Transform.Position, new Float3(8, 0, 8));
        Tick(scene, 240); // 4 simulated seconds at 60 Hz

        Assert.True(agent.IsOnNavMesh, "Agent should have joined the crowd.");
        double endDistance = Float3.Distance(agentGo.Transform.Position, new Float3(8, 0, 8));
        Assert.True(endDistance < startDistance - 5.0,
            $"Agent should approach the destination (start {startDistance:0.0}, end {endDistance:0.0}).");
    }

    [Fact]
    public void Agent_RegistersWhenNavMeshAppearsLater()
    {
        Scene scene = CreateScene(enable: true);

        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        // Agent first: no navmesh yet.
        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-5, 0, -5);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        Tick(scene, 2);
        Assert.False(agent.IsOnNavMesh);

        // Surface bakes afterwards (the runtime-rebake ordering).
        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);
        Assert.True(surface.BuildNavMesh());

        Tick(scene, 2);
        Assert.True(agent.IsOnNavMesh, "Agent should register via NavMeshChanged once a navmesh exists.");
    }

    [Fact]
    public void Agent_IsStoppedHaltsMovement()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, -8);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 10f;
        agent.Acceleration = 100f;

        Tick(scene, 2); // register
        agent.SetDestination(new Float3(8, 0, 8));
        Tick(scene, 30);

        agent.IsStopped = true;
        Tick(scene, 5); // let velocity decay
        Float3 posWhenStopped = agentGo.Transform.Position;
        Tick(scene, 60);
        double drift = Float3.Distance(agentGo.Transform.Position, posWhenStopped);
        Assert.True(drift < 0.6, $"Stopped agent should not keep moving (drifted {drift:0.00}).");

        agent.IsStopped = false;
        Tick(scene, 60);
        double moved = Float3.Distance(agentGo.Transform.Position, posWhenStopped);
        Assert.True(moved > 1.0, $"Resumed agent should move again (moved {moved:0.00}).");
    }

    [Fact]
    public void Agent_WarpMovesAgentAndKeepsWorking()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, -8);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        Tick(scene, 2);

        Assert.True(agent.Warp(new Float3(5, 0, 5)));
        Tick(scene, 1);
        Assert.True(Float3.Distance(agentGo.Transform.Position, new Float3(5, 0, 5)) < 1.0);
        Assert.True(agent.IsOnNavMesh);
    }

    /// <summary>
    /// The destructible-map flow: block a corridor at runtime, rebuild only the affected
    /// tiles, and the path reroutes. This is the RubbleRangers acceptance scenario.
    /// </summary>
    [Fact]
    public void RebuildTiles_ReflectsChangedGeometry()
    {
        (Scene scene, NavMeshSurface surface) = CreateBakedFloorScene(30f);

        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(-12, 0, 0), new Float3(12, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        int cornersBefore = path.CornerCount;

        // Drop a wall across the middle: x in [-1,1], z spanning the whole floor.
        GameObject wall = CreateGameObject("Wall");
        scene.Add(wall);
        wall.AddComponent<BoxCollider>().Size = new Float3(2, 4, 30);
        wall.Transform.Position = new Float3(0, 2, 0);

        // Partial rebuild only around the wall.
        Assert.True(surface.RebuildTiles(new AABB(new Float3(-2, -1, -16), new Float3(2, 5, 16))));

        // The straight path is now impossible; with the wall spanning the full width the
        // destination becomes unreachable (partial path at best).
        Assert.True(scene.Navigation.CalculatePath(new Float3(-12, 0, 0), new Float3(12, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathPartial, path.Status);
        Float3 last = path.Corners[path.CornerCount - 1];
        Assert.True(last.X < 0, $"Partial path must stop on the near side of the wall (end x = {last.X:0.0}).");

        // Remove the wall and rebuild the same tiles: route restored.
        wall.Enabled = false;
        Assert.True(surface.RebuildTiles(new AABB(new Float3(-2, -1, -16), new Float3(2, 5, 16))));
        Assert.True(scene.Navigation.CalculatePath(new Float3(-12, 0, 0), new Float3(12, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
        _ = cornersBefore;
    }

    private static NavMeshGeometrySource FloorQuad(float minX, float minZ, float maxX, float maxZ)
    {
        Float3[] verts =
        [
            new(minX, 0, minZ),
            new(minX, 0, maxZ),
            new(maxX, 0, maxZ),
            new(maxX, 0, minZ),
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return new NavMeshGeometrySource(verts, indices, Float4x4.Identity);
    }

    /// <summary>
    /// A surface whose navmesh was built from explicit geometry with NO scene colliders or
    /// renderers at all — the shape of a game with custom rendering/collision, where
    /// CollectSources() returns nothing (the RubbleRangers case).
    /// </summary>
    private (Scene scene, NavMeshSurface surface) CreateExplicitGeometryScene()
    {
        Scene scene = CreateScene(enable: true);
        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);

        // 30x30 floor from (0,0,0) to (30,0,30): 2x2 tiles at tileWorldSize 16.
        NavMeshData? data = NavMeshBuilder.Build(surface.ResolveBuildSettings(), [FloorQuad(0, 0, 30, 30)]);
        Assert.NotNull(data);
        surface.ApplyNavMeshData(data!);
        Assert.NotNull(surface.Instance);

        // Sanity: the collectors really do see nothing in this scene.
        Assert.Empty(surface.CollectSources());
        return (scene, surface);
    }

    /// <summary>
    /// The explicit-sources overload: rebuild one region from caller-supplied partial geometry
    /// (a hole punched in the floor) and verify the changed region changed, untouched tiles
    /// were not disturbed, and paths still cross the tile seam — i.e. the tile grid stayed
    /// anchored to the original bake even though the incoming geometry spans only a corner.
    /// </summary>
    [Fact]
    public void RebuildTiles_WithExplicitSources_ChangesRegionAndKeepsGridAnchored()
    {
        (Scene scene, NavMeshSurface surface) = CreateExplicitGeometryScene();

        var path = new NavMeshPath();
        Assert.True(scene.Navigation.SamplePosition(new Float3(8, 0.2f, 8), out _, 0.5f, NavMesh.AllAreas));

        // Punch a hole at x/z 4..12 by rebuilding from partial sources: the same floor but
        // with the hole missing. The AABB passed is the CHANGED region (the hole), from which
        // the engine derives the affected tile set (tile (0,0) here, border included); the
        // sources cover that tile plus its erosion border (out to 18) — NOT the whole bake.
        // That partial coverage is the point of the overload.
        const float pad = 18f;
        NavMeshGeometrySource[] holeRegion =
        [
            FloorQuad(0, 0, pad, 4),      // south strip
            FloorQuad(0, 12, pad, pad),   // north strip
            FloorQuad(0, 4, 4, 12),       // west strip
            FloorQuad(12, 4, pad, 12),    // east strip
        ];

        Assert.True(surface.RebuildTiles(new AABB(new Float3(4, -1, 4), new Float3(12, 1, 12)), holeRegion, out int rebuiltTiles));
        Assert.True(rebuiltTiles > 0);

        // Inside the hole: no longer walkable.
        Assert.False(scene.Navigation.SamplePosition(new Float3(8, 0.2f, 8), out _, 0.5f, NavMesh.AllAreas));
        // Rebuilt tile outside the hole: still walkable.
        Assert.True(scene.Navigation.SamplePosition(new Float3(2, 0.2f, 2), out _, 0.5f, NavMesh.AllAreas));
        // Untouched far tile: undisturbed.
        Assert.True(scene.Navigation.SamplePosition(new Float3(25, 0.2f, 25), out _, 0.5f, NavMesh.AllAreas));

        // The grid-anchoring assertion: a path from the rebuilt region into an untouched tile
        // must still connect across the tile seam. If the rebuild had re-anchored the grid to
        // the incoming geometry, the swapped tiles would misalign and this seam would break.
        Assert.True(scene.Navigation.CalculatePath(new Float3(2, 0, 2), new Float3(25, 0, 25), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>
    /// An empty source list empties the affected tiles (a fully walled-in region) rather than
    /// no-opping — "no geometry" and "no change" must not be conflated.
    /// </summary>
    [Fact]
    public void RebuildTiles_WithEmptySources_EmptiesAffectedTiles()
    {
        (Scene scene, NavMeshSurface surface) = CreateExplicitGeometryScene();

        // The changed-region AABB sits inside tile (0,0) so border expansion stays within it.
        Assert.True(surface.RebuildTiles(new AABB(new Float3(2, -1, 2), new Float3(14, 1, 14)), [], out int rebuiltTiles));
        Assert.True(rebuiltTiles > 0);

        // The affected region is gone...
        Assert.False(scene.Navigation.SamplePosition(new Float3(8, 0.2f, 8), out _, 0.5f, NavMesh.AllAreas));
        // ...but tiles outside the bounds (plus border bleed) survive.
        Assert.True(scene.Navigation.SamplePosition(new Float3(25, 0.2f, 25), out _, 0.5f, NavMesh.AllAreas));

        // Restoring the region with explicit sources brings it back (padded past the border).
        Assert.True(surface.RebuildTiles(new AABB(new Float3(2, -1, 2), new Float3(14, 1, 14)), [FloorQuad(0, 0, 18, 18)]));
        Assert.True(scene.Navigation.SamplePosition(new Float3(8, 0.2f, 8), out _, 0.5f, NavMesh.AllAreas));

        var path = new NavMeshPath();
        Assert.True(scene.Navigation.CalculatePath(new Float3(2, 0, 2), new Float3(25, 0, 25), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>
    /// The drill-outward scenario: bake a small spawn region inside declared world bounds
    /// much larger than it, then RebuildTiles a region FAR from the original geometry and
    /// assert walkable tiles appear there. Without explicit bounds the grid is sized to the
    /// spawn region and every such rebuild is silently clamped away.
    /// </summary>
    [Fact]
    public void RebuildTiles_OutsideOriginalGeometry_WorksWithDeclaredWorldBounds()
    {
        Scene scene = CreateScene(enable: true);
        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);

        // Spawn cavern: a 10x10 floor in the corner of a declared 100x100 world.
        Float3[] verts = [new(0, 0, 0), new(0, 0, 10), new(10, 0, 10), new(10, 0, 0)];
        int[] indices = [0, 1, 2, 0, 2, 3];
        var spawn = new NavMeshGeometrySource(verts, indices, Float4x4.Identity);
        NavMeshData? data = NavMeshBuilder.Build(surface.ResolveBuildSettings(), [spawn],
            worldBounds: new AABB(new Float3(0, -1, 0), new Float3(100, 1, 100)));
        Assert.NotNull(data);
        surface.ApplyNavMeshData(data!);

        // Far region (x/z 60..80) has nothing yet.
        Assert.False(scene.Navigation.SamplePosition(new Float3(70, 0.2f, 70), out _, 0.5f, NavMesh.AllAreas));

        // Drill opens a cavern there: rebuild with sources for just that region.
        Float3[] farVerts = [new(58, 0, 58), new(58, 0, 82), new(82, 0, 82), new(82, 0, 58)];
        var farFloor = new NavMeshGeometrySource(farVerts, indices, Float4x4.Identity);
        Assert.True(surface.RebuildTiles(new AABB(new Float3(60, -1, 60), new Float3(80, 1, 80)), [spawn, farFloor], out int rebuiltTiles));
        Assert.True(rebuiltTiles > 0, "Rebuild far from the original geometry must produce tiles, not clamp away.");

        // The far region is now walkable; the untouched spawn region still is.
        Assert.True(scene.Navigation.SamplePosition(new Float3(70, 0.2f, 70), out _, 0.5f, NavMesh.AllAreas));
        Assert.True(scene.Navigation.SamplePosition(new Float3(5, 0.2f, 5), out _, 0.5f, NavMesh.AllAreas));
    }

    /// <summary>
    /// Applying a second navmesh (map regeneration without a scene reload) must rebind the
    /// crowd: the old crowd steered against a DtNavMesh that no longer exists, and agents must
    /// rejoin the new one keeping their destination.
    /// </summary>
    [Fact]
    public void Agent_SurvivesNavMeshReplacement()
    {
        (Scene scene, NavMeshSurface surface) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, -8);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 10f;
        agent.Acceleration = 100f;

        Tick(scene, 2);
        Assert.True(agent.IsOnNavMesh);
        agent.SetDestination(new Float3(8, 0, 8));
        Tick(scene, 30);

        // Regenerate: a fresh bake of the same floor produces a NEW DtNavMesh instance.
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);

        Assert.True(agent.IsOnNavMesh, "Agent must rejoin the replacement crowd after the navmesh swap.");
        Assert.NotNull(scene.Navigation.NativeCrowd);

        // Destination survived the swap and the agent still gets there on the new mesh.
        Tick(scene, 240);
        double endDistance = Float3.Distance(agentGo.Transform.Position, new Float3(8, 0, 8));
        Assert.True(endDistance < 2.0, $"Agent should reach its destination on the new mesh (got within {endDistance:0.0}).");
    }

    /// <summary>
    /// Rotation regression (degrees fed into a radians wrap helper froze rotation ~2° in):
    /// travelling perpendicular to the initial facing, the yaw must converge to the direction
    /// of travel, not park a couple of degrees off.
    /// </summary>
    [Fact]
    public void Agent_RotatesTowardTravelDirection()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, 0); // facing +Z by default
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 3f;
        agent.Acceleration = 100f;
        agent.AngularSpeed = 360f;

        Tick(scene, 2);
        agent.SetDestination(new Float3(8, 0, 0)); // travel +X: yaw 90° from start
        Tick(scene, 90); // 1.5 simulated seconds: mid-travel, well past any turn time

        Float3 forward = agentGo.Transform.Forward;
        double alignment = forward.X; // dot(forward, +X)
        Assert.True(alignment > 0.98,
            $"Agent should face its +X travel direction (forward = ({forward.X:0.00}, {forward.Y:0.00}, {forward.Z:0.00})).");
    }

    /// <summary>
    /// Arrival regression: with AutoBraking on (the default) and StoppingDistance 0, the
    /// agent must still report arrival — RemainingDistance reads exactly 0, so the Unity
    /// idiom "!PathPending &amp;&amp; RemainingDistance &lt;= StoppingDistance" terminates.
    /// </summary>
    [Fact]
    public void Agent_DetectsArrival_WithAutoBraking()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, -8);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 6f;
        agent.Acceleration = 50f;
        agent.AutoBraking = true;
        agent.StoppingDistance = 0f;

        Tick(scene, 2);
        agent.SetDestination(new Float3(8, 0, 8));

        bool arrived = false;
        for (int i = 0; i < 600 && !arrived; i++)
        {
            Tick(scene, 1);
            arrived = !agent.PathPending && agent.RemainingDistance <= agent.StoppingDistance;
        }

        Assert.True(arrived, $"Arrival was never reported (RemainingDistance ended at {agent.RemainingDistance:0.000}).");
        double endDistance = Float3.Distance(agentGo.Transform.Position, new Float3(8, 0, 8));
        Assert.True(endDistance < 1.0, $"Arrival reported {endDistance:0.00} away from the destination.");

        // Unity parity: the destination stays readable after arrival (migrated code reads it).
        Assert.True(Float3.Distance(agent.Destination, new Float3(8, 0, 8)) < 0.01,
            $"Destination should still return the last target after arrival, got {agent.Destination}.");
    }

    /// <summary>
    /// Unity parity: SetDestination on a stopped agent remembers the target but does NOT
    /// clear the stopped state — IsStopped is a pause flag that survives new destinations.
    /// </summary>
    [Fact]
    public void Agent_SetDestinationWhileStopped_StaysHalted()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, -8);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 8f;
        agent.Acceleration = 100f;

        Tick(scene, 2);
        agent.IsStopped = true;
        agent.SetDestination(new Float3(8, 0, 8));
        Assert.True(agent.IsStopped, "SetDestination must not clear the stopped state.");

        Float3 posBefore = agentGo.Transform.Position;
        Tick(scene, 60);
        Assert.True(Float3.Distance(agentGo.Transform.Position, posBefore) < 0.2,
            "A stopped agent should not move toward a newly set destination.");

        agent.IsStopped = false;
        Tick(scene, 120);
        Assert.True(Float3.Distance(agentGo.Transform.Position, posBefore) > 3.0,
            "Resuming should start movement toward the remembered destination.");
    }

    /// <summary>
    /// A changed-geometry region entirely outside the baked bounds must be a no-op — the old
    /// clamp dragged the tile range onto the nearest edge column and, with explicit sources
    /// that don't cover it, destroyed healthy edge tiles.
    /// </summary>
    [Fact]
    public void RebuildTiles_RegionOutsideBakedBounds_IsNoOp()
    {
        (Scene scene, NavMeshSurface surface) = CreateBakedFloorScene(20f); // floor -10..10

        // Edge of the floor is walkable before.
        Assert.True(scene.Navigation.SamplePosition(new Float3(9, 0.2f, 0), out _, 1f, NavMesh.AllAreas));

        // Region entirely outside the baked bounds, with sources that don't cover the edge.
        bool changed = surface.RebuildTiles(new AABB(new Float3(50, -1, 50), new Float3(60, 1, 60)),
            [], out int rebuiltTiles);

        Assert.False(changed, "An out-of-bounds region should rebuild nothing.");
        Assert.Equal(0, rebuiltTiles);
        // The edge tiles survived.
        Assert.True(scene.Navigation.SamplePosition(new Float3(9, 0.2f, 0), out _, 1f, NavMesh.AllAreas),
            "Edge tiles must not be clamped into the rebuild and destroyed.");
    }

    /// <summary>
    /// Corridor stability: a lone agent walking a 3-unit-wide corridor with tuned steering
    /// (collision query range matched to the corridor instead of the open-level default of
    /// radius x 12) must track the centreline instead of weaving between avoidance samples.
    /// </summary>
    [Fact]
    public void Agent_TunedForCorridor_DoesNotWeave()
    {
        Scene scene = CreateScene(enable: true);

        GameObject floor = CreateGameObject("Corridor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(30, 1, 3);
        floor.Transform.Position = new Float3(15, -0.5f, 1.5f); // corridor x 0..30, z 0..3

        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);
        Assert.True(surface.BuildNavMesh());

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(2, 0, 1.5f);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 4f;
        agent.Acceleration = 50f;
        agent.CollisionQueryRange = 2f;  // corridor-scale, not radius x 12 = 6
        agent.Separation = false;        // lone agent; separation has nothing useful to add

        Tick(scene, 2);
        Assert.True(agent.IsOnNavMesh);
        agent.SetDestination(new Float3(28, 0, 1.5f));

        double maxDeviation = 0;
        for (int i = 0; i < 600; i++)
        {
            Tick(scene, 1);
            Float3 pos = agentGo.Transform.Position;
            if (pos.X > 3 && pos.X < 27) // measure the straightaway, not the endpoints
                maxDeviation = System.Math.Max(maxDeviation, System.Math.Abs(pos.Z - 1.5));
            if (!agent.PathPending && agent.RemainingDistance <= 0f) break;
        }

        Assert.True(maxDeviation < 0.6,
            $"Agent weaved {maxDeviation:0.00} units off the corridor centreline.");
    }
}
