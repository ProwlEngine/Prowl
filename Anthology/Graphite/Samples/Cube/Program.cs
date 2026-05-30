using Prowl.Vector;

using Silk.NET.Maths;
using Silk.NET.Windowing;


namespace Prowl.Veldrid.Samples.Cube;


public static class Program
{
    static IWindow window;

    static GraphicsDevice device;
    static CommandBuffer buffer;
    static RenderMSTracker tracker;
    static float time;


    private static void Main()
    {
        WindowOptions woptions = WindowOptions.Default;
        woptions.Title = "My Window";
        woptions.Size = new Vector2D<int>(600, 600);
        woptions.WindowState = WindowState.Normal;
        woptions.VideoMode = VideoMode.Default;
        woptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1));
        woptions.ShouldSwapAutomatically = false;
        window = Window.Create(woptions);

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
        Cube.Create(device);
        buffer = device.ResourceFactory.CreateCommandBuffer();

        window.FramebufferResize += (x) => device.ResizeMainWindow((uint)x.X, (uint)x.Y);
        window.Render += Render;
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        Frame frame = device.BeginFrame();

        buffer.Begin();
        buffer.SetFramebuffer(device.SwapchainFramebuffer);
        buffer.ClearDepthStencil(1, 0);
        buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        Cube.Draw(buffer);
        buffer.End();

        frame.SubmitCommands(buffer);
        device.EndFrame(frame);

        // Explicitly avoid timing SwapBuffers() to not pollute with OS throttling/presentation limits.
        tracker.End(dt);

        device.SwapBuffers();
    }


    public static void Close()
    {
        buffer.Dispose();
        Cube.Dispose();
        device.Dispose();
    }
}
