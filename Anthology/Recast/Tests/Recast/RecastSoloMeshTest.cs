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
using System.IO;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Geom;

namespace Prowl.Recast.Tests;

using static RcRecast;
using static RcAreas;

public class RecastSoloMeshTest
{
    private const float m_cellSize = 0.3f;
    private const float m_cellHeight = 0.2f;
    private const float m_agentHeight = 2.0f;
    private const float m_agentRadius = 0.6f;
    private const float m_agentMaxClimb = 0.9f;
    private const float m_agentMaxSlope = 45.0f;
    private const int m_regionMinSize = 8;
    private const int m_regionMergeSize = 20;
    private const float m_edgeMaxLen = 12.0f;
    private const float m_edgeMaxError = 1.3f;
    private const int m_vertsPerPoly = 6;
    private const float m_detailSampleDist = 6.0f;
    private const float m_detailSampleMaxError = 1.0f;
    private RcPartition m_partitionType = RcPartition.WATERSHED;

    [Fact]
    public void TestPerformance()
    {
        for (int i = 0; i < 10; i++)
        {
            TestBuild("dungeon.obj", RcPartition.WATERSHED, 52, 16, 15, 223, 118, 118, 513, 291);
            TestBuild("dungeon.obj", RcPartition.MONOTONE, 0, 17, 16, 210, 100, 100, 453, 264);
            TestBuild("dungeon.obj", RcPartition.LAYERS, 0, 5, 5, 203, 97, 97, 446, 266);
        }
    }

    [Fact]
    public void TestDungeonWatershed()
    {
        TestBuild("dungeon.obj", RcPartition.WATERSHED, 52, 16, 15, 223, 118, 118, 513, 291);
    }

    [Fact]
    public void TestDungeonMonotone()
    {
        TestBuild("dungeon.obj", RcPartition.MONOTONE, 0, 17, 16, 210, 100, 100, 453, 264);
    }

    [Fact]
    public void TestDungeonLayers()
    {
        TestBuild("dungeon.obj", RcPartition.LAYERS, 0, 5, 5, 203, 97, 97, 446, 266);
    }

    [Fact]
    public void TestWatershed()
    {
        TestBuild("nav_test.obj", RcPartition.WATERSHED, 60, 48, 47, 349, 155, 155, 812, 561);
    }

    [Fact]
    public void TestMonotone()
    {
        TestBuild("nav_test.obj", RcPartition.MONOTONE, 0, 50, 49, 341, 186, 186, 878, 567);
    }

    [Fact]
    public void TestLayers()
    {
        TestBuild("nav_test.obj", RcPartition.LAYERS, 0, 19, 32, 310, 150, 150, 773, 526);
    }

    public void TestBuild(string filename, RcPartition partitionType, int expDistance, int expRegions,
        int expContours, int expVerts, int expPolys, int expDetMeshes, int expDetVerts, int expDetTris)
    {
        m_partitionType = partitionType;
        IRcInputGeomProvider geomProvider = RcSampleInputGeomProvider.LoadFile(filename);
        long time = RcFrequency.Ticks;
        RcVec3f bmin = geomProvider.GetMeshBoundsMin();
        RcVec3f bmax = geomProvider.GetMeshBoundsMax();
        RcContext m_ctx = new RcContext();
        //
        // Step 1. Initialize build config.
        //

        // Init build configuration from GUI
        RcConfig cfg = new RcConfig(
            partitionType,
            m_cellSize, m_cellHeight,
            m_agentMaxSlope, m_agentHeight, m_agentRadius, m_agentMaxClimb,
            m_regionMinSize, m_regionMergeSize,
            m_edgeMaxLen, m_edgeMaxError,
            m_vertsPerPoly,
            m_detailSampleDist, m_detailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);
        RcBuilderConfig bcfg = new RcBuilderConfig(cfg, bmin, bmax);

        //
        // Step 2. Rasterize input polygon soup.
        //

        // Allocate voxel heightfield where we rasterize our input data to.
        RcHeightfield m_solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);

        foreach (RcTriMesh geom in geomProvider.Meshes())
        {
            float[] verts = geom.GetVerts();
            int[] tris = geom.GetTris();
            int ntris = tris.Length / 3;

            // Allocate array that can hold triangle area types.
            // If you have multiple meshes you need to process, allocate
            // and array which can hold the max number of triangles you need to process.

            // Find triangles which are walkable based on their slope and rasterize them.
            // If your input data is multiple meshes, you can transform them here, calculate
            // the are type for each of the meshes and rasterize them.
            int[] m_triareas = RcRecast.MarkWalkableTriangles(m_ctx, cfg.WalkableSlopeAngle, verts, tris, ntris, cfg.WalkableAreaMod);
            RcRasterizations.RasterizeTriangles(m_ctx, verts, tris, m_triareas, ntris, m_solid, cfg.WalkableClimb);
        }

        //
        // Step 3. Filter walkable surfaces.
        //

        // Once all geometry is rasterized, we do initial pass of filtering to
        // remove unwanted overhangs caused by the conservative rasterization
        // as well as filter spans where the character cannot possibly stand.
        RcFilters.FilterLowHangingWalkableObstacles(m_ctx, cfg.WalkableClimb, m_solid);
        RcFilters.FilterLedgeSpans(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);
        RcFilters.FilterWalkableLowHeightSpans(m_ctx, cfg.WalkableHeight, m_solid);

        //
        // Step 4. Partition walkable surface to simple regions.
        //

        // Compact the heightfield so that it is faster to handle from now on.
        // This will result more cache coherent data as well as the neighbours
        // between walkable cells will be calculated.
        RcCompactHeightfield m_chf = RcCompacts.BuildCompactHeightfield(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);

        // Erode the walkable area by agent radius.
        ErodeWalkableArea(m_ctx, cfg.WalkableRadius, m_chf);

        // (Optional) Mark areas.
        /*
         * ConvexVolume vols = m_geom->GetConvexVolumes(); for (int i = 0; i < m_geom->GetConvexVolumeCount(); ++i)
         * RcMarkConvexPolyArea(m_ctx, vols[i].verts, vols[i].nverts, vols[i].hmin, vols[i].hmax, (unsigned
         * char)vols[i].area, *m_chf);
         */

        // Partition the heightfield so that we can use simple algorithm later
        // to triangulate the walkable areas.
        // There are 3 partitioning methods, each with some pros and cons:
        // 1) Watershed partitioning
        // - the classic Recast partitioning
        // - creates the nicest tessellation
        // - usually slowest
        // - partitions the heightfield into nice regions without holes or
        // overlaps
        // - the are some corner cases where this method creates produces holes
        // and overlaps
        // - holes may appear when a small obstacles is close to large open area
        // (triangulation can handle this)
        // - overlaps may occur if you have narrow spiral corridors (i.e
        // stairs), this make triangulation to fail
        // * generally the best choice if you precompute the navmesh, use this
        // if you have large open areas
        // 2) Monotone partioning
        // - fastest
        // - partitions the heightfield into regions without holes and overlaps
        // (guaranteed)
        // - creates long thin polygons, which sometimes causes paths with
        // detours
        // * use this if you want fast navmesh generation
        // 3) Layer partitoining
        // - quite fast
        // - partitions the heighfield into non-overlapping regions
        // - relies on the triangulation code to cope with holes (thus slower
        // than monotone partitioning)
        // - produces better triangles than monotone partitioning
        // - does not have the corner cases of watershed partitioning
        // - can be slow and create a bit ugly tessellation (still better than
        // monotone)
        // if you have large open areas with small obstacles (not a problem if
        // you use tiles)
        // * good choice to use for tiled navmesh with medium and small sized
        // tiles
        long time3 = RcFrequency.Ticks;

        if (m_partitionType == RcPartition.WATERSHED)
        {
            // Prepare for region partitioning, by calculating distance field
            // along the walkable surface.
            RcRegions.BuildDistanceField(m_ctx, m_chf);
            // Partition the walkable surface into simple regions without holes.
            RcRegions.BuildRegions(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
        }
        else if (m_partitionType == RcPartition.MONOTONE)
        {
            // Partition the walkable surface into simple regions without holes.
            // Monotone partitioning does not need distancefield.
            RcRegions.BuildRegionsMonotone(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
        }
        else
        {
            // Partition the walkable surface into simple regions without holes.
            RcRegions.BuildLayerRegions(m_ctx, m_chf, cfg.MinRegionArea);
        }

        Assert.Equal(expDistance, m_chf.maxDistance);
        Assert.Equal(expRegions, m_chf.maxRegions);
        //
        // Step 5. Trace and simplify region contours.
        //

        // Create contours.
        RcContourSet m_cset = RcContours.BuildContours(m_ctx, m_chf, cfg.MaxSimplificationError, cfg.MaxEdgeLen, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);

        Assert.Equal(expContours, m_cset.conts.Count);
        //
        // Step 6. Build polygons mesh from contours.
        //

        // Build polygon navmesh from the contours.
        RcPolyMesh m_pmesh = RcMeshs.BuildPolyMesh(m_ctx, m_cset, cfg.MaxVertsPerPoly);
        Assert.Equal(expVerts, m_pmesh.nverts);
        Assert.Equal(expPolys, m_pmesh.npolys);

        //
        // Step 7. Create detail mesh which allows to access approximate height
        // on each polygon.
        //

        RcPolyMeshDetail m_dmesh = RcMeshDetails.BuildPolyMeshDetail(m_ctx, m_pmesh, m_chf, cfg.DetailSampleDist,
            cfg.DetailSampleMaxError);
        Assert.Equal(expDetMeshes, m_dmesh.nmeshes);
        Assert.Equal(expDetVerts, m_dmesh.nverts);
        Assert.Equal(expDetTris, m_dmesh.ntris);
        long time2 = RcFrequency.Ticks;
        Console.WriteLine(filename + " : " + partitionType + "  " + (time2 - time) / TimeSpan.TicksPerMillisecond + " ms");
        Console.WriteLine("           " + (time3 - time) / TimeSpan.TicksPerMillisecond + " ms");
        SaveObj(filename.Substring(0, filename.LastIndexOf('.')) + "_" + partitionType + "_detail.obj", m_dmesh);
        SaveObj(filename.Substring(0, filename.LastIndexOf('.')) + "_" + partitionType + ".obj", m_pmesh);
        foreach (var rtt in m_ctx.ToList())
        {
            Console.WriteLine($"{rtt.Key} : {rtt.Millis} ms");
        }
    }

    private void SaveObj(string filename, RcPolyMesh mesh)
    {
        try
        {
            string path = Path.Combine("test-output", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using StreamWriter fw = new StreamWriter(path);
            for (int v = 0; v < mesh.nverts; v++)
            {
                fw.Write("v " + (mesh.bmin.X + mesh.verts[v * 3] * mesh.cs) + " "
                         + (mesh.bmin.Y + mesh.verts[v * 3 + 1] * mesh.ch) + " "
                         + (mesh.bmin.Z + mesh.verts[v * 3 + 2] * mesh.cs) + "\n");
            }

            for (int i = 0; i < mesh.npolys; i++)
            {
                int p = i * mesh.nvp * 2;
                fw.Write("f ");
                for (int j = 0; j < mesh.nvp; ++j)
                {
                    int v = mesh.polys[p + j];
                    if (v == RC_MESH_NULL_IDX)
                    {
                        break;
                    }

                    fw.Write((v + 1) + " ");
                }

                fw.Write("\n");
            }

            fw.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private void SaveObj(string filename, RcPolyMeshDetail dmesh)
    {
        try
        {
            string filePath = Path.Combine("test-output", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            using StreamWriter fw = new StreamWriter(filePath);
            for (int v = 0; v < dmesh.nverts; v++)
            {
                fw.Write(
                    "v " + dmesh.verts[v * 3] + " " + dmesh.verts[v * 3 + 1] + " " + dmesh.verts[v * 3 + 2] + "\n");
            }

            for (int m = 0; m < dmesh.nmeshes; m++)
            {
                int vfirst = dmesh.meshes[m * 4];
                int tfirst = dmesh.meshes[m * 4 + 2];
                for (int f = 0; f < dmesh.meshes[m * 4 + 3]; f++)
                {
                    fw.Write("f " + (vfirst + dmesh.tris[(tfirst + f) * 4] + 1) + " "
                             + (vfirst + dmesh.tris[(tfirst + f) * 4 + 1] + 1) + " "
                             + (vfirst + dmesh.tris[(tfirst + f) * 4 + 2] + 1) + "\n");
                }
            }

            fw.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}