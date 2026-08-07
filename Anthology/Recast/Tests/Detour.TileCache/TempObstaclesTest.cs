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
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;

public class TempObstaclesTest : AbstractTileCacheTest
{
    [Fact]
    public void TestDungeon()
    {
        bool cCompatibility = true;
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        List<byte[]> layers = layerBuilder.Build(RcByteOrder.LITTLE_ENDIAN, cCompatibility, 1);
        DtTileCache tc = GetTileCache(geom, RcByteOrder.LITTLE_ENDIAN, cCompatibility);
        foreach (byte[] data in layers)
        {
            long refs = tc.AddTile(data, 0);
            tc.BuildNavMeshTile(refs);
        }

        const int MAX_NEIS = 32;
        DtMeshTile[] tiles = new DtMeshTile[MAX_NEIS];
        int nneis = 0;

        nneis = tc.GetNavMesh().GetTilesAt(1, 4, tiles, MAX_NEIS);
        DtMeshTile tile = tiles[0];
        Assert.Equal(16, tile.data.header.vertCount);
        Assert.Equal(6, tile.data.header.polyCount);

        long o = tc.AddObstacle(new RcVec3f(-1.815208f, 9.998184f, -20.307983f), 1f, 2f);
        bool upToDate = tc.Update();
        Assert.True(upToDate);

        nneis = tc.GetNavMesh().GetTilesAt(1, 4, tiles, MAX_NEIS);
        tile = tiles[0];
        Assert.Equal(22, tile.data.header.vertCount);
        Assert.Equal(11, tile.data.header.polyCount);

        tc.RemoveObstacle(o);
        upToDate = tc.Update();
        Assert.True(upToDate);

        nneis = tc.GetNavMesh().GetTilesAt(1, 4, tiles, MAX_NEIS);
        tile = tiles[0];
        Assert.Equal(16, tile.data.header.vertCount);
        Assert.Equal(6, tile.data.header.polyCount);
    }

    [Fact]
    public void TestDungeonBox()
    {
        bool cCompatibility = true;
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        List<byte[]> layers = layerBuilder.Build(RcByteOrder.LITTLE_ENDIAN, cCompatibility, 1);
        DtTileCache tc = GetTileCache(geom, RcByteOrder.LITTLE_ENDIAN, cCompatibility);
        foreach (byte[] data in layers)
        {
            long refs = tc.AddTile(data, 0);
            tc.BuildNavMeshTile(refs);
        }

        const int MAX_NEIS = 32;
        DtMeshTile[] tiles = new DtMeshTile[MAX_NEIS];
        int nneis = 0;

        nneis = tc.GetNavMesh().GetTilesAt(1, 4, tiles, MAX_NEIS);
        DtMeshTile tile = tiles[0];
        Assert.Equal(16, tile.data.header.vertCount);
        Assert.Equal(6, tile.data.header.polyCount);

        long o = tc.AddBoxObstacle(
            new RcVec3f(-2.315208f, 9.998184f, -20.807983f),
            new RcVec3f(-1.315208f, 11.998184f, -19.807983f)
        );
        bool upToDate = tc.Update();
        Assert.True(upToDate);

        nneis = tc.GetNavMesh().GetTilesAt(1, 4, tiles, MAX_NEIS);
        tile = tiles[0];
        Assert.Equal(22, tile.data.header.vertCount);
        Assert.Equal(11, tile.data.header.polyCount);

        tc.RemoveObstacle(o);
        upToDate = tc.Update();
        Assert.True(upToDate);

        nneis = tc.GetNavMesh().GetTilesAt(1, 4, tiles, MAX_NEIS);
        tile = tiles[0];
        Assert.Equal(16, tile.data.header.vertCount);
        Assert.Equal(6, tile.data.header.polyCount);
    }
}