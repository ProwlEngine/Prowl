using NeoVeldrid.Sdl2;
using NeoVeldrid.StartupUtilities;
using Xunit;

namespace NeoVeldrid.Tests
{
    // Regression tests for specific swapchain-related bugs that have been fixed in NeoVeldrid.
    // Unlike the generic SwapchainTests / MainSwapchainTests infrastructure in SwapchainTests.cs,
    // each test here creates its own device with bug-specific options rather than inheriting a
    // pre-built device from a shared fixture, because the bugs under test are in the device
    // creation path itself.
    public class SwapchainRegressionTests
    {
        // Regression test for a bug where the 2-argument convenience overload of
        // NeoVeldridStartup.CreateVulkanGraphicsDevice hardcoded `colorSrgb = false` instead of
        // passing `options.SwapchainSrgbFormat` through to the 3-argument overload. Because
        // NeoVeldridStartup.CreateGraphicsDevice (the default dispatch from
        // CreateWindowAndGraphicsDevice) routes to the 2-argument overload, every application
        // using the default code path to create a Vulkan device got a non-sRGB swapchain even
        // when they explicitly set GraphicsDeviceOptions.SwapchainSrgbFormat = true.
        //
        // D3D11 (and OpenGL, though not tested here) already honored SwapchainSrgbFormat, so the
        // fix restores parity with the contract the other backends established.
#if TEST_VULKAN
        [Fact]
        [Trait("Backend", "Vulkan")]
        public void CreateWindowAndGraphicsDevice_Vulkan_HonorsSwapchainSrgbFormat()
        {
            AssertMainSwapchainIsSrgb(GraphicsBackend.Vulkan);
        }
#endif

#if TEST_D3D11
        [Fact]
        [Trait("Backend", "D3D11")]
        public void CreateWindowAndGraphicsDevice_D3D11_HonorsSwapchainSrgbFormat()
        {
            AssertMainSwapchainIsSrgb(GraphicsBackend.Direct3D11);
        }
#endif

#if TEST_OPENGL
        [Fact]
        [Trait("Backend", "OpenGL")]
        public void CreateWindowAndGraphicsDevice_OpenGL_HonorsSwapchainSrgbFormat()
        {
            AssertMainSwapchainIsSrgb(GraphicsBackend.OpenGL);
        }
#endif

        private static void AssertMainSwapchainIsSrgb(GraphicsBackend backend)
        {
            WindowCreateInfo wci = new WindowCreateInfo
            {
                WindowWidth = 200,
                WindowHeight = 200,
                WindowInitialState = WindowState.Hidden,
            };

            GraphicsDeviceOptions options = new GraphicsDeviceOptions(
                debug: true,
                swapchainDepthFormat: PixelFormat.R16_UNorm,
                syncToVerticalBlank: false,
                resourceBindingModel: ResourceBindingModel.Default,
                preferDepthRangeZeroToOne: false,
                preferStandardClipSpaceYDirection: false,
                swapchainSrgbFormat: true);

            Sdl2Window window = null;
            GraphicsDevice gd = null;
            try
            {
                NeoVeldridStartup.CreateWindowAndGraphicsDevice(wci, options, backend, out window, out gd);

                PixelFormat colorFormat = gd.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;

                Assert.Equal(PixelFormat.B8_G8_R8_A8_UNorm_SRgb, colorFormat);
            }
            finally
            {
                gd?.Dispose();
                window?.Close();
            }
        }
    }
}
