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

using System;
using System.Collections.Generic;
using System.IO;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.TileCache.Io;
using Prowl.Recast.Detour.TileCache.Io.Compress;

namespace Prowl.Recast.Detour.TileCache.Tests;

public class TileCacheFindPathTest : AbstractTileCacheTest
{
    private readonly RcVec3f start = new RcVec3f(39.44734f, 9.998177f, -0.784811f);
    private readonly RcVec3f end = new RcVec3f(19.292645f, 11.611748f, -57.750366f);
    private readonly DtNavMesh navmesh;
    private readonly DtNavMeshQuery query;

    public TileCacheFindPathTest()
    {
        using var msr = new MemoryStream(RcIO.ReadFileIfFound("dungeon_all_tiles_tilecache.bin"));
        using var br = new BinaryReader(msr);
        DtTileCache tcC = new DtTileCacheReader(DtTileCacheCompressorFactory.Shared).Read(br, 6, new TestTileCacheMeshProcess());
        navmesh = tcC.GetNavMesh();
        query = new DtNavMeshQuery(navmesh);
    }

    [Fact]
    public void TestFindPath()
    {
        IDtQueryFilter filter = new DtQueryDefaultFilter();
        RcVec3f extents = new RcVec3f(2f, 4f, 2f);
        query.FindNearestPoly(start, extents, filter, out var startRef, out var startPos, out var _);
        query.FindNearestPoly(end, extents, filter, out var endRef, out var endPos, out var _);

        RcFixedArray256<long> path = new RcFixedArray256<long>();
        var status = query.FindPath(startRef, endRef, startPos, endPos, filter, path.AsSpan(), out var npath, path.Length);
        const int maxStraightPath = 256;
        int options = 0;

        Span<DtStraightPath> pathStr = stackalloc DtStraightPath[maxStraightPath];
        query.FindStraightPath(startPos, endPos, path.AsSpan(), npath, pathStr, out var npathStr, maxStraightPath, options);
        Assert.Equal(8, npathStr);
    }
}