using System.Runtime.InteropServices;
using Prowl.Graphite;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Windowing.Sdl;


namespace GraphiteExample;


public static class DeviceCreateUtilities
{
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new Exception("Cannot create graphics device with an uninitialized window!");

        if (window.ShouldSwapAutomatically)
            throw new Exception("Window was created with 'ShouldSwapAutomatically'. This is not allowed");

        GraphicsDevice? device;

        switch (backend)
        {
            case GraphicsBackend.Direct3D11:
                if (window.Native!.Win32 == null)
                    throw new Exception("Attempted to make a D3D11 graphics device without a Win32 window!");

                (nint Hwnd, nint HDC, nint HInstance) = window.Native!.Win32!.Value;

                D3D11DeviceOptions d3dOptions = default;

                SwapchainDescription desc = new()
                {
                    DepthFormat = options.SwapchainDepthFormat,
                    ColorSrgb = options.SwapchainSrgbFormat,
                    Width = (uint)window.FramebufferSize.X,
                    Height = (uint)window.FramebufferSize.Y,
                    SyncToVerticalBlank = options.SyncToVerticalBlank,
                    Source = SwapchainSource.CreateWin32(Hwnd, HInstance)
                };

                device = GraphicsDevice.CreateD3D11(options, d3dOptions, desc);
                break;

            case GraphicsBackend.Vulkan:
                if (window.API.API != ContextAPI.Vulkan)
                    throw new Exception("Attempted to make a Vulkan graphics device without an available Vulkan API");

                VulkanDeviceOptions vkOptions = default;
                SwapchainDescription vkDescription = new()
                {
                    DepthFormat = options.SwapchainDepthFormat,
                    ColorSrgb = options.SwapchainSrgbFormat,
                    Width = (uint)window.FramebufferSize.X,
                    Height = (uint)window.FramebufferSize.Y,
                    SyncToVerticalBlank = options.SyncToVerticalBlank,
                    Source = SwapchainSource.CreateVulkan(window.VkSurface!)
                };

                device = GraphicsDevice.CreateVulkan(options, vkDescription, vkOptions);
                break;

            default:
                throw new Exception($"Unsupported graphics backend: {backend}");
        }

        device.SyncToVerticalBlank = options.SyncToVerticalBlank;
        window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);

        return device;
    }
}