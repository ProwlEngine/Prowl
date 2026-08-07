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

using System.Collections.Generic;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;


public class TileCacheNavigationTest : AbstractTileCacheTest
{
    protected readonly long[] startRefs = { 281475006070787L };
    protected readonly long[] endRefs = { 281474986147841L };
    protected readonly RcVec3f[] startPoss = { new RcVec3f(39.447338f, 9.998177f, -0.784811f) };
    protected readonly RcVec3f[] endPoss = { new RcVec3f(19.292645f, 11.611748f, -57.750366f) };
    private readonly DtStatus[] statuses = { DtStatus.DT_SUCCESS };

    private readonly long[][] results =
    {
        new[]
        {
            281475006070787L, 281475006070785L, 281475005022208L, 281475005022209L, 281475003973633L,
            281475003973634L, 281475003973632L, 281474996633604L, 281474996633605L, 281474996633603L, 281474995585027L,
            281474995585029L, 281474995585026L, 281474995585028L, 281474995585024L, 281474991390721L, 281474991390722L,
            281474991390725L, 281474991390720L, 281474987196418L, 281474987196417L, 281474988244995L, 281474988245001L,
            281474988244997L, 281474988244998L, 281474988245002L, 281474988245000L, 281474988244999L, 281474988244994L,
            281474985099264L, 281474985099266L, 281474986147841L
        }
    };

    protected DtNavMesh navmesh;
    protected DtNavMeshQuery query;

    public TileCacheNavigationTest()
    {
        bool cCompatibility = true;
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        TestTileLayerBuilder layerBuilder = new TestTileLayerBuilder(geom);
        List<byte[]> layers = layerBuilder.Build(RcByteOrder.LITTLE_ENDIAN, cCompatibility, 1);
        DtTileCache tc = GetTileCache(geom, RcByteOrder.LITTLE_ENDIAN, cCompatibility);
        foreach (byte[] data in layers)
        {
            tc.AddTile(data, 0);
        }

        for (int y = 0; y < layerBuilder.th; ++y)
        {
            for (int x = 0; x < layerBuilder.tw; ++x)
            {
                foreach (long refs in tc.GetTilesAt(x, y))
                {
                    tc.BuildNavMeshTile(refs);
                }
            }
        }

        navmesh = tc.GetNavMesh();
        query = new DtNavMeshQuery(navmesh);
    }

    [Fact]
    public void TestFindPathWithDefaultHeuristic()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < startRefs.Length; i++)
        {
            long startRef = startRefs[i];
            long endRef = endRefs[i];
            RcVec3f startPos = startPoss[i];
            RcVec3f endPos = endPoss[i];

            RcFixedArray256<long> path = new RcFixedArray256<long>();
            var status = query.FindPath(startRef, endRef, startPos, endPos, filter, path.AsSpan(), out var npath, path.Length);
            Assert.Equal(statuses[i], status);
            Assert.Equal(results[i].Length, npath);
            for (int j = 0; j < results[i].Length; j++)
            {
                Assert.Equal(results[i][j], path[j]); // TODO: @ikpil, check
            }
        }
    }

    [Fact]
    public void TestFindPathWithNoHeuristic()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        for (int i = 0; i < startRefs.Length; i++)
        {
            long startRef = startRefs[i];
            long endRef = endRefs[i];
            RcVec3f startPos = startPoss[i];
            RcVec3f endPos = endPoss[i];
            RcFixedArray256<long> path = new RcFixedArray256<long>();
            var status = query.FindPath(startRef, endRef, startPos, endPos, filter, path.AsSpan(), out var npath, path.Length);
            Assert.Equal(statuses[i], status);
            Assert.Equal(results[i].Length, npath);
            for (int j = 0; j < results[i].Length; j++)
            {
                Assert.Equal(results[i][j], path[j]); // TODO: @ikpil, check
            }
        }
    }
}