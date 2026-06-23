using Prowl.Graphite;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace GraphiteExample;

public static class Program
{
    private static GraphicsBackend backend = GraphicsBackend.Vulkan;

    static GraphicsAPI SilkAPI => backend switch
    {
        GraphicsBackend.OpenGL => new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
        GraphicsBackend.OpenGLES => new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 2)),
        GraphicsBackend.Vulkan => new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(2, 1)),
        _ => GraphicsAPI.None
    };


    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            backend = args[0] switch
            {
                "opengl" => GraphicsBackend.OpenGL,
                "opengles" => GraphicsBackend.OpenGLES,
                "vulkan" => GraphicsBackend.Vulkan,
                "d3d11" => GraphicsBackend.Direct3D11,
                _ => throw new Exception("Unknown backend. Must be one of: [opengl, opengles, vulkan, d3d11]")
            };
        }

        WindowOptions windowOptions = new()
        {
            IsVisible = true,
            Title = $"Graphite Quill Demo ({backend})",
            Position = new Vector2D<int>(50, 50),
            Size = new Vector2D<int>(1280, 720),
            WindowState = WindowState.Normal,
            WindowBorder = WindowBorder.Resizable,
            VideoMode = VideoMode.Default,
            API = SilkAPI,
            VSync = false,
            ShouldSwapAutomatically = false
        };

        GraphicsDeviceOptions deviceOptions = new()
        {
            Debug = false,
            SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = false,
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
        };

        // Create and run the window
        using var window = new SilkWindow(windowOptions, deviceOptions, backend);

        window.Run();
    }
}