/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
Prowl.Recast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System.Collections.Generic;
using System.IO;
using Prowl.Recast.Core;
using Prowl.Recast.Detour.TileCache.Io;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests.Io;


public class TileCacheReaderWriterTest : AbstractTileCacheTest
{
    private readonly DtTileCacheReader reader = new DtTileCacheReader(DtTileCacheCompressorFactory.Shared);
    private readonly DtTileCacheWriter writer = new DtTileCacheWriter(DtTileCacheCompressorFactory.Shared);

    [Fact]
    public void TestFastLz()
    {
        TestDungeon(false);
        TestDungeon(true);
    }

    [Fact]
    public void TestLZ4()
    {
        TestDungeon(true);
        TestDungeon(false);
    }

    private void TestDungeon(bool cCompatibility)
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        List<byte[]> layers = layerBuilder.Build(RcByteOrder.LITTLE_ENDIAN, cCompatibility, 1);
        DtTileCache tc = GetTileCache(geom, RcByteOrder.LITTLE_ENDIAN, cCompatibility);
        foreach (byte[] layer in layers)
        {
            long refs = tc.AddTile(layer, 0);
            tc.BuildNavMeshTile(refs);
        }

        using var msw = new MemoryStream();
        using var bw = new BinaryWriter(msw);
        writer.Write(bw, tc, RcByteOrder.LITTLE_ENDIAN, cCompatibility);

        using var msr = new MemoryStream(msw.ToArray());
        using var br = new BinaryReader(msr);
        tc = reader.Read(br, 6, null);
        Assert.Equal(256, tc.GetNavMesh().GetMaxTiles());
        Assert.Equal(16384, tc.GetNavMesh().GetParams().maxPolys);
        Assert.Equal(14.4f, tc.GetNavMesh().GetParams().tileWidth, 0.001f);
        Assert.Equal(14.4f, tc.GetNavMesh().GetParams().tileHeight, 0.001f);
        Assert.Equal(6, tc.GetNavMesh().GetMaxVertsPerPoly());
        Assert.Equal(0.3f, tc.GetParams().cs, 0.0f);
        Assert.Equal(0.2f, tc.GetParams().ch, 0.0f);
        Assert.Equal(0.9f, tc.GetParams().walkableClimb, 0.0f);
        Assert.Equal(2f, tc.GetParams().walkableHeight, 0.0f);
        Assert.Equal(0.6f, tc.GetParams().walkableRadius, 0.0f);
        Assert.Equal(48, tc.GetParams().width);
        Assert.Equal(6 * 7 * 4, tc.GetParams().maxTiles);
        Assert.Equal(128, tc.GetParams().maxObstacles);
        Assert.Equal(168, tc.GetTileCount());
        // Tile0: Tris: 8, Verts: 18 Detail Meshed: 8 Detail Verts: 0 Detail Tris: 14
        DtMeshTile tile = tc.GetNavMesh().GetTile(0);
        DtMeshData data = tile.data;
        DtMeshHeader header = data.header;
        Assert.Equal(18, header.vertCount);
        Assert.Equal(8, header.polyCount);
        Assert.Equal(8, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(14, header.detailTriCount);
        Assert.Equal(8, data.polys.Length);
        Assert.Equal(3 * 18, data.verts.Length);
        Assert.Equal(8, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 14, data.detailTris.Length);
        // Tile8: Tris: 3, Verts: 8 Detail Meshed: 3 Detail Verts: 0 Detail Tris: 6
        tile = tc.GetNavMesh().GetTile(8);
        data = tile.data;
        header = data.header;
        Assert.Equal(8, header.vertCount);
        Assert.Equal(3, header.polyCount);
        Assert.Equal(3, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(6, header.detailTriCount);
        Assert.Equal(3, data.polys.Length);
        Assert.Equal(3 * 8, data.verts.Length);
        Assert.Equal(3, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 6, data.detailTris.Length);
        // Tile16: Tris: 10, Verts: 20 Detail Meshed: 10 Detail Verts: 0 Detail Tris: 18
        tile = tc.GetNavMesh().GetTile(16);
        data = tile.data;
        header = data.header;
        Assert.Equal(20, header.vertCount);
        Assert.Equal(10, header.polyCount);
        Assert.Equal(10, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(18, header.detailTriCount);
        Assert.Equal(10, data.polys.Length);
        Assert.Equal(3 * 20, data.verts.Length);
        Assert.Equal(10, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 18, data.detailTris.Length);
        // Tile29: Tris: 1, Verts: 5 Detail Meshed: 1 Detail Verts: 0 Detail Tris: 3
        tile = tc.GetNavMesh().GetTile(29);
        data = tile.data;
        header = data.header;
        Assert.Equal(5, header.vertCount);
        Assert.Equal(1, header.polyCount);
        Assert.Equal(1, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(3, header.detailTriCount);
        Assert.Equal(1, data.polys.Length);
        Assert.Equal(3 * 5, data.verts.Length);
        Assert.Equal(1, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 3, data.detailTris.Length);
    }
}