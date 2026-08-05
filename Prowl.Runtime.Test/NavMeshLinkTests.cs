// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using DotRecast.Detour;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// NavMeshLink: off-mesh connections baked from components — gap bridging, directionality,
/// area masking, parallel-connection width, crowd traversal, and runtime toggling via
/// targeted rebuilds.
/// </summary>
public class NavMeshLinkTests : RuntimeTestBase
{
    /// <summary>Two 8-wide floor islands separated by a 4-unit void gap (A: x -10..-2,
    /// B: x 2..10), with a surface ready to bake. Nothing walkable connects them.</summary>
    private (Scene scene, NavMeshSurface surface) CreateGapScene()
    {
        Scene scene = CreateScene(enable: true);

        GameObject islandA = CreateGameObject("IslandA");
        scene.Add(islandA);
        islandA.AddComponent<BoxCollider>().Size = new Float3(8, 1, 8);
        islandA.Transform.Position = new Float3(-6, -0.5f, 0);

        GameObject islandB = CreateGameObject("IslandB");
        scene.Add(islandB);
        islandB.AddComponent<BoxCollider>().Size = new Float3(8, 1, 8);
        islandB.Transform.Position = new Float3(6, -0.5f, 0);

        GameObject surfaceGo = CreateGameObject("NavMeshSurface");
        scene.Add(surfaceGo);
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        ApplyFastBakeSettings(surface);
        return (scene, surface);
    }

    private NavMeshLink AddLink(Scene scene, float width = 0f)
    {
        GameObject linkGo = CreateGameObject("Link");
        scene.Add(linkGo);
        var link = linkGo.AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(-3, 0, 0); // on island A
        link.EndPoint = new Float3(3, 0, 0);    // on island B
        link.Width = width;
        return link;
    }

    private static NavMeshPathStatus PathStatus(Scene scene, Float3 from, Float3 to, int areaMask = NavMesh.AllAreas)
    {
        var path = new NavMeshPath();
        return scene.Navigation.CalculatePath(from, to, areaMask, path) ? path.Status : NavMeshPathStatus.PathInvalid;
    }

    /// <summary>A link across the gap makes the far island reachable; without one (or with
    /// Activated off at bake) the path stays partial.</summary>
    [Fact]
    public void Link_BridgesGap_AndActivatedOffDoesNot()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();

        Assert.True(surface.BuildNavMesh());
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        NavMeshLink link = AddLink(scene);
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        link.Activated = false;
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>
    /// The headline for links: the connection survives the cache re-contouring a tile. Tiles are
    /// rebuilt from geometry-only layers, so connections cannot simply be baked in once — they
    /// ride on the asset and are re-injected on every tile build. An obstacle carving inside the
    /// link's own tile is the exact moment a baked-in connection would be regenerated away.
    /// </summary>
    [Fact]
    public void Link_SurvivesObstacleCarve()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        AddLink(scene);
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        // Carve a hole on island A, well clear of the link's landing point but in its tiles.
        GameObject crate = CreateGameObject("Crate");
        scene.Add(crate);
        crate.Transform.Position = new Float3(-7, 1, 2.5f);
        var obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(2, 3, 2);

        bool carved = false;
        for (int i = 0; i < 240 && !carved; i++)
        {
            Tick(scene, 1);
            carved = !scene.Navigation.SamplePosition(new Float3(-7, 0.2f, 2.5f), out _, 0.4f, NavMesh.AllAreas);
        }
        Assert.True(carved, "The obstacle should carve.");
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>A link added after a bake inserts itself through the catch-up path, re-contouring
    /// the affected tiles without re-voxelizing anything.</summary>
    [Fact]
    public void Link_AddedAtRuntime()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        AddLink(scene);
        Tick(scene, 3); // catch-up runs in LateUpdate
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>Disabling a link at runtime removes it — the removal path runs through OnDisable,
    /// which depends on the component already reading as disabled by the time collection
    /// re-runs.</summary>
    [Fact]
    public void Link_DisabledAtRuntime()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        link.Enabled = false;
        Tick(scene, 3);
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        link.Enabled = true;
        Tick(scene, 3);
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>
    /// Agent-type scoping is part of the link's definition, so writing it after AddComponent has
    /// to re-resolve like the endpoints do. Narrowing the scope must also REMOVE the link from
    /// the surfaces it no longer applies to, which needs the rebuild to visit the outgoing scope
    /// as well as the incoming one.
    /// </summary>
    [Fact]
    public void Link_AgentTypeScopeEdit_RemovesItFromUnaffectedSurfaces()
    {
        try
        {
            NavMeshAgentTypes.ApplyTable(
            [
                new NavMeshAgentType { Id = 0, Name = "Humanoid" },
                new NavMeshAgentType { Id = 3, Name = "Scout", Radius = 0.4f },
            ]);

            (Scene scene, NavMeshSurface surface) = CreateGapScene(); // agent type 0
            NavMeshLink link = AddLink(scene);
            Assert.True(surface.BuildNavMesh());
            Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

            // Spawn-then-configure, but for scoping: hand the link to another agent type only.
            link.AffectAllAgentTypes = false;
            link.AffectedAgentTypeIds = [3];
            Tick(scene, 3);
            Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

            // And back: widening re-attaches it.
            link.AffectAllAgentTypes = true;
            Tick(scene, 3);
            Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
        }
        finally
        {
            NavMeshAgentTypes.ApplyTable([new NavMeshAgentType { Id = 0, Name = "Humanoid" }]);
        }
    }

    /// <summary>Links round-trip on the asset: a reloaded navmesh re-injects them when it
    /// instantiates, with no live component present.</summary>
    [Fact]
    public void Link_RoundTripsOnAsset()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        AddLink(scene);
        Assert.True(surface.BuildNavMesh());

        Runtime.NavMeshData baked = surface.NavMeshData.Res!;
        Assert.NotEmpty(baked.Links);

        Prowl.Echo.EchoObject echo = Prowl.Echo.Serializer.Serialize(typeof(object), baked);
        var loaded = Prowl.Echo.Serializer.Deserialize<Runtime.NavMeshData>(echo);
        Assert.NotNull(loaded);
        Assert.Equal(baked.Links.Count, loaded!.Links.Count);

        var world = new NavMeshWorld();
        Assert.NotNull(world.AddNavMeshData(loaded));
        var path = new NavMeshPath();
        Assert.True(world.CalculatePath(new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas, path));
        Assert.Equal(NavMeshPathStatus.PathComplete, path.Status);
    }

    /// <summary>A one-directional link works one way only.</summary>
    [Fact]
    public void Link_OneDirectional_WorksOneWayOnly()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);
        link.Bidirectional = false;
        Assert.True(surface.BuildNavMesh());

        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(8, 0, 0), new Float3(-8, 0, 0)));
    }

    /// <summary>The link's area participates in masking: excluding it severs the route.</summary>
    [Fact]
    public void Link_AreaMask_ExcludingLinkAreaSeversRoute()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        AddLink(scene); // Area = Jump by default
        Assert.True(surface.BuildNavMesh());

        Assert.Equal(NavMeshPathStatus.PathComplete,
            PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
        Assert.Equal(NavMeshPathStatus.PathPartial,
            PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0), NavMesh.AllAreas & ~(1 << NavMeshAreas.Jump)));
    }

    private static int CountOffMeshPolys(NavMeshSurface surface)
    {
        DtNavMesh mesh = surface.Instance!.NativeNavMesh;
        int count = 0;
        for (int t = 0; t < mesh.GetMaxTiles(); t++)
        {
            DtMeshTile tile = mesh.GetTile(t);
            if (tile?.data?.polys == null) continue;
            for (int p = 0; p < tile.data.header.polyCount; p++)
                if (tile.data.polys[p].GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    count++;
        }
        return count;
    }

    /// <summary>Width expands into parallel connections (⌈width / 2·agentRadius⌉) so an agent
    /// enters at the nearest point along the span rather than queueing through its middle;
    /// width 0 is a single connection.</summary>
    [Fact]
    public void Link_Width_EmitsParallelConnections()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene, width: 3f); // agent radius 0.5 → 3 connections
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(3, CountOffMeshPolys(surface));

        link.Width = 0f;
        Assert.True(surface.BuildNavMesh());
        Assert.Equal(1, CountOffMeshPolys(surface));
    }

    /// <summary>An agent physically crosses via the crowd, reports the off-mesh state mid-hop,
    /// and the traversal data resolves back to the component.</summary>
    [Fact]
    public void Agent_CrossesLink_AndReportsOffMeshState()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);
        Assert.True(surface.BuildNavMesh());

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, 0);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 6f;
        agent.Acceleration = 100f;
        agent.Separation = false;

        Tick(scene, 2);
        Assert.True(agent.IsOnNavMesh);
        agent.SetDestination(new Float3(8, 0, 0));

        bool sawOffMesh = false;
        NavMeshLink? resolved = null;
        for (int i = 0; i < 900; i++)
        {
            Tick(scene, 1);
            if (agent.IsOnOffMeshLink)
            {
                sawOffMesh = true;
                OffMeshLinkData data = agent.CurrentOffMeshLinkData;
                if (data.Valid && data.Link != null) resolved = data.Link;
            }
            if (!agent.PathPending && agent.RemainingDistance <= 0f) break;
        }

        Assert.True(sawOffMesh, "Agent should traverse the gap through the off-mesh link.");
        Assert.Same(link, resolved);
        double endDistance = Float3.Distance(agentGo.Transform.Position, new Float3(8, 0, 0));
        Assert.True(endDistance < 2.0, $"Agent should reach the far island (ended {endDistance:0.0} away).");
    }

    /// <summary>Toggling Activated at runtime rebuilds the endpoint tiles automatically
    /// (AutoRebuild): the route severs and comes back without a full rebake.</summary>
    [Fact]
    public void Link_RuntimeToggle_RebuildsAffectedTiles()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        link.Activated = false;
        Tick(scene, 2); // LateUpdate change detection → targeted rebuild
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        link.Activated = true;
        Tick(scene, 2);
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>
    /// The Unity arrival idiom must not false-fire while the agent is traversing a link:
    /// RemainingDistance previously collapsed to ~0 as the hop animation landed, so waypoint
    /// scripts driven by "!PathPending &amp;&amp; RemainingDistance &lt;= StoppingDistance"
    /// issued their next destination mid-hop and ping-ponged the agent across the link
    /// forever. Mid-hop the value must stay bounded below by the path remaining AFTER landing.
    /// </summary>
    [Fact]
    public void Agent_MidHop_NeverReportsArrival()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        AddLink(scene);
        Assert.True(surface.BuildNavMesh());

        GameObject agentGo = CreateGameObject("Agent");
        scene.Add(agentGo);
        agentGo.Transform.Position = new Float3(-8, 0, 0);
        var agent = agentGo.AddComponent<NavMeshAgent>();
        agent.Speed = 6f;
        agent.Acceleration = 100f;
        agent.Separation = false;

        Tick(scene, 2);
        var goal = new Float3(8, 0, 0);
        agent.SetDestination(goal);

        // Drive with the arrival idiom, exactly like gameplay code would.
        bool sawHop = false;
        float minMidHopRemaining = float.MaxValue;
        int arrivedAtTick = -1;
        for (int i = 0; i < 900; i++)
        {
            Tick(scene, 1);
            if (agent.IsOnOffMeshLink)
            {
                sawHop = true;
                minMidHopRemaining = MathF.Min(minMidHopRemaining, agent.RemainingDistance);
            }
            if (!agent.PathPending && agent.RemainingDistance <= agent.StoppingDistance)
            {
                arrivedAtTick = i;
                break;
            }
        }

        Assert.True(sawHop, "Agent should traverse the link.");
        // The link lands ~5 units from the goal; mid-hop the reading must never drop below
        // most of that post-landing path (never anywhere near a stopping distance of 0).
        Assert.True(minMidHopRemaining > 3f,
            $"RemainingDistance collapsed to {minMidHopRemaining:0.00} mid-hop — the arrival idiom would false-fire.");
        Assert.True(arrivedAtTick >= 0, "Arrival idiom should eventually fire at the real destination.");
        Assert.True(Float3.Distance(agentGo.Transform.Position, goal) < 1.0,
            $"Arrival idiom fired {Float3.Distance(agentGo.Transform.Position, goal):0.00} away from the destination.");
    }

    /// <summary>
    /// Adverse enable order: a link enabled while no navmesh exists must insert itself when a
    /// surface later registers a STALE bake (one that predates the link) — the NavMeshChanged
    /// subscription closes the ordering hole.
    /// </summary>
    [Fact]
    public void Link_EnabledBeforeSurfaceRegisters_CatchesUpOnStaleBake()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);

        // Bake WITHOUT the link, then take the navmesh offline.
        link.GameObject.Enabled = false;
        Assert.True(surface.BuildNavMesh());
        surface.GameObject.Enabled = false;

        // Link enables first (no navmesh anywhere), surface second with the stale bake.
        link.GameObject.Enabled = true;
        surface.GameObject.Enabled = true;
        Tick(scene, 2);

        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>
    /// The baked-in safeguard observed directly: re-registering a navmesh that already
    /// contains the link must trigger NO catch-up rebuild — only the unregister/register
    /// events themselves fire. This is the safeguard whose failure mode is "everything still
    /// works, just slower at load", so it needs a direct observer.
    /// </summary>
    [Fact]
    public void Link_BakedIn_ReregistrationTriggersNoRebuild()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        AddLink(scene);
        Assert.True(surface.BuildNavMesh()); // link baked in
        Tick(scene, 2);

        int navMeshEvents = 0;
        scene.Navigation.NavMeshChanged += () => navMeshEvents++;

        // Unregister + re-register: a fresh NavMeshInstance from the same baked data.
        surface.GameObject.Enabled = false;
        surface.GameObject.Enabled = true;
        Tick(scene, 2);

        // Exactly the remove + add events; a catch-up rebuild would fire additional
        // mutation events on top.
        Assert.Equal(2, navMeshEvents);
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }

    /// <summary>Moving a link with AutoUpdatePosition rebuilds both the old and new endpoint
    /// regions: the connection follows the Transform.</summary>
    [Fact]
    public void Link_AutoUpdatePosition_FollowsTransform()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);
        link.AutoUpdatePosition = true;
        Assert.True(surface.BuildNavMesh());
        Tick(scene, 2);
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        // Slide the link into the void: both endpoints now hang over the gap, so the
        // connection can't attach and the route severs.
        link.GameObject.Transform.Position = new Float3(0, 0, 30);
        Tick(scene, 2);
        Assert.Equal(NavMeshPathStatus.PathPartial, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));

        // Slide it back: the route returns.
        link.GameObject.Transform.Position = Float3.Zero;
        Tick(scene, 2);
        Assert.Equal(NavMeshPathStatus.PathComplete, PathStatus(scene, new Float3(-8, 0, 0), new Float3(8, 0, 0)));
    }
}
