using Prowl.Vector;

using Silk.NET.Maths;
using Silk.NET.Windowing;


namespace Prowl.Graphite.Samples.TexturedQuad;


public static class Program
{
    static GraphicsDevice device;
    static CommandBuffer buffer;
    static Mesh leftQuad;
    static Mesh rightQuad;
    static GraphicsProgram shader;
    static PropertySet leftProperties;
    static PropertySet rightProperties;
    static Texture leftTexture;
    static Texture rightTexture;
    static Sampler leftSampler;
    static Sampler rightSampler;
    static RenderMSTracker tracker;


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
        leftQuad = ModelLoader.CreateQuad(device, -0.9f, -0.05f, -0.45f, 0.45f);
        rightQuad = ModelLoader.CreateQuad(device, 0.05f, 0.9f, -0.45f, 0.45f);

        (leftTexture, leftSampler) = ImageLoader.Load(device, "Cat_cat.png");
        (rightTexture, rightSampler) = ImageLoader.Load(device, "Cat_cat2.png");

        buffer = device.ResourceFactory.CreateCommandBuffer();

        leftProperties = new();
        leftProperties.SetTexture("MainTexture", leftTexture, leftSampler);

        rightProperties = new();
        rightProperties.SetTexture("MainTexture", rightTexture, rightSampler);
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        Frame frame = device.BeginFrame();

        buffer.Begin();
        buffer.SetFramebuffer(device.SwapchainFramebuffer);
        buffer.ClearDepthStencil(1, 0);
        buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        buffer.SetShader(shader);

        buffer.SetProperties(leftProperties);
        buffer.SetVertexSource(leftQuad);
        buffer.DrawIndexed();

        buffer.SetProperties(rightProperties);
        buffer.SetVertexSource(rightQuad);
        buffer.DrawIndexed();

        buffer.End();

        frame.SubmitCommands(buffer);
        device.EndFrame(frame);

        // Explicitly avoid timing SwapBuffers() to not pollute with OS throttling/presentation limits.
        tracker.End(dt);

        device.SwapBuffers();
    }


    public static void Close()
    {
        buffer.Dispose();
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
