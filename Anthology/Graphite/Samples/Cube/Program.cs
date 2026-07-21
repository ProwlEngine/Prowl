using Prowl.Graphite.RenderGraph;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.Cube;


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
internal sealed class CubePresentPass : IPresentPass<SceneView>
{
    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<SceneView> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("Cube");
        cmd.SetFramebuffer(target);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        Cube.Draw(cmd);
        context.SubmitCommandBuffer(cmd);
        context.Present();
    }
}


internal sealed class CubePipeline : RenderPipeline<SceneView>
{
    protected override void InitializePasses() => SetPresentPass(new CubePresentPass());
}


public static class Program
{
    static GraphicsDevice device;
    static RenderMSTracker tracker;
    static CubePipeline pipeline;
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
        device = newDevice;

        tracker = new(newDevice);
        Cube.Create(device);

        pipeline = new CubePipeline();
        views = new[] { new SceneView(600, 600) };
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        device.DispatchGraph(pipeline, views);

        // Explicitly avoid timing SwapBuffers() to not pollute with OS throttling/presentation limits.
        tracker.End(dt);
    }


    public static void Close()
    {
        pipeline.Dispose();
        Cube.Dispose();
        device.Dispose();
    }
}
