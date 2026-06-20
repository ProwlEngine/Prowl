using System;
using System.IO;
using System.Runtime.InteropServices;

using Silk.NET;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;


namespace Prowl.Graphite.Samples;


public static class DeviceCreateUtilities
{
    private static readonly (GraphicsBackend backend, APIVersion version) Backend = (GraphicsBackend.OpenGL, new APIVersion(4, 5));
    static readonly ContextAPI API = Backend.backend == GraphicsBackend.OpenGL ? ContextAPI.OpenGL : ContextAPI.Vulkan;


    public static IWindow CreateWindowAndDevice(Action<GraphicsDevice> load, Action<double> render, Action close, GraphicsDeviceOptions options)
    {
        MoltenVKMacWorkaround();

        WindowOptions woptions = WindowOptions.Default;
        woptions.Title = "My Window";
        woptions.Size = new Vector2D<int>(600, 600);
        woptions.WindowState = WindowState.Normal;
        woptions.VideoMode = VideoMode.Default;
        woptions.API = new GraphicsAPI(API, ContextProfile.Core, ContextFlags.ForwardCompatible, Backend.version);
        woptions.ShouldSwapAutomatically = false;

        IWindow window = Silk.NET.Windowing.Window.Create(woptions);

        window.Load += () =>
        {
            GraphicsDevice device = CreateDevice(window, options, Backend.backend);
            device.SyncToVerticalBlank = options.SyncToVerticalBlank;

            window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);

            load.Invoke(device);
        };

        window.Render += render;
        window.Closing += close;

        window.Run();

        return window;
    }

    // Some idiot didn't configure MoltenVK correctly in Silk.NET so SDL can't find it
    // Workaround involves loading the library at the correct path manually.
    private static void MoltenVKMacWorkaround()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || Backend.backend != GraphicsBackend.Vulkan)
            return;

        SdlWindowing.RegisterPlatform();
        SdlWindowing.Use();

        Sdl? sdl = Sdl.GetApi();

        if (sdl.Init(Sdl.InitVideo) != 0)
            Console.WriteLine($"SDL video initialization failed: {sdl.GetErrorS()}");

        string basePath = Environment.ProcessPath != null ? AppContext.BaseDirectory :
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        string libraryPath = Path.Join(basePath, "runtimes/osx/native/libMoltenVK.dylib");

        if (sdl.VulkanLoadLibrary(libraryPath) != 0)
            Console.WriteLine($"SDL VulkanLoadLibrary failed for '{libraryPath}': {sdl.GetErrorS()}");
    }

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

                OpenGL.OpenGLPlatformInfo glInfo = new(
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
                    Width = (uint)window.FramebufferSize.X,
                    Height = (uint)window.FramebufferSize.Y,
                    SyncToVerticalBlank = options.SyncToVerticalBlank,
                    Source = SwapchainSource.CreateVulkan(window.VkSurface!)
                };

                return GraphicsDevice.CreateVulkan(options, vkDescription, vkOptions);
        }

        throw new Exception($"Unsupported graphics backend: {backend}");
    }
}
