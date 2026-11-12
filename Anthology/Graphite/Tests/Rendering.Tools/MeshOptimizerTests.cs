using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Graphite.Rendering.Tools.Tests;

public sealed class MeshOptimizerTests
{
    // A grid of quads in rows longer than the cache, the top half one submesh and the bottom the other, a colour per vertex
    private static MeshData Grid(int columns = 100, int rows = 60)
    {
        List<Float3> positions = new();
        List<uint> colors = new();
        for (int y = 0; y <= rows; y++)
        {
            for (int x = 0; x <= columns; x++)
            {
                positions.Add(new Float3(x, y, 0));
                colors.Add((uint)(x | y << 16));
            }
        }
        List<uint> indices = new();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                uint a = (uint)(y * (columns + 1) + x), b = a + 1, c = a + (uint)columns + 1, d = c + 1;
                indices.AddRange([a, c, b, b, c, d]);
            }
        }
        uint half = (uint)indices.Count / 2;
        return new MeshData
        {
            VertexCount = (uint)positions.Count,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.ToArray().AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Color, Format = VertexElementFormat.Byte4_Norm, Data = MemoryMarshal.AsBytes(colors.ToArray().AsSpan()).ToArray() },
            ],
            Indices = indices.ToArray(),
            SubMeshes = [new SubMesh { IndexStart = 0, IndexCount = half }, new SubMesh { IndexStart = half, IndexCount = half }],
        };
    }

    // Each submesh's triangles by the colours of their corners, turned to start at the smallest so winding is kept
    private static List<string>[] Triangles(MeshData mesh)
    {
        uint[] colors = MemoryMarshal.Cast<byte, uint>(mesh.Streams[1].Data.Span).ToArray();
        ReadOnlySpan<uint> indices = mesh.Indices.Span;
        List<string>[] submeshes = new List<string>[mesh.SubMeshes.Length];
        for (int s = 0; s < submeshes.Length; s++)
        {
            submeshes[s] = new();
            SubMesh sub = mesh.SubMeshes[s];
            for (uint i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i += 3)
            {
                uint[] corner = [colors[indices[(int)i]], colors[indices[(int)i + 1]], colors[indices[(int)i + 2]]];
                int first = Array.IndexOf(corner, corner.Min());
                submeshes[s].Add($"{corner[first]} {corner[(first + 1) % 3]} {corner[(first + 2) % 3]}");
            }
            submeshes[s].Sort(StringComparer.Ordinal);
        }
        return submeshes;
    }

    [Fact]
    public void Optimize_KeepsEveryTriangleInItsSubmesh_AndMissesTheCacheLess()
    {
        MeshData source = Grid();
        MeshData optimized = MeshOptimizer.Optimize(source);

        List<string>[] before = Triangles(source), after = Triangles(optimized);
        for (int s = 0; s < before.Length; s++)
            Assert.Equal(before[s], after[s]);

        double was = VertexCacheOptimizer.MissRatio(source.Indices.Span), now = VertexCacheOptimizer.MissRatio(optimized.Indices.Span);
        Assert.True(now < 0.8 && now < was * 0.75, $"miss ratio {was:0.000} to {now:0.000}");
    }

    [Fact]
    public void Optimize_NumbersVerticesInTheOrderTheTrianglesFirstUseThem()
    {
        MeshData optimized = MeshOptimizer.Optimize(Grid());
        uint next = 0;
        foreach (uint index in optimized.Indices.Span)
        {
            Assert.True(index <= next, $"vertex {index} used before {next}");
            if (index == next)
                next++;
        }
        Assert.Equal(optimized.VertexCount, next);
    }
}
