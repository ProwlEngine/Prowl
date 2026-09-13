// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshSystem.UnloadTiles"/>/<see cref="NavMeshSystem.LoadTiles"/>: a tile removed
/// at runtime (without touching the baked asset's own stored layers) makes paths across it fail, an
/// already-moving agent's crowd slot survives the whole round trip untouched, and reloading the tile from
/// storage restores the same path without any rebake.
/// </summary>
public class NavMeshTileStreamingTests : RuntimeTestBase
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

    private NavMeshSurface BuildBakedSurface(Scene scene)
    {
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(40f);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        surface.TileSize = 8f;
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

    private static bool ReachedFully(NavMeshPath path) => path.Success && !path.Partial;

    [Fact]
    public void UnloadingTheGoalsTile_PathFails_ReloadSucceeds_CrowdSlotNeverChanges()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var start = new Float3(-17, 0, -17);
        var goal = new Float3(17, 0, 17);

        NavMeshAgent agent = CreateGameObject("Agent").AddComponent<NavMeshAgent>();
        agent.Transform.Position = start;
        scene.Add(agent.GameObject);
        Assert.True(agent.SetDestination(goal));
        Tick(scene, 30);

        NavMeshCrowd crowd = system.GetOrCreateCrowd(agent.AgentTypeId)!;
        Assert.Equal(1, crowd.ActiveAgentCount);

        Assert.True(ReachedFully(surface.Query!.FindPath(start, goal)));

        // A generously large region around the goal, not just its own tile - Detour's own nearest-poly
        // snap is deliberately forgiving near a mesh edge (see NavMeshQuery's own doc comment), so a
        // literal point sitting in a hole can still silently resolve to a nearby *loaded* polygon's edge
        // unless nothing loaded remains within that snap radius at all.
        system.UnloadTiles(agent.AgentTypeId, goal, new Float3(20, 20, 20));

        // The tile the goal sat on is gone - a fresh path there must fail (or come back partial), not throw.
        Assert.False(ReachedFully(surface.Query!.FindPath(start, goal)));

        // The already-moving agent's own crowd slot is completely untouched by the unload, and ticking
        // it further (with its target now sitting on missing ground) must not throw either.
        Assert.Equal(1, crowd.ActiveAgentCount);
        Tick(scene, 30);
        Assert.Equal(1, crowd.ActiveAgentCount);

        system.LoadTiles(agent.AgentTypeId, goal, new Float3(20, 20, 20));

        Assert.True(ReachedFully(surface.Query!.FindPath(start, goal)));
        Assert.Equal(1, crowd.ActiveAgentCount);

        // The agent can be given a fresh destination and actually reach it now that the tile is back.
        // It still has the whole diagonal left to cover at this point, not just the last stretch.
        Assert.True(agent.SetDestination(goal));
        Tick(scene, 1200);
        Assert.True(agent.HasArrived, $"Agent ended at {agent.Position}, expected to reach {goal}.");
    }

    [Fact]
    public void LoadTiles_IsANoOp_WhenNothingWasEverUnloaded()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out _));
        system.LoadTiles(surface.AgentTypeId, Float3.Zero, new Float3(4, 4, 4)); // nothing to load - must not throw
        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out _));
    }

    [Fact]
    public void RebuildTilesAsync_AppliesTheSameChangeAsTheSyncVersion()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);

        var volumeGo = CreateGameObject("Blocker");
        volumeGo.Transform.Position = Float3.Zero;
        var volume = volumeGo.AddComponent<NavMeshModifierVolume>();
        volume.Size = new Float3(4, 4, 4);
        volume.Area = NavMeshAreas.NotWalkable;
        scene.Add(volumeGo);

        bool applied = surface.RebuildTilesAsync(Float3.Zero, new Float3(6, 6, 6)).GetAwaiter().GetResult();
        Assert.True(applied);

        Assert.True(surface.Query!.SamplePosition(Float3.Zero, out Float3 snap));
        Assert.True(Float3.Distance(snap, Float3.Zero) > 1.5f, $"Expected the new volume to carve; snapped to {snap}.");
    }
}
