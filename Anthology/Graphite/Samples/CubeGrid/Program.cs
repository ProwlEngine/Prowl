using Prowl.Vector;


namespace Prowl.Veldrid.Samples.CubeGrid;


public static class Program
{
    static GraphicsDevice device;
    static CommandBuffer buffer;
    static RenderMSTracker tracker;
    static float time;


    private static void Main()
    {
        GraphicsDeviceOptions options = new()
        {
            Debug = false,
            SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = false,
            PreferStandardClipSpaceYDirection = true
        };

        DeviceCreateUtilities.CreateWindowAndDevice(Load, Render, Close, options);
    }

    public static void Load(GraphicsDevice newDevice)
    {
        tracker = new(newDevice);

        device = newDevice;
        buffer = device.ResourceFactory.CreateCommandBuffer();

        CubeGrid.Create(device);
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        Frame frame = device.BeginFrame();

        buffer.Begin();
        buffer.SetFramebuffer(device.SwapchainFramebuffer);
        buffer.ClearDepthStencil(1, 0);
        buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        CubeGrid.Draw(time += (float)dt, buffer);
        buffer.End();

        frame.SubmitCommands(buffer);
        device.EndFrame(frame);

        device.SwapBuffers();

        tracker.End(dt);
    }


    public static void Close()
    {
        CubeGrid.Dispose();

        buffer.Dispose();
        device.Dispose();
    }
}
