using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Silk.NET.Windowing;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct SwapchainView : IRenderView
{
    public Swapchain TargetSwapchain { get; }

    public SwapchainView(uint width, uint height, Swapchain swapchain)
    {
        TargetSwapchain = swapchain;
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class ClearSwapchainPass : RasterPass<SwapchainView>
{
    public override string Name => "ClearSwapchain";

    public override void Setup(RenderContextBuilder builder) => SetViewTarget(builder, TargetLoadStoreOps.Clear(Color.Blue));

    public override void Render(RenderContext<SwapchainView> context, CommandBuffer cmd)
    {
        BindTarget(context, cmd);
    }
}

file sealed class DepthSwapchainPass : RasterPass<SwapchainView>
{
    private readonly PixelFormat _depthFormat;

    public DepthSwapchainPass(PixelFormat depthFormat) => _depthFormat = depthFormat;

    public override string Name => "DepthSwapchain";

    public override void Setup(RenderContextBuilder builder)
        => SetViewTarget(builder, TargetLoadStoreOps.Clear(Color.Blue), _depthFormat);

    public override void Render(RenderContext<SwapchainView> context, CommandBuffer cmd)
    {
        BindTarget(context, cmd);
    }
}

file sealed class OffscreenPass : IPass<SwapchainView>
{
    public string Name => "Offscreen";

    public void Setup(RenderContextBuilder builder)
        => builder.DeclareOutputTexture("Offscreen", GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm));

    public void Render(RenderContext<SwapchainView> context, CommandBuffer cmd) { }
}

// Coverage for the main swapchain: the framebuffer it exposes, presentation, and resize. These
// run on the windowed device creators (a headless device has no swapchain).
public abstract class MainSwapchainTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Framebuffer_Targets_HaveExpectedProperties()
    {
        Texture color = GD.MainSwapchain.Framebuffer.ColorTargets[0].Target;
        Assert.Equal(TextureType.Texture2D, color.Type);
        Assert.InRange(color.Width, 1u, uint.MaxValue);
        Assert.InRange(color.Height, 1u, uint.MaxValue);
        Assert.Equal(1u, color.Depth);
        Assert.Equal(1u, color.ArrayLayers);
        Assert.Equal(1u, color.MipLevels);
        Assert.Equal(TextureUsage.RenderTarget, color.Usage);
        Assert.Equal(TextureSampleCount.Count1, color.SampleCount);
        Assert.Null(GD.MainSwapchain.Framebuffer.DepthTarget);
    }

    [Fact]
    public void SwapBuffers_DoesNotThrow()
    {
        ExecutionTask task = GD.BeginExecution();
        GD.CompleteExecution(task);
        GD.SwapBuffers(GD.MainSwapchain);
        GD.WaitForIdle();
    }

    [Fact]
    public void DispatchGraph_PresentsMoreFramesThanSwapchainImages()
    {
        using RenderPipeline<SwapchainView> presenting = new([new ClearSwapchainPass()]);
        using RenderPipeline<SwapchainView> offscreen = new([new OffscreenPass()]);
        SwapchainView[] views = [new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height, GD.MainSwapchain)];

        for (int frame = 0; frame < 12; frame++)
        {
            GD.DispatchGraph(frame % 4 != 3 ? presenting : offscreen, views);
        }

        GD.ResizeMainWindow(128, 96);
        views[0] = new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height, GD.MainSwapchain);

        for (int frame = 0; frame < 12; frame++)
        {
            GD.DispatchGraph(presenting, views);
        }

        GD.WaitForIdle();
    }

    [Fact]
    public void DispatchGraph_ViewTargetDepth_CreatesSwapchainDepth()
    {
        using RenderPipeline<SwapchainView> pipeline = new([new DepthSwapchainPass(PixelFormat.R16_UNorm)]);
        SwapchainView[] views = [new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height, GD.MainSwapchain)];

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Framebuffer fb = GD.MainSwapchain.Framebuffer;
        Assert.NotNull(fb.DepthTarget);
        Texture depth = fb.DepthTarget.Value.Target;
        Assert.Equal(PixelFormat.R16_UNorm, depth.Format);
        Assert.Equal(fb.ColorTargets[0].Target.Width, depth.Width);
        Assert.Equal(fb.ColorTargets[0].Target.Height, depth.Height);
        Assert.Equal(PixelFormat.R16_UNorm, fb.OutputDescription.DepthFormat);
    }

    [Fact]
    public void DispatchGraph_ViewTargetDepth_SurvivesResizeAndFormatChange()
    {
        using RenderPipeline<SwapchainView> shallow = new([new DepthSwapchainPass(PixelFormat.R16_UNorm)]);
        using RenderPipeline<SwapchainView> deep = new([new DepthSwapchainPass(PixelFormat.R32_Float)]);
        SwapchainView[] views = [new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height, GD.MainSwapchain)];

        for (int frame = 0; frame < 6; frame++)
            GD.DispatchGraph(shallow, views);

        GD.DispatchGraph(deep, views);
        Assert.Equal(PixelFormat.R32_Float, GD.MainSwapchain.Framebuffer.DepthTarget!.Value.Target.Format);

        GD.ResizeMainWindow(128, 96);
        views[0] = new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height, GD.MainSwapchain);
        for (int frame = 0; frame < 6; frame++)
            GD.DispatchGraph(deep, views);

        Texture depth = GD.MainSwapchain.Framebuffer.DepthTarget!.Value.Target;
        Assert.Equal(GD.MainSwapchain.Framebuffer.Width, depth.Width);
        GD.WaitForIdle();
    }

    [Fact]
    public void Resize_KeepsFramebufferValid()
    {
        // The presented surface clamps to the backing window, so the exact dimensions are
        // platform-dependent; the contract under test is that resize is honored without throwing
        // and the framebuffer stays usable.
        GD.ResizeMainWindow(128, 96);
        Assert.InRange(GD.MainSwapchain.Framebuffer.Width, 1u, uint.MaxValue);
        Assert.InRange(GD.MainSwapchain.Framebuffer.Height, 1u, uint.MaxValue);

        ExecutionTask task = GD.BeginExecution();
        GD.CompleteExecution(task);
        GD.SwapBuffers(GD.MainSwapchain);
        GD.WaitForIdle();
    }
}

// Regression coverage for device creation honoring SwapchainDescription.ColorSrgb.
// Each test stands up its own windowed device because the behavior under test is in the device
// creation path. See the original bug: the Vulkan convenience path hardcoded colorSrgb = false.
public class SwapchainRegressionTests
{
#if TEST_VULKAN
    [Fact]
    [Trait("Backend", "Vulkan")]
    public void Create_Vulkan_HonorsSwapchainSrgbFormat() => AssertMainSwapchainIsSrgb(GraphicsBackend.Vulkan);
#endif

    private static void AssertMainSwapchainIsSrgb(GraphicsBackend backend)
    {
        GraphicsDeviceOptions options = new(true);
        SwapchainDescription swapchain = new()
        {
            ColorSrgb = true,
        };

        IWindow window = TestUtils.CreateWindow(backend);
        GraphicsDevice gd = null;
        try
        {
            gd = TestUtils.CreateDevice(window, options, swapchain, backend);
            PixelFormat colorFormat = gd.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;
            Assert.Contains("SRgb", colorFormat.ToString());
        }
        finally
        {
            gd?.Dispose();
            window.Dispose();
        }
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanMainSwapchainTests : MainSwapchainTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
