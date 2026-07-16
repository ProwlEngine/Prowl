// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite;

using Silk.NET.Windowing;

namespace Prowl.Runtime;

public static class DeviceCreateUtilities
{
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new Exception("Cannot create graphics device with an uninitialized window!");

        if (window.ShouldSwapAutomatically)
            throw new Exception("Window was created with 'ShouldSwapAutomatically'. This is not allowed");

        GraphicsDevice device;

        switch (backend)
        {
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
        if (device.MainSwapchain != null)
            device.MainSwapchain.Name = "Main Swapchain";
        window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);

        return device;
    }
}
