using System;


using Silk.NET;
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;


namespace Prowl.Veldrid.Samples;


public static class DeviceCreateUtilities
{
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new Exception("Cannot create graphics device with an uninitialized window!");

        switch (backend)
        {
            case GraphicsBackend.OpenGLES:
            case GraphicsBackend.OpenGL:
                if (window.API.API != ContextAPI.OpenGLES && window.API.API != ContextAPI.OpenGL)
                    throw new Exception("Attempted to make a GL graphics device without an available GL or GLES context");

                OpenGL.OpenGLPlatformInfo glInfo = new OpenGL.OpenGLPlatformInfo(
                    glContext: window.GLContext!,
                    setSyncToVerticalBlank: sync =>
                    {
                        window.VSync = sync;
                        window.GLContext!.SwapInterval(window.VSync ? 1 : 0);
                    });

                return GraphicsDevice.CreateOpenGL(options, glInfo, (uint)window.Size.X, (uint)window.Size.Y);

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

                return GraphicsDevice.CreateD3D11(options, d3dOptions, desc);

            case GraphicsBackend.Vulkan:
                if (window.API.API != ContextAPI.Vulkan)
                    throw new Exception("Attempted to make a Vulkan graphics device without an available Vulkan API");

                VulkanDeviceOptions vkOptions = default;
                SwapchainDescription vkDescription = new()
                {
                    DepthFormat = options.SwapchainDepthFormat,
                    ColorSrgb = options.SwapchainSrgbFormat,
                    Width = (uint)window.Size.X,
                    Height = (uint)window.Size.Y,
                    SyncToVerticalBlank = options.SyncToVerticalBlank,
                    Source = SwapchainSource.CreateVulkan(window.VkSurface!)
                };

                return GraphicsDevice.CreateVulkan(options, vkDescription, vkOptions);
        }

        throw new Exception($"Unsupported graphics backend: {backend}");
    }
}
