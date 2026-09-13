// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshSystem.QueryTacticalPositions"/> and the three shipped
/// <see cref="ITacticalScorer"/> implementations: a wall physically blocks line of sight (but not floor
/// connectivity - the query needs both sides of it reachable to prove it is sight, not the mesh, doing
/// the filtering) between two open areas, and a "must be hidden from the target" query only ever returns
/// points on the far side of it.
/// </summary>
public class NavMeshTacticalQueryTests : RuntimeTestBase
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

    private GameObject AddWall(Scene scene, float x, float widthZ, float height, float thickness = 0.4f)
    {
        GameObject go = CreateGameObject("Wall");
        go.Transform.Position = new Float3(x, height * 0.5f, 0);
        go.AddComponent<BoxCollider>().Size = new Float3(thickness, height, widthZ);
        scene.Add(go);
        return go;
    }

    private NavMeshSurface BuildBakedSurface(Scene scene)
    {
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(30f, 30f);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.TileSize = 8f; // finer tessellation - more, smaller candidate-bearing polygons near the wall
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

    [Fact]
    public void HiddenFromTarget_ReturnsOnlyPointsBehindTheWall()
    {
        Scene scene = CreateScene(enable: true);
        AddWall(scene, x: 0, widthZ: 30f, height: 3f); // a full-width wall straight down the middle
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var target = new Float3(-10, 0, 0); // on the -X side of the wall
        var hidden = new NavMeshLineOfSightScorer(scene, target, wantsVisible: false);

        var results = system.QueryTacticalPositions(surface.AgentTypeId, new Float3(5, 0, 0), radius: 20f, [hidden]);

        Assert.NotEmpty(results);
        foreach (NavMeshTacticalResult result in results)
            Assert.True(result.Position.X > 0.2f, $"Expected every hidden-from-target result on the far side of the wall; got {result.Position}.");
    }

    [Fact]
    public void VisibleToTarget_ReturnsOnlyPointsOnTheSameSideOfTheWall()
    {
        Scene scene = CreateScene(enable: true);
        AddWall(scene, x: 0, widthZ: 30f, height: 3f);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var target = new Float3(-10, 0, 0);
        var visible = new NavMeshLineOfSightScorer(scene, target, wantsVisible: true);

        var results = system.QueryTacticalPositions(surface.AgentTypeId, new Float3(5, 0, 0), radius: 20f, [visible]);

        Assert.NotEmpty(results);
        foreach (NavMeshTacticalResult result in results)
            Assert.True(result.Position.X < -0.2f, $"Expected every visible-to-target result on the target's own side of the wall; got {result.Position}.");
    }

    [Fact]
    public void DistanceBandScorer_ExcludesCandidatesOutsideTheBand()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var origin = Float3.Zero;
        var band = new NavMeshDistanceBandScorer(origin, min: 5f, max: 8f);

        var results = system.QueryTacticalPositions(surface.AgentTypeId, origin, radius: 14f, [band]);

        Assert.NotEmpty(results);
        foreach (NavMeshTacticalResult result in results)
        {
            float d = Float3.Distance(result.Position, origin);
            Assert.True(d >= 4.5f && d <= 8.5f, $"Expected every result within the 5-8 unit band (small voxel-grid slack); distance was {d}.");
        }
    }

    [Fact]
    public void DirectionConeScorer_ExcludesCandidatesOutsideTheCone()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var origin = Float3.Zero;
        var cone = new NavMeshDirectionConeScorer(origin, forward: new Float3(1, 0, 0), angle: 60f); // facing +X

        var results = system.QueryTacticalPositions(surface.AgentTypeId, origin, radius: 12f, [cone]);

        Assert.NotEmpty(results);
        foreach (NavMeshTacticalResult result in results)
            Assert.True(result.Position.X > -0.5f, $"Expected every result roughly in front (+X); got {result.Position}.");
    }

    [Fact]
    public void CombiningScorers_RanksHigherWhenMoreCriteriaAreSatisfied()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var origin = Float3.Zero;
        var band = new NavMeshDistanceBandScorer(origin, min: 3f, max: 12f);
        var cone = new NavMeshDirectionConeScorer(origin, forward: new Float3(1, 0, 0), angle: 90f);

        var results = system.QueryTacticalPositions(surface.AgentTypeId, origin, radius: 12f, [band, cone]);

        Assert.NotEmpty(results);
        foreach (NavMeshTacticalResult result in results)
        {
            Assert.Equal(2, result.ScorerBreakdown.Count);
            Assert.Equal(result.ScorerBreakdown[0] + result.ScorerBreakdown[1], result.TotalScore, 3);
        }
    }
}
