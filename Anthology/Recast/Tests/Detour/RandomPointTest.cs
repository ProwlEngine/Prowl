/*
recast4j Copyright (c) 2015-2021 Piotr Piastucki piotr@jtilia.org
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
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;


namespace Prowl.Recast.Detour.Tests;

public class RandomPointTest : AbstractDetourTest
{
    [Fact]
    public void TestRandom()
    {
        for (int __r = 0; __r < 10; __r++)
        {
            RcRand f = new RcRand(1);
            IDtQueryFilter filter = new DtQueryDefaultFilter();

            var begin = RcFrequency.Ticks;
            for (int i = 0; i < 10000; i++)
            {
                var status = query.FindRandomPoint(filter, f, out var randomRef, out var randomPt);
                Assert.True(status.Succeeded());

                status = navmesh.GetTileAndPolyByRef(randomRef, out var tile, out var poly);
                float[] bmin = new float[2];
                float[] bmax = new float[2];
                for (int j = 0; j < poly.vertCount; j++)
                {
                    int v = poly.verts[j] * 3;
                    bmin[0] = j == 0 ? tile.data.verts[v] : Math.Min(bmin[0], tile.data.verts[v]);
                    bmax[0] = j == 0 ? tile.data.verts[v] : Math.Max(bmax[0], tile.data.verts[v]);
                    bmin[1] = j == 0 ? tile.data.verts[v + 2] : Math.Min(bmin[1], tile.data.verts[v + 2]);
                    bmax[1] = j == 0 ? tile.data.verts[v + 2] : Math.Max(bmax[1], tile.data.verts[v + 2]);
                }

                Assert.True(randomPt.X >= bmin[0]);
                Assert.True(randomPt.X <= bmax[0]);
                Assert.True(randomPt.Z >= bmin[1]);
                Assert.True(randomPt.Z <= bmax[1]);
            }

            var ticks = RcFrequency.Ticks - begin;
            Console.WriteLine($"RandomPointTest::TestRandom() - {(double)ticks / TimeSpan.TicksPerMillisecond} ms");
        }
    }

    [Fact]
    public void TestRandomAroundCircle()
    {
        RcRand f = new RcRand(1);
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        query.FindRandomPoint(filter, f, out var randomRef, out var randomPt);
        for (int i = 0; i < 1000; i++)
        {
            var status = query.FindRandomPointAroundCircle(randomRef, randomPt, 5f, filter, f, out var nextRandomRef, out var nextRandomPt);
            Assert.False(status.Failed());

            randomRef = nextRandomRef;
            randomPt = nextRandomPt;

            status = navmesh.GetTileAndPolyByRef(randomRef, out var tile, out var poly);

            float[] bmin = new float[2];
            float[] bmax = new float[2];
            for (int j = 0; j < poly.vertCount; j++)
            {
                int v = poly.verts[j] * 3;
                bmin[0] = j == 0 ? tile.data.verts[v] : Math.Min(bmin[0], tile.data.verts[v]);
                bmax[0] = j == 0 ? tile.data.verts[v] : Math.Max(bmax[0], tile.data.verts[v]);
                bmin[1] = j == 0 ? tile.data.verts[v + 2] : Math.Min(bmin[1], tile.data.verts[v + 2]);
                bmax[1] = j == 0 ? tile.data.verts[v + 2] : Math.Max(bmax[1], tile.data.verts[v + 2]);
            }

            Assert.True(randomPt.X >= bmin[0]);
            Assert.True(randomPt.X <= bmax[0]);
            Assert.True(randomPt.Z >= bmin[1]);
            Assert.True(randomPt.Z <= bmax[1]);
        }
    }

    [Fact]
    public void TestRandomWithinCircle()
    {
        RcRand f = new RcRand(1);
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        query.FindRandomPoint(filter, f, out var randomRef, out var randomPt);
        float radius = 5f;
        for (int i = 0; i < 1000; i++)
        {
            var status = query.FindRandomPointWithinCircle(randomRef, randomPt, radius, filter, f, out var nextRandomRef, out var nextRandomPt);
            Assert.False(status.Failed());

            float distance = RcVec.Dist2D(randomPt, nextRandomPt);
            Assert.True(distance <= radius);

            randomRef = nextRandomRef;
            randomPt = nextRandomPt;
        }
    }

    [Fact]
    public void TestPerformance()
    {
        RcRand f = new RcRand(1);
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        query.FindRandomPoint(filter, f, out var randomRef, out var randomPt);

        float radius = 5f;
        // jvm warmup
        for (int i = 0; i < 1000; i++)
        {
            query.FindRandomPointAroundCircle(randomRef, randomPt, radius, filter, f, out var _, out var _);
        }

        for (int i = 0; i < 1000; i++)
        {
            query.FindRandomPointWithinCircle(randomRef, randomPt, radius, filter, f, out var _, out var _);
        }

        long t1 = RcFrequency.Ticks;
        for (int i = 0; i < 10000; i++)
        {
            query.FindRandomPointAroundCircle(randomRef, randomPt, radius, filter, f, out var _, out var _);
        }

        long t2 = RcFrequency.Ticks;
        for (int i = 0; i < 10000; i++)
        {
            query.FindRandomPointWithinCircle(randomRef, randomPt, radius, filter, f, out var _, out var _);
        }

        long t3 = RcFrequency.Ticks;
        Console.WriteLine("Random point around circle: " + (t2 - t1) / TimeSpan.TicksPerMillisecond + "ms");
        Console.WriteLine("Random point within circle: " + (t3 - t2) / TimeSpan.TicksPerMillisecond + "ms");
    }
}