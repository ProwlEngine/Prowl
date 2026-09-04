// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Locks in the tile-bake allocation behaviour destructible-map games depend on: tiles with
/// no geometry skip the pipeline entirely, and span-pool recycling across rebuilds does not
/// change build output.
/// </summary>
public class NavMeshAllocationTests
{
    private static NavMeshBuildSettings TestSettings() => new()
    {
        OverrideVoxelSize = true,
        VoxelSize = 0.5f,
        OverrideTileSize = true,
        TileSize = 64,
    };

    private static NavMeshGeometrySource CornerQuad()
    {
        // Covers tile (0,0) of a 96x96 (3x3 tile) world; the other 8 tiles are empty.
        Float3[] verts = [new(0, 0, 0), new(0, 0, 36), new(36, 0, 36), new(36, 0, 0)];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return new NavMeshGeometrySource(verts, indices, Float4x4.Identity);
    }

    /// <summary>
    /// Tiles nothing overlaps must cost (almost) nothing: the chunky-index check replaces the
    /// full heightfield + pipeline run (~132 KB each before the skip). On bounded bakes of
    /// mostly-sealed worlds ~99% of tiles take this path, so this bound is what keeps
    /// full-bake allocation proportional to walkable area rather than world area.
    /// </summary>
    [Fact]
    public void EmptyTile_SkipsPipeline_AllocatingAlmostNothing()
    {
        var data = NavMeshBuilder.Build(TestSettings(), [CornerQuad()],
            worldBounds: new AABB(new Float3(0, -1, 0), new Float3(96, 1, 96)));
        Assert.NotNull(data);

        // Rebuild an empty region twice: warm-up, then measure.
        List<(int X, int Z, List<byte[]> Layers)> warm = NavMeshBuilder.BuildTilesInBounds(
            data!, [CornerQuad()], new Float3(70, -1, 70), new Float3(80, 1, 80));
        Assert.All(warm, t => Assert.Empty(t.Layers));

        long before = GC.GetAllocatedBytesForCurrentThread();
        NavMeshBuilder.BuildTilesInBounds(data!, [CornerQuad()], new Float3(70, -1, 70), new Float3(80, 1, 80));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 16 * 1024,
            $"Rebuilding an empty tile should skip the pipeline (allocated {allocated / 1024.0:0.0} KB).");
    }

    /// <summary>
    /// Span pooling must not change output: rebuilding the same tile repeatedly (cold pool,
    /// then warm recycled pool) must produce byte-identical layers. This is the guard against a
    /// recycled span leaking stale state into a later build.
    /// </summary>
    [Fact]
    public void PooledRebuilds_ProduceIdenticalTiles()
    {
        var data = NavMeshBuilder.Build(TestSettings(), [CornerQuad()],
            worldBounds: new AABB(new Float3(0, -1, 0), new Float3(96, 1, 96)));
        Assert.NotNull(data);

        var bounds = (Min: new Float3(2, -1, 2), Max: new Float3(30, 1, 30));
        List<(int X, int Z, List<byte[]> Layers)> first = NavMeshBuilder.BuildTilesInBounds(data!, [CornerQuad()], bounds.Min, bounds.Max);
        Assert.Contains(first, t => t.Layers.Count > 0);

        for (int i = 0; i < 4; i++)
        {
            List<(int X, int Z, List<byte[]> Layers)> again = NavMeshBuilder.BuildTilesInBounds(data!, [CornerQuad()], bounds.Min, bounds.Max);
            Assert.Equal(first.Count, again.Count);
            for (int t = 0; t < first.Count; t++)
            {
                Assert.Equal(first[t].X, again[t].X);
                Assert.Equal(first[t].Z, again[t].Z);
                Assert.Equal(first[t].Layers, again[t].Layers);
            }
        }
    }
}
