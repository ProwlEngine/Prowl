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
using Prowl.Recast;
using Prowl.Recast.Geom;


namespace Prowl.Recast.Detour.Tests;

public class TestTiledNavMeshBuilder
{
    private readonly DtNavMesh navMesh;
    private const float m_cellSize = 0.3f;
    private const float m_cellHeight = 0.2f;
    private const float m_agentHeight = 2.0f;
    private const float m_agentRadius = 0.6f;
    private const float m_agentMaxClimb = 0.9f;
    private const float m_agentMaxSlope = 45.0f;
    private const int m_regionMinSize = 8;
    private const int m_regionMergeSize = 20;
    private const float m_regionMinArea = m_regionMinSize * m_regionMinSize * m_cellSize * m_cellSize;
    private const float m_regionMergeArea = m_regionMergeSize * m_regionMergeSize * m_cellSize * m_cellSize;
    private const float m_edgeMaxLen = 12.0f;
    private const float m_edgeMaxError = 1.3f;
    private const int m_vertsPerPoly = 6;
    private const float m_detailSampleDist = 6.0f;
    private const float m_detailSampleMaxError = 1.0f;
    private const int m_tileSize = 32;

    public TestTiledNavMeshBuilder() :
        this(RcSampleInputGeomProvider.LoadFile("dungeon.obj"),
            RcPartition.WATERSHED, m_cellSize, m_cellHeight, m_agentHeight, m_agentRadius, m_agentMaxClimb, m_agentMaxSlope,
            m_regionMinSize, m_regionMergeSize, m_edgeMaxLen, m_edgeMaxError, m_vertsPerPoly, m_detailSampleDist,
            m_detailSampleMaxError, m_tileSize)
    {
    }

    public TestTiledNavMeshBuilder(IRcInputGeomProvider geom, RcPartition partitionType, float cellSize, float cellHeight,
        float agentHeight, float agentRadius, float agentMaxClimb, float agentMaxSlope, int regionMinSize,
        int regionMergeSize, float edgeMaxLen, float edgeMaxError, int vertsPerPoly, float detailSampleDist,
        float detailSampleMaxError, int tileSize)
    {
        // Create empty nav mesh
        DtNavMeshParams navMeshParams = new DtNavMeshParams();
        navMeshParams.orig = geom.GetMeshBoundsMin();
        navMeshParams.tileWidth = tileSize * cellSize;
        navMeshParams.tileHeight = tileSize * cellSize;
        navMeshParams.maxTiles = 128;
        navMeshParams.maxPolys = 32768;
        navMesh = new DtNavMesh();
        navMesh.Init(navMeshParams, 6);

        // Build all tiles
        RcConfig cfg = new RcConfig(true, tileSize, tileSize, RcConfig.CalcBorder(agentRadius, cellSize),
            partitionType,
            cellSize, cellHeight,
            agentMaxSlope, agentHeight, agentRadius, agentMaxClimb,
            m_regionMinArea, m_regionMergeArea,
            edgeMaxLen, edgeMaxError,
            vertsPerPoly,
            detailSampleDist, detailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);
        RcBuilder rcBuilder = new RcBuilder();
        List<RcBuilderResult> rcResult = rcBuilder.BuildTiles(geom, cfg, false, true);

        // Add tiles to nav mesh

        foreach (RcBuilderResult result in rcResult)
        {
            RcPolyMesh pmesh = result.Mesh;
            if (pmesh.npolys == 0)
            {
                continue;
            }

            for (int i = 0; i < pmesh.npolys; ++i)
            {
                pmesh.flags[i] = 1;
            }

            DtNavMeshCreateParams option = new DtNavMeshCreateParams();
            option.verts = pmesh.verts;
            option.vertCount = pmesh.nverts;
            option.polys = pmesh.polys;
            option.polyAreas = pmesh.areas;
            option.polyFlags = pmesh.flags;
            option.polyCount = pmesh.npolys;
            option.nvp = pmesh.nvp;
            RcPolyMeshDetail dmesh = result.MeshDetail;
            option.detailMeshes = dmesh.meshes;
            option.detailVerts = dmesh.verts;
            option.detailVertsCount = dmesh.nverts;
            option.detailTris = dmesh.tris;
            option.detailTriCount = dmesh.ntris;
            option.walkableHeight = agentHeight;
            option.walkableRadius = agentRadius;
            option.walkableClimb = agentMaxClimb;
            option.bmin = pmesh.bmin;
            option.bmax = pmesh.bmax;
            option.cs = cellSize;
            option.ch = cellHeight;
            option.tileX = result.TileX;
            option.tileZ = result.TileZ;
            option.buildBvTree = true;
            navMesh.AddTile(Detour.DtNavMeshBuilder.CreateNavMeshData(option), 0, 0, out _);
        }
    }

    public DtNavMesh GetNavMesh()
    {
        return navMesh;
    }
}