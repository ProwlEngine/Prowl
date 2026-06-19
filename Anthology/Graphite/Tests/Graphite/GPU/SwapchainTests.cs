using Silk.NET.Windowing;

using Xunit;

namespace Prowl.Graphite.Tests;

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
        Frame frame = GD.BeginFrame();
        GD.EndFrame(frame);
        GD.SwapBuffers();
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

        Frame frame = GD.BeginFrame();
        GD.EndFrame(frame);
        GD.SwapBuffers();
        GD.WaitForIdle();
    }
}

// Regression coverage for device creation honoring GraphicsDeviceOptions.SwapchainSrgbFormat.
// Each test stands up its own windowed device because the behavior under test is in the device
// creation path. See the original bug: the Vulkan convenience path hardcoded colorSrgb = false.
public class SwapchainRegressionTests
{
#if TEST_VULKAN
    [Fact]
    [Trait("Backend", "Vulkan")]
    public void Create_Vulkan_HonorsSwapchainSrgbFormat() => AssertMainSwapchainIsSrgb(GraphicsBackend.Vulkan);
#endif
#if TEST_D3D11
    [Fact]
    [Trait("Backend", "D3D11")]
    public void Create_D3D11_HonorsSwapchainSrgbFormat() => AssertMainSwapchainIsSrgb(GraphicsBackend.Direct3D11);
#endif
#if TEST_OPENGL
    [Fact]
    [Trait("Backend", "OpenGL")]
    public void Create_OpenGL_HonorsSwapchainSrgbFormat() => AssertMainSwapchainIsSrgb(GraphicsBackend.OpenGL);
#endif

    private static void AssertMainSwapchainIsSrgb(GraphicsBackend backend)
    {
        GraphicsDeviceOptions options = new(true, PixelFormat.R16_UNorm, false)
        {
            SwapchainSrgbFormat = true,
        };

        IWindow window = TestUtils.CreateWindow(backend);
        GraphicsDevice? gd = null;
        try
        {
            gd = TestUtils.CreateDevice(window, options, backend);
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
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11MainSwapchainTests : MainSwapchainTests<D3D11DeviceCreatorWithMainSwapchain> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLMainSwapchainTests : MainSwapchainTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
[Collection("GPU Tests")]
public class OpenGLESMainSwapchainTests : MainSwapchainTests<OpenGLESDeviceCreator> { }
#endif
