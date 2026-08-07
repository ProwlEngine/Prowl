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

using Prowl.Recast.Geom;

namespace Prowl.Recast.Tests;

public class RecastLayersTest
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
    private readonly int m_partitionType = RcPartitionType.WATERSHED.Value;
    private const int m_tileSize = 48;

    [Fact]
    public void TestDungeon2()
    {
    }


    [Fact]
    public void TestDungeon()
    {
        RcHeightfieldLayerSet lset = Build("dungeon.obj", 3, 2);
        Assert.Equal(1, lset.layers.Length);
        Assert.Equal(48, lset.layers[0].width);
        Assert.Equal(51, lset.layers[0].hmin);
        Assert.Equal(67, lset.layers[0].hmax);
        Assert.Equal(17, lset.layers[0].heights[7]);
        Assert.Equal(15, lset.layers[0].heights[107]);
        Assert.Equal(13, lset.layers[0].heights[257]);
        Assert.Equal(255, lset.layers[0].heights[1814]);
        Assert.Equal(135, lset.layers[0].cons[12]);
        Assert.Equal(15, lset.layers[0].cons[109]);
        Assert.Equal(15, lset.layers[0].cons[530]);
        Assert.Equal(0, lset.layers[0].cons[1600]);
    }

    [Fact]
    public void Test()
    {
        RcHeightfieldLayerSet lset = Build("nav_test.obj", 3, 2);
        Assert.Equal(3, lset.layers.Length);
        Assert.Equal(48, lset.layers[0].width);
        Assert.Equal(13, lset.layers[0].hmin);
        Assert.Equal(30, lset.layers[0].hmax);
        Assert.Equal(0, lset.layers[0].heights[7]);
        Assert.Equal(255, lset.layers[0].heights[107]);
        Assert.Equal(0, lset.layers[0].heights[257]);
        Assert.Equal(255, lset.layers[0].heights[1814]);
        Assert.Equal(133, lset.layers[0].cons[12]);
        Assert.Equal(0, lset.layers[0].cons[109]);
        Assert.Equal(0, lset.layers[0].cons[530]);
        Assert.Equal(15, lset.layers[0].cons[1600]);

        Assert.Equal(48, lset.layers[1].width);
        Assert.Equal(13, lset.layers[1].hmin);
        Assert.Equal(13, lset.layers[1].hmax);
        Assert.Equal(255, lset.layers[1].heights[7]);
        Assert.Equal(255, lset.layers[1].heights[107]);
        Assert.Equal(255, lset.layers[1].heights[257]);
        Assert.Equal(255, lset.layers[1].heights[1814]);
        Assert.Equal(0, lset.layers[1].cons[12]);
        Assert.Equal(0, lset.layers[1].cons[109]);
        Assert.Equal(0, lset.layers[1].cons[530]);
        Assert.Equal(0, lset.layers[1].cons[1600]);

        Assert.Equal(48, lset.layers[2].width);
        Assert.Equal(76, lset.layers[2].hmin);
        Assert.Equal(76, lset.layers[2].hmax);
        Assert.Equal(255, lset.layers[2].heights[7]);
        Assert.Equal(255, lset.layers[2].heights[107]);
        Assert.Equal(255, lset.layers[2].heights[257]);
        Assert.Equal(255, lset.layers[2].heights[1814]);
        Assert.Equal(0, lset.layers[2].cons[12]);
        Assert.Equal(0, lset.layers[2].cons[109]);
        Assert.Equal(0, lset.layers[2].cons[530]);
        Assert.Equal(0, lset.layers[2].cons[1600]);
    }

    [Fact]
    public void Test2()
    {
        RcHeightfieldLayerSet lset = Build("nav_test.obj", 2, 4);
        Assert.Equal(2, lset.layers.Length);
        Assert.Equal(48, lset.layers[0].width);
        Assert.Equal(13, lset.layers[0].hmin);
        Assert.Equal(13, lset.layers[0].hmax);
        Assert.Equal(0, lset.layers[0].heights[7]);
        Assert.Equal(0, lset.layers[0].heights[107]);
        Assert.Equal(0, lset.layers[0].heights[257]);
        Assert.Equal(0, lset.layers[0].heights[1814]);
        Assert.Equal(135, lset.layers[0].cons[12]);
        Assert.Equal(15, lset.layers[0].cons[109]);
        Assert.Equal(0, lset.layers[0].cons[530]);
        Assert.Equal(15, lset.layers[0].cons[1600]);

        Assert.Equal(48, lset.layers[1].width);
        Assert.Equal(68, lset.layers[1].hmin);
        Assert.Equal(101, lset.layers[1].hmax);
        Assert.Equal(33, lset.layers[1].heights[7]);
        Assert.Equal(255, lset.layers[1].heights[107]);
        Assert.Equal(255, lset.layers[1].heights[257]);
        Assert.Equal(3, lset.layers[1].heights[1814]);
        Assert.Equal(0, lset.layers[1].cons[12]);
        Assert.Equal(0, lset.layers[1].cons[109]);
        Assert.Equal(15, lset.layers[1].cons[530]);
        Assert.Equal(0, lset.layers[1].cons[1600]);
    }

    private RcHeightfieldLayerSet Build(string filename, int x, int y)
    {
        IRcInputGeomProvider geom = RcSampleInputGeomProvider.LoadFile(filename);
        RcBuilder builder = new RcBuilder();
        RcConfig cfg = new RcConfig(true, m_tileSize, m_tileSize,
            RcConfig.CalcBorder(m_agentRadius, m_cellSize),
            RcPartitionType.OfValue(m_partitionType),
            m_cellSize, m_cellHeight,
            m_agentMaxSlope, m_agentHeight, m_agentRadius, m_agentMaxClimb,
            m_regionMinArea, m_regionMergeArea,
            m_edgeMaxLen, m_edgeMaxError,
            m_vertsPerPoly,
            m_detailSampleDist, m_detailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);
        RcBuilderConfig bcfg = new RcBuilderConfig(cfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax(), x, y);
        RcHeightfieldLayerSet lset = builder.BuildLayers(geom, bcfg);
        return lset;
    }
}