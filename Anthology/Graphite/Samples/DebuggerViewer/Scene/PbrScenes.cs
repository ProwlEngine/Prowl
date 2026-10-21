using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.Samples;
using Prowl.Graphite.Samples.PBRRenderer;
using Prowl.Vector;

namespace DebuggerViewer.Scene;

/// <summary>The PBR sample's helmet, lights and shadows scenes, rendered offscreen by its renderer for the debugger to record.</summary>
internal sealed class PbrScenes : IDisposable
{
    public const uint Width = 960;
    public const uint Height = 540;
    public static readonly string[] Names = ["Helmet", "Lights", "Shadows"];

    private readonly Demo _demo;
    private readonly DemoScene?[] _scenes = new DemoScene?[Names.Length];
    private readonly FlyCamera _camera = new();
    private DemoScene _scene = null!;
    private ulong _frame;

    public PbrScenes(GraphicsDevice device, int scene)
    {
        _demo = new Demo(device);
        PixelFormat format = device.MainSwapchain.Framebuffer.OutputDescription.ColorFormats[0];
        Target = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(Width, Height, [format], depth: false));
        Select(Math.Clamp(scene, 0, Names.Length - 1));
    }

    public RenderTexture Target { get; }

    public int Selected { get; private set; }

    public double Time { get; private set; }

    public void Select(int index)
    {
        _scene = _scenes[index] ??= index switch
        {
            0 => new HelmetScene(_demo),
            1 => new LightsScene(_demo),
            _ => new ShadowsScene(_demo),
        };
        Selected = index;
        _camera.Position = _scene.StartPosition;
        _camera.Yaw = _scene.StartYaw;
        _camera.Pitch = _scene.StartPitch;
    }

    public void Advance(float deltaTime)
    {
        Time += deltaTime;
        _scene.Update((float)Time, deltaTime);
    }

    public void Render(float deltaTime, params IProfiler[] profilers)
    {
        ViewDesc view = new()
        {
            World = _scene.World,
            ViewId = 0,
            Name = "Main camera",
            Target = Target.Framebuffer,
            ClearColor = new Float4(0.01f, 0.012f, 0.02f, 1),
            Near = FlyCamera.Near,
            Kind = ViewKind.Game,
            Features = _demo.Post,
        };
        view.Layers[0] = _camera.Layer(Width / (float)Height);

        _demo.Renderer.SubmitFrame(new FrameDesc
        {
            Views = new[] { view },
            Time = new FrameTime { Time = Time, DeltaTime = deltaTime, FrameIndex = _frame++ },
            Profilers = profilers,
        });
        _demo.Renderer.WaitForRenderThread();
    }

    public void Dispose()
    {
        _demo.Renderer.WaitForRenderThread();
        foreach (DemoScene? scene in _scenes)
            scene?.Dispose();
        _demo.Dispose();
        Target.Dispose();
    }
}
