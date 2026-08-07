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
using System.Linq;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;

public class TestTileLayerBuilder : DtTileCacheLayerBuilder
{
    private const float CellSize = 0.3f;
    private const float CellHeight = 0.2f;

    private const float AgentHeight = 2.0f;
    private const float AgentRadius = 0.6f;
    private const float AgentMaxClimb = 0.9f;
    private const float AgentMaxSlope = 45.0f;

    private const int RegionMinSize = 8;
    private const int RegionMergeSize = 20;
    private const float RegionMinArea = RegionMinSize * RegionMinSize * CellSize * CellSize;
    private const float RegionMergeArea = RegionMergeSize * RegionMergeSize * CellSize * CellSize;

    private const float EdgeMaxLen = 12.0f;
    private const float EdgeMaxError = 1.3f;
    private const int VertsPerPoly = 6;
    private const float DetailSampleDist = 6.0f;
    private const float DetailSampleMaxError = 1.0f;

    private readonly RcConfig _cfg;
    private const int m_tileSize = 48;

    private readonly IRcInputGeomProvider _geom;
    public readonly int tw;
    public readonly int th;

    public TestTileLayerBuilder(IRcInputGeomProvider geom) : base(DtTileCacheCompressorFactory.Shared)
    {
        _geom = geom;
        _cfg = new RcConfig(true, m_tileSize, m_tileSize,
            RcConfig.CalcBorder(AgentRadius, CellSize),
            RcPartition.WATERSHED,
            CellSize, CellHeight,
            AgentMaxSlope, AgentHeight, AgentRadius, AgentMaxClimb,
            RegionMinArea, RegionMergeArea,
            EdgeMaxLen, EdgeMaxError,
            VertsPerPoly,
            DetailSampleDist, DetailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);

        RcVec3f bmin = geom.GetMeshBoundsMin();
        RcVec3f bmax = geom.GetMeshBoundsMax();
        RcRecast.CalcTileCount(bmin, bmax, CellSize, m_tileSize, m_tileSize, out tw, out th);
    }

    public List<byte[]> Build(RcByteOrder order, bool cCompatibility, int threads)
    {
        var storageParams = new DtTileCacheStorageParams(order, cCompatibility);
        var results = Build(_geom, _cfg, storageParams, threads, tw, th);
        return results
            .SelectMany(x => x.layers)
            .ToList();
    }
}