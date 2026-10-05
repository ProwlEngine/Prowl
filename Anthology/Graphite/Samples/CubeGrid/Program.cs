using Prowl.Graphite.RenderGraph;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.CubeGrid;


internal readonly struct SceneView : IRenderView
{
    public Swapchain TargetSwapchain { get; }

    public SceneView(uint width, uint height, Swapchain swapchain)
    {
        TargetSwapchain = swapchain;
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}


// One draw, no dependencies between passes: the pass clears and draws straight into the backbuffer.
internal sealed class CubeGridPass : RasterPass<SceneView>
{
    private float _time;

    public override string Name => "Backbuffer";

    public override void Setup(RenderContextBuilder builder) => SetViewTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)), PixelFormat.D24_UNorm_S8_UInt);

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        BindTarget(context, cmd);
        CubeGrid.Draw(_time, cmd);
    }

    public void Advance(float dt) => _time += dt;
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static RenderPipeline<SceneView> pipeline;
    static CubeGridPass gridPass;
    static SceneView[] views;


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
        pipeline = new([gridPass]);
        views = new[] { new SceneView(600, 600, device.MainSwapchain) };
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        gridPass.Advance((float)dt);
        device.DispatchGraph(pipeline, views);

        tracker.End(dt);
    }


    public static void Close()
    {
        CubeGrid.Dispose();
        pipeline.Dispose();
        device.Dispose();
    }
}
