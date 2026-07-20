using Prowl.Graphite.RenderGraph;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.HelloTriangle;


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


// The whole demo is one draw, so it needs no offscreen passes to order or textures to share between
// passes: the present pass alone clears and draws straight into the swapchain target.
internal sealed class TrianglePresentPass : IPresentPass<SceneView, int>
{
    private readonly Mesh _triangle;
    private readonly GraphicsProgram _shader;

    public TrianglePresentPass(Mesh triangle, GraphicsProgram shader)
    {
        _triangle = triangle;
        _shader = shader;
    }

    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<SceneView, int> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("Triangle");
        cmd.Begin();
        cmd.SetFramebuffer(target);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();
        cmd.End();

        context.SubmitCommandBuffer(cmd);
        context.ArmPresent();
    }
}


internal sealed class TrianglePipeline : RenderPipeline<SceneView, int>
{
    private readonly IPresentPass<SceneView, int> _present;

    public TrianglePipeline(IPresentPass<SceneView, int> present) => _present = present;

    protected override void InitializePasses() => SetPresentPass(_present);
}


public static class Program
{
    static GraphicsDevice device;
    static Mesh triangle;
    static GraphicsProgram shader;
    static RenderMSTracker tracker;
    static TrianglePipeline pipeline;
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
        shader = ShaderLoader.CreateShader(device);
        triangle = ModelLoader.CreateTriangle(device);

        pipeline = new TrianglePipeline(new TrianglePresentPass(triangle, shader));
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
        triangle.Dispose();
        shader.Dispose();
        device.Dispose();
    }
}
