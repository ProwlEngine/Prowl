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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prowl.Recast.Core;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Tests;

public class RecastTileMeshTest
{
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
    private RcPartition m_partitionType = RcPartition.WATERSHED;
    private const int m_tileSize = 32;

    [Fact]
    public void TestDungeon()
    {
        TestBuild("dungeon.obj");
    }

    public void TestBuild(string filename)
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile(filename);
        RcBuilder builder = new RcBuilder();
        RcConfig cfg = new RcConfig(
            true, m_tileSize, m_tileSize, RcConfig.CalcBorder(m_agentRadius, m_cellSize),
            m_partitionType,
            m_cellSize, m_cellHeight,
            m_agentMaxSlope, m_agentHeight, m_agentRadius, m_agentMaxClimb,
            m_regionMinArea, m_regionMergeArea,
            m_edgeMaxLen, m_edgeMaxError,
            m_vertsPerPoly,
            m_detailSampleDist, m_detailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);
        RcBuilderConfig bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), 7, 8);
        RcBuilderResult rcResult = builder.Build(geom, bcfg, false);
        Assert.Equal(1, rcResult.Mesh.npolys);
        Assert.Equal(5, rcResult.Mesh.nverts);
        bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), 6, 9);
        rcResult = builder.Build(geom, bcfg, false);
        Assert.Equal(2, rcResult.Mesh.npolys);
        Assert.Equal(7, rcResult.Mesh.nverts);
        bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), 2, 9);
        rcResult = builder.Build(geom, bcfg, false);
        Assert.Equal(2, rcResult.Mesh.npolys);
        Assert.Equal(9, rcResult.Mesh.nverts);
        bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), 4, 3);
        rcResult = builder.Build(geom, bcfg, false);
        Assert.Equal(3, rcResult.Mesh.npolys);
        Assert.Equal(6, rcResult.Mesh.nverts);
        bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), 2, 8);
        rcResult = builder.Build(geom, bcfg, false);
        Assert.Equal(5, rcResult.Mesh.npolys);
        Assert.Equal(17, rcResult.Mesh.nverts);
        bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), 0, 8);
        rcResult = builder.Build(geom, bcfg, false);
        Assert.Equal(6, rcResult.Mesh.npolys);
        Assert.Equal(15, rcResult.Mesh.nverts);
    }

    [Fact]
    public void TestPerformance()
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile("dungeon.obj");
        RcBuilder builder = new RcBuilder();
        RcConfig cfg = new RcConfig(
            true, m_tileSize, m_tileSize,
            RcConfig.CalcBorder(m_agentRadius, m_cellSize),
            m_partitionType,
            m_cellSize, m_cellHeight,
            m_agentMaxSlope, m_agentHeight, m_agentRadius, m_agentMaxClimb,
            m_regionMinArea, m_regionMergeArea,
            m_edgeMaxLen, m_edgeMaxError,
            m_vertsPerPoly,
            m_detailSampleDist, m_detailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);
        for (int i = 0; i < 4; i++)
        {
            Build(geom, builder, cfg, 1, true);
            Build(geom, builder, cfg, 4, true);
        }

        long t1 = RcFrequency.Ticks;
        for (int i = 0; i < 4; i++)
        {
            Build(geom, builder, cfg, 1, false);
        }

        long t2 = RcFrequency.Ticks;
        for (int i = 0; i < 4; i++)
        {
            Build(geom, builder, cfg, 4, false);
        }

        long t3 = RcFrequency.Ticks;
        Console.WriteLine(" Time ST : " + (t2 - t1) / TimeSpan.TicksPerMillisecond);
        Console.WriteLine(" Time MT : " + (t3 - t2) / TimeSpan.TicksPerMillisecond);
    }

    private void Build(IRcInputGeomProvider geom, RcBuilder builder, RcConfig cfg, int threads, bool validate)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        List<RcBuilderResult> tiles = builder.BuildTiles(geom, cfg, false, true, threads, Task.Factory, cts.Token);
        if (validate)
        {
            RcBuilderResult rcResult = GetTile(tiles, 7, 8);
            Assert.Equal(1, rcResult.Mesh.npolys);
            Assert.Equal(5, rcResult.Mesh.nverts);
            rcResult = GetTile(tiles, 6, 9);
            Assert.Equal(2, rcResult.Mesh.npolys);
            Assert.Equal(7, rcResult.Mesh.nverts);
            rcResult = GetTile(tiles, 2, 9);
            Assert.Equal(2, rcResult.Mesh.npolys);
            Assert.Equal(9, rcResult.Mesh.nverts);
            rcResult = GetTile(tiles, 4, 3);
            Assert.Equal(3, rcResult.Mesh.npolys);
            Assert.Equal(6, rcResult.Mesh.nverts);
            rcResult = GetTile(tiles, 2, 8);
            Assert.Equal(5, rcResult.Mesh.npolys);
            Assert.Equal(17, rcResult.Mesh.nverts);
            rcResult = GetTile(tiles, 0, 8);
            Assert.Equal(6, rcResult.Mesh.npolys);
            Assert.Equal(15, rcResult.Mesh.nverts);
        }

        try
        {
            cts.Cancel();
            //executor.AwaitTermination(1000, TimeUnit.HOURS);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private RcBuilderResult GetTile(List<RcBuilderResult> tiles, int x, int z)
    {
        return tiles.FirstOrDefault(tile => tile.TileX == x && tile.TileZ == z);
    }
}