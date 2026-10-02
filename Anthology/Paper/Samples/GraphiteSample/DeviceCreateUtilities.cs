using System.Runtime.InteropServices;
using Prowl.Graphite;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Windowing.Sdl;


namespace GraphiteSample;


public static class DeviceCreateUtilities
{
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, SwapchainDescription swapchain, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new Exception("Cannot create graphics device with an uninitialized window!");

        if (window.ShouldSwapAutomatically)
            throw new Exception("Window was created with 'ShouldSwapAutomatically'. This is not allowed");

        GraphicsDevice? device;

        switch (backend)
        {
            case GraphicsBackend.Vulkan:
                if (window.API.API != ContextAPI.Vulkan)
                    throw new Exception("Attempted to make a Vulkan graphics device without an available Vulkan API");

                VulkanDeviceOptions vkOptions = default;
                SwapchainDescription vkDescription = swapchain;
                vkDescription.Width = (uint)window.FramebufferSize.X;
                vkDescription.Height = (uint)window.FramebufferSize.Y;
                vkDescription.Source = SwapchainSource.CreateVulkan(window.VkSurface!);

                device = GraphicsDevice.CreateVulkan(options, vkDescription, vkOptions);
                break;

            default:
                throw new Exception($"Unsupported graphics backend: {backend}");
        }

        device.SyncToVerticalBlank = swapchain.SyncToVerticalBlank;
        window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);

        return device;
    }
}