namespace Prowl.Graphite.Samples.HelloTriangle;


public static class Program
{
    static GraphicsDevice device;
    static Mesh triangle;
    static GraphicsProgram shader;
    static RenderMSTracker tracker;
    static FrameGraph graph;


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

        graph = new FrameGraph(device, "Sample");
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        SampleView.Draw(graph, device.MainSwapchain, "Backbuffer", cmd =>
        {
            cmd.SetShader(shader);
            cmd.SetVertexSource(triangle);
            cmd.DrawIndexed();
        });
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
