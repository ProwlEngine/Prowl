/*
recast4j copyright (c) 2021 Piotr Piastucki piotr@jtilia.org
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
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.Dynamic.Io;

namespace Prowl.Recast.Detour.Dynamic.Tests.Io;


public class VoxelFileReaderWriterTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldReadSingleTileFile(bool compression)
    {
        byte[] bytes = RcIO.ReadFileIfFound("test.voxels");
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        DtVoxelFile f = ReadWriteRead(br, compression);
        Assert.False(f.useTiles);
        Assert.Equal(new[] { -100.0f, 0f, -100f, 100f, 5f, 100f }, f.bounds);
        Assert.Equal(0.25f, f.cellSize);
        Assert.Equal(0.5f, f.walkableRadius);
        Assert.Equal(2f, f.walkableHeight);
        Assert.Equal(0.5f, f.walkableClimb);
        Assert.Equal(20f, f.maxEdgeLen);
        Assert.Equal(2f, f.maxSimplificationError);
        Assert.Equal(2f, f.minRegionArea);
        Assert.Equal(12f, f.regionMergeArea);
        Assert.Equal(1, f.tiles.Count);
        Assert.Equal(0.001f, f.tiles[0].cellHeight);
        Assert.Equal(810, f.tiles[0].width);
        Assert.Equal(810, f.tiles[0].depth);
        Assert.Equal(9021024, f.tiles[0].spanData.Length);
        Assert.Equal(new RcVec3f(-101.25f, 0f, -101.25f), f.tiles[0].boundsMin);
        Assert.Equal(new RcVec3f(101.25f, 5.0f, 101.25f), f.tiles[0].boundsMax);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldReadMultiTileFile(bool compression)
    {
        byte[] bytes = RcIO.ReadFileIfFound("test_tiles.voxels");
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        DtVoxelFile f = ReadWriteRead(br, compression);

        Assert.True(f.useTiles);
        Assert.Equal(new[] { -100.0f, 0f, -100f, 100f, 5f, 100f }, f.bounds);
        Assert.Equal(0.25f, f.cellSize);
        Assert.Equal(0.5f, f.walkableRadius);
        Assert.Equal(2f, f.walkableHeight);
        Assert.Equal(0.5f, f.walkableClimb);
        Assert.Equal(20f, f.maxEdgeLen);
        Assert.Equal(2f, f.maxSimplificationError);
        Assert.Equal(2f, f.minRegionArea);
        Assert.Equal(12f, f.regionMergeArea);
        Assert.Equal(100, f.tiles.Count);
        Assert.Equal(0.001f, f.tiles[0].cellHeight);
        Assert.Equal(90, f.tiles[0].width);
        Assert.Equal(90, f.tiles[0].depth);
        Assert.Equal(104952, f.tiles[0].spanData.Length);
        Assert.Equal(109080, f.tiles[5].spanData.Length);
        Assert.Equal(113400, f.tiles[18].spanData.Length);
        Assert.Equal(new RcVec3f(-101.25f, 0f, -101.25f), f.tiles[0].boundsMin);
        Assert.Equal(new RcVec3f(-78.75f, 5.0f, -78.75f), f.tiles[0].boundsMax);
    }

    private DtVoxelFile ReadWriteRead(BinaryReader bis, bool compression)
    {
        DtVoxelFileReader reader = new DtVoxelFileReader(DtVoxelTileLZ4ForTestCompressor.Shared);
        DtVoxelFile f = reader.Read(bis);

        using var msw = new MemoryStream();
        using var bw = new BinaryWriter(msw);
        DtVoxelFileWriter writer = new DtVoxelFileWriter(DtVoxelTileLZ4ForTestCompressor.Shared);
        writer.Write(bw, f, compression);

        using var msr = new MemoryStream(msw.ToArray());
        using var br = new BinaryReader(msr);
        return reader.Read(br);
    }
}