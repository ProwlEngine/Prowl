// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Xunit;

using Prowl.Runtime.Graphite;
using Prowl.Runtime.Graphite.OpenGL;
using Prowl.Vector;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class CommandListTests
{
    private readonly GraphiteTestFixture _fixture;

    private const string VertexShaderSource = @"
#version 430 core
layout(location = 0) in vec3 aPosition;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
}
";

    private const string FragmentShaderSource = @"
#version 430 core
out vec4 FragColor;

void main()
{
    FragColor = vec4(1.0, 0.0, 0.0, 1.0);
}
";

    public CommandListTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateCommandList_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_BeginEnd_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        // Should complete without error
        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_SubmitEmpty_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        // Should complete without error
        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_BeginRenderPass_ToSwapchain_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = _fixture.Device.GetSwapchainTexture(),
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(0.1f, 0.2f, 0.3f, 1.0f)
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_BeginRenderPass_ToTexture_Succeeds()
    {
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        });

        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = new Float4(1.0f, 0.0f, 0.0f, 1.0f)
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_BeginRenderPass_WithDepthStencil_Succeeds()
    {
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var depthTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                Texture = depthTarget,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                StencilLoadOp = LoadOp.Clear,
                StencilStoreOp = StoreOp.Store,
                StencilClearValue = 0
            }
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_SetViewport_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 512,
            Height = 512,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.SetViewport(0, 0, 256, 256, 0, 1);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_SetScissor_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 512,
            Height = 512,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.SetScissor(0, 0, 128, 128);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_Draw_WithPipeline_Succeeds()
    {
        using var vertexShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.VertexGLSL(VertexShaderSource));
        using var fragmentShader = _fixture.Device.CreateShaderModule(
            ShaderModuleDescriptor.FragmentGLSL(FragmentShaderSource));
        using var pipeline = _fixture.Device.CreatePipelineState(new PipelineStateDescriptor
        {
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Topology = PrimitiveTopology.TriangleList
        });
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);
        cmd.SetPipeline(pipeline);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_CopyBufferToBuffer_Succeeds()
    {
        var srcData = new float[] { 1, 2, 3, 4 };
        using var srcBuffer = _fixture.Device.CreateBuffer<float>(
            BufferUsage.CopySource,
            srcData.AsSpan(),
            MemoryAccess.CpuToGpu);

        using var dstBuffer = _fixture.Device.CreateBuffer(new BufferDescriptor
        {
            SizeInBytes = 16,
            Usage = BufferUsage.CopyDestination | BufferUsage.Vertex,
            MemoryAccess = MemoryAccess.GpuOnly
        });

        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.CopyBufferToBuffer(srcBuffer, 0, dstBuffer, 0, 16);
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_NotRecording_ThrowsOnCommands()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        // Should throw because we haven't called Begin()
        Assert.Throws<InvalidOperationException>(() =>
        {
            cmd.SetViewport(0, 0, 100, 100, 0, 1);
        });
    }

    [Fact]
    public void CommandList_NestedRenderPass_ThrowsException()
    {
        using var colorTarget = _fixture.Device.CreateTexture(new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        });

        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    Texture = colorTarget,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearColor = Float4.Zero
                }
            ]
        };

        cmd.BeginRenderPass(in renderPassDesc);

        // Should throw because we're already in a render pass
        Assert.Throws<InvalidOperationException>(() =>
        {
            cmd.BeginRenderPass(in renderPassDesc);
        });
    }

    [Fact]
    public void CommandList_Fence_Succeeds()
    {
        using var fence = _fixture.Device.CreateFence(false);
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd, fence);

        // Wait for completion
        fence.Wait();

        Assert.True(fence.IsSignaled);
    }

    [Fact]
    public void CommandList_MemoryBarrier_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.MemoryBarrier();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }

    [Fact]
    public void CommandList_DebugMarkers_Succeeds()
    {
        using var cmd = _fixture.Device.CreateCommandList();

        cmd.Begin();
        cmd.PushDebugGroup("TestGroup");
        cmd.InsertDebugMarker("TestMarker");
        cmd.PopDebugGroup();
        cmd.End();

        _fixture.Device.SubmitCommands(cmd);

        Assert.NotNull(cmd);
    }
}
