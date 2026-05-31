using Prowl.Vector;


namespace Prowl.Veldrid.Samples.HelloTriangle;


public static class Program
{
    static GraphicsDevice device;
    static CommandBuffer buffer;
    static Mesh triangle;
    static GraphicsProgram shader;
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
        triangle = ModelLoader.CreateTriangle(device);
        buffer = device.ResourceFactory.CreateCommandBuffer();
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
        buffer.SetVertexSource(triangle);
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
        triangle.Dispose();
        shader.Dispose();
        device.Dispose();
    }
}
