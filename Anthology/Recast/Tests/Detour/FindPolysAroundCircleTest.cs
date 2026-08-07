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


public class FindPolysAroundCircleTest : AbstractDetourTest
{
    private static readonly long[][] REFS =
    {
        new[]
        {
            281474976710696L, 281474976710695L, 281474976710694L, 281474976710691L, 281474976710697L, 281474976710693L,
            281474976710686L, 281474976710687L, 281474976710692L, 281474976710703L, 281474976710689L
        },
        new[] { 281474976710773L, 281474976710770L, 281474976710769L, 281474976710772L, 281474976710771L },
        new[]
        {
            281474976710680L, 281474976710674L, 281474976710679L, 281474976710684L, 281474976710683L, 281474976710678L,
            281474976710682L, 281474976710677L, 281474976710676L, 281474976710688L, 281474976710687L, 281474976710675L,
            281474976710685L, 281474976710672L, 281474976710666L, 281474976710668L, 281474976710681L, 281474976710673L
        },
        new[]
        {
            281474976710753L, 281474976710748L, 281474976710755L, 281474976710756L, 281474976710750L, 281474976710752L,
            281474976710731L, 281474976710729L, 281474976710749L, 281474976710719L, 281474976710717L, 281474976710726L
        },
        new[]
        {
            281474976710733L, 281474976710735L, 281474976710736L, 281474976710734L, 281474976710739L, 281474976710742L,
            281474976710740L, 281474976710746L, 281474976710747L,
        }
    };

    private static readonly long[][] PARENT_REFS =
    {
        new[]
        {
            0L, 281474976710696L, 281474976710695L, 281474976710695L, 281474976710695L, 281474976710695L, 281474976710697L,
            281474976710686L, 281474976710693L, 281474976710694L, 281474976710687L
        },
        new[] { 0L, 281474976710773L, 281474976710773L, 281474976710773L, 281474976710772L },
        new[]
        {
            0L, 281474976710680L, 281474976710680L, 281474976710680L, 281474976710680L, 281474976710679L, 281474976710683L,
            281474976710683L, 281474976710678L, 281474976710684L, 281474976710688L, 281474976710677L, 281474976710687L,
            281474976710682L, 281474976710672L, 281474976710672L, 281474976710675L, 281474976710666L
        },
        new[]
        {
            0L, 281474976710753L, 281474976710753L, 281474976710753L, 281474976710753L, 281474976710748L, 281474976710752L,
            281474976710731L, 281474976710756L, 281474976710729L, 281474976710729L, 281474976710717L
        },
        new[]
        {
            0L, 281474976710733L, 281474976710733L, 281474976710736L, 281474976710736L, 281474976710735L, 281474976710742L,
            281474976710740L, 281474976710746L
        }
    };

    private static readonly float[][] COSTS =
    {
        new[]
        {
            0.000000f, 0.391453f, 6.764245f, 4.153431f, 3.721995f, 6.109188f, 5.378797f, 7.178796f, 7.009186f, 7.514245f,
            12.655564f
        },
        new[] { 0.000000f, 6.161580f, 2.824478f, 2.828730f, 8.035697f },
        new[]
        {
            0.000000f, 1.162604f, 1.954029f, 2.776051f, 2.046001f, 2.428367f, 6.429493f, 6.032851f, 2.878368f, 5.333885f,
            6.394545f, 9.596563f, 12.457960f, 7.096575f, 10.413582f, 10.362305f, 10.665442f, 10.593861f
        },
        new[]
        {
            0.000000f, 2.483205f, 6.723722f, 5.727250f, 3.126022f, 3.543865f, 5.043865f, 6.843868f, 7.212173f, 10.602858f,
            8.793867f, 13.146453f
        },
        new[] { 0.000000f, 2.480514f, 0.823685f, 5.002500f, 8.229258f, 3.983844f, 5.483844f, 6.655379f, 11.996962f }
    };

    [Fact]
    public void TestFindPolysAroundCircle()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < startRefs.Length; i++)
        {
            long startRef = startRefs[i];
            RcVec3f startPos = startPoss[i];

            RcFixedArray256<long> refs = new RcFixedArray256<long>();
            RcFixedArray256<long> parentRefs = new RcFixedArray256<long>();
            RcFixedArray256<float> costs = new RcFixedArray256<float>();

            var status = query.FindPolysAroundCircle(startRef, startPos, 7.5f, filter, refs.AsSpan(), parentRefs.AsSpan(), costs.AsSpan(), out var resultCount, refs.Length);
            Assert.True(status.Succeeded());

            Assert.Equal(REFS[i].Length, resultCount);
            for (int v = 0; v < REFS[i].Length; v++)
            {
                bool found = false;
                for (int w = 0; w < REFS[i].Length; w++)
                {
                    if (REFS[i][v] == refs[w])
                    {
                        Assert.Equal(PARENT_REFS[i][v], parentRefs[w]);
                        Assert.Equal(COSTS[i][v], costs[w], 0.01f);
                        found = true;
                    }
                }

                Assert.True(found, $"Ref not found {REFS[i][v]}");
            }
        }
    }
}