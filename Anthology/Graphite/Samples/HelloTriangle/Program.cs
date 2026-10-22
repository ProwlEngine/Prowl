using Prowl.Vector;


namespace Prowl.Graphite.Samples.HelloTriangle;


// The whole demo is one draw, so it needs no offscreen passes to order or textures to share between
// passes: one pass clears and draws straight into the backbuffer.
internal sealed class TrianglePass : InstanceRasterPass
{
    private readonly Mesh _triangle;
    private readonly GraphicsProgram _shader;

    public TrianglePass(Mesh triangle, GraphicsProgram shader)
    {
        _triangle = triangle;
        _shader = shader;
    }

    public override string Name => "Backbuffer";

    public override GraphKind Kind => SampleView.Kind;

    public override void Setup(InstanceContextBuilder builder) => SetInstanceTarget(builder, TargetLoadStoreOps.Clear(new Color(0.10f, 0.12f, 0.16f, 1.0f)), PixelFormat.D24_UNorm_S8_UInt);

    public override void Render(InstanceContext context, CommandBuffer cmd)
    {
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
        shader = ShaderLoader.CreateShader(device);
        triangle = ModelLoader.CreateTriangle(device);

        graph = SampleView.Build(new TrianglePass(triangle, shader));
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
        triangle.Dispose();
        shader.Dispose();
        device.Dispose();
    }
}
