namespace Prowl.Graphite.Samples.Cube;


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static FrameGraph graph;


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
        device = newDevice;

        tracker = new(newDevice);
        Cube.Create(device);

        graph = new FrameGraph(device, "Sample");
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        SampleView.Draw(graph, device.MainSwapchain, "Backbuffer", Cube.Draw);
        tracker.End(dt);
    }


    public static void Close()
    {
        graph.Dispose();
        Cube.Dispose();
        device.Dispose();
    }
}
