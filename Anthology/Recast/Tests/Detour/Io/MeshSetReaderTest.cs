/*
recast4j Copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
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

using System.IO;
using Prowl.Recast.Core;
using Prowl.Recast.Detour.Io;

namespace Prowl.Recast.Detour.Tests.Io;

public class MeshSetReaderTest
{
    private readonly DtMeshSetReader reader = new DtMeshSetReader();

    [Fact]
    public void TestNavmesh()
    {
        byte[] @is = RcIO.ReadFileIfFound("all_tiles_navmesh.bin");
        using var ms = new MemoryStream(@is);
        using var br = new BinaryReader(ms);
        DtNavMesh mesh = reader.Read(br, 6);
        Assert.Equal(128, mesh.GetMaxTiles());
        Assert.Equal(0x8000, mesh.GetParams().maxPolys);
        Assert.Equal(9.6f, mesh.GetParams().tileWidth, 0.001f);

        const int MAX_NEIS = 32;
        DtMeshTile[] tiles = new DtMeshTile[MAX_NEIS];
        int nneis = 0;

        nneis = mesh.GetTilesAt(4, 7, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(7, tiles[0].data.polys.Length);
        Assert.Equal(22 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(1, 6, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(7, tiles[0].data.polys.Length);
        Assert.Equal(26 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(6, 2, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(1, tiles[0].data.polys.Length);
        Assert.Equal(4 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(7, 6, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(8, tiles[0].data.polys.Length);
        Assert.Equal(24 * 3, tiles[0].data.verts.Length);
    }

    [Fact]
    public void TestDungeon()
    {
        byte[] @is = RcIO.ReadFileIfFound("dungeon_all_tiles_navmesh.bin");
        using var ms = new MemoryStream(@is);
        using var br = new BinaryReader(ms);

        DtNavMesh mesh = reader.Read(br, 6);
        Assert.Equal(128, mesh.GetMaxTiles());
        Assert.Equal(0x8000, mesh.GetParams().maxPolys);
        Assert.Equal(9.6f, mesh.GetParams().tileWidth, 0.001f);

        const int MAX_NEIS = 32;
        DtMeshTile[] tiles = new DtMeshTile[MAX_NEIS];
        int nneis = 0;

        nneis = mesh.GetTilesAt(6, 9, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(2, tiles[0].data.polys.Length);
        Assert.Equal(7 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(2, 9, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(2, tiles[0].data.polys.Length);
        Assert.Equal(9 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(4, 3, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(3, tiles[0].data.polys.Length);
        Assert.Equal(6 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(2, 8, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(5, tiles[0].data.polys.Length);
        Assert.Equal(17 * 3, tiles[0].data.verts.Length);
    }

    [Fact]
    public void TestDungeon32Bit()
    {
        byte[] @is = RcIO.ReadFileIfFound("dungeon_all_tiles_navmesh_32bit.bin");
        using var ms = new MemoryStream(@is);
        using var br = new BinaryReader(ms);

        DtNavMesh mesh = reader.Read32Bit(br, 6);
        Assert.Equal(128, mesh.GetMaxTiles());
        Assert.Equal(0x8000, mesh.GetParams().maxPolys);
        Assert.Equal(9.6f, mesh.GetParams().tileWidth, 0.001f);

        const int MAX_NEIS = 32;
        DtMeshTile[] tiles = new DtMeshTile[MAX_NEIS];
        int nneis = 0;

        nneis = mesh.GetTilesAt(6, 9, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(2, tiles[0].data.polys.Length);
        Assert.Equal(7 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(2, 9, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(2, tiles[0].data.polys.Length);
        Assert.Equal(9 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(4, 3, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(3, tiles[0].data.polys.Length);
        Assert.Equal(6 * 3, tiles[0].data.verts.Length);

        nneis = mesh.GetTilesAt(2, 8, tiles, MAX_NEIS);
        Assert.Equal(1, nneis);
        Assert.Equal(5, tiles[0].data.polys.Length);
        Assert.Equal(17 * 3, tiles[0].data.verts.Length);
    }
}