// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class VertexCacheOptimizerTests
{
    // A grid of quads in rows longer than the cache, row by row
    private static uint[] Grid(int columns, int rows, out uint vertexCount)
    {
        List<uint> indices = new();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                uint a = (uint)(y * (columns + 1) + x), b = a + 1, c = a + (uint)columns + 1, d = c + 1;
                indices.AddRange([a, c, b, b, c, d]);
            }
        }
        vertexCount = (uint)((columns + 1) * (rows + 1));
        return indices.ToArray();
    }

    // Each triangle turned to start at its smallest index, so winding is kept, then sorted
    private static List<string> Triangles(ReadOnlySpan<uint> indices)
    {
        List<string> triangles = new();
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint[] corner = [indices[i], indices[i + 1], indices[i + 2]];
            int first = Array.IndexOf(corner, corner.Min());
            triangles.Add($"{corner[first]} {corner[(first + 1) % 3]} {corner[(first + 2) % 3]}");
        }
        triangles.Sort(StringComparer.Ordinal);
        return triangles;
    }

    [Fact]
    public void Optimize_KeepsEveryTriangle_AndMissesTheCacheLess()
    {
        uint[] source = Grid(100, 60, out uint vertices), optimized = (uint[])source.Clone();
        VertexCacheOptimizer.Optimize(optimized, vertices);

        Assert.Equal(Triangles(source), Triangles(optimized));
        double was = VertexCacheOptimizer.MissRatio(source), now = VertexCacheOptimizer.MissRatio(optimized);
        Assert.True(now < 0.8 && now < was * 0.75, $"miss ratio {was:0.000} to {now:0.000}");
    }

    [Fact]
    public void TriangleOrder_NamesEverySourceTriangleOnce()
    {
        uint[] indices = Grid(40, 10, out uint vertices);
        int[] order = VertexCacheOptimizer.TriangleOrder(indices, vertices);
        Assert.Equal(Enumerable.Range(0, indices.Length / 3), order.OrderBy(t => t));
    }

    [Fact]
    public void Optimize_HandlesDegenerateTriangles()
    {
        uint[] indices = [0, 0, 1, 1, 2, 3, 2, 2, 2, 0, 1, 2];
        uint[] optimized = (uint[])indices.Clone();
        VertexCacheOptimizer.Optimize(optimized, 4);
        Assert.Equal(Triangles(indices), Triangles(optimized));
    }

    [Fact]
    public void FirstUseOrder_NumbersByFirstUse_UnusedLast()
    {
        uint[] remap = VertexCacheOptimizer.FirstUseOrder([4, 2, 0, 2, 4, 1], 6);
        Assert.Equal([2u, 3, 1, 4, 0, 5], remap);
    }

    [Fact]
    public void MissRatio_IsThreeWithoutSharing_AndLowerWithIt()
    {
        Assert.Equal(3, VertexCacheOptimizer.MissRatio([0, 1, 2, 3, 4, 5]));
        Assert.Equal(2, VertexCacheOptimizer.MissRatio([0, 1, 2, 2, 1, 3]));
    }
}
