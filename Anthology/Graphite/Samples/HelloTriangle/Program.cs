using Prowl.Graphite.RenderGraph;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.HelloTriangle;


internal readonly struct SceneView : IRenderView
{
    public bool TargetSwapchain => true;

    public SceneView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}


// The whole demo is one draw, so it needs no offscreen passes to order or textures to share between
// passes: one pass clears and draws straight into the backbuffer.
internal sealed class TrianglePass : RasterPass<SceneView>
{
    private readonly Mesh _triangle;
    private readonly GraphicsProgram _shader;

    public TrianglePass(Mesh triangle, GraphicsProgram shader)
    {
        _triangle = triangle;
        _shader = shader;
    }

    public override string Name => "Backbuffer";

    public override void Setup(RenderContextBuilder builder) => SetViewTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)));

    public override void Render(RenderContext<SceneView> context, CommandBuffer cmd)
    {
        BindTarget(context, cmd);
        cmd.SetShader(_shader);
        cmd.SetVertexSource(_triangle);
        cmd.DrawIndexed();
    }
}


public static class Program
{
    static GraphicsDevice device;
    static Mesh triangle;
    static GraphicsProgram shader;
    static RenderMSTracker tracker;
    static RenderPipeline<SceneView> pipeline;
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
        device = newDevice;

        tracker = new(newDevice);
        shader = ShaderLoader.CreateShader(device);
        triangle = ModelLoader.CreateTriangle(device);

        pipeline = new([new TrianglePass(triangle, shader)]);
        views = new[] { new SceneView(600, 600) };
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
        triangle.Dispose();
        shader.Dispose();
        device.Dispose();
    }
}
