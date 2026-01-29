// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using Prowl.Runtime.Graphite;
using Prowl.Runtime.Graphite.OpenGL;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class BindGroupTests
{
    private readonly GraphiteTestFixture _fixture;

    public BindGroupTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateBindGroupLayout_UniformBuffer_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0, ShaderStage.Vertex | ShaderStage.Fragment)
        );

        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        Assert.NotNull(layout);
    }

    [Fact]
    public void CreateBindGroupLayout_StorageBuffer_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageBuffer(0, ShaderStage.Compute)
        );

        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        Assert.NotNull(layout);
    }

    [Fact]
    public void CreateBindGroupLayout_CombinedTextureSampler_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.CombinedTextureSampler(0, ShaderStage.Fragment)
        );

        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        Assert.NotNull(layout);
    }

    [Fact]
    public void CreateBindGroupLayout_MultipleEntries_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0, ShaderStage.AllGraphics),
            BindGroupLayoutEntry.CombinedTextureSampler(1, ShaderStage.Fragment),
            BindGroupLayoutEntry.CombinedTextureSampler(2, ShaderStage.Fragment),
            BindGroupLayoutEntry.StorageBuffer(3, ShaderStage.Fragment, readOnly: true)
        );

        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        Assert.NotNull(layout);
    }

    [Fact]
    public void CreateBindGroup_WithUniformBuffer_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        using var uniformBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 256,
            Usage = BufferUsage.Uniform,
            MemoryAccess = MemoryAccess.CpuToGpu
        });

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForBuffer(0, uniformBuffer)
        );

        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        Assert.NotNull(bindGroup);
    }

    [Fact]
    public void CreateBindGroup_WithTextureSampler_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.CombinedTextureSampler(0, ShaderStage.Fragment)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 64,
            Height = 64,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        });

        using var sampler = _fixture.Device.CreateSampler(new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Repeat,
            AddressModeV = TextureAddressMode.Repeat,
            AddressModeW = TextureAddressMode.Repeat
        });

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForTextureSampler(0, texture, sampler)
        );

        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        Assert.NotNull(bindGroup);
    }

    [Fact]
    public void CreateBindGroup_WithStorageTexture_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.StorageTexture(0, ShaderStage.Compute)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        using var texture = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 64,
            Height = 64,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Storage,
            SampleCount = SampleCount.Count1
        });

        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForTexture(0, texture)
        );

        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        Assert.NotNull(bindGroup);
    }

    [Fact]
    public void CreateBindGroup_WithBufferRange_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        using var uniformBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 1024,
            Usage = BufferUsage.Uniform,
            MemoryAccess = MemoryAccess.CpuToGpu
        });

        // Bind only a portion of the buffer
        var bindGroupDesc = new BindGroupDescriptor(
            layout,
            BindGroupEntry.ForBuffer(0, uniformBuffer, offset: 256, size: 256)
        );

        using var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        Assert.NotNull(bindGroup);
    }

    [Fact]
    public void CreateBindGroupLayout_WithDynamicOffset_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0, ShaderStage.Vertex, hasDynamicOffset: true)
        );

        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        Assert.NotNull(layout);
    }

    [Fact]
    public void BindGroup_Dispose_CleansUpResources()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0)
        );
        using var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        using var uniformBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 256,
            Usage = BufferUsage.Uniform,
            MemoryAccess = MemoryAccess.CpuToGpu
        });

        var bindGroupDesc = new BindGroupDescriptor(layout, BindGroupEntry.ForBuffer(0, uniformBuffer));

        var bindGroup = _fixture.Device.CreateBindGroup(in bindGroupDesc);

        // BindGroup doesn't own GPU resources directly in OpenGL, but should not throw
        bindGroup.Dispose();

        Assert.NotNull(bindGroup);
    }

    [Fact]
    public void BindGroupLayout_Dispose_Succeeds()
    {
        var layoutDesc = new BindGroupLayoutDescriptor(
            BindGroupLayoutEntry.UniformBuffer(0)
        );

        var layout = _fixture.Device.CreateBindGroupLayout(in layoutDesc);

        // Layout doesn't own GPU resources in OpenGL
        layout.Dispose();

        Assert.NotNull(layout);
    }
}
