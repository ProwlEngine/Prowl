using Prowl.Vector;

using Silk.NET.Maths;
using Silk.NET.Windowing;


namespace Prowl.Veldrid.Samples.TexturedQuad;


public static class Program
{
    static GraphicsDevice device;
    static CommandBuffer buffer;
    static Mesh quad;
    static GraphicsProgram shader;
    static PropertySet properties;
    static Texture texture;
    static Sampler sampler;
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

        tracker = new();
        shader = ShaderLoader.CreateShader(device);
        quad = ModelLoader.CreateQuad(device);
        (texture, sampler) = ImageLoader.Load(device, "Cat_cat.png");
        buffer = device.ResourceFactory.CreateCommandBuffer();
        properties = new();
        properties.SetTexture("MainTexture", texture, sampler);
    }


    public static void Render(double dt)
    {
        tracker.Begin();

        buffer.Begin();
        buffer.SetFramebuffer(device.SwapchainFramebuffer);
        buffer.ClearDepthStencil(1, 0);
        buffer.ClearColorTarget(0, new Color(0.10f, 0.12f, 0.16f, 1.0f));
        buffer.SetShader(shader);
        buffer.SetProperties(properties);
        buffer.SetVertexSource(quad);
        buffer.DrawIndexed();
        buffer.End();

        Frame frame = device.BeginFrame();
        frame.SubmitCommands(buffer);
        device.EndFrame(frame);

        // Explicitly avoid timing SwapBuffers() to not pollute with OS throttling/presentation limits.
        tracker.End(dt);

        device.SwapBuffers();
    }


    public static void Close()
    {
        buffer.Dispose();
        quad.Dispose();
        shader.Dispose();
        device.Dispose();
    }
}
