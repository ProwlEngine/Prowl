using Prowl.Vector;

using Silk.NET.Windowing;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the main swapchain: the framebuffer it exposes, presentation, and resize. These
// run on the windowed device creators (a headless device has no swapchain).
public abstract class MainSwapchainTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private ExecutionTask Present(FrameGraph graph, PixelFormat? depthFormat = null)
    {
        TextureHandle target = graph.ImportTarget(GD.MainSwapchain.Framebuffer, depthFormat);
        graph.AddPass("ClearSwapchain", pass => pass.Write(target, firstWrite: TargetLoadStoreOps.Clear(Color.Blue)), _ => { });
        return graph.Execute();
    }

    private ExecutionTask Offscreen(FrameGraph graph)
    {
        Framebuffer fb = GD.MainSwapchain.Framebuffer;
        TextureHandle texture = graph.Create("Offscreen", GraphTextureDesc.Sized((int)fb.Width, (int)fb.Height, PixelFormat.R8_G8_B8_A8_UNorm));
        graph.AddPass("Offscreen", pass => pass.Write(texture), _ => { });
        return graph.Execute();
    }

    [Fact]
    public void Execute_PresentsMoreFramesThanSwapchainImages()
    {
        using FrameGraph graph = new(GD, "Test");
        for (int frame = 0; frame < 12; frame++)
        {
            if (frame % 4 != 3)
                Present(graph);
            else
                Offscreen(graph);
        }

        GD.ResizeMainWindow(128, 96);
        for (int frame = 0; frame < 12; frame++)
            Present(graph);

        GD.WaitForIdle();
    }

    [Fact]
    public void ImportTarget_WithDepthFormat_CreatesSwapchainDepth()
    {
        using FrameGraph graph = new(GD, "Test");
        Present(graph, PixelFormat.R16_UNorm);
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
    public void ImportTarget_WithDepthFormat_SurvivesResizeAndFormatChange()
    {
        using FrameGraph graph = new(GD, "Test");
        for (int frame = 0; frame < 6; frame++)
            Present(graph, PixelFormat.R16_UNorm);

        Present(graph, PixelFormat.R32_Float);
        Assert.Equal(PixelFormat.R32_Float, GD.MainSwapchain.Framebuffer.DepthTarget!.Value.Target.Format);

        GD.ResizeMainWindow(128, 96);
        for (int frame = 0; frame < 6; frame++)
            Present(graph, PixelFormat.R32_Float);

        Texture depth = GD.MainSwapchain.Framebuffer.DepthTarget!.Value.Target;
        Assert.Equal(GD.MainSwapchain.Framebuffer.Width, depth.Width);
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
