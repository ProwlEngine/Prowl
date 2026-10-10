using System;
using System.IO;
using System.Runtime.InteropServices;

using Silk.NET;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;


namespace Prowl.Graphite.Samples;


public static class DeviceCreateUtilities
{
    private static readonly (GraphicsBackend backend, APIVersion version) Backend = (GraphicsBackend.Vulkan, new APIVersion(2, 1));
    static readonly ContextAPI API = ContextAPI.Vulkan;


    public static IWindow CreateWindowAndDevice(Action<GraphicsDevice> load, Action<double> render, Action close, GraphicsDeviceOptions options, SwapchainDescription swapchain)
    {
        InitializeWindowing();

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
            GraphicsDevice device = CreateDevice(window, options, swapchain, Backend.backend);

            window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);

            load.Invoke(device);
        };

        window.Render += render;
        window.Closing += close;

        window.Run();

        return window;
    }

    // Call this before Window.Create when creating a window without CreateWindowAndDevice.
    public static void InitializeWindowing()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || Backend.backend != GraphicsBackend.Vulkan)
            return;

        GlfwMoltenVKWorkaround();
    }

    private delegate void GlfwInitVulkanLoader(IntPtr vkGetInstanceProcAddr);

    // GLFW 3.4 on macOS only looks for libvulkan.1.dylib (the Vulkan SDK loader), so it can't find the
    // MoltenVK that Silk.NET ships. glfwInitVulkanLoader lets us hand it MoltenVK's vkGetInstanceProcAddr
    // directly, but Silk.NET doesn't bind it, so it's resolved by hand. Must run before glfwInit.
    private static void GlfwMoltenVKWorkaround()
    {
        IntPtr moltenVK = NativeLibrary.Load(Path.Join(AppContext.BaseDirectory, "runtimes/osx/native/libMoltenVK.dylib"));
        IntPtr getInstanceProcAddr = NativeLibrary.GetExport(moltenVK, "vkGetInstanceProcAddr");

        Silk.NET.GLFW.Glfw glfw = Silk.NET.GLFW.Glfw.GetApi();
        IntPtr initVulkanLoader = glfw.Context.GetProcAddress("glfwInitVulkanLoader");
        Marshal.GetDelegateForFunctionPointer<GlfwInitVulkanLoader>(initVulkanLoader)(getInstanceProcAddr);
    }

    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, SwapchainDescription swapchain, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new Exception("Cannot create graphics device with an uninitialized window!");

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

                return GraphicsDevice.CreateVulkan(options, vkDescription, vkOptions);
        }

        throw new Exception($"Unsupported graphics backend: {backend}");
    }
}
