using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

public class RangeAllocatorTests
{
    [Fact]
    public void Allocations_DoNotOverlap_AndFreeingThemAllRestoresTheWholeSpace()
    {
        RangeAllocator allocator = new(1 << 20);
        Random random = new(3);
        List<RangeAllocation> live = new();

        for (int step = 0; step < 20000; step++)
        {
            if (live.Count > 0 && random.Next(3) == 0)
            {
                int pick = random.Next(live.Count);
                allocator.Free(live[pick]);
                live.RemoveAt(pick);
                continue;
            }

            uint size = (uint)random.Next(1, 4000);
            uint alignment = 1u << random.Next(0, 6);
            if (allocator.TryAllocate(size, alignment, out RangeAllocation allocation))
            {
                Assert.True(allocation.Size >= size);
                Assert.Equal(0u, allocation.Offset % alignment);
                live.Add(allocation);
            }
        }

        RangeAllocation[] sorted = [.. live.OrderBy(a => a.Offset)];
        for (int i = 1; i < sorted.Length; i++)
            Assert.True(sorted[i - 1].Offset + sorted[i - 1].Size <= sorted[i].Offset);
        Assert.Equal((uint)live.Sum(a => (long)a.Size), allocator.Used);

        foreach (RangeAllocation allocation in live)
            allocator.Free(allocation);

        Assert.Equal(0u, allocator.Used);
        Assert.Equal(allocator.Capacity, allocator.LargestFree);
    }

    [Fact]
    public void Allocation_FailsCleanly_WhenNothingFits()
    {
        RangeAllocator allocator = new(1000);
        Assert.True(allocator.TryAllocate(600, 1, out RangeAllocation first));
        Assert.False(allocator.TryAllocate(600, 1, out _));

        allocator.Free(first);
        Assert.True(allocator.TryAllocate(1000, 1, out _));
    }

    [Fact]
    public void FreeingTwice_Throws()
    {
        RangeAllocator allocator = new(100);
        Assert.True(allocator.TryAllocate(10, 1, out RangeAllocation allocation));
        allocator.Free(allocation);
        Assert.Throws<InvalidOperationException>(() => allocator.Free(allocation));
    }
}

public class MeshLayoutTests
{
    private static VertexStream Stream<T>(VertexSemantic semantic, VertexElementFormat format, T[] data) where T : unmanaged
        => new() { Semantic = semantic, Format = format, Data = MemoryMarshal.AsBytes(data.AsSpan()).ToArray() };

    private static MeshData Triangle(params VertexStream[] extra) => new()
    {
        VertexCount = 3,
        Streams = [Stream(VertexSemantic.Position, VertexElementFormat.Float3, new[] { new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(0, 2, 3) }), .. extra],
        Indices = new uint[] { 0, 1, 2 },
    };

    private static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    [Fact]
    public void Positions_CopyAsIs_AndTheMeshBoundsCoverThem()
    {
        MeshLayout layout = new(Triangle());

        Assert.Equal(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 2f, 3f }, layout.Streams[(int)VertexSemantic.Position]!.Select(F));
        Assert.Equal(new Float3(0, 0, 0), layout.Bounds.Min);
        Assert.Equal(new Float3(1, 2, 3), layout.Bounds.Max);
        Assert.Equal(1u << (int)VertexSemantic.Position, layout.StreamsPresent);
    }

    [Fact]
    public void HalfUVs_WidenToFloats()
    {
        MeshLayout layout = new(Triangle(Stream(VertexSemantic.UV0, VertexElementFormat.Half2,
            new[] { (Half)0.5f, (Half)0.25f, (Half)1f, (Half)0f, (Half)0f, (Half)1f })));

        Assert.Equal(new[] { 0.5f, 0.25f, 1f, 0f, 0f, 1f }, layout.Streams[(int)VertexSemantic.UV0]!.Select(F));
    }

    [Fact]
    public void Colors_PackToUnorm8()
    {
        MeshLayout layout = new(Triangle(Stream(VertexSemantic.Color, VertexElementFormat.Float4,
            new[] { new Float4(1, 0, 0, 1), new Float4(0, 1, 0, 0.5f), new Float4(0, 0, 1, 0) })));

        Assert.Equal(new[] { 0xFF0000FFu, 0x8000FF00u, 0x00FF0000u }, layout.Streams[(int)VertexSemantic.Color]);
    }

    [Fact]
    public void Tangents_WithoutHandedness_AreRightHanded()
    {
        MeshLayout layout = new(Triangle(Stream(VertexSemantic.Tangent, VertexElementFormat.Float3,
            new[] { new Float3(1, 0, 0), new Float3(1, 0, 0), new Float3(1, 0, 0) })));

        Assert.Equal(new[] { 1f, 0f, 0f, 1f }, layout.Streams[(int)VertexSemantic.Tangent]!.Take(4).Select(F));
    }

    [Fact]
    public void BoneIndicesAndWeights_PackTo16Bits()
    {
        MeshLayout layout = new(Triangle(
            Stream(VertexSemantic.BoneIndices, VertexElementFormat.UShort4, new ushort[] { 1, 2, 3, 300, 0, 0, 0, 0, 0, 0, 0, 0 }),
            Stream(VertexSemantic.BoneWeights, VertexElementFormat.Float4, new[] { new Float4(0.5f, 0.5f, 0, 0), new Float4(1, 0, 0, 0), new Float4(1, 0, 0, 0) })));

        Assert.Equal(new[] { 1u | (2u << 16), 3u | (300u << 16) }, layout.Streams[(int)VertexSemantic.BoneIndices]!.Take(2));
        Assert.Equal(new[] { 32768u | (32768u << 16), 0u }, layout.Streams[(int)VertexSemantic.BoneWeights]!.Take(2));
    }

    [Fact]
    public void BigSubmeshes_SplitIntoChunks_EachWithItsOwnBounds()
    {
        const int triangles = 5000;
        Float3[] positions = new Float3[triangles * 3];
        uint[] indices = new uint[triangles * 3];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = new Float3(i / 3, 0, 0);
            indices[i] = (uint)i;
        }

        MeshLayout layout = new(new MeshData
        {
            VertexCount = (uint)positions.Length,
            Streams = [Stream(VertexSemantic.Position, VertexElementFormat.Float3, positions)],
            Indices = indices,
        });

        Assert.Equal(new uint[] { 6144, 6144, 2712 }, layout.Chunks.Select(c => c.IndexCount));
        Assert.Equal(new uint[] { 0, 6144, 12288 }, layout.Chunks.Select(c => c.IndexStart));
        Assert.Equal(0f, layout.Chunks[0].Bounds.Min.X);
        Assert.Equal(2047f, layout.Chunks[0].Bounds.Max.X);
        Assert.Equal(2048f, layout.Chunks[1].Bounds.Min.X);
        Assert.Equal(4999f, layout.Bounds.Max.X);
    }

    [Fact]
    public void Strips_AreNotSplit()
    {
        Float3[] positions = Enumerable.Range(0, 10000).Select(i => new Float3(i, 0, 0)).ToArray();
        MeshLayout layout = new(new MeshData
        {
            VertexCount = (uint)positions.Length,
            Streams = [Stream(VertexSemantic.Position, VertexElementFormat.Float3, positions)],
            Indices = Enumerable.Range(0, 10000).Select(i => (uint)i).ToArray(),
            SubMeshes = [new SubMesh { IndexStart = 0, IndexCount = 10000, Topology = PrimitiveTopology.TriangleStrip }],
        });

        Assert.Single(layout.Chunks);
    }

    [Fact]
    public void BadMeshes_Throw()
    {
        Assert.Throws<ArgumentException>(() => new MeshLayout(new MeshData { VertexCount = 3, Indices = new uint[] { 0, 1, 2 } }));
        Assert.Throws<ArgumentException>(() => new MeshLayout(new MeshData
        {
            VertexCount = 3,
            Streams = Triangle().Streams,
            Indices = new uint[] { 0, 1, 7 },
        }));
        Assert.Throws<ArgumentException>(() => new MeshLayout(Triangle(Stream(VertexSemantic.UV0, VertexElementFormat.Float2, new[] { new Float2(0, 0) }))));
    }
}
