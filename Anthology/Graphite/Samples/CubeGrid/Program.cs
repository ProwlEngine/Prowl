using Prowl.Vector;

using Silk.NET.Windowing;
using Silk.NET.Maths;

using System.IO;


namespace Prowl.Veldrid.Samples.CubeGrid;


public static class Program
{
    static IWindow window;

    static GraphicsDevice device;
    static CommandBuffer buffer;
    static RenderMSTracker tracker;
    static float time;


    private static void Main()
    {
        Window.PrioritizeSdl();

        WindowOptions woptions = WindowOptions.Default;
        woptions.Title = "My Window";
        woptions.Size = new Vector2D<int>(600, 600);
        woptions.WindowState = WindowState.Normal;
        woptions.VideoMode = VideoMode.Default;
        woptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1));
        woptions.ShouldSwapAutomatically = false;
        window = Window.Create(woptions);

        var sdl = Silk.NET.SDL.Sdl.GetApi();
        string basePath = System.Environment.ProcessPath;
        sdl.VulkanLoadLibrary(Path.Join(basePath, "vulkan/path/relative/to/your/exe/dll.dylib"));

        window.Load += Load;
        window.Closing += Close;

        window.Run();
    }

    public static void Load()
    {
        GraphicsDeviceOptions options = new(
            debug: false,
            swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
            syncToVerticalBlank: true);

        device = DeviceCreateUtilities.CreateDevice(window, options, GraphicsBackend.OpenGL);
        device.SyncToVerticalBlank = false;

        tracker = new();
        CubeGrid.Create(device);
        buffer = device.ResourceFactory.CreateCommandBuffer();

        window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);
        window.Render += Render;
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        buffer.Begin();
        buffer.SetFramebuffer(device.SwapchainFramebuffer);
        buffer.ClearDepthStencil(1, 0);
        buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        CubeGrid.Draw(time += (float)dt, buffer);
        buffer.End();

        Frame frame = device.BeginFrame();
        frame.SubmitCommands(buffer);
        device.EndFrame(frame);

        device.SwapBuffers();

        tracker.End(dt);
    }


    public static void Close()
    {
        buffer.Dispose();
        CubeGrid.Dispose();
        device.Dispose();
    }
}
