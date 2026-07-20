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
}


// One draw, no dependencies between passes: present clears and draws straight into the swapchain.
internal sealed class CubeGridPresentPass : IPresentPass<SceneView, int>
{
    private float _time;

    public string Name => "Present";

    public void Present(RenderContext<SceneView, int> context)
    {
        Framebuffer? target = context.RequestSwapchainTarget();
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("CubeGrid");
        cmd.Begin();
        cmd.SetFramebuffer(target);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        CubeGrid.Draw(_time, cmd);
        cmd.End();

        context.SubmitCommandBuffer(cmd);
        context.ArmPresent();
    }

    public void Advance(float dt) => _time += dt;
}


internal sealed class CubeGridPipeline : RenderPipeline<SceneView, int>
{
    private readonly CubeGridPresentPass _present = new();

    public CubeGridPresentPass Present => _present;

    protected override void InitializePasses() => SetPresentPass(_present);
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
        CubeGrid.Create(device);

        pipeline = new CubeGridPipeline();
        views = new[] { new SceneView(600, 600) };
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        pipeline.Present.Advance((float)dt);
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
