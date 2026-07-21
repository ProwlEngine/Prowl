using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Silk.NET.Maths;
using Silk.NET.Windowing;


namespace Prowl.Graphite.Samples.TexturedQuad;


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


// Three draws sharing one shader but switching PropertySets, no dependencies between passes: present
// clears and draws straight into the swapchain.
internal sealed class TexturedQuadPresentPass : IPresentPass<SceneView>
{
    private readonly GraphicsProgram _shader;
    private readonly Mesh _leftQuad;
    private readonly Mesh _rightQuad;
    private readonly Mesh _midQuad;
    private readonly PropertySet _leftProperties;
    private readonly PropertySet _rightProperties;
    private readonly PropertySet _midProperties;

    public TexturedQuadPresentPass(
        GraphicsProgram shader,
        Mesh leftQuad, Mesh rightQuad, Mesh midQuad,
        PropertySet leftProperties, PropertySet rightProperties, PropertySet midProperties)
    {
        _shader = shader;
        _leftQuad = leftQuad;
        _rightQuad = rightQuad;
        _midQuad = midQuad;
        _leftProperties = leftProperties;
        _rightProperties = rightProperties;
        _midProperties = midProperties;
    }

    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<SceneView> context)
    {
        Framebuffer? target = context.SwapchainTarget;
        if (target == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer("TexturedQuad");
        cmd.Begin();
        cmd.SetFramebuffer(target);
        cmd.ClearDepthStencil(1, 0);
        cmd.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        cmd.SetShader(_shader);

        cmd.SetProperties(_leftProperties);
        cmd.SetVertexSource(_leftQuad);
        cmd.DrawIndexed();

        cmd.SetProperties(_rightProperties);
        cmd.SetVertexSource(_rightQuad);
        cmd.DrawIndexed();

        cmd.SetProperties(_midProperties);
        cmd.SetVertexSource(_midQuad);
        cmd.DrawIndexed();

        cmd.End();

        context.SubmitCommandBuffer(cmd);
        context.Present();
    }
}


internal sealed class TexturedQuadPipeline : RenderPipeline<SceneView>
{
    private readonly IPresentPass<SceneView> _present;

    public TexturedQuadPipeline(IPresentPass<SceneView> present) => _present = present;

    protected override void InitializePasses() => SetPresentPass(_present);
}


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
    static TexturedQuadPipeline pipeline;
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

        pipeline = new TexturedQuadPipeline(new TexturedQuadPresentPass(
            shader, leftQuad, rightQuad, midQuad, leftProperties, rightProperties, midProperties));
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
