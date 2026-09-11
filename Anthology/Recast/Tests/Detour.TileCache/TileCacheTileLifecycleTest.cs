using System;
using System.Collections.Generic;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;

/// Tiles removed and replaced while the cache is live, which a cache built once and never touched
/// again does not exercise.
public class TileCacheTileLifecycleTest : AbstractTileCacheTest
{
    private static List<byte[]> BuildDungeonLayers(out IRcInputGeomProvider geom)
    {
        geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        return new TestTileLayerBuilder(geom).Build(RcByteOrder.LITTLE_ENDIAN, true, 1);
    }

    private DtTileCache BuildDungeonCache(out List<long> refs)
    {
        List<byte[]> layers = BuildDungeonLayers(out IRcInputGeomProvider geom);
        DtTileCache tc = GetTileCache(geom, RcByteOrder.LITTLE_ENDIAN, true);
        refs = [];
        foreach (byte[] data in layers)
        {
            long r = tc.AddTile(data, 0);
            tc.BuildNavMeshTile(r);
            refs.Add(r);
        }

        return tc;
    }

    /// A layer that produced navmesh polygons; an empty one leaves nothing behind to assert on.
    private static long FindMeshedTile(DtTileCache tc, List<long> refs)
    {
        foreach (long r in refs)
        {
            DtTileCacheLayerHeader header = tc.GetTileByRef(r).header;
            if (tc.GetNavMesh().GetTileRefAt(header.tx, header.ty, header.tlayer) != 0)
            {
                return r;
            }
        }

        return 0;
    }

    [Fact]
    public void RemoveTile_AlsoRemovesTheNavMeshTile()
    {
        DtTileCache tc = BuildDungeonCache(out List<long> refs);
        long meshed = FindMeshedTile(tc, refs);
        Assert.NotEqual(0, meshed);
        DtTileCacheLayerHeader header = tc.GetTileByRef(meshed).header;

        tc.RemoveTile(meshed);

        Assert.Null(tc.GetTileByRef(meshed));
        Assert.Equal(0, tc.GetNavMesh().GetTileRefAt(header.tx, header.ty, header.tlayer));
    }

    [Fact]
    public void RemoveCompressedTileOnly_LeavesTheNavMeshTile()
    {
        DtTileCache tc = BuildDungeonCache(out List<long> refs);
        long meshed = FindMeshedTile(tc, refs);
        Assert.NotEqual(0, meshed);
        DtTileCacheLayerHeader header = tc.GetTileByRef(meshed).header;

        tc.RemoveCompressedTileOnly(meshed);

        Assert.Null(tc.GetTileByRef(meshed));
        Assert.NotEqual(0, tc.GetNavMesh().GetTileRefAt(header.tx, header.ty, header.tlayer));
    }

    [Fact]
    public void TryAddTile_WhenTheTilePoolIsExhausted_ReturnsFalse()
    {
        List<byte[]> layers = BuildDungeonLayers(out IRcInputGeomProvider geom);
        Assert.True(layers.Count > 1);
        DtTileCache tc = GetTileCache(geom, RcByteOrder.LITTLE_ENDIAN, true, 1);

        Assert.True(tc.TryAddTile(layers[0], 0, out long first));
        Assert.NotEqual(0, first);

        bool exhausted = false;
        int firstRefused = -1;
        for (int i = 1; i < layers.Count && !exhausted; i++)
        {
            exhausted = !tc.TryAddTile(layers[i], 0, out _);
            if (exhausted) firstRefused = i;
        }

        Assert.True(exhausted);
        // AddTile keeps throwing where TryAddTile reports, so package consumers see no change.
        Assert.Throws<Exception>(() => tc.AddTile(layers[firstRefused], 0));
    }

    /// A tile removed while its rebuild is still queued: the salt bump makes the queued ref name a
    /// tile that no longer exists, and building it throws.
    [Fact]
    public void RemoveTile_WithARebuildStillQueued_DoesNotThrowOnTheNextUpdate()
    {
        DtTileCache tc = BuildDungeonCache(out _);
        tc.AddObstacle(new RcVec3f(-1.815208f, 9.998184f, -20.307983f), 25f, 4f);
        tc.Update(1);

        DtTileCacheObstacle obstacle = tc.GetObstacle(0);
        Assert.NotEmpty(obstacle.pending);
        long queued = obstacle.pending[0];

        tc.RemoveTile(queued);

        Assert.DoesNotContain(queued, obstacle.pending);
        Assert.DoesNotContain(queued, obstacle.touched);
        while (!tc.Update(8))
        {
        }

        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSED, obstacle.state);
    }

    /// Removing an obstacle's last pending tile has to complete it: the update loop can no longer
    /// do that, and a REMOVING obstacle would keep its slot forever.
    [Fact]
    public void RemoveTile_CompletesAnObstacleWhoseLastPendingTileGoes()
    {
        DtTileCache tc = BuildDungeonCache(out _);
        long obstacleRef = tc.AddObstacle(new RcVec3f(-1.815208f, 9.998184f, -20.307983f), 25f, 4f);
        while (!tc.Update(8))
        {
        }

        DtTileCacheObstacle obstacle = tc.GetObstacle(0);
        tc.RemoveObstacle(obstacleRef);
        tc.Update(1);
        Assert.Equal(DtObstacleState.DT_OBSTACLE_REMOVING, obstacle.state);
        Assert.NotEmpty(obstacle.pending);

        while (obstacle.pending.Count > 0)
        {
            tc.RemoveTile(obstacle.pending[0]);
        }

        Assert.Equal(DtObstacleState.DT_OBSTACLE_EMPTY, obstacle.state);
    }

    [Fact]
    public void TryAddTile_WhenThePositionIsTaken_SucceedsWithNoRef()
    {
        DtTileCache tc = BuildDungeonCache(out List<long> refs);
        byte[] data = tc.GetTileByRef(refs[0]).data;

        Assert.True(tc.TryAddTile(data, 0, out long again));
        Assert.Equal(0, again);
    }

    /// The refresh has to widen by the seam border exactly as the add path does: a tile an obstacle
    /// reaches only through that border still carves it, and would otherwise be dropped from the
    /// obstacle's list the first time it was replaced.
    [Fact]
    public void RefreshObstacleTouchedTiles_ReListsATileTouchedOnlyThroughItsSeamBorder()
    {
        DtTileCache tc = BuildDungeonCache(out List<long> refs);
        long target = FindMeshedTile(tc, refs);
        Assert.NotEqual(0, target);

        DtTileCacheLayerHeader header = tc.GetTileByRef(target).header;
        float cs = tc.GetParams().cs;
        float reach = DtTileCacheBuilder.SeamBorder * cs;

        // Just past the tile's own extent, inside the border it still reads.
        float edgeX = header.bmin.X + (header.maxx + 1) * cs;
        float centreZ = header.bmin.Z + (header.miny + header.maxy) * 0.5f * cs;
        var beyond = new RcVec3f(edgeX + reach * 0.5f, (header.bmin.Y + header.bmax.Y) * 0.5f, centreZ);

        tc.AddObstacle(beyond, 0.1f, 2f);
        while (!tc.Update(8))
        {
        }

        DtTileCacheObstacle obstacle = tc.GetObstacle(0);
        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSED, obstacle.state);
        Assert.Contains(target, obstacle.touched);

        byte[] data = tc.GetTileByRef(target).data;
        tc.RemoveTile(target);
        Assert.True(tc.TryAddTile(data, 0, out long replaced));
        Assert.DoesNotContain(replaced, obstacle.touched);

        tc.RefreshObstacleTouchedTiles();

        Assert.Contains(replaced, obstacle.touched);

        // And the listing is what actually puts the carve back: rebuilt after the refresh, the
        // replacement tile carries the obstacle again.
        tc.BuildNavMeshTile(replaced);
        Assert.NotEqual(0, tc.GetNavMesh().GetTileRefAt(header.tx, header.ty, header.tlayer));
    }
}
