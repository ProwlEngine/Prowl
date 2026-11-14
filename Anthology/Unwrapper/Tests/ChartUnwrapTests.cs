// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Unwrapper;
using Prowl.Vector;

namespace Prowl.Unwrapper.Tests;

/// <summary>Tests for unwrapping a mesh whose seams are already cut.</summary>
public class ChartUnwrapTests
{
    [Fact]
    public void Open_piece_is_one_chart()
    {
        var (verts, tris) = Meshes.Quad();
        ChartUnwrapResult result = ChartUnwrapper.Unwrap(verts, tris);

        Assert.Equal(1, result.ChartCount);
        Assert.All(result.TriangleCharts, chart => Assert.Equal(0, chart));
        AssertInUnitSquare(result.PerCornerUVs);
        Assert.True(UvArea(result.PerCornerUVs) > 0.5);
    }

    [Fact]
    public void Shared_vertices_share_their_uv()
    {
        var (verts, tris) = Meshes.Quad();
        ChartUnwrapResult result = ChartUnwrapper.Unwrap(verts, tris);

        // Corners 0 and 3 are vertex 0, corners 2 and 4 are vertex 2
        AssertSame(result.PerCornerUVs[0], result.PerCornerUVs[3]);
        AssertSame(result.PerCornerUVs[2], result.PerCornerUVs[4]);
    }

    [Fact]
    public void A_face_with_its_own_vertices_is_its_own_chart()
    {
        var (verts, tris) = Meshes.Cube();

        // The first face of the cube gets copies of its four vertices, which cuts it loose and opens the rest
        var cut = new List<Double3>(verts);
        var copies = new Dictionary<int, int>();
        int[] cutTris = (int[])tris.Clone();
        for (int corner = 0; corner < 6; corner++)
        {
            if (!copies.TryGetValue(tris[corner], out int copy))
            {
                copies[tris[corner]] = copy = cut.Count;
                cut.Add(verts[tris[corner]]);
            }
            cutTris[corner] = copy;
        }

        ChartUnwrapResult result = ChartUnwrapper.Unwrap(cut.ToArray(), cutTris);

        Assert.Equal(2, result.ChartCount);
        Assert.Equal(result.TriangleCharts[0], result.TriangleCharts[1]);
        for (int t = 2; t < 12; t++)
        {
            Assert.NotEqual(result.TriangleCharts[0], result.TriangleCharts[t]);
            Assert.Equal(result.TriangleCharts[2], result.TriangleCharts[t]);
        }
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Fact]
    public void A_closed_piece_is_cut_into_charts()
    {
        var (verts, tris) = Meshes.Cube();
        ChartUnwrapResult result = ChartUnwrapper.Unwrap(verts, tris);

        Assert.True(result.ChartCount > 1);
        Assert.All(result.TriangleCharts, chart => Assert.InRange(chart, 0, result.ChartCount - 1));
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Fact]
    public void A_triangle_without_area_gets_no_chart()
    {
        var verts = new Double3[] { new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(2, 0, 0) };
        var tris = new[] { 0, 1, 2, 0, 2, 3, 0, 1, 4 };
        ChartUnwrapResult result = ChartUnwrapper.Unwrap(verts, tris);

        Assert.Equal(1, result.ChartCount);
        Assert.Equal(-1, result.TriangleCharts[2]);
        Assert.Equal(0, result.TriangleCharts[0]);
    }

    [Fact]
    public void Pins_hold_their_vertices()
    {
        var (verts, tris) = Meshes.Quad();
        var pins = new Dictionary<int, Double2> { [0] = new(2, 2), [2] = new(4, 4) };
        ChartUnwrapResult result = ChartUnwrapper.Unwrap(verts, tris, null, pins);

        Assert.Equal(1, result.ChartCount);
        AssertSame(result.PerCornerUVs[0], new Double2(2, 2));
        AssertSame(result.PerCornerUVs[2], new Double2(4, 4));

        // The square keeps its shape: the other two corners land on the other diagonal
        Double2 b = result.PerCornerUVs[1], d = result.PerCornerUVs[5];
        Assert.Equal(3.0, (b.X + d.X) / 2, 3);
        Assert.Equal(3.0, (b.Y + d.Y) / 2, 3);
        Assert.Equal(2.0 * System.Math.Sqrt(2), Double2.Length(b - d), 3);
    }

    [Fact]
    public void One_pin_is_not_enough_to_hold_a_piece()
    {
        var (verts, tris) = Meshes.Quad();
        ChartUnwrapResult result = ChartUnwrapper.Unwrap(verts, tris, null, new Dictionary<int, Double2> { [0] = new(5, 5) });
        AssertInUnitSquare(result.PerCornerUVs);
    }

    [Fact]
    public void Pack_fits_islands_in_the_unit_square_without_resizing_one_against_another()
    {
        Double2[] big = { new(10, 10), new(12, 10), new(12, 12), new(10, 10), new(12, 12), new(10, 12) };
        Double2[] small = { new(-5, 0), new(-4, 0), new(-4, 1), new(-5, 0), new(-4, 1), new(-5, 1) };
        ChartUnwrapper.Pack(new[] { big, small }, 0.01);

        AssertInUnitSquare(big);
        AssertInUnitSquare(small);
        Assert.Equal(4.0, UvArea(big) / UvArea(small), 3);
        Assert.True(big.Max(p => p.X) <= small.Min(p => p.X) || small.Max(p => p.X) <= big.Min(p => p.X)
            || big.Max(p => p.Y) <= small.Min(p => p.Y) || small.Max(p => p.Y) <= big.Min(p => p.Y));
    }

    private static void AssertSame(Double2 a, Double2 b)
    {
        Assert.Equal(a.X, b.X, 6);
        Assert.Equal(a.Y, b.Y, 6);
    }

    private static void AssertInUnitSquare(Double2[] uvs)
    {
        foreach (Double2 uv in uvs)
        {
            Assert.InRange(uv.X, 0.0, 1.0);
            Assert.InRange(uv.Y, 0.0, 1.0);
        }
    }

    private static double UvArea(Double2[] uvs)
    {
        double area = 0;
        for (int i = 0; i < uvs.Length; i += 3)
        {
            Double2 e1 = uvs[i + 1] - uvs[i], e2 = uvs[i + 2] - uvs[i];
            area += 0.5 * System.Math.Abs(e1.X * e2.Y - e1.Y * e2.X);
        }
        return area;
    }
}
