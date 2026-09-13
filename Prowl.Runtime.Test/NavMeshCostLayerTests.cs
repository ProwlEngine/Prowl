// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshCostLayer"/> directly (no navmesh needed - it is keyed by opaque polygon
/// references, not positions), <see cref="NavMeshSystem.AddCost"/>/<see cref="NavMeshSystem.ClearCosts"/>
/// end to end against a real bake (a cost is placed near a point, it decays back to nothing on its own
/// schedule, and clearing removes it immediately regardless of decay), and that a costed region actually
/// changes which route <see cref="NavMeshQuery.FindPath"/> returns. The route-avoidance test bakes with a
/// small tile size specifically to give the search more than one or two polygons to choose between - a
/// coarse bake of a small open area can merge the whole thing into a single polygon, which leaves a
/// correctly elevated cost nothing to actually prefer an alternative over, regardless of magnitude.
/// </summary>
public class NavMeshCostLayerTests : RuntimeTestBase
{
    [Fact]
    public void Set_ThenGetAdditiveCost_ReturnsWhatWasSet()
    {
        var layer = new NavMeshCostLayer();
        Assert.Equal(0f, layer.GetAdditiveCost(polyRef: 42));

        layer.Set(polyRef: 42, cost: 7.5f, decayPerSecond: 0f);
        Assert.Equal(7.5f, layer.GetAdditiveCost(42));
        Assert.Equal(1, layer.Count);
    }

    [Fact]
    public void Decay_ReducesCostOverTime_AndRemovesItOnceItReachesZero()
    {
        var layer = new NavMeshCostLayer();
        layer.Set(polyRef: 1, cost: 10f, decayPerSecond: 4f);

        layer.Decay(1f); // 10 - 4 = 6
        Assert.Equal(6f, layer.GetAdditiveCost(1));
        Assert.Equal(1, layer.Count);

        layer.Decay(1f); // 6 - 4 = 2
        Assert.Equal(2f, layer.GetAdditiveCost(1));

        layer.Decay(1f); // 2 - 4 <= 0, removed
        Assert.Equal(0f, layer.GetAdditiveCost(1));
        Assert.Equal(0, layer.Count);
    }

    [Fact]
    public void Decay_NeverFadesAnEntryWithZeroDecayRate()
    {
        var layer = new NavMeshCostLayer();
        layer.Set(polyRef: 1, cost: 5f, decayPerSecond: 0f);

        layer.Decay(1000f); // however much time passes, nothing here decays on its own

        Assert.Equal(5f, layer.GetAdditiveCost(1));
    }

    [Fact]
    public void Set_OverwritesAnExistingEntryForTheSamePolygon()
    {
        var layer = new NavMeshCostLayer();
        layer.Set(polyRef: 1, cost: 5f, decayPerSecond: 0f);
        layer.Set(polyRef: 1, cost: 20f, decayPerSecond: 1f);

        Assert.Equal(20f, layer.GetAdditiveCost(1));
        Assert.Equal(1, layer.Count); // replaced, not appended
    }

    [Fact]
    public void Clear_RemovesEveryEntryRegardlessOfDecayRate()
    {
        var layer = new NavMeshCostLayer();
        layer.Set(polyRef: 1, cost: 5f, decayPerSecond: 0f);
        layer.Set(polyRef: 2, cost: 5f, decayPerSecond: 1f);

        layer.Clear();

        Assert.Equal(0, layer.Count);
        Assert.Equal(0f, layer.GetAdditiveCost(1));
        Assert.Equal(0f, layer.GetAdditiveCost(2));
    }

    private static Mesh CreateQuad(float size)
    {
        float h = size * 0.5f;
        return new Mesh
        {
            Vertices = [new(-h, 0, -h), new(h, 0, -h), new(h, 0, h), new(-h, 0, h)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    private NavMeshSurface BuildBakedSurface(Scene scene)
    {
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(30f);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();

        return surface;
    }

    /// <summary>Same ground as <see cref="BuildBakedSurface"/>, but tiled finely enough that the open
    /// plane bakes into many polygons rather than one or two - see this class's own doc comment for why
    /// route-avoidance specifically needs that.</summary>
    private NavMeshSurface BuildBakedSurfaceWithFineTiles(Scene scene)
    {
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(30f);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.TileSize = 4f;
        scene.Add(surface.GameObject);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();

        return surface;
    }

    private static float PathLength(NavMeshPath path)
    {
        float total = 0f;
        for (int i = 1; i < path.Corners.Length; i++)
            total += Float3.Distance(path.Corners[i - 1], path.Corners[i]);
        return total;
    }

    [Fact]
    public void AddCost_BiasesThePathAroundTheCostedRegion_AndTheDirectRouteReturnsAfterClearing()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurfaceWithFineTiles(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var start = new Float3(-13, 0, -13);
        var goal = new Float3(13, 0, 13);

        // With nothing costed, the direct diagonal is a clear, uninterrupted straight line - just the
        // two endpoints, no intermediate corners.
        NavMeshPath direct = surface.Query!.FindPath(start, goal);
        Assert.True(direct.Success);
        Assert.Equal(2, direct.Corners.Length);
        float directLength = PathLength(direct);

        // A costed disc dead center on that diagonal, wide enough that going straight through it is
        // genuinely more expensive than bending around it.
        Assert.True(system.AddCost(surface.AgentTypeId, Float3.Zero, radius: 6f, cost: 1000f, decayPerSecond: 0f));

        NavMeshPath avoiding = surface.Query!.FindPath(start, goal);
        Assert.True(avoiding.Success);
        Assert.True(avoiding.Corners.Length > 2, "Expected the search to bend around the costed region, not go straight through it.");
        Assert.True(PathLength(avoiding) > directLength, "Expected the avoiding route to be geometrically longer than the direct one.");

        // Clearing the cost (rather than waiting on decay - already covered by ClearCosts_RemovesEveryEntryWithoutWaitingForDecay)
        // must restore the exact same direct route, not just some other, still-different one.
        system.ClearCosts(surface.AgentTypeId);
        NavMeshPath restored = surface.Query!.FindPath(start, goal);
        Assert.True(restored.Success);
        Assert.Equal(2, restored.Corners.Length);
        Assert.Equal(directLength, PathLength(restored), precision: 3);
    }

    [Fact]
    public void AddCost_MarksNearbyPolygons_AndTheyDecayAwayOnSchedule()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        Assert.True(system.AddCost(surface.AgentTypeId, Float3.Zero, radius: 5f, cost: 100f, decayPerSecond: 20f));

        NavMeshCostLayer layer = system.GetOrCreateCostLayer(surface.AgentTypeId);
        Assert.True(layer.Count > 0, "Expected at least one polygon near the origin to have been marked.");

        // 100 / 20 per second = 5 seconds to fully decay - tick well past that.
        Tick(scene, 400); // ~6.7s at the default 60Hz fixed step

        Assert.Equal(0, layer.Count);
    }

    [Fact]
    public void ClearCosts_RemovesEveryEntryWithoutWaitingForDecay()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        Assert.True(system.AddCost(surface.AgentTypeId, Float3.Zero, radius: 5f, cost: 100f, decayPerSecond: 0f));
        NavMeshCostLayer layer = system.GetOrCreateCostLayer(surface.AgentTypeId);
        Assert.True(layer.Count > 0);

        system.ClearCosts(surface.AgentTypeId);

        Assert.Equal(0, layer.Count);
    }

    [Fact]
    public void AddCost_ReturnsFalse_WhenNothingIsBakedNearThePoint()
    {
        Scene scene = CreateScene(enable: true);
        BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        Assert.False(system.AddCost(NavMeshAgentTypes.HumanoidId, new Float3(5000, 0, 5000), radius: 4f, cost: 10f, decayPerSecond: 1f));
    }

    [Fact]
    public void AddCost_ReturnsFalse_WhenNothingIsBakedForThatAgentTypeAtAll()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        Assert.False(system.AddCost(NavMeshAgentTypes.HumanoidId, Float3.Zero, radius: 4f, cost: 10f, decayPerSecond: 1f));
    }

    [Fact]
    public void CostLayerSurvivesAQueryRebuild()
    {
        // A rebake or a link change rebuilds a surface's NavMeshQuery - a cost entry must not be lost
        // just because that happened, since NavMeshSystem owns the layer independently of any one query.
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        Assert.True(system.AddCost(surface.AgentTypeId, Float3.Zero, radius: 5f, cost: 100f, decayPerSecond: 0f));
        NavMeshCostLayer layer = system.GetOrCreateCostLayer(surface.AgentTypeId);
        int countBefore = layer.Count;
        Assert.True(countBefore > 0);

        surface.RebuildQuery();

        Assert.Same(layer, system.GetOrCreateCostLayer(surface.AgentTypeId));
        Assert.Equal(countBefore, layer.Count);
    }
}
