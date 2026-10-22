namespace Prowl.Graphite.Samples.CubeGrid;


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static FrameGraph graph;
    static float time;


    private static void Main()
    {
        GraphicsDeviceOptions options = new()
        {
            VulkanValidationLayers = false,
        };

        SwapchainDescription swapchain = new()
        {
            SyncToVerticalBlank = false
        };

        DeviceCreateUtilities.CreateWindowAndDevice(Load, Render, Close, options, swapchain);
    }

    public static void Load(GraphicsDevice newDevice)
    {
        tracker = new(newDevice);

        device = newDevice;
        CubeGrid.Create(device);

        graph = new FrameGraph(device, "Sample");
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        time += (float)dt;
        SampleView.Draw(graph, device.MainSwapchain, "Backbuffer", cmd => CubeGrid.Draw(time, cmd));

        tracker.End(dt);
    }


    public static void Close()
    {
        CubeGrid.Dispose();
        graph.Dispose();
        device.Dispose();
    }
}
