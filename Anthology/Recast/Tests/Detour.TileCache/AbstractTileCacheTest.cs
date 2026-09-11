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

using Prowl.Recast.Core;
using Prowl.Recast.Detour.TileCache.Io.Compress;
using Prowl.Recast;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Detour.TileCache.Tests;

public class AbstractTileCacheTest
{
    private readonly float m_cellSize = 0.3f;
    private readonly float m_cellHeight = 0.2f;
    private readonly float m_agentHeight = 2.0f;
    private readonly float m_agentRadius = 0.6f;
    private readonly float m_agentMaxClimb = 0.9f;
    private readonly float m_edgeMaxError = 1.3f;
    private readonly int m_tileSize = 48;


    public DtTileCache GetTileCache(IRcInputGeomProvider geom, RcByteOrder order, bool cCompatibility)
    {
        RcRecast.CalcTileCount(geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), m_cellSize, m_tileSize, m_tileSize, out var tw, out var th);
        return GetTileCache(geom, order, cCompatibility, tw * th * DtTileCacheLayer.EXPECTED_LAYERS_PER_TILE);
    }

    /// A cache on the bordered path — watershed partitioning and height detail, which is what a
    /// tile's border grid exists for and what Prowl bakes with. The plain cache above leaves both
    /// off, so it never reads its neighbours' layers at all.
    public DtTileCache GetBorderedTileCache(IRcInputGeomProvider geom, RcByteOrder order, bool cCompatibility)
    {
        RcRecast.CalcTileCount(geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), m_cellSize, m_tileSize, m_tileSize, out var tw, out var th);
        DtTileCache tc = GetTileCache(geom, order, cCompatibility, tw * th * DtTileCacheLayer.EXPECTED_LAYERS_PER_TILE,
            watershedPartition: true, detailSampleDist: m_cellSize * 6);
        return tc;
    }

    public DtTileCache GetTileCache(IRcInputGeomProvider geom, RcByteOrder order, bool cCompatibility, int maxTiles)
        => GetTileCache(geom, order, cCompatibility, maxTiles, watershedPartition: false, detailSampleDist: 0);

    public DtTileCache GetTileCache(IRcInputGeomProvider geom, RcByteOrder order, bool cCompatibility, int maxTiles,
        bool watershedPartition, float detailSampleDist)
    {
        DtTileCacheParams option = new DtTileCacheParams();
        option.watershedPartition = watershedPartition;
        option.detailSampleDist = detailSampleDist;
        option.detailSampleMaxError = m_cellHeight;
        option.ch = m_cellHeight;
        option.cs = m_cellSize;
        option.orig = geom.GetMeshBoundsMin();
        option.height = m_tileSize;
        option.width = m_tileSize;
        option.walkableHeight = m_agentHeight;
        option.walkableRadius = m_agentRadius;
        option.walkableClimb = m_agentMaxClimb;
        option.maxSimplificationError = m_edgeMaxError;
        option.maxTiles = maxTiles;
        option.maxObstacles = 128;

        DtNavMeshParams navMeshParams = new DtNavMeshParams();
        navMeshParams.orig = geom.GetMeshBoundsMin();
        navMeshParams.tileWidth = m_tileSize * m_cellSize;
        navMeshParams.tileHeight = m_tileSize * m_cellSize;
        navMeshParams.maxTiles = 256;
        navMeshParams.maxPolys = 16384;

        var navMesh = new DtNavMesh();
        navMesh.Init(navMeshParams, 6);
        var comp = DtTileCacheCompressorFactory.Shared.Create(cCompatibility ? 0 : 1);
        var storageParams = new DtTileCacheStorageParams(order, cCompatibility);
        var process = new TestTileCacheMeshProcess();
        DtTileCache tc = new DtTileCache(option, storageParams, navMesh, comp, process);
        return tc;
    }
}