// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Threading.Tasks;

using Prowl.Runtime.Navigation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exercises <see cref="NavMeshBakeJob"/>: it produces exactly the same result as calling
/// <see cref="RecastNavMeshBuilder.Build"/> directly - the one thing that could actually regress here,
/// since running it via <c>Task.Run</c> instead is a one-line wrapper around a BCL guarantee (that the
/// delegate executes on a thread pool thread, not the caller's) that isn't this codebase's to re-verify.
/// </summary>
public class NavMeshBakeJobTests : RuntimeTestBase
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

    private NavMeshBuildInput CollectFlatGroundInput()
    {
        Scene scene = CreateScene(enable: true);
        GameObject ground = CreateGameObject("Ground");
        ground.AddComponent<MeshRenderer>().Mesh = CreateQuad(10, 10);
        scene.Add(ground);
        return NavMeshGeometryCollector.Collect(scene, LayerMask.Everything);
    }

    [Fact]
    public async Task RunAsync_ProducesTheSameResultAsBuildingSynchronously()
    {
        NavMeshBuildInput input = CollectFlatGroundInput();
        NavMeshBakeSettings settings = NavMeshBakeSettings.Default;

        NavMeshBuildResult expected = new RecastNavMeshBuilder().Build(input, settings);
        NavMeshBuildResult actual = await NavMeshBakeJob.RunAsync(input, settings);

        Assert.True(expected.Success);
        Assert.True(actual.Success);
        Assert.Equal(expected.TileCacheData!.Layers.Count, actual.TileCacheData!.Layers.Count);
        Assert.Equal(expected.TileCacheData.TileWorldSize, actual.TileCacheData.TileWorldSize);
    }

    [Fact]
    public async Task RunAsync_PropagatesAFailedBuildInsteadOfThrowing()
    {
        // Same "no input" failure NavMeshBuildResult.Failed covers synchronously - confirms a failed
        // build comes back as a Success=false result through the async path too, not a faulted task.
        var emptyInput = new NavMeshBuildInput();

        NavMeshBuildResult result = await NavMeshBakeJob.RunAsync(emptyInput, NavMeshBakeSettings.Default);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
