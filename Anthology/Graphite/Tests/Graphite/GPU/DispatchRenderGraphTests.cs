#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct DispatchView : IRenderView
{
    public Framebuffer? Target { get; }

    public DispatchView(uint width, uint height, Swapchain? swapchain = null, Framebuffer? framebuffer = null)
    {
        PixelWidth = width;
        PixelHeight = height;
        Target = framebuffer ?? swapchain?.Framebuffer;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class RecordingPass : IPass<DispatchView>
{
    public int RenderCount => ViewWidths.Count;
    public List<uint> ViewWidths { get; } = new();

    public string Name => "Recording";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<DispatchView> context, CommandBuffer cmd) => ViewWidths.Add(context.View.PixelWidth);
}

file sealed class BackbufferPass : IPass<DispatchView>
{
    private TextureHandle _backbuffer;

    public int RenderCount { get; private set; }
    public bool SawFramebuffer { get; private set; }
    public Framebuffer? Resolved { get; private set; }

    public string Name => "Backbuffer";

    public void Setup(RenderContextBuilder builder) => _backbuffer = builder.DeclareViewTarget();

    public void Render(RenderContext<DispatchView> context, CommandBuffer cmd)
    {
        RenderCount++;
        Resolved = context.GetRenderTexture(_backbuffer).Framebuffer;
        SawFramebuffer = Resolved != null;
        cmd.SetFramebuffer(Resolved!, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(0.25f, 0.5f, 0.75f, 1f)), AttachmentOps.Loaded));
    }
}

public abstract class DispatchRenderGraphTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_RunsEachPassOncePerView()
    {
        RecordingPass passA = new();
        RecordingPass passB = new();
        using RenderPipeline<DispatchView> pipeline = new([passA, passB]);
        DispatchView[] views = { new(64, 64), new(80, 48), new(32, 32) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(new uint[] { 64, 80, 32 }, passA.ViewWidths);
        Assert.Equal(new uint[] { 64, 80, 32 }, passB.ViewWidths);
    }

    [Fact]
    public void Dispatch_ViewWithNoTarget_SkipsViewTargetPass()
    {
        BackbufferPass targetPass = new();
        RecordingPass other = new();
        using RenderPipeline<DispatchView> pipeline = new([other, targetPass]);

        GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.Equal(0, targetPass.RenderCount);
        Assert.Equal(1, other.RenderCount);
    }

    [Fact]
    public void Dispatch_FramebufferTarget_PassDrawsIntoIt()
    {
        Texture color = RF.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Framebuffer target = RF.CreateFramebuffer(new FramebufferDescription(null, color));
        BackbufferPass pass = new();
        using RenderPipeline<DispatchView> pipeline = new([pass]);

        GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64, framebuffer: target) });
        GD.WaitForIdle();

        Assert.Equal(1, pass.RenderCount);
        Assert.Same(target, pass.Resolved);
    }
}

public abstract class DispatchRenderGraphPresentTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_ViewTargetPass_ResolvesSwapchainAndPresents()
    {
        BackbufferPass pass = new();
        using RenderPipeline<DispatchView> pipeline = new([new RecordingPass(), pass]);
        DispatchView[] views = { new(64, 64, GD.MainSwapchain) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(1, pass.RenderCount);
        Assert.True(pass.SawFramebuffer);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDispatchRenderGraphTests : DispatchRenderGraphTests<VulkanDeviceCreator> { }

[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDispatchRenderGraphPresentTests : DispatchRenderGraphPresentTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
