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


public class MeshDataReaderWriterTest
{
    private const int VERTS_PER_POLYGON = 6;
    private DtMeshData meshData;

    public MeshDataReaderWriterTest()
    {
        meshData = TestMeshDataFactory.Create();
    }

    [Fact]
    public void TestCCompatibility()
    {
        Test(true, RcByteOrder.BIG_ENDIAN);
    }

    [Fact]
    public void TestCompact()
    {
        Test(false, RcByteOrder.BIG_ENDIAN);
    }

    [Fact]
    public void TestCCompatibilityLE()
    {
        Test(true, RcByteOrder.LITTLE_ENDIAN);
    }

    [Fact]
    public void TestCompactLE()
    {
        Test(false, RcByteOrder.LITTLE_ENDIAN);
    }

    public void Test(bool cCompatibility, RcByteOrder order)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        DtMeshDataWriter writer = new DtMeshDataWriter();
        writer.Write(bw, meshData, order, cCompatibility);
        ms.Seek(0, SeekOrigin.Begin);

        using var br = new BinaryReader(ms);
        DtMeshDataReader reader = new DtMeshDataReader();
        DtMeshData readData = reader.Read(br, VERTS_PER_POLYGON);

        Assert.Equal(meshData.header.vertCount, readData.header.vertCount);
        Assert.Equal(meshData.header.polyCount, readData.header.polyCount);
        Assert.Equal(meshData.header.detailMeshCount, readData.header.detailMeshCount);
        Assert.Equal(meshData.header.detailTriCount, readData.header.detailTriCount);
        Assert.Equal(meshData.header.detailVertCount, readData.header.detailVertCount);
        Assert.Equal(meshData.header.bvNodeCount, readData.header.bvNodeCount);
        Assert.Equal(meshData.header.offMeshConCount, readData.header.offMeshConCount);
        for (int i = 0; i < meshData.header.vertCount; i++)
        {
            Assert.Equal(meshData.verts[i], readData.verts[i]);
        }

        for (int i = 0; i < meshData.header.polyCount; i++)
        {
            Assert.Equal(meshData.polys[i].vertCount, readData.polys[i].vertCount);
            Assert.Equal(meshData.polys[i].areaAndtype, readData.polys[i].areaAndtype);
            for (int j = 0; j < meshData.polys[i].vertCount; j++)
            {
                Assert.Equal(meshData.polys[i].verts[j], readData.polys[i].verts[j]);
                Assert.Equal(meshData.polys[i].neis[j], readData.polys[i].neis[j]);
            }
        }

        for (int i = 0; i < meshData.header.detailMeshCount; i++)
        {
            Assert.Equal(meshData.detailMeshes[i].vertBase, readData.detailMeshes[i].vertBase);
            Assert.Equal(meshData.detailMeshes[i].vertCount, readData.detailMeshes[i].vertCount);
            Assert.Equal(meshData.detailMeshes[i].triBase, readData.detailMeshes[i].triBase);
            Assert.Equal(meshData.detailMeshes[i].triCount, readData.detailMeshes[i].triCount);
        }

        for (int i = 0; i < meshData.header.detailVertCount; i++)
        {
            Assert.Equal(meshData.detailVerts[i], readData.detailVerts[i]);
        }

        for (int i = 0; i < meshData.header.detailTriCount; i++)
        {
            Assert.Equal(meshData.detailTris[i], readData.detailTris[i]);
        }

        for (int i = 0; i < meshData.header.bvNodeCount; i++)
        {
            Assert.Equal(meshData.bvTree[i].i, readData.bvTree[i].i);
            Assert.Equal(meshData.bvTree[i].bmin, readData.bvTree[i].bmin);
            Assert.Equal(meshData.bvTree[i].bmax, readData.bvTree[i].bmax);
        }

        for (int i = 0; i < meshData.header.offMeshConCount; i++)
        {
            Assert.Equal(meshData.offMeshCons[i].flags, readData.offMeshCons[i].flags);
            Assert.Equal(meshData.offMeshCons[i].rad, readData.offMeshCons[i].rad);
            Assert.Equal(meshData.offMeshCons[i].poly, readData.offMeshCons[i].poly);
            Assert.Equal(meshData.offMeshCons[i].side, readData.offMeshCons[i].side);
            Assert.Equal(meshData.offMeshCons[i].userId, readData.offMeshCons[i].userId);
            for (int j = 0; j < 2; j++)
            {
                Assert.Equal(meshData.offMeshCons[i].pos[j], readData.offMeshCons[i].pos[j]);
            }
        }
    }
}