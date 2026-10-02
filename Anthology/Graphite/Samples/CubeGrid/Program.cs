using Prowl.Graphite.RenderGraph;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.CubeGrid;


internal readonly struct SceneView : IRenderView
{
    public SceneView(uint width, uint height)
    {
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

    public override void Setup(RenderContextBuilder builder) => SetBackbufferTarget(builder);

    public override void Render(RenderContext<SceneView> context)
    {
        CommandBuffer cmd = context.GetCommandBuffer("CubeGrid");
        BindTarget(context, cmd, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        CubeGrid.Draw(_time, cmd);
        context.SubmitCommandBuffer(cmd);
    }

    public void Advance(float dt) => _time += dt;
}


internal sealed class CubeGridPipeline : RenderPipeline<SceneView>
{
    private readonly CubeGridPass _pass = new();

    public CubeGridPass Pass => _pass;

    protected override void InitializePasses() => AddPass(_pass);
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static CubeGridPipeline pipeline;
    static SceneView[] views;


    private static void Main()
    {
        GraphicsDeviceOptions options = new()
        {
            VulkanValidationLayers = false,
            PreferStandardClipSpaceYDirection = true
        };

        SwapchainDescription swapchain = new()
        {
            DepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SyncToVerticalBlank = false
        };

        DeviceCreateUtilities.CreateWindowAndDevice(Load, Render, Close, options, swapchain);
    }

    public static void Load(GraphicsDevice newDevice)
    {
        tracker = new(newDevice);

        device = newDevice;
        CubeGrid.Create(device);

        pipeline = new CubeGridPipeline();
        views = new[] { new SceneView(600, 600) };
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        pipeline.Pass.Advance((float)dt);
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
