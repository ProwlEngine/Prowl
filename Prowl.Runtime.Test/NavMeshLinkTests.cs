// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Detour;

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

    /// <summary>
    /// Link ids come from the component's persistent identifier, so each link has its own and it
    /// does not change as the component is enabled and disabled. Nothing mints one at runtime,
    /// which would dirty the scene just by entering play mode.
    /// </summary>
    [Fact]
    public void Link_Id_IsDistinctPerComponentAndStable()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshLink a = AddLink(scene);
        NavMeshLink b = AddLink(scene);

        Assert.NotEqual(0, a.LinkId);
        Assert.NotEqual(a.LinkId, b.LinkId);

        int before = a.LinkId;
        a.Enabled = false;
        a.Enabled = true;
        Assert.Equal(before, a.LinkId);
        Assert.Same(a, scene.Navigation.FindLink(before));
    }

    /// <summary>
    /// Baking is something you do in the editor, and a bake gathers its links from the world's
    /// registry. A link that stayed inert outside play mode would never register, and would go
    /// silently missing from every navmesh baked from the button — with nothing to see until an
    /// agent refused to cross.
    /// </summary>
    [Fact]
    public void Link_IsBakedIntoASurfaceBuiltInTheEditor()
    {
        using (EditMode())
        {
            (Scene scene, NavMeshSurface surface) = CreateGapScene();
            AddLink(scene);

            Assert.True(surface.BuildNavMesh());
            Assert.NotEmpty(surface.NavMeshData.Res!.Links);
            Assert.Equal(NavMeshPathStatus.PathComplete,
                PathStatus(scene, new Float3(-6, 0, 0), new Float3(6, 0, 0)));
        }
    }

    /// <summary>
    /// What the scene view draws for a link comes from the mesh, not the component: the
    /// endpoints Detour snapped onto walkable polygons, which are held on the connection's
    /// polygon rather than on the connection (that keeps the positions originally asked for).
    /// A link that attached to nothing is left out entirely, which is what makes a broken one
    /// distinguishable from a working one at a glance.
    /// </summary>
    [Fact]
    public void Link_ReportsTheConnectionTheMeshActuallyHolds()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        NavMeshLink link = AddLink(scene);
        link.Bidirectional = false;
        Assert.True(surface.BuildNavMesh());

        NavMeshConnection connection = Assert.Single(scene.Navigation.CalculateTriangulation().Connections);
        Assert.Equal(link.LinkId, connection.LinkId);
        Assert.Equal(NavMeshAreas.Jump, connection.Area);
        Assert.False(connection.Bidirectional);

        // Snapped onto the islands, so within a tolerance of the authored ends rather than at
        // them — and on the walkable surface, not the y=0 plane the component was authored on.
        Assert.True(Float3.Distance(connection.Start, link.WorldStart) < 1.5f,
            $"Start {connection.Start} should be near the authored {link.WorldStart}.");
        Assert.True(Float3.Distance(connection.End, link.WorldEnd) < 1.5f,
            $"End {connection.End} should be near the authored {link.WorldEnd}.");

        // Move an end over the gap: inside the tile grid, so the connection is still stored, but
        // with no walkable polygon under it Detour never attaches that end. Either end failing
        // makes the link untraversable, and both must therefore be reported as nothing — the far
        // end is attached separately from the start, so it is its own way to fail.
        link.EndPoint = new Float3(0, 0, 0);
        Tick(scene, 2);
        Assert.Empty(scene.Navigation.CalculateTriangulation().Connections);
        Assert.Equal(NavMeshPathStatus.PathPartial,
            PathStatus(scene, new Float3(-6, 0, 0), new Float3(6, 0, 0)));

        link.StartPoint = new Float3(0, 0, 0);
        link.EndPoint = new Float3(3, 0, 0);
        Tick(scene, 2);
        Assert.Empty(scene.Navigation.CalculateTriangulation().Connections);
    }

    /// <summary>
    /// A frame's worth of link edits is applied as one pass per surface. Demolishing a building
    /// disables all its ladders in the same frame; applied one at a time, each edit re-collects
    /// the scene's links, replaces the whole link set again, and re-contours tiles the edit
    /// before it just did — so the cost is per link rather than per affected tile.
    /// </summary>
    [Fact]
    public void Links_ChangedInOneFrame_RebuildInOnePass()
    {
        (Scene scene, NavMeshSurface surface) = CreateGapScene();
        var links = new List<NavMeshLink>();
        for (int i = 0; i < 6; i++)
            links.Add(AddLink(scene));

        Assert.True(surface.BuildNavMesh());
        Assert.True(TickUntil(scene, () => !scene.Navigation.GetInstance()!.CachePending) >= 0);

        int changes = 0;
        scene.Navigation.NavMeshChanged += () => changes++;

        foreach (NavMeshLink link in links)
            link.Activated = false;
        Tick(scene, 2); // links mark from LateUpdate; the world drains them at the next update

        // One from the coalesced rebuild, one from the pump draining the tile work it queued.
        // Uncoalesced this is two per link — each endpoint region is its own rebuild.
        Assert.InRange(changes, 1, 2);
        Assert.Equal(NavMeshPathStatus.PathPartial,
            PathStatus(scene, new Float3(-6, 0, 0), new Float3(6, 0, 0)));
    }

    /// <summary>A link answers for its own scene only: two additively loaded scenes derive ids
    /// from their own components and have no reason to agree on them, so one scene resolving
    /// another's link would hand an agent a component from the wrong world.</summary>
    [Fact]
    public void Link_ResolvesOnlyWithinItsOwnScene()
    {
        Scene home = CreateScene(enable: true);
        Scene other = CreateScene(enable: true);
        NavMeshLink link = AddLink(home);

        Assert.Same(link, home.Navigation.FindLink(link.LinkId));
        Assert.Null(other.Navigation.FindLink(link.LinkId));
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
    /// The Unity arrival idiom "!PathPending &amp;&amp; RemainingDistance &lt;= StoppingDistance"
    /// must not false-fire mid-link, or a waypoint script issues its next destination during the
    /// hop and ping-pongs the agent across the link forever. Mid-hop the value stays bounded below
    /// by the path remaining AFTER landing.
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
