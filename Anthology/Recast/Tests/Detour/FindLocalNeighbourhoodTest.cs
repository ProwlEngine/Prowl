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

using System.Collections.Generic;
using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Numerics;

namespace Prowl.Recast.Detour.Tests;


public class FindLocalNeighbourhoodTest : AbstractDetourTest
{
    private static readonly long[][] REFS =
    {
        new[] { 281474976710696L, 281474976710695L, 281474976710691L, 281474976710697L },
        new[] { 281474976710773L, 281474976710769L, 281474976710772L },
        new[]
        {
            281474976710680L, 281474976710674L, 281474976710679L, 281474976710684L, 281474976710683L,
            281474976710678L, 281474976710677L, 281474976710676L
        },
        new[] { 281474976710753L, 281474976710748L, 281474976710750L, 281474976710752L },
        new[] { 281474976710733L, 281474976710735L, 281474976710736L }
    };

    private static readonly long[][] PARENT_REFS =
    {
        new[] { 0L, 281474976710696L, 281474976710695L, 281474976710695L },
        new[] { 0L, 281474976710773L, 281474976710773L },
        new[]
        {
            0L, 281474976710680L, 281474976710680L, 281474976710680L, 281474976710680L, 281474976710679L,
            281474976710683L, 281474976710678L
        },
        new[] { 0L, 281474976710753L, 281474976710753L, 281474976710748L },
        new[] { 0L, 281474976710733L, 281474976710733L }
    };

    [Fact]
    public void TestFindNearestPoly()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < startRefs.Length; i++)
        {
            RcVec3f startPos = startPoss[i];
            RcFixedArray256<long> refs = new RcFixedArray256<long>();
            RcFixedArray256<long> parentRefs = new RcFixedArray256<long>();
            var status = query.FindLocalNeighbourhood(startRefs[i], startPos, 3.5f, filter, refs.AsSpan(), parentRefs.AsSpan(), out var nrefs, refs.Length);
            Assert.Equal(REFS[i].Length, nrefs);
            for (int v = 0; v < REFS[i].Length; v++)
            {
                Assert.Equal(REFS[i][v], refs[v]);
            }
        }
    }
}