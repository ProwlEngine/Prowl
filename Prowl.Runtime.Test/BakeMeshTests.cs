// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for Mesh.Version and the shared, cached physics-mesh bake (PhysicsWorld.BakeMesh /
/// BakedPhysicsMesh) - so the Jitter triangle mesh is built once per mesh and reused by every collider,
/// rebuilt only when the mesh changes.
/// </summary>
public class BakeMeshTests
{
    [Fact]
    public void MeshVersion_IncrementsOnGeometryChange()
    {
        var m = Mesh.CreateCube(Float3.One);
        uint before = m.Version;

        m.Vertices = m.Vertices; // any geometry assignment marks the mesh changed

        Assert.True(m.Version > before);
    }

    [Fact]
    public void MeshVersion_HasChanged_TracksAndResets()
    {
        var m = Mesh.CreateCube(Float3.One);
        uint last = m.Version;

        m.Vertices = m.Vertices;
        Assert.True(m.HasChanged(ref last));
        Assert.False(m.HasChanged(ref last)); // no change since last check
    }

    [Fact]
    public void BakeMesh_ExtractsTrianglesAndBuildsTriangleMesh()
    {
        var m = Mesh.CreateCube(Float3.One);

        var baked = PhysicsWorld.BakeMesh(m);

        Assert.NotNull(baked.TriangleMesh);
        Assert.True(baked.Triangles.Count > 0);
        Assert.Equal(m.Indices.Length / 3, baked.Triangles.Count);
        Assert.Equal(m.Version, baked.Version);
    }

    [Fact]
    public void BakeMesh_IsCachedPerMesh()
    {
        var m = Mesh.CreateCube(Float3.One);

        var a = PhysicsWorld.BakeMesh(m);
        var b = PhysicsWorld.BakeMesh(m);

        Assert.Same(a, b); // same mesh, same version -> shared bake (not rebuilt)
    }

    [Fact]
    public void BakeMesh_RebakesAfterMeshChanges()
    {
        var m = Mesh.CreateCube(Float3.One);
        var first = PhysicsWorld.BakeMesh(m);

        m.Vertices = m.Vertices; // bumps Version, invalidating the cached bake

        var second = PhysicsWorld.BakeMesh(m);
        Assert.NotSame(first, second);
        Assert.Equal(m.Version, second.Version);
    }

    [Fact]
    public void BakeMesh_ConcurrentCalls_ReturnSingleSharedInstance()
    {
        var m = Mesh.CreateCube(Float3.One);
        var results = new ConcurrentBag<BakedPhysicsMesh>();

        // Pure-CPU bake must be safe to call from many threads; all should converge on one cached bake.
        Parallel.For(0, 64, _ => results.Add(PhysicsWorld.BakeMesh(m)));

        Assert.Single(results.Distinct());
    }
}
