using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Silk.NET.Windowing;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct SwapchainView : IRenderView
{
    public SwapchainView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class ClearSwapchainPresentPass : IPresentPass<SwapchainView>
{
    public bool PresentThisFrame { get; set; } = true;

    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<SwapchainView> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("ClearSwapchain");
        cmd.SetFramebuffer(target);
        cmd.ClearColorTarget(0, Color.Blue);
        context.SubmitCommandBuffer(cmd);

        if (PresentThisFrame)
            context.Present();
    }
}

file sealed class ClearSwapchainPipeline : RenderPipeline<SwapchainView>
{
    public ClearSwapchainPresentPass PresentStep { get; } = new();

    protected override void InitializePasses() => SetPresentPass(PresentStep);
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

        // The test swapchain is created with an R16_UNorm depth format.
        Assert.NotNull(GD.MainSwapchain.Framebuffer.DepthTarget);
        Texture depth = GD.MainSwapchain.Framebuffer.DepthTarget.Value.Target;
        Assert.Equal(color.Width, depth.Width);
        Assert.Equal(color.Height, depth.Height);
        Assert.Equal(TextureUsage.DepthStencil, depth.Usage);
    }

    [Fact]
    public void SwapBuffers_DoesNotThrow()
    {
        ExecutionTask task = GD.BeginExecution();
        GD.CompleteExecution(task);
        GD.SwapBuffers();
        GD.WaitForIdle();
    }

    [Fact]
    public void DispatchGraph_PresentsMoreFramesThanSwapchainImages()
    {
        using ClearSwapchainPipeline pipeline = new();
        SwapchainView[] views = [new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height)];

        for (int frame = 0; frame < 12; frame++)
        {
            pipeline.PresentStep.PresentThisFrame = frame % 4 != 3;
            GD.DispatchGraph(pipeline, views);
        }

        GD.ResizeMainWindow(128, 96);
        views[0] = new SwapchainView(GD.MainSwapchain.Framebuffer.Width, GD.MainSwapchain.Framebuffer.Height);

        for (int frame = 0; frame < 12; frame++)
        {
            pipeline.PresentStep.PresentThisFrame = true;
            GD.DispatchGraph(pipeline, views);
        }

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
        GD.SwapBuffers();
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
            DepthFormat = PixelFormat.R16_UNorm,
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
