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

using System;
using System.Collections.Generic;
using Prowl.Recast.Core;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;


public class TileCacheTest : AbstractTileCacheTest
{
    [Fact]
    public void TestFastLz()
    {
        TestDungeon(RcByteOrder.LITTLE_ENDIAN, false);
        TestDungeon(RcByteOrder.LITTLE_ENDIAN, true);
        TestDungeon(RcByteOrder.BIG_ENDIAN, false);
        TestDungeon(RcByteOrder.BIG_ENDIAN, true);
        Test(RcByteOrder.LITTLE_ENDIAN, false);
        Test(RcByteOrder.LITTLE_ENDIAN, true);
        Test(RcByteOrder.BIG_ENDIAN, false);
        Test(RcByteOrder.BIG_ENDIAN, true);
    }

    [Fact]
    public void TestLZ4()
    {
        TestDungeon(RcByteOrder.LITTLE_ENDIAN, false);
        TestDungeon(RcByteOrder.LITTLE_ENDIAN, true);
        TestDungeon(RcByteOrder.BIG_ENDIAN, false);
        TestDungeon(RcByteOrder.BIG_ENDIAN, true);
        Test(RcByteOrder.LITTLE_ENDIAN, false);
        Test(RcByteOrder.LITTLE_ENDIAN, true);
        Test(RcByteOrder.BIG_ENDIAN, false);
        Test(RcByteOrder.BIG_ENDIAN, true);
    }

    private void TestDungeon(RcByteOrder order, bool cCompatibility)
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        DtTileCache tc = GetTileCache(geom, order, cCompatibility);
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        List<byte[]> layers = layerBuilder.Build(order, cCompatibility, 1);
        int cacheLayerCount = 0;
        int cacheCompressedSize = 0;
        int cacheRawSize = 0;
        foreach (byte[] layer in layers)
        {
            long refs = tc.AddTile(layer, 0);
            tc.BuildNavMeshTile(refs);
            cacheLayerCount++;
            cacheCompressedSize += layer.Length;
            cacheRawSize += 4 * 48 * 48 + 56; // FIXME
        }

        Console.WriteLine("Compressor: " + tc.GetCompressor().GetType().Name + " C Compatibility: " + cCompatibility
                          + " Layers: " + cacheLayerCount + " Raw Size: " + cacheRawSize + " Compressed: " + cacheCompressedSize);
        Assert.Equal(256, tc.GetNavMesh().GetMaxTiles());
        Assert.Equal(16384, tc.GetNavMesh().GetParams().maxPolys);
        Assert.Equal(14.4f, tc.GetNavMesh().GetParams().tileWidth, 0.001f);
        Assert.Equal(14.4f, tc.GetNavMesh().GetParams().tileHeight, 0.001f);
        Assert.Equal(6, tc.GetNavMesh().GetMaxVertsPerPoly());
        Assert.Equal(0.3f, tc.GetParams().cs);
        Assert.Equal(0.2f, tc.GetParams().ch);
        Assert.Equal(0.9f, tc.GetParams().walkableClimb);
        Assert.Equal(2f, tc.GetParams().walkableHeight);
        Assert.Equal(0.6f, tc.GetParams().walkableRadius);
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
        Assert.Equal(14.997294f, data.verts[1], 0.0001f);
        Assert.Equal(15.484785f, data.verts[6], 0.0001f);
        Assert.Equal(15.484785f, data.verts[9], 0.0001f);
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

    private void Test(RcByteOrder order, bool cCompatibility)
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("nav_test.obj");
        DtTileCache tc = GetTileCache(geom, order, cCompatibility);
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        List<byte[]> layers = layerBuilder.Build(order, cCompatibility, 1);
        int cacheLayerCount = 0;
        int cacheCompressedSize = 0;
        int cacheRawSize = 0;
        foreach (byte[] layer in layers)
        {
            long refs = tc.AddTile(layer, 0);
            tc.BuildNavMeshTile(refs);
            cacheLayerCount++;
            cacheCompressedSize += layer.Length;
            cacheRawSize += 4 * 48 * 48 + 56;
        }

        Console.WriteLine("Compressor: " + tc.GetCompressor().GetType().Name + " C Compatibility: " + cCompatibility
                          + " Layers: " + cacheLayerCount + " Raw Size: " + cacheRawSize + " Compressed: " + cacheCompressedSize);
    }

    [Fact]
    public void TestPerformance()
    {
        int threads = Environment.ProcessorCount;
        RcByteOrder order = RcByteOrder.LITTLE_ENDIAN;
        bool cCompatibility = false;

        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        for (int i = 0; i < 4; i++)
        {
            layerBuilder.Build(order, cCompatibility, 1);
            layerBuilder.Build(order, cCompatibility, threads);
        }

        long t1 = RcFrequency.Ticks;
        List<byte[]> layers = null;
        for (int i = 0; i < 8; i++)
        {
            layers = layerBuilder.Build(order, cCompatibility, 1);
        }

        long t2 = RcFrequency.Ticks;
        for (int i = 0; i < 8; i++)
        {
            layers = layerBuilder.Build(order, cCompatibility, threads);
        }

        long t3 = RcFrequency.Ticks;
        Console.WriteLine(" Time ST : " + (t2 - t1) / TimeSpan.TicksPerMillisecond);
        Console.WriteLine(" Time MT : " + (t3 - t2) / TimeSpan.TicksPerMillisecond);
        DtTileCache tc = GetTileCache(geom, order, cCompatibility);
        foreach (byte[] layer in layers)
        {
            long refs = tc.AddTile(layer, 0);
            tc.BuildNavMeshTile(refs);
        }

        Assert.Equal(256, tc.GetNavMesh().GetMaxTiles());
        Assert.Equal(16384, tc.GetNavMesh().GetParams().maxPolys);
        Assert.Equal(14.4f, tc.GetNavMesh().GetParams().tileWidth, 0.001f);
        Assert.Equal(14.4f, tc.GetNavMesh().GetParams().tileHeight, 0.001f);
        Assert.Equal(6, tc.GetNavMesh().GetMaxVertsPerPoly());
        Assert.Equal(0.3f, tc.GetParams().cs);
        Assert.Equal(0.2f, tc.GetParams().ch);
        Assert.Equal(0.9f, tc.GetParams().walkableClimb);
        Assert.Equal(2f, tc.GetParams().walkableHeight);
        Assert.Equal(0.6f, tc.GetParams().walkableRadius);
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
        Assert.Equal(14.997294f, data.verts[1], 0.0001f);
        Assert.Equal(15.484785f, data.verts[6], 0.0001f);
        Assert.Equal(15.484785f, data.verts[9], 0.0001f);
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