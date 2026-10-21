using Prowl.Vector;


namespace Prowl.Graphite.Samples.Cube;


// One draw, no dependencies between passes: the pass clears and draws straight into the backbuffer.
internal sealed class CubePass : InstanceRasterPass
{
    public override string Name => "Backbuffer";

    public override GraphKind Kind => SampleView.Kind;

    public override void Setup(InstanceContextBuilder builder) => SetInstanceTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)), PixelFormat.D24_UNorm_S8_UInt);

    public override void Render(InstanceContext context, CommandBuffer cmd)
    {
        Cube.Draw(cmd);
    }
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static RenderGraph graph;
    static GraphInstances instances;


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

        graph = SampleView.Build(new CubePass());
        instances = SampleView.Instances(graph, 600, 600, device.MainSwapchain);
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        device.DispatchGraph(graph, instances);
        tracker.End(dt);
    }


    public static void Close()
    {
        graph.Dispose();
        Cube.Dispose();
        device.Dispose();
    }
}
