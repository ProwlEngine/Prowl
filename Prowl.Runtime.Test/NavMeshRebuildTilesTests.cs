// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshSurface.RebuildTiles(Float3, Float3)"/> - a partial retile applied
/// directly to the live query's <c>DtTileCache</c>, distinct from <see cref="NavMeshSurface.RebuildQuery"/>
/// (which reconstructs the entire persistent navmesh, dropping every crowd built against the old one).
/// </summary>
public class NavMeshRebuildTilesTests : RuntimeTestBase
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

    private NavMeshSurface BuildBakedSurface(Scene scene, float groundSize, float tileSize)
    {
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

        return surface;
    }

    [Fact]
    public void RebuildTiles_PicksUpNewGeometryInTheRegionWithoutDisruptingAnAgentElsewhere()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene, groundSize: 40f, tileSize: 8f);

        // An agent walking a path nowhere near the region about to change.
        NavMeshAgent agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = new Float3(-15, 0, -15);
        scene.Add(agent.GameObject);
        Assert.True(agent.SetDestination(new Float3(15, 0, -15)));
        Tick(scene, 60);
        Float3 positionBeforeRebuild = agent.Position;

        // A NotWalkable volume placed well away from the agent's path, added after the initial bake.
        var volumeGo = CreateGameObject("Blocker");
        volumeGo.Transform.Position = new Float3(0, 0, 15);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(4, 4, 4);
        volume.Area = NavMeshAreas.NotWalkable;
        scene.Add(volumeGo);

        Assert.True(surface.RebuildTiles(new Float3(0, 0, 15), new Float3(6, 6, 6)));

        // The new volume actually took effect...
        Assert.True(surface.Query!.SamplePosition(new Float3(0, 0, 15), out Float3 snap));
        Assert.True(Float3.Distance(snap, new Float3(0, 0, 15)) > 1.5f, $"Expected the new volume to carve; snapped to {snap}.");

        // ...and the agent, whose path never went near it, was never disrupted by the retile.
        Tick(scene, 60);
        Assert.True(Float3.Distance(agent.Position, positionBeforeRebuild) > 0.01f, "Agent should still be progressing after RebuildTiles, not frozen.");
    }

    [Fact]
    public void RebuildTiles_LeavesTilesOutsideTheRegionUntouched()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene, groundSize: 40f, tileSize: 8f);

        // A NotWalkable volume far from the region RebuildTiles is asked to touch.
        var volumeGo = CreateGameObject("FarBlocker");
        volumeGo.Transform.Position = new Float3(15, 0, 15);
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(4, 4, 4);
        volume.Area = NavMeshAreas.NotWalkable;
        scene.Add(volumeGo);

        // Ask RebuildTiles to touch a completely different, empty region instead.
        Assert.True(surface.RebuildTiles(new Float3(-15, 0, -15), new Float3(6, 6, 6)));

        // The far blocker was never applied - RebuildTiles only rebuilds tiles overlapping its own region.
        Assert.True(surface.Query!.SamplePosition(new Float3(15, 0, 15), out Float3 snap));
        Assert.True(Float3.Distance(snap, new Float3(15, 0, 15)) < 0.5f, $"Expected the untouched region to be unaffected; snapped to {snap}.");
    }

    [Fact]
    public void RebuildTiles_ReturnsFalse_WhenNothingHasBeenBakedYet()
    {
        Scene scene = CreateScene(enable: true);
        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        scene.Add(surface.GameObject);

        Assert.False(surface.RebuildTiles(Float3.Zero, new Float3(4, 4, 4)));
    }
}
