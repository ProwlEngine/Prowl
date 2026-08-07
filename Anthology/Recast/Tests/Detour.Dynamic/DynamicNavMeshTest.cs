using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Prowl.Recast.Core;
using Prowl.Recast.Core.Collections;
using Prowl.Recast.Core.Numerics;
using Prowl.Recast.Detour.Dynamic.Colliders;
using Prowl.Recast.Detour.Dynamic.Io;
using Prowl.Recast.Detour.Dynamic.Tests.Io;
using Prowl.Recast.Detour.Io;

namespace Prowl.Recast.Detour.Dynamic.Tests;

public class DynamicNavMeshTest
{
    private static readonly RcVec3f START_POS = new RcVec3f(70.87453f, 0.0010070801f, 86.69021f);
    private static readonly RcVec3f END_POS = new RcVec3f(-50.22061f, 0.0010070801f, -70.761444f);
    private static readonly RcVec3f EXTENT = new RcVec3f(0.1f, 0.1f, 0.1f);
    private static readonly RcVec3f SPHERE_POS = new RcVec3f(45.381645f, 0.0010070801f, 52.68981f);


    [Fact]
    public void E2eTest()
    {
        byte[] bytes = RcIO.ReadFileIfFound("test_tiles.voxels");
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        // load voxels from file
        DtVoxelFileReader reader = new DtVoxelFileReader(DtVoxelTileLZ4ForTestCompressor.Shared);
        DtVoxelFile f = reader.Read(br);
        // create dynamic navmesh
        DtDynamicNavMesh mesh = new DtDynamicNavMesh(f);
        // build navmesh asynchronously using multiple threads
        mesh.Build(Task.Factory);

        // create new query
        DtNavMeshQuery query = new DtNavMeshQuery(mesh.NavMesh());
        IDtQueryFilter filter = new DtQueryDefaultFilter();

        // find path
        query.FindNearestPoly(START_POS, EXTENT, filter, out var startRef, out var startPt, out var _);
        query.FindNearestPoly(END_POS, EXTENT, filter, out var endRef, out var endPt, out var _);

        RcFixedArray256<long> path = new RcFixedArray256<long>();
        query.FindPath(startRef, endRef, startPt, endPt, filter, path.AsSpan(), out var npath, path.Length);
        // check path length without any obstacles
        Assert.Equal(16, npath);

        // place obstacle
        IDtCollider colldier = new DtSphereCollider(SPHERE_POS, 20, SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GROUND, 0.1f);
        long colliderId = mesh.AddCollider(colldier);

        // update navmesh asynchronously
        mesh.Update(Task.Factory);

        // create new query
        query = new DtNavMeshQuery(mesh.NavMesh());

        // find path again
        query.FindNearestPoly(START_POS, EXTENT, filter, out startRef, out startPt, out var _);
        query.FindNearestPoly(END_POS, EXTENT, filter, out endRef, out endPt, out var _);
        query.FindPath(startRef, endRef, startPt, endPt, filter, path.AsSpan(), out npath, path.Length);

        // check path length with obstacles
        Assert.Equal(19, npath);
        // remove obstacle
        mesh.RemoveCollider(colliderId);
        // update navmesh asynchronously
        mesh.Update(Task.Factory);
        // create new query
        query = new DtNavMeshQuery(mesh.NavMesh());

        // find path one more time
        query.FindNearestPoly(START_POS, EXTENT, filter, out startRef, out startPt, out var _);
        query.FindNearestPoly(END_POS, EXTENT, filter, out endRef, out endPt, out var _);
        query.FindPath(startRef, endRef, startPt, endPt, filter, path.AsSpan(), out npath, path.Length);

        // path length should be back to the initial value
        Assert.Equal(16, npath);
    }


    [Fact]
    public void ShouldSaveAndLoadDynamicNavMesh()
    {
        using var writerMs = new MemoryStream();
        using var bw = new BinaryWriter(writerMs);


        int maxVertsPerPoly = 6;
        // load voxels from file

        {
            byte[] bytes = RcIO.ReadFileIfFound("test_tiles.voxels");
            using var readMs = new MemoryStream(bytes);
            using var br = new BinaryReader(readMs);

            DtVoxelFileReader reader = new DtVoxelFileReader(DtVoxelTileLZ4ForTestCompressor.Shared);
            DtVoxelFile f = reader.Read(br);

            // create dynamic navmesh
            DtDynamicNavMesh mesh = new DtDynamicNavMesh(f);

            // build navmesh asynchronously using multiple threads
            mesh.Build(Task.Factory);

            // Save the resulting nav mesh and re-use it
            new DtMeshSetWriter().Write(bw, mesh.NavMesh(), RcByteOrder.LITTLE_ENDIAN, true);
            maxVertsPerPoly = mesh.NavMesh().GetMaxVertsPerPoly();
        }

        {
            byte[] bytes = RcIO.ReadFileIfFound("test_tiles.voxels");
            using var readMs = new MemoryStream(bytes);
            using var br = new BinaryReader(readMs);

            // load voxels from file
            DtVoxelFileReader reader = new DtVoxelFileReader(DtVoxelTileLZ4ForTestCompressor.Shared);
            DtVoxelFile f = reader.Read(br);

            // create dynamic navmesh
            DtDynamicNavMesh mesh = new DtDynamicNavMesh(f);
            // use the saved nav mesh instead of building from scratch
            DtNavMesh navMesh = new DtMeshSetReader().Read(new RcByteBuffer(writerMs.ToArray()), maxVertsPerPoly);
            mesh.NavMesh(navMesh);

            DtNavMeshQuery query = new DtNavMeshQuery(mesh.NavMesh());
            IDtQueryFilter filter = new DtQueryDefaultFilter();

            // find path
            _ = query.FindNearestPoly(START_POS, EXTENT, filter, out var startNearestRef, out var startNearestPos, out var _);
            _ = query.FindNearestPoly(END_POS, EXTENT, filter, out var endNearestRef, out var endNearestPos, out var _);

            RcFixedArray256<long> path = new RcFixedArray256<long>();
            query.FindPath(startNearestRef, endNearestRef, startNearestPos, endNearestPos, filter, path.AsSpan(), out var npath, path.Length);

            // check path length without any obstacles
            Assert.Equal(16, npath);

            // place obstacle
            DtCollider colldier = new DtSphereCollider(SPHERE_POS, 20, SampleAreaModifications.SAMPLE_POLYAREA_TYPE_GROUND, 0.1f);
            long colliderId = mesh.AddCollider(colldier);

            // update navmesh asynchronously
            mesh.Update(Task.Factory);

            // create new query
            query = new DtNavMeshQuery(mesh.NavMesh());

            // find path again
            _ = query.FindNearestPoly(START_POS, EXTENT, filter, out startNearestRef, out startNearestPos, out var _);
            _ = query.FindNearestPoly(END_POS, EXTENT, filter, out endNearestRef, out endNearestPos, out var _);

            path = new RcFixedArray256<long>();
            query.FindPath(startNearestRef, endNearestRef, startNearestPos, endNearestPos, filter, path.AsSpan(), out npath, path.Length);

            // check path length with obstacles
            Assert.Equal(19, npath);

            // remove obstacle
            mesh.RemoveCollider(colliderId);
            // update navmesh asynchronously
            mesh.Update(Task.Factory);

            // create new query
            query = new DtNavMeshQuery(mesh.NavMesh());
            // find path one more time
            _ = query.FindNearestPoly(START_POS, EXTENT, filter, out startNearestRef, out startNearestPos, out var _);
            _ = query.FindNearestPoly(END_POS, EXTENT, filter, out endNearestRef, out endNearestPos, out var _);

            path = new RcFixedArray256<long>();
            query.FindPath(startNearestRef, endNearestRef, startNearestPos, endNearestPos, filter, path.AsSpan(), out npath, path.Length);

            // path length should be back to the initial value
            Assert.Equal(16, npath);
        }
    }
}