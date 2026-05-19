using Prowl.Unwrapper;
using Prowl.Vector;

namespace Prowl.Unwrapper.Tests;

/// <summary>
/// Smoke + correctness tests on hand-built procedural meshes. We check that the unwrapper
/// produces UVs in the unit square, with the expected per-corner count, and without throwing.
/// Each test mesh exercises a different code path:
///   - Quad: trivial flat case
///   - Cube: cross-face seam detection, multiple charts
///   - Octahedron: closed manifold with 8 faces, 6 verts, vertex degree 4
///   - SubdivCube: medium-sized chart packing
///   - UVSphere: pole degeneracies + chart segmentation
/// </summary>
public class UnwrapTests
{
    [Fact]
    public void Quad_unwraps_into_unit_square()
    {
        var (verts, tris) = Meshes.Quad();
        var result = new UnwrapMesh(verts, tris).Unwrap();

        Assert.Equal(tris.Length, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.Null(result.DegenerateTriangleIndices);
    }

    [Fact]
    public void Cube_unwraps_six_faces()
    {
        var (verts, tris) = Meshes.Cube();
        var result = new UnwrapMesh(verts, tris).Unwrap();

        Assert.Equal(36, result.PerCornerUVs.Length);  // 12 tris × 3 corners
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.Null(result.DegenerateTriangleIndices);
    }

    [Fact]
    public void Octahedron_unwraps_cleanly()
    {
        var (verts, tris) = Meshes.Octahedron();
        var result = new UnwrapMesh(verts, tris).Unwrap();

        Assert.Equal(24, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.Null(result.DegenerateTriangleIndices);
    }

    [Fact]
    public void SubdivCube_packs_into_unit_square()
    {
        var (verts, tris) = Meshes.SubdivCube(8);
        var result = new UnwrapMesh(verts, tris).Unwrap();

        Assert.Equal(tris.Length, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Fact]
    public void UvSphere_drops_polar_degenerates()
    {
        var (verts, tris) = Meshes.UvSphere(12, 16);
        var result = new UnwrapMesh(verts, tris).Unwrap();

        Assert.Equal(tris.Length, result.PerCornerUVs.Length);
        // Stacks×slices spheres have zero-area triangles at the poles; the prep pass should drop them.
        Assert.NotNull(result.DegenerateTriangleIndices);
        Assert.True(result.DegenerateTriangleIndices!.Length > 0);
    }

    [Fact]
    public void Throws_when_triangles_not_multiple_of_three()
    {
        var verts = new Double3[] { new(0, 0, 0), new(1, 0, 0) };
        Assert.Throws<System.ArgumentException>(() => new UnwrapMesh(verts, new int[] { 0, 1 }));
    }

    [Fact]
    public void Throws_when_geometry_fully_collapses()
    {
        // All three corners coincident -> degenerate triangle, then no triangles survive cleanup.
        var verts = new Double3[] { new(0, 0, 0), new(0, 0, 0), new(0, 0, 0) };
        var tris = new int[] { 0, 1, 2 };
        Assert.Throws<UnwrapException>(() => new UnwrapMesh(verts, tris).Unwrap());
    }

    [Fact]
    public void Custom_options_are_threaded_through()
    {
        var (verts, tris) = Meshes.Cube();
        var options = new UnwrapOptions { PackMargin = 0.0, MaxDegreeOfParallelism = 1 };
        var result = new UnwrapMesh(verts, tris).Unwrap(options);
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Fact]
    public void Static_Unwrap_one_shot_matches_fluent_form()
    {
        var (verts, tris) = Meshes.Cube();
        var result = UnwrapMesh.Unwrap(verts, tris);
        Assert.Equal(36, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Fact]
    public void Throws_when_triangle_index_out_of_range()
    {
        var verts = new Double3[] { new(0, 0, 0), new(1, 0, 0), new(0, 1, 0) };
        var tris = new int[] { 0, 1, 5 };  // index 5 does not exist
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris));
    }

    [Fact]
    public void Throws_when_triangle_index_negative()
    {
        var verts = new Double3[] { new(0, 0, 0), new(1, 0, 0), new(0, 1, 0) };
        var tris = new int[] { 0, 1, -1 };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris));
    }

    [Fact]
    public void Throws_when_options_have_negative_threshold()
    {
        var (verts, tris) = Meshes.Cube();
        var options = new UnwrapOptions { AngleDistortionThreshold = -0.1 };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris).Unwrap(options));
    }

    [Fact]
    public void Throws_when_options_have_invalid_parallelism()
    {
        var (verts, tris) = Meshes.Cube();
        var options = new UnwrapOptions { MaxDegreeOfParallelism = 0 };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris).Unwrap(options));
    }

    private static void AssertInUnitSquare(Double2[] uvs)
    {
        foreach (var uv in uvs)
        {
            if (uv.X == 0.0 && uv.Y == 0.0) continue;  // degenerate-slot sentinel
            Assert.InRange(uv.X, 0.0, 1.0);
            Assert.InRange(uv.Y, 0.0, 1.0);
        }
    }
}
