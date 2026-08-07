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
using System.IO;
using Prowl.Recast.Core;
using Prowl.Recast.Detour.TileCache.Io;
using Prowl.Recast.Detour.TileCache.Io.Compress;

namespace Prowl.Recast.Detour.TileCache.Tests.Io;


public class TileCacheReaderTest
{
    private readonly DtTileCacheReader reader = new DtTileCacheReader(DtTileCacheCompressorFactory.Shared);

    [Fact]
    public void TestNavmesh()
    {
        using var ms = new MemoryStream(RcIO.ReadFileIfFound("all_tiles_tilecache.bin"));
        using var br = new BinaryReader(ms);
        DtTileCache tc = reader.Read(br, 6, null);
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
        // Tile0: Tris: 1, Verts: 4 Detail Meshed: 1 Detail Verts: 0 Detail Tris: 2
        // Verts: -2.269517, 28.710686, 28.710686
        DtMeshTile tile = tc.GetNavMesh().GetTile(0);
        DtMeshData data = tile.data;
        DtMeshHeader header = data.header;
        Assert.Equal(4, header.vertCount);
        Assert.Equal(1, header.polyCount);
        Assert.Equal(1, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(2, header.detailTriCount);
        Assert.Equal(1, data.polys.Length);
        Assert.Equal(3 * 4, data.verts.Length);
        Assert.Equal(1, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 2, data.detailTris.Length);
        Assert.Equal(-2.269517f, data.verts[1], 0.0001f);
        Assert.Equal(28.710686f, data.verts[6], 0.0001f);
        Assert.Equal(28.710686f, data.verts[9], 0.0001f);
        // Tile8: Tris: 7, Verts: 10 Detail Meshed: 7 Detail Verts: 0 Detail Tris: 10
        // Verts: 0.330483, 43.110687, 43.110687
        tile = tc.GetNavMesh().GetTile(8);
        data = tile.data;
        header = data.header;
        Console.WriteLine(data.header.x + "  " + data.header.y + "  " + data.header.layer);
        Assert.Equal(4, header.x);
        Assert.Equal(1, header.y);
        Assert.Equal(0, header.layer);
        Assert.Equal(10, header.vertCount);
        Assert.Equal(7, header.polyCount);
        Assert.Equal(7, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(10, header.detailTriCount);
        Assert.Equal(7, data.polys.Length);
        Assert.Equal(3 * 10, data.verts.Length);
        Assert.Equal(7, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 10, data.detailTris.Length);
        Assert.Equal(0.330483f, data.verts[1], 0.0001f);
        Assert.Equal(43.110687f, data.verts[6], 0.0001f);
        Assert.Equal(43.110687f, data.verts[9], 0.0001f);
        // Tile16: Tris: 13, Verts: 33 Detail Meshed: 13 Detail Verts: 0 Detail Tris: 25
        // Verts: 1.130483, 5.610685, 6.510685
        tile = tc.GetNavMesh().GetTile(16);
        data = tile.data;
        header = data.header;
        Assert.Equal(33, header.vertCount);
        Assert.Equal(13, header.polyCount);
        Assert.Equal(13, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(25, header.detailTriCount);
        Assert.Equal(13, data.polys.Length);
        Assert.Equal(3 * 33, data.verts.Length);
        Assert.Equal(13, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 25, data.detailTris.Length);
        Assert.Equal(1.130483f, data.verts[1], 0.0001f);
        Assert.Equal(5.610685f, data.verts[6], 0.0001f);
        Assert.Equal(6.510685f, data.verts[9], 0.0001f);
        // Tile29: Tris: 5, Verts: 15 Detail Meshed: 5 Detail Verts: 0 Detail Tris: 11
        // Verts: 10.330483, 10.110685, 10.110685
        tile = tc.GetNavMesh().GetTile(29);
        data = tile.data;
        header = data.header;
        Assert.Equal(15, header.vertCount);
        Assert.Equal(5, header.polyCount);
        Assert.Equal(5, header.detailMeshCount);
        Assert.Equal(0, header.detailVertCount);
        Assert.Equal(11, header.detailTriCount);
        Assert.Equal(5, data.polys.Length);
        Assert.Equal(3 * 15, data.verts.Length);
        Assert.Equal(5, data.detailMeshes.Length);
        Assert.Equal(0, data.detailVerts.Length);
        Assert.Equal(4 * 11, data.detailTris.Length);
        Assert.Equal(10.330483f, data.verts[1], 0.0001f);
        Assert.Equal(10.110685f, data.verts[6], 0.0001f);
        Assert.Equal(10.110685f, data.verts[9], 0.0001f);
    }

    [Fact]
    public void TestDungeon()
    {
        using var ms = new MemoryStream(RcIO.ReadFileIfFound("dungeon_all_tiles_tilecache.bin"));
        using var br = new BinaryReader(ms);
        DtTileCache tc = reader.Read(br, 6, null);
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
        // Verts: 14.997294, 15.484785, 15.484785
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
        // Verts: 13.597294, 17.584785, 17.584785
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
        Assert.Equal(13.597294f, data.verts[1], 0.0001f);
        Assert.Equal(17.584785f, data.verts[6], 0.0001f);
        Assert.Equal(17.584785f, data.verts[9], 0.0001f);
        // Tile16: Tris: 10, Verts: 20 Detail Meshed: 10 Detail Verts: 0 Detail Tris: 18
        // Verts: 6.197294, -22.315216, -22.315216
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
        Assert.Equal(6.197294f, data.verts[1], 0.0001f);
        Assert.Equal(-22.315216f, data.verts[6], 0.0001f);
        Assert.Equal(-22.315216f, data.verts[9], 0.0001f);
        // Tile29: Tris: 1, Verts: 5 Detail Meshed: 1 Detail Verts: 0 Detail Tris: 3
        // Verts: 10.197294, 48.484783, 48.484783
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
        Assert.Equal(10.197294f, data.verts[1], 0.0001f);
        Assert.Equal(48.484783f, data.verts[6], 0.0001f);
        Assert.Equal(48.484783f, data.verts[9], 0.0001f);
    }
}