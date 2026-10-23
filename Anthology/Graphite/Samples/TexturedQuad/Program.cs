using Silk.NET.Windowing;


namespace Prowl.Graphite.Samples.TexturedQuad;


public static class Program
{
    static GraphicsDevice device;
    static Mesh leftQuad;
    static Mesh rightQuad;
    static Mesh midQuad;
    static GraphicsProgram shader;
    static PropertySet leftProperties;
    static PropertySet rightProperties;
    static PropertySet midProperties;
    static Texture leftTexture;
    static Texture rightTexture;
    static Texture midTexture;
    static Sampler leftSampler;
    static Sampler rightSampler;
    static Sampler midSampler;
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

        // Two side-by-side quads, each bound to its own texture through its own PropertySet, to
        // exercise switching textures / resource sets between draws.
        leftQuad = ModelLoader.CreateQuad(device, -0.75f, -0.25f, -0.45f, 0.45f);
        rightQuad = ModelLoader.CreateQuad(device, 0.25f, 0.75f, -0.45f, 0.45f);
        midQuad = ModelLoader.CreateQuad(device, -0.25f, 0.25f, -0.45f, 0.45f);

        (leftTexture, leftSampler) = ImageLoader.Load(device, "Cat_cat.png");
        (rightTexture, rightSampler) = ImageLoader.Load(device, "Cat_cat2.png");
        (midTexture, midSampler) = ImageLoader.Load(device, "Cat_cat3.jpg");

        leftProperties = new();
        leftProperties.SetTexture("MainTexture", leftTexture, leftSampler);

        rightProperties = new();
        rightProperties.SetTexture("MainTexture", rightTexture, rightSampler);

        midProperties = new();
        midProperties.SetTexture("MainTexture", midTexture, midSampler);

        graph = new FrameGraph(device, "Sample");
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        SampleView.Draw(graph, device.MainSwapchain, "Backbuffer", cmd =>
        {
            cmd.SetShader(shader);

            cmd.SetProperties(leftProperties);
            cmd.SetVertexSource(leftQuad);
            cmd.DrawIndexed();

            cmd.SetProperties(rightProperties);
            cmd.SetVertexSource(rightQuad);
            cmd.DrawIndexed();

            cmd.SetProperties(midProperties);
            cmd.SetVertexSource(midQuad);
            cmd.DrawIndexed();
        });
        tracker.End(dt);
    }


    public static void Close()
    {
        graph.Dispose();
        leftQuad.Dispose();
        rightQuad.Dispose();
        leftTexture.Dispose();
        rightTexture.Dispose();
        leftSampler.Dispose();
        rightSampler.Dispose();
        shader.Dispose();
        device.Dispose();
    }
}
