// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshQuery"/>'s hierarchical fast path: on a large enough bake, <see cref="NavMeshQuery.FindPath"/>
/// switches to it automatically once start and end are far enough apart in tile terms, and stays close to
/// the same route an unrestricted ("flat") search would take while restricting the underlying search to a
/// narrow tile corridor a coarse tile-level A* found first.
/// </summary>
public class NavMeshHierarchicalPathfindingTests : RuntimeTestBase
{
    private static Mesh CreateQuad(float size)
    {
        float h = size * 0.5f;
        return new Mesh
        {
            Vertices = [new(-h, 0, -h), new(h, 0, -h), new(h, 0, h), new(-h, 0, h)],
            Indices = [0, 2, 1, 0, 3, 2],
        };
    }

    // A 20x20-tile bake: one flat, open ground plane cut into a real 20x20 tile grid by a small tile
    // size, so start and end far apart genuinely span many tiles rather than one or two large ones.
    private NavMeshQuery Build20By20TileFixture()
    {
        Scene scene = CreateScene(enable: true);
        const float tileSize = 8f;
        const int tilesPerSide = 20;
        float groundSize = tileSize * tilesPerSide;

        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(groundSize);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.TileSize = tileSize;
        scene.Add(surface.GameObject);

        NavMeshBuildInput input = NavMeshGeometryCollector.Collect(scene, surface);
        NavMeshBuildResult result = new RecastNavMeshBuilder().Build(input, surface.EffectiveBakeSettings);
        Assert.True(result.Success, result.Error);

        var asset = new NavMesh();
        asset.Apply(result, surface.EffectiveBakeSettings, surface.AgentTypeId);
        surface.NavMeshAsset = asset;
        surface.RebuildQuery();

        return surface.Query!;
    }

    private static float PathLength(NavMeshPath path)
    {
        float length = 0f;
        for (int i = 1; i < path.Corners.Length; i++)
            length += Float3.Distance(path.Corners[i - 1], path.Corners[i]);
        return length;
    }

    [Fact]
    public void FindPath_AcrossManyTiles_MatchesFlatSearchWithinFivePercent()
    {
        NavMeshQuery query = Build20By20TileFixture();

        // Corner to corner across the whole 160x160 bake - comfortably many tiles apart.
        var start = new Float3(-75, 0, -75);
        var end = new Float3(75, 0, 75);

        NavMeshPath hierarchical = query.FindPath(start, end);
        NavMeshPath flat = query.FindPathFlat(start, end);

        Assert.True(hierarchical.Success);
        Assert.True(flat.Success);
        Assert.False(hierarchical.Partial);
        Assert.False(flat.Partial);

        float hierarchicalLength = PathLength(hierarchical);
        float flatLength = PathLength(flat);

        Assert.True(hierarchicalLength <= flatLength * 1.05f,
            $"Expected the hierarchical path within 5% of the flat one; hierarchical={hierarchicalLength}, flat={flatLength}.");
    }

    [Fact]
    public void FindPath_AcrossManyTiles_RestrictsSearchToASmallTileCorridor()
    {
        NavMeshQuery query = Build20By20TileFixture();

        var start = new Float3(-75, 0, -75);
        var end = new Float3(75, 0, 75);

        int? corridorTiles = query.GetHierarchicalCorridorTileCount(start, end);
        Assert.NotNull(corridorTiles);

        // The full bake is 20x20 = 400 tiles; a straight corner-to-corner corridor across it should stay
        // a small fraction of that - well under half, generously - never anywhere close to visiting the
        // whole grid the way an unrestricted search's own polygon visitation would be free to.
        Assert.True(corridorTiles!.Value < 200,
            $"Expected the coarse tile corridor to stay a small fraction of the full 400-tile grid; was {corridorTiles.Value}.");
    }

    [Fact]
    public void FindPath_WithinTheThreshold_NeverUsesTheHierarchicalPath()
    {
        NavMeshQuery query = Build20By20TileFixture();

        // Two points a couple of tiles apart, well under the hierarchical threshold - both calls should
        // trivially agree, since FindPath itself should just be running the flat search here anyway.
        var start = new Float3(-4, 0, -4);
        var end = new Float3(4, 0, 4);

        NavMeshPath a = query.FindPath(start, end);
        NavMeshPath b = query.FindPathFlat(start, end);

        Assert.True(a.Success);
        Assert.True(b.Success);
        Assert.Equal(PathLength(b), PathLength(a), 2);
    }
}
