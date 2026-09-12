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

    /// An obstacle whose footprint covers no compressed layer touches no tile, so the per-tile loop
    /// that completes an obstacle never runs for it. Without completion it sits in PROCESSING — and
    /// a removal never reaches EMPTY, leaking the slot for the life of the cache.
    [Fact]
    public void ZeroTouchObstacle_CompletesAndFreesItsSlot()
    {
        DtTileCache tc = BuildDungeonCache(out _);

        long far = tc.AddObstacle(new RcVec3f(1000f, 0f, 1000f), 1f, 2f);
        Assert.True(tc.Update(8));

        DtTileCacheObstacle obstacle = tc.GetObstacleByRef(far);
        Assert.Empty(obstacle.pending);
        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSED, obstacle.state);

        tc.RemoveObstacle(far);
        Assert.True(tc.Update(8));
        Assert.Equal(DtObstacleState.DT_OBSTACLE_EMPTY, obstacle.state);

        // The freed slot is reusable, and its salt moved on so the old ref cannot resolve to the new
        // obstacle.
        long again = tc.AddObstacle(new RcVec3f(1000f, 0f, 1000f), 1f, 2f);
        Assert.NotEqual(far, again);
        Assert.Same(obstacle, tc.GetObstacleByRef(again));
    }

    /// An obstacle is completed by its OWN last pending tile, not by whichever tile happens to be
    /// rebuilt next. A request is only drained on an Update with no rebuilds outstanding, so an
    /// obstacle queued behind one sits in PROCESSING with an empty pending list — and completing it
    /// there left it PROCESSED holding a list the per-tile loop would never drain again.
    [Fact]
    public void ObstacleAwaitingItsRequest_IsNotCompletedByAnotherTilesRebuild()
    {
        DtTileCache tc = BuildDungeonCache(out _);

        // A wide obstacle so its rebuilds outlast one Update slice, leaving the request loop shut.
        long wide = tc.AddObstacle(new RcVec3f(-1.815208f, 9.998184f, -20.307983f), 25f, 4f);
        Assert.False(tc.Update(1));
        Assert.NotEmpty(tc.GetObstacleByRef(wide).pending);

        // Queued behind those rebuilds: its own request cannot be drained yet, so it has been told
        // nothing about which tiles it touches.
        long queued = tc.AddObstacle(new RcVec3f(-1.815208f, 9.998184f, -20.307983f), 1f, 2f);
        DtTileCacheObstacle obstacle = tc.GetObstacleByRef(queued);
        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSING, obstacle.state);
        Assert.Empty(obstacle.pending);

        // This slice rebuilds a tile for the FIRST obstacle. That must not complete the second.
        Assert.False(tc.Update(1));
        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSING, obstacle.state);

        // Once its own request is drained and its tiles built, it completes with nothing left over.
        while (!tc.Update(8))
        {
        }

        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSED, obstacle.state);
        Assert.Empty(obstacle.pending);
    }

    /// The reachable consequence: an obstacle placed over a column with no layer, then a layer added
    /// there. RefreshObstacleTouchedTiles skips anything not PROCESSED, so only a completed obstacle
    /// picks the new tile up — and Prowl's tile-replacement path depends on exactly that listing.
    [Fact]
    public void ObstacleOverAnUnbakedColumn_ListsTheTileThatArrives()
    {
        DtTileCache tc = BuildDungeonCache(out List<long> refs);
        long target = FindMeshedTile(tc, refs);
        Assert.NotEqual(0, target);

        DtTileCacheLayerHeader header = tc.GetTileByRef(target).header;
        byte[] data = tc.GetTileByRef(target).data;
        float cs = tc.GetParams().cs;
        var centre = new RcVec3f(
            header.bmin.X + (header.minx + header.maxx + 1) * 0.5f * cs,
            header.bmin.Y + 1f,
            header.bmin.Z + (header.miny + header.maxy + 1) * 0.5f * cs);

        // Empty the column, then place the obstacle over it: nothing for it to touch.
        tc.RemoveTile(target);
        long obstacleRef = tc.AddObstacle(centre, 1f, 2f);
        Assert.True(tc.Update(8));
        DtTileCacheObstacle obstacle = tc.GetObstacleByRef(obstacleRef);
        Assert.Empty(obstacle.pending);
        Assert.Equal(DtObstacleState.DT_OBSTACLE_PROCESSED, obstacle.state);

        // The layer arrives. Neither the add nor the refresh queues a build, so Prowl asks for one
        // itself; what matters here is that the obstacle now knows about the tile.
        Assert.True(tc.TryAddTile(data, 0, out long arrived));
        Assert.NotEqual(0, arrived);
        tc.RefreshObstacleTouchedTiles();
        Assert.Contains(arrived, obstacle.touched);
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
