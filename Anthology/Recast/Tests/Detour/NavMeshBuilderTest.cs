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

using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Tests;

using static DtDetour;

public class NavMeshBuilderTest
{
    private DtMeshData nmd;

    public NavMeshBuilderTest()
    {
        nmd = TestMeshDataFactory.Create();
    }

    [Fact]
    public void TestBVTree()
    {
        Assert.Equal(225, nmd.verts.Length / 3);
        Assert.Equal(119, nmd.polys.Length);
        Assert.Equal(457, nmd.header.maxLinkCount);
        Assert.Equal(118, nmd.detailMeshes.Length);
        Assert.Equal(291, nmd.detailTris.Length / 4);
        Assert.Equal(60, nmd.detailVerts.Length / 3);
        Assert.Equal(1, nmd.offMeshCons.Length);
        Assert.Equal(118, nmd.header.offMeshBase);
        Assert.Equal(236, nmd.bvTree.Length);
        Assert.True(nmd.bvTree.Length >= nmd.header.bvNodeCount);
        for (int i = 0; i < nmd.header.bvNodeCount; i++)
        {
            Assert.NotNull(nmd.bvTree[i]);
        }

        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(nmd.offMeshCons[0].pos[i], nmd.verts.ToVec3(223 * 3 + (i * 3)));
        }

        Assert.Equal(0.1f, nmd.offMeshCons[0].rad);
        Assert.Equal(118, nmd.offMeshCons[0].poly);
        Assert.Equal(DT_OFFMESH_CON_BIDIR, nmd.offMeshCons[0].flags);
        Assert.Equal(0xFF, nmd.offMeshCons[0].side);
        Assert.Equal(0x4567, nmd.offMeshCons[0].userId);
        Assert.Equal(2, nmd.polys[118].vertCount);
        Assert.Equal(223, nmd.polys[118].verts[0]);
        Assert.Equal(224, nmd.polys[118].verts[1]);
        Assert.Equal(12, nmd.polys[118].flags);
        Assert.Equal(2, nmd.polys[118].GetArea());
        Assert.Equal(DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION, nmd.polys[118].GetPolyType());
    }
}