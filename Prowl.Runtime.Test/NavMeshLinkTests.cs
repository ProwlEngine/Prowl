// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshLink"/>: bridging a gap the baked mesh alone can't cross, a disabled or
/// removed link no longer contributing to a rebuilt query, agent-type filtering, and the plain
/// <see cref="NavMeshLink.AppliesTo"/>/<see cref="NavMeshLink.ToLinkData"/> logic that doesn't need a
/// live scene at all.
/// </summary>
public class NavMeshLinkTests : RuntimeTestBase
{
    private static Mesh CreateQuad(float width, float depth)
    {
        float hw = width * 0.5f;
        float hd = depth * 0.5f;
        return new Mesh
        {
            Vertices = [new(-hw, 0, -hd), new(hw, 0, -hd), new(hw, 0, hd), new(-hw, 0, hd)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private GameObject AddGround(Scene scene, string name, Float3 position)
    {
        GameObject go = CreateGameObject(name);
        go.Transform.Position = position;
        go.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        scene.Add(go);
        return go;
    }

    private static void Bake(NavMeshSurface surface)
    {
        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(surface.Scene!, surface.LayerMask);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();
    }

    private NavMeshSurface CreateBakedSurfaceOverTwoDisjointSlabs(Scene scene)
    {
        AddGround(scene, "Left", new Float3(0, 0, 0));
        AddGround(scene, "Right", new Float3(20, 0, 0)); // 10-unit gap between edges: far wider than any agent

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);
        Bake(surface);
        return surface;
    }

    // Detour's FindPath still returns a result when the start and end are in different, disconnected
    // regions: whatever poly path it could find within the start's own region, flagged DT_PARTIAL_RESULT
    // (NavMeshPath.Partial). "The gap isn't crossable" means every result here is partial, not that
    // Success comes back false - see FindPath_UnreachableEnd_ReturnsNone in NavMeshQueryTests for the
    // genuinely-nothing-nearby case that does return NavMeshPath.None instead.
    private static bool CrossedTheGap(NavMeshPath path) => path.Success && !path.Partial;

    [Fact]
    public void WithoutALink_TheGapIsNotCrossable()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedSurfaceOverTwoDisjointSlabs(scene);

        NavMeshPath path = surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0));
        Assert.False(CrossedTheGap(path));
    }

    [Fact]
    public void EnablingALink_BridgesTheGapAndFlagsTheCrossingCorner()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedSurfaceOverTwoDisjointSlabs(scene);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);  // inside the Left slab, near its edge
        link.EndPoint = new Float3(16, 0, 0);   // inside the Right slab, near its edge
        scene.Add(link.GameObject); // OnEnable registers the link and rebuilds the surface's query

        NavMeshPath path = surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0));

        Assert.True(CrossedTheGap(path));
        Assert.Contains(true, path.CornerIsOffMeshLink);
    }

    [Fact]
    public void DisablingALink_RemovesItFromTheRebuiltQuery()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedSurfaceOverTwoDisjointSlabs(scene);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        scene.Add(link.GameObject);

        Assert.True(CrossedTheGap(surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0))));

        link.Enabled = false; // triggers OnDisable -> unregister -> rebuild

        Assert.False(CrossedTheGap(surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0))));
    }

    [Fact]
    public void AutoUpdatePosition_MovingTheLinkRebridgesTheGapAtTheNewSpot()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedSurfaceOverTwoDisjointSlabs(scene);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        link.AutoUpdatePosition = true;
        link.Transform.Position = new Float3(0, 0, 100); // starts far from both slabs - bridges nothing
        scene.Add(link.GameObject);

        Assert.False(CrossedTheGap(surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0))));

        link.Transform.Position = Float3.Zero; // StartPoint/EndPoint now land exactly on the two slabs
        Tick(scene, 5);

        Assert.True(CrossedTheGap(surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0))));
    }

    [Fact]
    public void AutoRebuild_Off_DoesNotTakeEffectUntilRequestRebuildIsCalled()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = CreateBakedSurfaceOverTwoDisjointSlabs(scene);

        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(4, 0, 0);
        link.EndPoint = new Float3(16, 0, 0);
        link.AutoRebuild = false;
        scene.Add(link.GameObject); // would normally rebuild on enable - AutoRebuild off suppresses that

        Assert.False(CrossedTheGap(surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0))),
            "AutoRebuild off must not rebuild automatically on enable.");

        link.RequestRebuild(); // the manual counterpart a caller batching changes would call instead

        Assert.True(CrossedTheGap(surface.Query!.FindPath(new Float3(-3, 0, 0), new Float3(23, 0, 0))));
    }

    [Fact]
    public void AppliesTo_FalseWhenNotActivated()
    {
        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.Activated = false;

        Assert.False(link.AppliesTo(NavMeshAgentTypes.HumanoidId));
    }

    [Fact]
    public void AppliesTo_RespectsAgentTypeIdsWhenNotAffectingAllTypes()
    {
        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.AffectAllAgentTypes = false;
        link.AgentTypeIds.Add(42);

        Assert.False(link.AppliesTo(NavMeshAgentTypes.HumanoidId));
        Assert.True(link.AppliesTo(42));
    }

    [Fact]
    public void ToLinkData_WidthZero_ProducesExactlyOneConnection()
    {
        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(0, 0, -1);
        link.EndPoint = new Float3(0, 0, 1);
        link.Width = 0f;

        var data = new System.Collections.Generic.List<NavMeshLinkData>(link.ToLinkData());
        Assert.Single(data);
    }

    [Fact]
    public void ToLinkData_PositiveWidth_ProducesTwoParallelConnections()
    {
        var link = CreateGameObject("Link").AddComponent<NavMeshLink>();
        link.StartPoint = new Float3(0, 0, -1);
        link.EndPoint = new Float3(0, 0, 1);
        link.Width = 2f;

        var data = new System.Collections.Generic.List<NavMeshLinkData>(link.ToLinkData());
        Assert.Equal(2, data.Count);
        // The two lanes should be offset symmetrically in X (perpendicular to the Z-aligned span).
        Assert.NotEqual(data[0].Start.X, data[1].Start.X);
    }
}
