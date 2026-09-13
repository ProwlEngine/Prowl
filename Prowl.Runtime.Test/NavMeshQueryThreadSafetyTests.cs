// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshQuery"/>'s thread-safety contract: <c>FindPath</c>/<c>Raycast</c>/
/// <c>SamplePosition</c>/<c>FindClosestEdge</c> and <see cref="NavMeshQuery.TryRentQuery"/> are safe to
/// call from worker threads concurrently with each other and with the main thread carving an obstacle
/// (the one operation that structurally mutates the live navmesh's tiles).
/// </summary>
public class NavMeshQueryThreadSafetyTests : RuntimeTestBase
{
    private static Mesh CreateFlatGround(float size)
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
        ground.AddComponent<MeshRenderer>().Mesh = CreateFlatGround(40f);
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

    [Fact]
    public void ConcurrentQueriesFromWorkerThreads_WhileMainThreadCarvesAnObstacle_NeverThrow()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshQuery query = surface.Query!;

        GameObject obstacleGo = CreateGameObject("Obstacle");
        var obstacle = obstacleGo.AddComponent<NavMeshObstacle>();
        obstacle.Size = new Float3(4, 4, 4);
        scene.Add(obstacleGo);

        using var cts = new CancellationTokenSource();
        Exception? readerException = null;

        Task[] readers = new Task[4];
        for (int r = 0; r < readers.Length; r++)
        {
            int seed = r;
            readers[r] = Task.Run(() =>
            {
                var rnd = new Random(seed);
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        Float3 a = new(rnd.Next(-18, 18), 0, rnd.Next(-18, 18));
                        Float3 b = new(rnd.Next(-18, 18), 0, rnd.Next(-18, 18));
                        query.FindPath(a, b);
                        query.Raycast(a, b, out _);
                        query.SamplePosition(a, out _);
                        query.FindClosestEdge(a, 5f, out _, out _, out _);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref readerException, ex, null);
                }
            });
        }

        // The main thread repeatedly moves the obstacle, which drives real TickTileCache() write-lock
        // activity (via Tick -> NavMeshSystem.TickCrowds) concurrently with the readers above.
        for (int i = 0; i < 60; i++)
        {
            obstacle.Transform.Position = new Float3(i % 8 - 4, 0, i % 6 - 3);
            Tick(scene, 2);
        }

        cts.Cancel();
        Task.WaitAll(readers, TimeSpan.FromSeconds(10));

        Assert.Null(readerException);
    }

    [Fact]
    public void TryRentQuery_HandleAnswersQueries_AndDisposeReleasesTheLockForLaterCalls()
    {
        Scene scene = CreateScene(enable: true);
        NavMeshSurface surface = BuildBakedSurface(scene);
        NavMeshQuery query = surface.Query!;

        Assert.True(query.TryRentQuery(out NavMeshQueryHandle handle));
        try
        {
            Assert.True(handle.SamplePosition(Float3.Zero, out Float3 snap));
            Assert.True(Float3.Distance(snap, Float3.Zero) < 0.5f);

            NavMeshPath path = handle.FindPath(new Float3(-5, 0, 0), new Float3(5, 0, 0));
            Assert.True(path.Success);
        }
        finally
        {
            handle.Dispose();
        }

        // The rental's read lock must actually be released - an ordinary call afterward must not hang.
        var completed = Task.Run(() => query.FindPath(new Float3(-5, 0, 0), new Float3(5, 0, 0)))
            .Wait(TimeSpan.FromSeconds(5));
        Assert.True(completed, "FindPath after disposing a rented handle should not block.");
    }
}
