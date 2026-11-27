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

using Prowl.Recast.Core;

namespace Prowl.Recast.Tests;

using static RcRecast;

public class RecastTest
{
    [Fact]
    public void TestClearUnwalkableTriangles()
    {
        float walkableSlopeAngle = 45;
        float[] verts = { 0, 0, 0, 1, 0, 0, 0, 0, -1 };
        int nv = 3;
        int[] walkable_tri = { 0, 1, 2 };
        int[] unwalkable_tri = { 0, 2, 1 };
        int nt = 1;

        RcContext ctx = new RcContext();
        {
            int[] areas = { 42 };
            RcRecast.ClearUnwalkableTriangles(ctx, walkableSlopeAngle, verts, nv, unwalkable_tri, nt, areas);
            Assert.Equal(RC_NULL_AREA, areas[0]);
        }
        {
            int[] areas = { 42 };
            RcRecast.ClearUnwalkableTriangles(ctx, walkableSlopeAngle, verts, nv, walkable_tri, nt, areas);
            Assert.Equal(42, areas[0]);
        }
        {
            int[] areas = { 42 };
            walkableSlopeAngle = 0;
            RcRecast.ClearUnwalkableTriangles(ctx, walkableSlopeAngle, verts, nv, walkable_tri, nt, areas);
            Assert.Equal(RC_NULL_AREA, areas[0]);
        }
    }
}