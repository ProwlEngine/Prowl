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


public class FindPolysAroundShapeTest : AbstractDetourTest
{
    private static readonly long[][] REFS =
    {
        new[]
        {
            281474976710696L, 281474976710695L, 281474976710694L, 281474976710691L, 281474976710697L,
            281474976710693L, 281474976710692L, 281474976710703L, 281474976710706L, 281474976710699L,
            281474976710705L, 281474976710698L, 281474976710700L, 281474976710704L
        },
        new[]
        {
            281474976710773L, 281474976710769L, 281474976710772L, 281474976710768L, 281474976710771L,
            281474976710754L, 281474976710755L, 281474976710753L, 281474976710751L, 281474976710756L,
            281474976710749L
        },
        new[]
        {
            281474976710680L, 281474976710679L, 281474976710684L, 281474976710683L, 281474976710688L,
            281474976710678L, 281474976710676L, 281474976710687L, 281474976710690L, 281474976710686L,
            281474976710689L, 281474976710685L, 281474976710697L, 281474976710695L, 281474976710694L,
            281474976710691L, 281474976710696L, 281474976710693L, 281474976710692L, 281474976710703L,
            281474976710706L, 281474976710699L, 281474976710705L, 281474976710700L, 281474976710704L
        },
        new[] { 281474976710753L, 281474976710748L, 281474976710752L, 281474976710731L },
        new[]
        {
            281474976710733L, 281474976710735L, 281474976710736L, 281474976710742L, 281474976710734L,
            281474976710739L, 281474976710738L, 281474976710740L, 281474976710746L, 281474976710743L,
            281474976710745L, 281474976710741L, 281474976710747L, 281474976710737L, 281474976710732L,
            281474976710728L, 281474976710724L, 281474976710744L, 281474976710725L, 281474976710717L,
            281474976710729L, 281474976710726L, 281474976710721L, 281474976710719L, 281474976710731L,
            281474976710720L, 281474976710752L, 281474976710748L, 281474976710753L, 281474976710755L,
            281474976710756L, 281474976710750L, 281474976710749L, 281474976710754L, 281474976710751L,
            281474976710768L, 281474976710772L, 281474976710773L, 281474976710771L, 281474976710769L
        }
    };

    private static readonly long[][] PARENT_REFS =
    {
        new[]
        {
            0L, 281474976710696L, 281474976710695L, 281474976710695L, 281474976710695L, 281474976710695L,
            281474976710693L, 281474976710694L, 281474976710703L, 281474976710706L, 281474976710706L,
            281474976710705L, 281474976710705L, 281474976710705L
        },
        new[]
        {
            0L, 281474976710773L, 281474976710773L, 281474976710772L, 281474976710772L, 281474976710768L,
            281474976710754L, 281474976710755L, 281474976710755L, 281474976710753L, 281474976710756L
        },
        new[]
        {
            0L, 281474976710680L, 281474976710680L, 281474976710680L, 281474976710684L, 281474976710679L,
            281474976710678L, 281474976710688L, 281474976710687L, 281474976710687L, 281474976710687L,
            281474976710687L, 281474976710686L, 281474976710697L, 281474976710695L, 281474976710695L,
            281474976710695L, 281474976710695L, 281474976710693L, 281474976710694L, 281474976710703L,
            281474976710706L, 281474976710706L, 281474976710705L, 281474976710705L
        },
        new[] { 0L, 281474976710753L, 281474976710748L, 281474976710752L },
        new[]
        {
            0L, 281474976710733L, 281474976710733L, 281474976710735L, 281474976710736L, 281474976710736L,
            281474976710736L, 281474976710742L, 281474976710740L, 281474976710746L, 281474976710746L,
            281474976710746L, 281474976710746L, 281474976710738L, 281474976710738L, 281474976710737L,
            281474976710728L, 281474976710745L, 281474976710724L, 281474976710724L, 281474976710717L,
            281474976710717L, 281474976710717L, 281474976710729L, 281474976710729L, 281474976710721L,
            281474976710731L, 281474976710752L, 281474976710748L, 281474976710753L, 281474976710753L,
            281474976710753L, 281474976710756L, 281474976710755L, 281474976710755L, 281474976710754L,
            281474976710768L, 281474976710772L, 281474976710772L, 281474976710773L
        }
    };

    private static readonly float[][] COSTS =
    {
        new[]
        {
            0.000000f, 16.188787f, 22.561579f, 19.950766f, 19.519329f, 21.906523f, 22.806520f, 23.311579f, 25.124035f,
            28.454576f, 26.084503f, 36.438854f, 30.526634f, 31.942192f
        },
        new[]
        {
            0.000000f, 16.618738f, 12.136283f, 20.387646f, 17.343250f, 22.037645f, 22.787645f, 27.178831f, 26.501472f,
            31.691311f, 33.176235f
        },
        new[]
        {
            0.000000f, 36.657764f, 35.197689f, 37.484924f, 37.755524f, 37.132103f, 37.582104f, 38.816185f, 52.426109f,
            55.945839f, 51.882935f, 44.879601f, 57.745838f, 59.402641f, 65.063034f, 64.934372f, 62.733185f,
            62.756744f, 63.656742f, 65.813034f, 67.625488f, 70.956032f, 68.585960f, 73.028091f, 74.443649f
        },
        new[] { 0.000000f, 2.097958f, 3.158618f, 4.658618f },
        new[]
        {
            0.000000f, 20.495766f, 21.352942f, 21.999096f, 25.531757f, 28.758514f, 30.264732f, 23.499096f, 24.670631f,
            33.166218f, 35.651184f, 34.371792f, 30.012215f, 33.886887f, 33.855347f, 34.643524f, 36.300327f,
            38.203144f, 40.339203f, 40.203213f, 47.254810f, 50.043945f, 49.054485f, 49.804810f, 49.204811f,
            52.813477f, 51.004814f, 52.504814f, 53.565475f, 62.748611f, 61.504147f, 57.915474f, 62.989071f,
            67.139801f, 66.507599f, 67.889801f, 69.539803f, 77.791168f, 75.186256f, 83.111412f
        }
    };

    [Fact]
    public void TestFindPolysAroundShape()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();

        for (int i = 0; i < startRefs.Length; i++)
        {
            long startRef = startRefs[i];
            RcVec3f startPos = startPoss[i];

            RcFixedArray256<long> refs = new RcFixedArray256<long>();
            RcFixedArray256<long> parentRefs = new RcFixedArray256<long>();
            RcFixedArray256<float> costs = new RcFixedArray256<float>();

            var verts = GetQueryPoly(startPos, endPoss[i]);
            query.FindPolysAroundShape(startRef, verts, verts.Length, filter, refs.AsSpan(), parentRefs.AsSpan(), costs.AsSpan(), out var nrefs, refs.Length);

            Assert.Equal(REFS[i].Length, nrefs);
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

                Assert.True(found);
            }
        }
    }

    private RcVec3f[] GetQueryPoly(RcVec3f m_spos, RcVec3f m_epos)
    {
        float nx = (m_epos.Z - m_spos.Z) * 0.25f;
        float nz = -(m_epos.X - m_spos.X) * 0.25f;
        float agentHeight = 2.0f;

        RcVec3f[] m_queryPoly = new RcVec3f[4];
        m_queryPoly[0].X = m_spos.X + nx * 1.2f;
        m_queryPoly[0].Y = m_spos.Y + agentHeight / 2;
        m_queryPoly[0].Z = m_spos.Z + nz * 1.2f;

        m_queryPoly[1].X = m_spos.X - nx * 1.3f;
        m_queryPoly[1].Y = m_spos.Y + agentHeight / 2;
        m_queryPoly[1].Z = m_spos.Z - nz * 1.3f;

        m_queryPoly[2].X = m_epos.X - nx * 0.8f;
        m_queryPoly[2].Y = m_epos.Y + agentHeight / 2;
        m_queryPoly[2].Z = m_epos.Z - nz * 0.8f;

        m_queryPoly[3].X = m_epos.X + nx;
        m_queryPoly[3].Y = m_epos.Y + agentHeight / 2;
        m_queryPoly[3].Z = m_epos.Z + nz;
        return m_queryPoly;
    }
}