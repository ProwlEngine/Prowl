// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Per-agent-type crowds and per-agent steering filters (area mask + cost overrides flowing
/// through the crowd's query-filter slots, not just explicit queries).
/// </summary>
public class NavMeshCrowdTests : RuntimeTestBase
{
    private NavMeshAgent AddAgent(Scene scene, Float3 position)
    {
        GameObject go = CreateGameObject("Agent");
        scene.Add(go);
        go.Transform.Position = position;
        var agent = go.AddComponent<NavMeshAgent>();
        agent.Speed = 6f;
        agent.Acceleration = 100f;
        agent.Separation = false;       // lone-agent tests; separation adds noise
        agent.CollisionQueryRange = 2f; // corridor-scale steering
        return agent;
    }

    /// <summary>
    /// An agent walking a straight line must not shiver as it brakes into its destination. Its
    /// facing used to follow the crowd's ACTUAL velocity, which carries avoidance and collision
    /// corrections that do not shrink with speed: once the agent slowed near the goal those
    /// corrections dominated a small vector and swung its direction frame to frame, so it
    /// wobbled left and right while tracking the path exactly. Facing follows the steering
    /// vector now, which points down the path the whole way in.
    /// </summary>
    [Fact]
    public void Agent_ApproachingDestination_DoesNotWobble()
    {
        (Scene scene, _) = CreateBakedFloorScene();
        NavMeshAgent agent = AddAgent(scene, new Float3(-8, 0, 0));
        Tick(scene, 2);

        // Straight down +X, no turns and nothing to avoid: every heading change is jitter.
        Assert.True(agent.SetDestination(new Float3(8, 0, 0)));

        // Only the braking zone is measured. The agent legitimately turns 90° at full angular
        // speed at the start, to face down the path; the wobble is what happens AFTER it is
        // already aligned and slowing, where every heading change is jitter by definition.
        double worstStep = 0;
        int samples = 0;
        double previousYaw = double.NaN;
        bool arrived = false;
        for (int i = 0; i < 400 && !arrived; i++)
        {
            Tick(scene, 1);
            arrived = !agent.PathPending && agent.RemainingDistance <= 0f;

            Float3 v = agent.Velocity;
            if (v.X * v.X + v.Z * v.Z < 0.0025) continue; // stopped: heading is meaningless
            if (agent.RemainingDistance > 4f) { previousYaw = double.NaN; continue; }

            double yaw = agent.Transform.Rotation.EulerAngles.Y;
            if (!double.IsNaN(previousYaw))
            {
                double step = Math.Abs(Maths.DeltaAngle(yaw * Maths.Deg2Rad, previousYaw * Maths.Deg2Rad)) * Maths.Rad2Deg;
                worstStep = Math.Max(worstStep, step);
                samples++;
            }
            previousYaw = yaw;
        }

        Assert.True(arrived, "The agent should reach the destination.");
        Assert.True(samples > 5, $"Expected to sample the braking approach, got {samples} frames.");
        // Already pointed down a straight path, so a real correction is a fraction of a degree
        // per frame. The wobble showed up as degree-scale swings back and forth near the goal.
        Assert.True(worstStep < 0.5,
            $"Agent heading jittered by {worstStep:0.00}° in one frame while braking on a straight path.");
    }

    /// <summary>
    /// With nothing in range to dodge, an agent must travel the straight line it was given —
    /// exactly, not approximately. Velocity-obstacle sampling picks from a discrete candidate
    /// set, so running it against zero obstacles still rounds the chosen velocity, and the
    /// rounding walked the agent centimetres off its line by the time it arrived. Avoidance is
    /// skipped for an agent with no neighbours now, which also skips the most expensive part of
    /// its crowd step. <see cref="NavMeshObstacleTests"/> covers the other half — that a blocker
    /// in range still deflects it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Agent_AloneOnAStraightPath_DoesNotDriftSideways(bool alongX)
    {
        (Scene scene, _) = CreateBakedFloorScene();
        NavMeshAgent agent = AddAgent(scene, alongX ? new Float3(-8, 0, 0) : new Float3(0, 0, -8));
        Assert.NotEqual(ObstacleAvoidanceType.NoObstacleAvoidance, agent.ObstacleAvoidanceQuality);
        Tick(scene, 2);

        Assert.True(agent.SetDestination(alongX ? new Float3(8, 0, 0) : new Float3(0, 0, 8)));

        double worstLateral = 0;
        bool arrived = false;
        for (int i = 0; i < 400 && !arrived; i++)
        {
            Tick(scene, 1);
            Float3 p = agent.Transform.Position;
            worstLateral = Math.Max(worstLateral, Math.Abs(alongX ? p.Z : p.X));
            arrived = !agent.PathPending && agent.RemainingDistance <= 0f;
        }

        Assert.True(arrived, "The agent should reach the destination.");
        Assert.True(worstLateral < 0.005,
            $"Agent strayed {worstLateral:0.0000} units off a straight path with nothing to avoid.");
    }

    // ── Per-agent-type crowds ───────────────────────────────────────────

    private static void RestoreDefaultAgentTable()
        => NavMeshAgentTypes.ApplyTable([new NavMeshAgentType { Id = 0, Name = "Humanoid" }]);

    /// <summary>
    /// Two agent types in one scene get two independent crowds, and each agent walks the mesh
    /// baked for its own type.
    /// </summary>
    [Fact]
    public void TwoAgentTypes_GetSeparateCrowds_AndBothWalk()
    {
        try
        {
            NavMeshAgentTypes.ApplyTable(
            [
                new NavMeshAgentType { Id = 0, Name = "Humanoid", Radius = 0.5f },
                new NavMeshAgentType { Id = 3, Name = "Scout", Radius = 0.4f },
            ]);

            Scene scene = CreateScene(enable: true);
            GameObject floor = CreateGameObject("Floor");
            scene.Add(floor);
            floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
            floor.Transform.Position = new Float3(0, -0.5f, 0);

            foreach (int typeId in (int[])[0, 3])
            {
                GameObject surfaceGo = CreateGameObject($"Surface{typeId}");
                scene.Add(surfaceGo);
                var surface = surfaceGo.AddComponent<NavMeshSurface>();
                ApplyFastBakeSettings(surface);
                surface.AgentTypeId = typeId;
                Assert.True(surface.BuildNavMesh());
            }

            NavMeshAgent humanoid = AddAgent(scene, new Float3(-8, 0, -8));
            NavMeshAgent scout = AddAgent(scene, new Float3(-8, 0, 8));
            scout.AgentTypeId = 3;

            Tick(scene, 2);
            Assert.True(humanoid.IsOnNavMesh);
            Assert.True(scout.IsOnNavMesh);

            // One crowd per type, and they are distinct objects.
            Assert.NotNull(scene.Navigation.GetNativeCrowd(0));
            Assert.NotNull(scene.Navigation.GetNativeCrowd(3));
            Assert.NotSame(scene.Navigation.GetNativeCrowd(0), scene.Navigation.GetNativeCrowd(3));
            // NativeCrowd stays sugar for type 0.
            Assert.Same(scene.Navigation.NativeCrowd, scene.Navigation.GetNativeCrowd(0));

            humanoid.SetDestination(new Float3(8, 0, -8));
            scout.SetDestination(new Float3(8, 0, 8));
            Tick(scene, 300);

            Assert.True(Float3.Distance(humanoid.GameObject.Transform.Position, new Float3(8, 0, -8)) < 2.0,
                "Humanoid should reach its destination on its own crowd.");
            Assert.True(Float3.Distance(scout.GameObject.Transform.Position, new Float3(8, 0, 8)) < 2.0,
                "Scout should reach its destination on its own crowd.");
        }
        finally
        {
            RestoreDefaultAgentTable();
        }
    }

    /// <summary>
    /// Replacing one agent type's navmesh rebinds only that type's crowd: its agents rejoin
    /// (keeping destinations) while the other type's crowd is the same object throughout.
    /// </summary>
    [Fact]
    public void NavMeshReplacement_RebindsOnlyThatTypesCrowd()
    {
        try
        {
            NavMeshAgentTypes.ApplyTable(
            [
                new NavMeshAgentType { Id = 0, Name = "Humanoid", Radius = 0.5f },
                new NavMeshAgentType { Id = 3, Name = "Scout", Radius = 0.4f },
            ]);

            Scene scene = CreateScene(enable: true);
            GameObject floor = CreateGameObject("Floor");
            scene.Add(floor);
            floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
            floor.Transform.Position = new Float3(0, -0.5f, 0);

            NavMeshSurface? scoutSurface = null;
            foreach (int typeId in (int[])[0, 3])
            {
                GameObject surfaceGo = CreateGameObject($"Surface{typeId}");
                scene.Add(surfaceGo);
                var surface = surfaceGo.AddComponent<NavMeshSurface>();
                ApplyFastBakeSettings(surface);
                surface.AgentTypeId = typeId;
                Assert.True(surface.BuildNavMesh());
                if (typeId == 3) scoutSurface = surface;
            }

            NavMeshAgent humanoid = AddAgent(scene, new Float3(-8, 0, -8));
            NavMeshAgent scout = AddAgent(scene, new Float3(-8, 0, 8));
            scout.AgentTypeId = 3;
            Tick(scene, 2);

            var humanoidCrowd = scene.Navigation.GetNativeCrowd(0);
            var scoutCrowd = scene.Navigation.GetNativeCrowd(3);
            scout.SetDestination(new Float3(8, 0, 8));
            humanoid.SetDestination(new Float3(8, 0, -8));
            Tick(scene, 30);

            // Regenerate the scout mesh only.
            Assert.True(scoutSurface!.BuildNavMesh());
            Tick(scene, 2);

            Assert.True(scout.IsOnNavMesh, "Scout must rejoin its replacement crowd.");
            Assert.NotSame(scoutCrowd, scene.Navigation.GetNativeCrowd(3));
            Assert.Same(humanoidCrowd, scene.Navigation.GetNativeCrowd(0));

            // Destinations survived on both sides of the swap.
            Tick(scene, 300);
            Assert.True(Float3.Distance(scout.GameObject.Transform.Position, new Float3(8, 0, 8)) < 2.0,
                "Scout should reach its destination on the new mesh.");
            Assert.True(Float3.Distance(humanoid.GameObject.Transform.Position, new Float3(8, 0, -8)) < 2.0,
                "Humanoid should be undisturbed by the other type's rebind.");
        }
        finally
        {
            RestoreDefaultAgentTable();
        }
    }

    /// <summary>
    /// Regression: writing AgentTypeId and then having ANY navmesh event fire before the
    /// LateUpdate drift check runs must not strand a ghost agent in the old type's still-alive
    /// crowd. The rebind check compares against the type the agent REGISTERED under, so the
    /// event leaves it alone and the drift check later re-places it through Unregister.
    /// </summary>
    [Fact]
    public void AgentTypeIdChange_RacingNavMeshEvent_LeavesNoGhostAgent()
    {
        try
        {
            NavMeshAgentTypes.ApplyTable(
            [
                new NavMeshAgentType { Id = 0, Name = "Humanoid", Radius = 0.5f },
                new NavMeshAgentType { Id = 3, Name = "Scout", Radius = 0.4f },
            ]);

            Scene scene = CreateScene(enable: true);
            GameObject floor = CreateGameObject("Floor");
            scene.Add(floor);
            floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
            floor.Transform.Position = new Float3(0, -0.5f, 0);

            NavMeshSurface? humanoidSurface = null;
            foreach (int typeId in (int[])[0, 3])
            {
                GameObject surfaceGo = CreateGameObject($"Surface{typeId}");
                scene.Add(surfaceGo);
                var surface = surfaceGo.AddComponent<NavMeshSurface>();
                ApplyFastBakeSettings(surface);
                surface.AgentTypeId = typeId;
                Assert.True(surface.BuildNavMesh());
                if (typeId == 0) humanoidSurface = surface;
            }

            NavMeshAgent agent = AddAgent(scene, new Float3(0, 0, 0));
            Tick(scene, 2);
            Assert.True(agent.IsOnNavMesh);
            var typeZeroCrowd = scene.Navigation.GetNativeCrowd(0)!;
            Assert.Equal(1, typeZeroCrowd.GetActiveAgents().Count);

            // The race: gameplay retypes the agent, then a navmesh event (here a partial tile
            // rebuild — the event a destructible world produces constantly) fires BEFORE any
            // LateUpdate has run the drift check. The type-0 crowd survives the event.
            agent.AgentTypeId = 3;
            Assert.True(humanoidSurface!.RebuildTiles(new AABB(new Float3(-2, -1, -2), new Float3(2, 1, 2))));
            Assert.Same(typeZeroCrowd, scene.Navigation.GetNativeCrowd(0));

            Tick(scene, 2); // drift check re-places the agent onto type 3
            Assert.True(agent.IsOnNavMesh);
            Assert.Same(agent.NativeAgent, System.Linq.Enumerable.FirstOrDefault(scene.Navigation.GetNativeCrowd(3)!.GetActiveAgents()));
            Assert.Equal(0, typeZeroCrowd.GetActiveAgents().Count); // no ghost left behind
        }
        finally
        {
            RestoreDefaultAgentTable();
        }
    }

    // ── Steering filters (mask + costs through the crowd) ───────────────

    private static NavMeshGeometrySource Quad(float minX, float minZ, float maxX, float maxZ, int area = NavMeshGeometrySource.UnspecifiedArea)
    {
        Float3[] verts =
        [
            new(minX, 0, minZ),
            new(minX, 0, maxZ),
            new(maxX, 0, maxZ),
            new(maxX, 0, minZ),
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return new NavMeshGeometrySource(verts, indices, Float4x4.Identity, area);
    }

    private const int MudArea = 3;

    /// <summary>
    /// A two-corridor map (the plan's "which side does the agent take" scenario): bottom
    /// corridor z 0..3 and top corridor z 6..9, joined only by connectors at both ends
    /// (x 0..4 and x 26..30) — the gap between them is void, so the corridor's raycast-based
    /// visibility optimization cannot splice a shortcut across (it CAN re-straighten a route
    /// through costly-but-passable ground in an open field, which is upstream Detour
    /// behaviour). The bottom corridor carries a mud strip (area 3) at x 12..18. From
    /// (2,1.5) to (28,1.5) the short route runs through the mud; the alternative goes up
    /// and around through the top corridor.
    /// </summary>
    private (Scene scene, NavMeshSurface surface) CreateTwoCorridorScene()
    {
        Scene scene = CreateScene(enable: true);
        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);

        NavMeshData? data = NavMeshBuilder.Build(surface.ResolveBuildSettings(),
        [
            Quad(0, 0, 12, 3),            // bottom corridor, west of the mud
            Quad(12, 0, 18, 3, MudArea),  // the mud strip
            Quad(18, 0, 30, 3),           // bottom corridor, east of the mud
            Quad(0, 6, 30, 9),            // top corridor
            Quad(0, 3, 4, 6),             // west connector
            Quad(26, 3, 30, 6),           // east connector
        ]);
        Assert.NotNull(data);
        surface.ApplyNavMeshData(data!);
        return (scene, surface);
    }

    /// <summary>Walk the agent from the bottom corridor's west end to its east end, recording
    /// the highest z reached — crossing into the top corridor (z &gt; 6) means it took the
    /// detour; staying under z 3 means it went straight through the mud.</summary>
    private (double maxZ, double endDistance) WalkBottomCorridor(Scene scene, NavMeshAgent agent)
    {
        var goal = new Float3(28, 0, 1.5f);
        Tick(scene, 2);
        Assert.True(agent.IsOnNavMesh);
        agent.SetDestination(goal);

        double maxZ = double.MinValue;
        for (int i = 0; i < 900; i++)
        {
            Tick(scene, 1);
            maxZ = System.Math.Max(maxZ, agent.GameObject.Transform.Position.Z);
            if (!agent.PathPending && agent.RemainingDistance <= 0f) break;
        }
        return (maxZ, Float3.Distance(agent.GameObject.Transform.Position, goal));
    }

    /// <summary>
    /// The area mask steers CROWD movement, not just explicit queries: an agent whose mask
    /// excludes the mud area takes the top corridor, while an unrestricted agent walks
    /// straight through the mud.
    /// </summary>
    [Fact]
    public void AreaMask_RoutesCrowdSteeringAroundExcludedArea()
    {
        (Scene scene, _) = CreateTwoCorridorScene();

        NavMeshAgent direct = AddAgent(scene, new Float3(2, 0, 1.5f));
        (double directMaxZ, double directEnd) = WalkBottomCorridor(scene, direct);
        Assert.True(directEnd < 2.0, $"Unrestricted agent should arrive (ended {directEnd:0.0} away).");
        Assert.True(directMaxZ < 4.0,
            $"Unrestricted agent should stay in the bottom corridor (reached z {directMaxZ:0.00}).");
        direct.GameObject.Enabled = false;

        NavMeshAgent masked = AddAgent(scene, new Float3(2, 0, 1.5f));
        masked.AreaMask = NavMeshAreas.AllAreas & ~(1 << MudArea);
        (double maskedMaxZ, double maskedEnd) = WalkBottomCorridor(scene, masked);
        Assert.True(maskedEnd < 2.0, $"Masked agent should still arrive via the detour (ended {maskedEnd:0.0} away).");
        Assert.True(maskedMaxZ > 6.0,
            $"Masked agent should take the top corridor around the mud (reached only z {maskedMaxZ:0.00}).");
    }

    /// <summary>
    /// SetAreaCost biases the crowd's corridor choice: with the mud expensive enough the
    /// agent takes the top corridor, exactly like the mask case but by cost rather than
    /// exclusion.
    /// </summary>
    [Fact]
    public void SetAreaCost_BiasesCrowdCorridorChoice()
    {
        (Scene scene, _) = CreateTwoCorridorScene();

        NavMeshAgent agent = AddAgent(scene, new Float3(2, 0, 1.5f));
        agent.SetAreaCost(MudArea, 20f);
        Assert.Equal(20f, agent.GetAreaCost(MudArea));

        (double maxZ, double endDistance) = WalkBottomCorridor(scene, agent);
        Assert.True(endDistance < 2.0, $"Agent should arrive (ended {endDistance:0.0} away).");
        Assert.True(maxZ > 6.0,
            $"With mud at cost 20 the agent should take the top corridor (reached only z {maxZ:0.00}).");
    }

    // ── Filter slot allocation ──────────────────────────────────────────

    private (Scene scene, NavMeshSurface surface) CreateBakedFloorScene()
    {
        Scene scene = CreateScene(enable: true);
        GameObject floor = CreateGameObject("Floor");
        scene.Add(floor);
        floor.AddComponent<BoxCollider>().Size = new Float3(20, 1, 20);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);
        Assert.True(surface.BuildNavMesh());
        return (scene, surface);
    }

    /// <summary>Agents with identical steering configs share one crowd filter slot; a
    /// different config gets its own; the default config stays on slot 0.</summary>
    [Fact]
    public void FilterSlots_SharedByIdenticalConfigs()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        NavMeshAgent defaultAgent = AddAgent(scene, new Float3(0, 0, 0));
        NavMeshAgent maskedA = AddAgent(scene, new Float3(2, 0, 0));
        NavMeshAgent maskedB = AddAgent(scene, new Float3(4, 0, 0));
        NavMeshAgent costly = AddAgent(scene, new Float3(6, 0, 0));
        maskedA.AreaMask = ~(1 << 2);
        maskedB.AreaMask = ~(1 << 2);
        costly.SetAreaCost(2, 5f);

        Tick(scene, 2);
        Assert.True(defaultAgent.IsOnNavMesh && maskedA.IsOnNavMesh && maskedB.IsOnNavMesh && costly.IsOnNavMesh);

        Assert.Equal(0, defaultAgent.NativeAgent!.option.queryFilterType);
        Assert.NotEqual(0, maskedA.NativeAgent!.option.queryFilterType);
        Assert.Equal(maskedA.NativeAgent.option.queryFilterType, maskedB.NativeAgent!.option.queryFilterType);
        Assert.NotEqual(0, costly.NativeAgent!.option.queryFilterType);
        Assert.NotEqual(maskedA.NativeAgent.option.queryFilterType, costly.NativeAgent.option.queryFilterType);
    }

    /// <summary>
    /// More distinct steering configs than slots: the overflow agents warn and steer with the
    /// default filter (slot 0) but remain fully functional, and unregistering a slotted agent
    /// frees its slot for the next distinct config.
    /// </summary>
    [Fact]
    public void FilterSlots_ExhaustionFallsBackToDefault_AndReleaseFrees()
    {
        (Scene scene, _) = CreateBakedFloorScene();

        // 17 distinct configs against 15 assignable slots (1..15; 0 is the shared default).
        var agents = new NavMeshAgent[17];
        for (int i = 0; i < agents.Length; i++)
        {
            agents[i] = AddAgent(scene, new Float3(-8 + i, 0, -8));
            agents[i].SetAreaCost(1, 2f + i); // each cost table is unique
        }
        Tick(scene, 2);

        int onDefaultSlot = 0;
        foreach (NavMeshAgent agent in agents)
        {
            Assert.True(agent.IsOnNavMesh);
            if (agent.NativeAgent!.option.queryFilterType == 0) onDefaultSlot++;
        }
        Assert.Equal(2, onDefaultSlot); // 15 got slots, 2 overflowed onto the default

        // Overflowed or not, every agent still navigates.
        agents[16].SetDestination(new Float3(8, 0, 8));
        Tick(scene, 300);
        Assert.True(Float3.Distance(agents[16].GameObject.Transform.Position, new Float3(8, 0, 8)) < 2.0,
            "An overflow agent should still reach its destination on the default filter.");

        // Free one slot and bring in an 18th distinct config: it should take the freed slot.
        NavMeshAgent slotted = System.Array.Find(agents, a => a.NativeAgent!.option.queryFilterType != 0)!;
        slotted.GameObject.Enabled = false;
        NavMeshAgent late = AddAgent(scene, new Float3(0, 0, 6));
        late.SetAreaCost(1, 99f);
        Tick(scene, 2);
        Assert.NotEqual(0, late.NativeAgent!.option.queryFilterType);
    }
}
