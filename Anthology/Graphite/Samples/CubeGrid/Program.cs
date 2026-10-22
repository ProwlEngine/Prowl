using Prowl.Vector;


namespace Prowl.Graphite.Samples.CubeGrid;


// One draw, no dependencies between passes: the pass clears and draws straight into the backbuffer.
internal sealed class CubeGridPass : InstanceRasterPass
{
    private float _time;

    public override string Name => "Backbuffer";

    public override GraphKind Kind => SampleView.Kind;

    public override void Setup(InstanceContextBuilder builder) => SetInstanceTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)), PixelFormat.D24_UNorm_S8_UInt);

    public override void Render(InstanceContext context, CommandBuffer cmd)
    {
        CubeGrid.Draw(_time, cmd);
    }

    public void Advance(float dt) => _time += dt;
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static RenderGraph graph;
    static GraphInstances instances;
    static CubeGridPass gridPass;


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

        gridPass = new CubeGridPass();
        graph = SampleView.Build(gridPass);
        instances = SampleView.Instances(graph, 600, 600, device.MainSwapchain);
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        gridPass.Advance((float)dt);
        device.DispatchGraph(graph, instances);

        tracker.End(dt);
    }


    public static void Close()
    {
        CubeGrid.Dispose();
        graph.Dispose();
        device.Dispose();
    }
}
