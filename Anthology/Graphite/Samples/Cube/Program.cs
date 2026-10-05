using Prowl.Graphite.RenderGraph;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.Cube;


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
internal sealed class CubePass : RasterPass<SceneView>
{
    public override string Name => "Backbuffer";

    public override void Setup(RenderContextBuilder builder) => SetViewTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)), PixelFormat.D24_UNorm_S8_UInt);

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        Cube.Draw(cmd);
    }
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static RenderPipeline<SceneView> pipeline;
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
        device = newDevice;

        tracker = new(newDevice);
        Cube.Create(device);

        pipeline = new([new CubePass()]);
        views = new[] { new SceneView(600, 600, device.MainSwapchain) };
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        device.DispatchGraph(pipeline, views);
        tracker.End(dt);
    }


    public static void Close()
    {
        pipeline.Dispose();
        Cube.Dispose();
        device.Dispose();
    }
}
