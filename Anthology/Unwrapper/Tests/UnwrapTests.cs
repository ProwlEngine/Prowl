// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Unwrapper;
using Prowl.Vector;

namespace Prowl.Unwrapper.Tests;

/// <summary>
/// Smoke + correctness tests on hand-built procedural meshes. We check that the unwrapper
/// produces UVs in the unit square, with the expected per-corner count, and without throwing.
/// Every mesh case runs against both unwrap methods. Each test mesh exercises a different path:
///   - Quad: trivial flat case
///   - Cube: cross-face seam detection, multiple charts
///   - Octahedron: closed manifold with 8 faces, 6 verts, vertex degree 4
///   - SubdivCube: medium-sized chart packing
///   - UVSphere: pole degeneracies + chart segmentation
/// </summary>
public class UnwrapTests
{
    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void Quad_unwraps_into_unit_square(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.Quad();
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));

        Assert.Equal(tris.Length, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.Null(result.DegenerateTriangleIndices);
    }

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void Cube_unwraps_six_faces(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.Cube();
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));

        Assert.Equal(36, result.PerCornerUVs.Length);  // 12 tris × 3 corners
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.Null(result.DegenerateTriangleIndices);
    }

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void Octahedron_unwraps_cleanly(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.Octahedron();
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));

        Assert.Equal(24, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.Null(result.DegenerateTriangleIndices);
    }

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void SubdivCube_packs_into_unit_square(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.SubdivCube(8);
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));

        Assert.Equal(tris.Length, result.PerCornerUVs.Length);
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void UvSphere_drops_polar_degenerates(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.UvSphere(12, 16);
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));

        Assert.Equal(tris.Length, result.PerCornerUVs.Length);
        // Stacks×slices spheres have zero-area triangles at the poles; the prep pass should drop them.
        Assert.NotNull(result.DegenerateTriangleIndices);
        Assert.True(result.DegenerateTriangleIndices!.Length > 0);
    }

    [Fact]
    public void Projection_is_the_default_method()
    {
        Assert.Equal(UnwrapMethod.Projection, new UnwrapOptions().Method);
    }

    [Fact]
    public void Projection_keeps_texel_density_uniform_on_a_cube()
    {
        var (verts, tris) = Meshes.Cube();
        var result = new UnwrapMesh(verts, tris).Unwrap(For(UnwrapMethod.Projection));

        // Every cube face projects flat onto its own axis, so world-area per UV-area must agree
        // across all twelve triangles.
        double min = double.MaxValue, max = 0.0;
        for (int t = 0; t < tris.Length / 3; t++)
        {
            double ratio = WorldArea(verts, tris, t) / UvArea(result.PerCornerUVs, t);
            min = System.Math.Min(min, ratio);
            max = System.Math.Max(max, ratio);
        }
        Assert.True(max / min < 1.01, $"texel density varied by {max / min:F3}x across a cube");
    }

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void Charts_do_not_overlap_on_a_cube(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.Cube();
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));
        Assert.Equal(0, CountOverlappingTexels(result.PerCornerUVs, tris.Length / 3, resolution: 256));
    }

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void Charts_do_not_overlap_on_an_octahedron(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.Octahedron();
        var result = new UnwrapMesh(verts, tris).Unwrap(For(method));
        Assert.Equal(0, CountOverlappingTexels(result.PerCornerUVs, tris.Length / 3, resolution: 256));
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

    [Theory]
    [InlineData(UnwrapMethod.Projection)]
    [InlineData(UnwrapMethod.Conformal)]
    public void Custom_options_are_threaded_through(UnwrapMethod method)
    {
        var (verts, tris) = Meshes.Cube();
        var options = For(method);
        options.PackMargin = 0.0;
        options.MaxDegreeOfParallelism = 1;
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
        var options = new UnwrapOptions();
        options.Conformal.AngleDistortionThreshold = -0.1;
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris).Unwrap(options));
    }

    [Fact]
    public void Throws_when_options_have_negative_min_chart_size()
    {
        var (verts, tris) = Meshes.Cube();
        var options = new UnwrapOptions();
        options.Projection.MinChartTriangles = -1;
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris).Unwrap(options));
    }

    [Fact]
    public void Throws_when_options_have_invalid_parallelism()
    {
        var (verts, tris) = Meshes.Cube();
        var options = new UnwrapOptions { MaxDegreeOfParallelism = 0 };
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new UnwrapMesh(verts, tris).Unwrap(options));
    }

    private static UnwrapOptions For(UnwrapMethod method) => new() { Method = method };

    private static void AssertInUnitSquare(Double2[] uvs)
    {
        foreach (var uv in uvs)
        {
            if (uv.X == 0.0 && uv.Y == 0.0) continue;  // degenerate-slot sentinel
            Assert.InRange(uv.X, 0.0, 1.0);
            Assert.InRange(uv.Y, 0.0, 1.0);
        }
    }

    private static double WorldArea(Double3[] verts, int[] tris, int triangle)
    {
        Double3 a = verts[tris[3 * triangle + 0]];
        Double3 b = verts[tris[3 * triangle + 1]];
        Double3 c = verts[tris[3 * triangle + 2]];
        return 0.5 * Double3.Length(Double3.Cross(b - a, c - a));
    }

    private static double UvArea(Double2[] uvs, int triangle)
    {
        Double2 a = uvs[3 * triangle + 0], b = uvs[3 * triangle + 1], c = uvs[3 * triangle + 2];
        return 0.5 * System.Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
    }

    /// <summary>Sample the atlas at texel centres and count the ones covered by more than one triangle.</summary>
    private static int CountOverlappingTexels(Double2[] uvs, int triangleCount, int resolution)
    {
        int overlapping = 0;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                var p = new Double2((x + 0.5) / resolution, (y + 0.5) / resolution);
                int hits = 0;
                for (int t = 0; t < triangleCount && hits < 2; t++)
                    if (Covers(uvs, t, p)) ++hits;
                if (hits > 1) ++overlapping;
            }
        }
        return overlapping;
    }

    /// <summary>
    /// Strictly inside, so a texel centre landing exactly on the edge two triangles of the same
    /// quad share doesn't read as an overlap.
    /// </summary>
    private static bool Covers(Double2[] uvs, int triangle, Double2 p)
    {
        Double2 a = uvs[3 * triangle + 0], b = uvs[3 * triangle + 1], c = uvs[3 * triangle + 2];
        double d0 = Edge(a, b, p), d1 = Edge(b, c, p), d2 = Edge(c, a, p);
        return (d0 > 0 && d1 > 0 && d2 > 0) || (d0 < 0 && d1 < 0 && d2 < 0);

        static double Edge(Double2 u, Double2 v, Double2 q) => (v.X - u.X) * (q.Y - u.Y) - (v.Y - u.Y) * (q.X - u.X);
    }
}
