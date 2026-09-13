// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshFlowField"/>/<see cref="NavMeshAgent.FollowFlowField"/>: many agents share
/// one field built with a single call rather than each pathing to the same destination independently, and
/// the field picks up a carved obstacle once it invalidates the cache and gets rebuilt.
/// </summary>
public class NavMeshFlowFieldTests : RuntimeTestBase
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

    private NavMeshSurface BuildBakedSurface(Scene scene, float groundSize)
    {
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(groundSize);
        scene.Add(ground);

        var surface = CreateGameObject("Surface").AddComponent<NavMeshSurface>();
        // A large, featureless open field otherwise decomposes into a handful of enormous polygons -
        // Recast merges aggressively where nothing forces a split - which leaves a polygon-graph flow
        // field with almost no within-polygon steering resolution (every position inside one of those
        // few giant polygons reports the same single direction). A smaller tile size keeps individual
        // polygons small enough that the field actually has something to steer by throughout the space.
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

    [Fact]
    public void ManyAgentsConvergeOnOneGoal_FromASingleFieldBuild()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene, groundSize: 60f);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var goal = Float3.Zero;
        NavMeshFlowField? field = system.BuildFlowField(surface.AgentTypeId, goal, maxRadius: 40f);
        Assert.NotNull(field);

        const int agentCount = 200;
        var agents = new NavMeshAgent[agentCount];
        var rnd = new System.Random(1);
        for (int i = 0; i < agentCount; i++)
        {
            float x = (float)(rnd.NextDouble() * 44 - 22);
            float z = (float)(rnd.NextDouble() * 44 - 22);
            NavMeshAgent agent = CreateGameObject($"Agent{i}").AddComponent<NavMeshAgent>();
            agent.Transform.Position = new Float3(x, 0, z);
            scene.Add(agent.GameObject);
            Assert.True(agent.FollowFlowField(field!)); // no per-agent FindPath/SetDestination call
            agents[i] = agent;
        }

        Tick(scene, 1200); // 20s - plenty of time even with 200 agents jostling for room near the goal

        // "Converged" means reaching the goal's own polygon's own neighborhood, not one exact geometric
        // point, so remaining graph distance (what the field itself already computed) is the meaningful
        // check here, not raw Euclidean radius.
        int closeEnough = 0;
        foreach (NavMeshAgent agent in agents)
        {
            if (field!.TryGetDistanceToGoal(agent.Position, out float remaining) && remaining < 5f)
                closeEnough++;
        }

        Assert.True(closeEnough > agentCount * 9 / 10,
            $"Expected the overwhelming majority of agents to converge on the goal; only {closeEnough}/{agentCount} did.");
    }

    [Fact]
    public void BuildFlowField_ReusesACachedFieldForANearbyGoal()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene, groundSize: 40f);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        NavMeshFlowField? first = system.BuildFlowField(surface.AgentTypeId, new Float3(1, 0, 1), maxRadius: 20f);
        NavMeshFlowField? second = system.BuildFlowField(surface.AgentTypeId, new Float3(1.2f, 0, 0.9f), maxRadius: 20f);

        Assert.NotNull(first);
        Assert.Same(first, second); // same tile-sized cell - reused, not rebuilt
    }

    [Fact]
    public void FlowField_RoutesAroundACarvedObstacle_AfterInvalidation()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene, groundSize: 40f);
        NavMeshSystem system = NavMeshSystem.GetOrCreate(scene);

        var start = new Float3(-15, 0, 0);
        var goal = new Float3(15, 0, 0);

        NavMeshFlowField? before = system.BuildFlowField(surface.AgentTypeId, goal, maxRadius: 40f);
        Assert.NotNull(before);
        Assert.True(before!.TryGetDirection(start, out Float3 directionBefore));
        Assert.True(directionBefore.X > 0.5f, $"Expected a clear line to point roughly toward +X; was {directionBefore}.");

        // Carve a wide obstacle directly on the straight line between start and goal.
        GameObject obstacleGo = CreateGameObject("Obstacle");
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(6, 4, 30);
        scene.Add(obstacleGo);
        Tick(scene, 10); // let it carve, which invalidates the cache

        NavMeshFlowField? after = system.BuildFlowField(surface.AgentTypeId, goal, maxRadius: 40f);
        Assert.NotNull(after);
        Assert.NotSame(before, after); // the carve invalidated the cache - this is a fresh field

        Assert.True(after!.TryGetDirection(start, out Float3 directionAfter));
        Assert.True(MathF.Abs(directionAfter.Z) > 0.3f,
            $"Expected the field to route laterally around the obstacle rather than straight through it; direction was {directionAfter}.");
    }
}
