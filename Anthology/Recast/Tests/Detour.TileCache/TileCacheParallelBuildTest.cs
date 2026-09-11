using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;

/// Meshing a cache's tiles on several threads at once, which is what registration does. The whole
/// point is that it changes nothing: the split build must produce the same navmesh the serial one
/// does, tile for tile.
public class TileCacheParallelBuildTest : AbstractTileCacheTest
{
    private DtTileCache BuildCache(out List<long> refs, bool parallel)
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        List<byte[]> layers = new TestTileLayerBuilder(geom).Build(RcByteOrder.LITTLE_ENDIAN, true, 1);
        DtTileCache tc = GetBorderedTileCache(geom, RcByteOrder.LITTLE_ENDIAN, true);

        refs = [];
        foreach (byte[] data in layers)
            refs.Add(tc.AddTile(data, 0));

        if (!parallel)
        {
            foreach (long r in refs)
                tc.BuildNavMeshTile(r);
            return tc;
        }

        // Build off the navmesh with a scratch per thread, then publish in ref order — the order
        // is what makes the result identical, since AddTile's neighbour linking follows it.
        var built = new DtMeshData[refs.Count];
        List<long> order = refs;
        Parallel.For(0, refs.Count,
            () => new DtTileCacheBuildScratch(),
            (i, _, scratch) =>
            {
                built[i] = tc.BuildTileMeshData(order[i], scratch);
                return scratch;
            },
            _ => { });

        for (int i = 0; i < refs.Count; i++)
            tc.CommitTile(refs[i], built[i]);

        return tc;
    }

    [Fact]
    public void ParallelBuild_ProducesTheSameNavMeshAsSerial()
    {
        DtTileCache serial = BuildCache(out List<long> serialRefs, parallel: false);
        DtTileCache parallel = BuildCache(out List<long> parallelRefs, parallel: true);
        Assert.Equal(serialRefs.Count, parallelRefs.Count);

        DtNavMesh a = serial.GetNavMesh();
        DtNavMesh b = parallel.GetNavMesh();
        Assert.Equal(a.GetMaxTiles(), b.GetMaxTiles());

        int compared = 0;
        for (int t = 0; t < a.GetMaxTiles(); t++)
        {
            DtMeshData da = a.GetTile(t)?.data;
            DtMeshData db = b.GetTile(t)?.data;
            if (da?.header == null && db?.header == null) continue;

            Assert.NotNull(da?.header);
            Assert.NotNull(db?.header);
            Assert.Equal(da.header.x, db.header.x);
            Assert.Equal(da.header.y, db.header.y);
            Assert.Equal(da.header.layer, db.header.layer);
            Assert.Equal(da.header.polyCount, db.header.polyCount);
            Assert.Equal(da.header.vertCount, db.header.vertCount);
            Assert.Equal(da.header.detailMeshCount, db.header.detailMeshCount);
            Assert.Equal(da.header.detailVertCount, db.header.detailVertCount);
            Assert.Equal(da.header.detailTriCount, db.header.detailTriCount);
            Assert.Equal(da.header.offMeshConCount, db.header.offMeshConCount);
            Assert.Equal(da.verts, db.verts);
            Assert.Equal(da.detailVerts, db.detailVerts);
            Assert.Equal(da.detailTris, db.detailTris);

            for (int p = 0; p < da.header.polyCount; p++)
            {
                Assert.Equal(da.polys[p].vertCount, db.polys[p].vertCount);
                Assert.Equal(da.polys[p].flags, db.polys[p].flags);
                Assert.Equal(da.polys[p].GetArea(), db.polys[p].GetArea());
                Assert.Equal(da.polys[p].verts, db.polys[p].verts);
                Assert.Equal(da.polys[p].neis, db.polys[p].neis);
            }

            compared++;
        }

        Assert.True(compared > 1, $"only {compared} tiles were meshed; the fixture is not exercising this");
    }

    /// A border grid over a buffer grown for a bigger tile has to look exactly like one over an
    /// exact buffer. The tile fixtures are all one size, so nothing else reaches this: an unreset
    /// cell would read as ground at whatever height the previous tile had there.
    [Fact]
    public void BorderGridOverAnOversizedBuffer_MatchesAnExactOne()
    {
        const int border = DtTileCacheBuilder.SeamBorder;
        int bigCells = (32 + border * 2) * (32 + border * 2);
        int[] heights = new int[bigCells];
        int[] areas = new int[bigCells];

        // Fill it as a big tile would have, then hand it back for a small one.
        var big = new DtTileCacheBuilder.DtTileBorderGrid(32, 32, border, heights, areas);
        for (int i = 0; i < bigCells; i++)
        {
            heights[i] = 7;
            areas[i] = 3;
        }

        var reused = new DtTileCacheBuilder.DtTileBorderGrid(8, 8, border, heights, areas);
        var exact = new DtTileCacheBuilder.DtTileBorderGrid(8, 8, border);

        int smallCells = (8 + border * 2) * (8 + border * 2);
        for (int i = 0; i < smallCells; i++)
        {
            Assert.Equal(exact.heights[i], reused.heights[i]);
            Assert.Equal(exact.areas[i], reused.areas[i]);
        }

        Assert.Equal(big.border, reused.border);
    }

    /// The heightfield's cell array is sized by the tile's cells, and every tile in the fixtures has
    /// the same count — so nothing else hands a converter a cell buffer grown for a bigger tile.
    /// Sizes chosen so the second conversion uses a fraction of each buffer.
    [Fact]
    public void CompactHeightfieldOverAnOversizedBuffer_MatchesAnExactOne()
    {
        var option = new DtTileCacheParams { cs = 0.3f, ch = 0.2f, walkableHeight = 2f, walkableClimb = 0.9f };
        var scratch = new DtTileCacheBuildScratch();

        // Inflate every pooled buffer on a big grid with ground everywhere, then convert a small one.
        var big = new DtTileCacheBuilder.DtTileBorderGrid(48, 48, DtTileCacheBuilder.SeamBorder);
        for (int i = 0; i < big.heights.Length; i++) big.heights[i] = 5;
        DtTileCacheBuilder.ToCompactHeightfield(big, option, scratch);

        var small = new DtTileCacheBuilder.DtTileBorderGrid(8, 8, DtTileCacheBuilder.SeamBorder);
        for (int z = 0; z < 8; z++)
            for (int x = 0; x < 8; x++)
                small.heights[small.Index(x, z)] = 3;

        RcCompactHeightfield pooled = DtTileCacheBuilder.ToCompactHeightfield(small, option, scratch);
        RcCompactHeightfield exact = DtTileCacheBuilder.ToCompactHeightfield(small, option, null);

        Assert.Equal(exact.width, pooled.width);
        Assert.Equal(exact.height, pooled.height);
        Assert.Equal(exact.spanCount, pooled.spanCount);
        Assert.Equal(exact.maxDistance, pooled.maxDistance);
        Assert.Equal(exact.maxRegions, pooled.maxRegions);
        Assert.Equal(exact.dist, pooled.dist);
        Assert.True(pooled.spans.Length >= exact.spans.Length, "the buffer was supposed to still be oversized");

        for (int i = 0; i < exact.width * exact.height; i++)
        {
            Assert.Equal(exact.cells[i].index, pooled.cells[i].index);
            Assert.Equal(exact.cells[i].count, pooled.cells[i].count);
        }

        for (int i = 0; i < exact.spanCount; i++)
        {
            Assert.Equal(exact.areas[i], pooled.areas[i]);
            Assert.Equal(exact.spans[i].y, pooled.spans[i].y);
            Assert.Equal(exact.spans[i].h, pooled.spans[i].h);
            Assert.Equal(exact.spans[i].con, pooled.spans[i].con);
            Assert.Equal(exact.spans[i].reg, pooled.spans[i].reg);
        }
    }

    /// A scratch caches its neighbours' layers, so a scratch held across a carve would build the
    /// seam from the ground as it used to be — silently, since the tile itself is always
    /// re-decompressed and only the border would be stale.
    [Fact]
    public void ScratchHeldAcrossACarve_DoesNotBuildAStaleSeam()
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        List<byte[]> layers = new TestTileLayerBuilder(geom).Build(RcByteOrder.LITTLE_ENDIAN, true, 1);
        DtTileCache tc = GetBorderedTileCache(geom, RcByteOrder.LITTLE_ENDIAN, true);

        var refs = new List<long>();
        foreach (byte[] data in layers)
            refs.Add(tc.AddTile(data, 0));

        // Two tiles sharing an edge in X, and the edge itself.
        long west = 0, east = 0;
        foreach (long a in refs)
        {
            DtTileCacheLayerHeader ha = tc.GetTileByRef(a).header;
            foreach (long b in refs)
            {
                DtTileCacheLayerHeader hb = tc.GetTileByRef(b).header;
                if (hb.tx != ha.tx + 1 || hb.ty != ha.ty) continue;
                west = a;
                east = b;
                break;
            }

            if (east != 0) break;
        }

        Assert.NotEqual(0, east);
        DtTileCacheLayerHeader wh = tc.GetTileByRef(west).header;
        DtTileCacheLayerHeader eh = tc.GetTileByRef(east).header;

        var scratch = new DtTileCacheBuildScratch();
        tc.BuildTileMeshData(east, scratch); // caches the west layer as it is now

        // A block inside the west tile only, within the border strip the east tile reads. On the
        // east side of the edge it must carve nothing, or the east tile's own carve would erase the
        // very cells that read the border and the stale layer would not show.
        float edge = eh.bmin.X;
        float cs = wh.bmax.X - wh.bmin.X > 0 ? (wh.bmax.X - wh.bmin.X) / wh.width : 0.3f;
        var min = new RcVec3f(edge - DtTileCacheBuilder.SeamBorder * cs, wh.bmin.Y, wh.bmin.Z);
        var max = new RcVec3f(edge - 0.01f, wh.bmax.Y, wh.bmax.Z);
        Assert.NotEqual(0, tc.AddBoxObstacle(min, max));
        for (int i = 0; i < 8 && !tc.Update(int.MaxValue); i++) { }

        DtMeshData reused = tc.BuildTileMeshData(east, scratch);
        DtMeshData fresh = tc.BuildTileMeshData(east, new DtTileCacheBuildScratch());

        Assert.Equal(fresh == null, reused == null);
        if (fresh == null) return;
        Assert.Equal(fresh.header.vertCount, reused.header.vertCount);
        Assert.Equal(fresh.header.polyCount, reused.header.polyCount);
        Assert.Equal(fresh.verts, reused.verts);
        Assert.Equal(fresh.detailVerts, reused.detailVerts);
    }

    /// The neighbour-layer cache inside a scratch is what makes a serial bake cheap, and sharing one
    /// across threads would be the easy mistake. Reusing one scratch across tiles in sequence has to
    /// stay correct — that is the cache's own path, and the parallel path with one worker.
    [Fact]
    public void OneScratchAcrossTiles_MatchesAScratchPerTile()
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        List<byte[]> layers = new TestTileLayerBuilder(geom).Build(RcByteOrder.LITTLE_ENDIAN, true, 1);

        DtTileCache shared = GetBorderedTileCache(geom, RcByteOrder.LITTLE_ENDIAN, true);
        DtTileCache fresh = GetBorderedTileCache(geom, RcByteOrder.LITTLE_ENDIAN, true);
        var sharedRefs = new List<long>();
        var freshRefs = new List<long>();
        foreach (byte[] data in layers)
        {
            sharedRefs.Add(shared.AddTile(data, 0));
            freshRefs.Add(fresh.AddTile(data, 0));
        }

        var oneScratch = new DtTileCacheBuildScratch();
        for (int i = 0; i < sharedRefs.Count; i++)
        {
            shared.CommitTile(sharedRefs[i], shared.BuildTileMeshData(sharedRefs[i], oneScratch));
            fresh.CommitTile(freshRefs[i], fresh.BuildTileMeshData(freshRefs[i], new DtTileCacheBuildScratch()));
        }

        for (int t = 0; t < shared.GetNavMesh().GetMaxTiles(); t++)
        {
            DtMeshData da = shared.GetNavMesh().GetTile(t)?.data;
            DtMeshData db = fresh.GetNavMesh().GetTile(t)?.data;
            if (da?.header == null && db?.header == null) continue;
            Assert.NotNull(da?.header);
            Assert.NotNull(db?.header);
            Assert.Equal(da.header.polyCount, db.header.polyCount);
            Assert.Equal(da.verts, db.verts);
            Assert.Equal(da.detailVerts, db.detailVerts);
        }
    }
}
