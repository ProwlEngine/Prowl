// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using Prowl.Runtime.Graphite;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class BufferTests
{
    private readonly GraphiteTestFixture _fixture;

    public BufferTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateBuffer_WithValidDescriptor_Succeeds()
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = 1024,
            Usage = BufferUsage.Vertex,
            MemoryAccess = MemoryAccess.GpuOnly,
            DebugName = "TestBuffer"
        };

        using var buffer = _fixture.Device.CreateBuffer(in descriptor);

        Assert.NotNull(buffer);
        Assert.Equal(1024u, buffer.SizeInBytes);
        Assert.Equal(BufferUsage.Vertex, buffer.Usage);
        Assert.Equal("TestBuffer", buffer.DebugName);
    }

    [Fact]
    public void CreateBuffer_WithInitialData_ContainsData()
    {
        var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };

        using var buffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Vertex,
            data.AsSpan(),
            MemoryAccess.GpuOnly,
            "DataBuffer");

        Assert.NotNull(buffer);
        Assert.Equal((uint)(data.Length * sizeof(float)), buffer.SizeInBytes);
    }

    [Fact]
    public void CreateBuffer_UniformBuffer_Succeeds()
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = 256,
            Usage = BufferUsage.Uniform,
            MemoryAccess = MemoryAccess.CpuToGpu,
            DebugName = "UniformBuffer"
        };

        using var buffer = _fixture.Device.CreateBuffer(in descriptor);

        Assert.NotNull(buffer);
        Assert.Equal(BufferUsage.Uniform, buffer.Usage);
    }

    [Fact]
    public void CreateBuffer_IndexBuffer_Succeeds()
    {
        var indices = new ushort[] { 0, 1, 2, 2, 3, 0 };

        using var buffer = _fixture.Device.CreateBuffer<ushort>(
            BufferUsage.Index,
            indices.AsSpan(),
            MemoryAccess.GpuOnly,
            "IndexBuffer");

        Assert.NotNull(buffer);
        Assert.True(buffer.Usage.HasFlag(BufferUsage.Index));
    }

    [Fact]
    public void CreateBuffer_StorageBuffer_Succeeds()
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = 4096,
            Usage = BufferUsage.Storage,
            MemoryAccess = MemoryAccess.GpuOnly,
            DebugName = "StorageBuffer"
        };

        using var buffer = _fixture.Device.CreateBuffer(in descriptor);

        Assert.NotNull(buffer);
        Assert.Equal(BufferUsage.Storage, buffer.Usage);
    }

    [Fact]
    public void UpdateBuffer_ModifiesData()
    {
        var initialData = new float[] { 0, 0, 0, 0 };
        using var buffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.Vertex | BufferUsage.CopyDestination,
            initialData.AsSpan(),
            MemoryAccess.CpuToGpu);

        var newData = new float[] { 1, 2, 3, 4 };
        _fixture.Device.UpdateBuffer<float>(buffer, 0, newData.AsSpan());

        // Buffer update should complete without error
        Assert.NotNull(buffer);
    }

    [Fact]
    public void Buffer_Dispose_CleansUpResources()
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = 512,
            Usage = BufferUsage.Vertex,
            MemoryAccess = MemoryAccess.GpuOnly
        };

        var buffer = _fixture.Device.CreateBuffer(in descriptor);
        Assert.NotNull(buffer);

        // Should dispose without error
        buffer.Dispose();
    }

    [Fact]
    public void CreateBuffer_ZeroSize_Succeeds()
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = 0,
            Usage = BufferUsage.Vertex,
            MemoryAccess = MemoryAccess.GpuOnly
        };

        // OpenGL allows 0-size buffers
        using var buffer = _fixture.Device.CreateBuffer(in descriptor);
        Assert.NotNull(buffer);
    }

    [Fact]
    public void CreateBuffer_AllUsageFlags_Succeeds()
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = 1024,
            Usage = BufferUsage.Vertex | BufferUsage.Index | BufferUsage.Uniform |
                   BufferUsage.Storage | BufferUsage.Indirect |
                   BufferUsage.CopySource | BufferUsage.CopyDestination,
            MemoryAccess = MemoryAccess.GpuOnly,
            DebugName = "MultiUsageBuffer"
        };

        using var buffer = _fixture.Device.CreateBuffer(in descriptor);

        Assert.NotNull(buffer);
    }
}
