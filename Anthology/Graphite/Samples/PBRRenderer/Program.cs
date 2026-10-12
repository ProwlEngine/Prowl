using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Graphite.Samples;
using Prowl.Vector;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class DemoWindow
{
    private readonly IWindow _window;
    private readonly Dictionary<int, DemoScene> _scenes = new();
    private readonly FlyCamera _camera = new();
    private readonly string? _shotPath;
    private readonly int _shotFrames;
    private IInputContext _input = null!;
    private GraphicsDevice _device = null!;
    private Demo _demo = null!;
    private DemoScene _scene = null!;
    private double _time;
    private ulong _frame;
    private bool _paused;
    private bool _looking;
    private System.Numerics.Vector2 _lastMouse;
    private double _titleTimer;
    private int _titleFrames;
    private int _startScene;
    private bool _freeze;
    private float? _hour;

    public DemoWindow(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--scene" && i + 1 < args.Length)
                _startScene = int.Parse(args[++i]) - 1;
            else if (args[i] == "--shot" && i + 1 < args.Length)
                _shotPath = args[++i];
            else if (args[i] == "--hour" && i + 1 < args.Length)
                _hour = float.Parse(args[++i]);
            else if (args[i] == "--freeze")
                _freeze = true;
            else if (args[i] == "--frames" && i + 1 < args.Length)
                _shotFrames = int.Parse(args[++i]);
        }

        DeviceCreateUtilities.InitializeWindowing();
        WindowOptions options = WindowOptions.Default with
        {
            Title = "Graphite Renderer",
            Size = new Vector2D<int>(1600, 900),
            VSync = false,
            ShouldSwapAutomatically = false,
            API = new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(2, 1)),
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _device = DeviceCreateUtilities.CreateDevice(_window, new GraphicsDeviceOptions { PreferDepthRangeZeroToOne = true }, new SwapchainDescription { SyncToVerticalBlank = false }, GraphicsBackend.Vulkan);
        _window.FramebufferResize += size => _device.ResizeMainWindow((uint)size.X, (uint)size.Y);

        _demo = new Demo(_device);
        SelectScene(Math.Clamp(_startScene, 0, 2));
        _paused = _freeze;
        if (_hour is { } hour && _scene is ShadowsScene shadows)
            shadows.Hour = hour;

        _input = _window.CreateInput();
        IKeyboard keyboard = _input.Keyboards[0];
        keyboard.KeyDown += (_, key, _) => OnKey(key);
        IMouse mouse = _input.Mice[0];
        mouse.MouseDown += (m, button) =>
        {
            if (button == MouseButton.Right)
            {
                _looking = true;
                _lastMouse = m.Position;
            }
        };
        mouse.MouseUp += (_, button) =>
        {
            if (button == MouseButton.Right)
                _looking = false;
        };
        mouse.MouseMove += (_, position) =>
        {
            if (_looking)
                _camera.Look(position.X - _lastMouse.X, position.Y - _lastMouse.Y);
            _lastMouse = position;
        };
    }

    private void SelectScene(int index)
    {
        if (!_scenes.TryGetValue(index, out DemoScene? scene))
        {
            scene = index switch
            {
                0 => new HelmetScene(_demo),
                1 => new LightsScene(_demo),
                _ => new ShadowsScene(_demo),
            };
            _scenes[index] = scene;
        }

        _scene = scene;
        _camera.Position = scene.StartPosition;
        _camera.Yaw = scene.StartYaw;
        _camera.Pitch = scene.StartPitch;
    }

    private void OnKey(Key key)
    {
        switch (key)
        {
            case Key.Number1: SelectScene(0); break;
            case Key.Number2: SelectScene(1); break;
            case Key.Number3: SelectScene(2); break;
            case Key.Space: _paused = !_paused; break;
            case Key.B: _demo.Bloom.Enabled = !_demo.Bloom.Enabled; break;
            case Key.T: _demo.Tonemap.Enabled = !_demo.Tonemap.Enabled; break;
            case Key.Minus: _demo.Tonemap.Settings.Exposure *= 0.8f; break;
            case Key.Equal: _demo.Tonemap.Settings.Exposure *= 1.25f; break;
            case Key.Escape: _window.Close(); break;
            default: _scene.OnKey(key); break;
        }
    }

    private void OnRender(double deltaTime)
    {
        float dt = (float)deltaTime;
        if (!_paused)
            _time += deltaTime;

        IKeyboard keyboard = _input.Keyboards[0];
        float speed = keyboard.IsKeyPressed(Key.ShiftLeft) ? 40f : 10f;
        float right = (keyboard.IsKeyPressed(Key.D) ? 1 : 0) - (keyboard.IsKeyPressed(Key.A) ? 1 : 0);
        float forward = (keyboard.IsKeyPressed(Key.W) ? 1 : 0) - (keyboard.IsKeyPressed(Key.S) ? 1 : 0);
        float up = (keyboard.IsKeyPressed(Key.E) ? 1 : 0) - (keyboard.IsKeyPressed(Key.Q) ? 1 : 0);
        _camera.Move(right, up, forward, speed * dt);

        _scene.Update((float)_time, _paused ? 0f : dt);

        Framebuffer target = _device.MainSwapchain.Framebuffer;
        ViewDesc view = new()
        {
            World = _scene.World,
            ViewId = 0,
            Target = target,
            ClearColor = new Float4(0.01f, 0.012f, 0.02f, 1),
            Near = FlyCamera.Near,
            Kind = ViewKind.Game,
            Features = _demo.Post,
        };
        view.Layers[0] = _camera.Layer(target.Width / (float)Math.Max(target.Height, 1u));

        _demo.Renderer.SubmitFrame(new FrameDesc
        {
            Views = new[] { view },
            Time = new FrameTime { Time = _time, DeltaTime = dt, FrameIndex = _frame++ },
        });

        _titleTimer += deltaTime;
        _titleFrames++;
        if (_titleTimer >= 0.5)
        {
            _window.Title = $"Graphite Renderer - {_scene.Name} - {_scene.Status} - {_titleFrames / _titleTimer:0} fps - 1 2 3 scene, B bloom, T tonemap, +/- exposure, space pause, {_scene.Controls}";
            _titleTimer = 0;
            _titleFrames = 0;
        }

        if (_shotPath != null && _frame >= (ulong)_shotFrames)
        {
            _demo.Renderer.WaitForRenderThread();
            _device.WaitForIdle();
            System.Diagnostics.Process.Start("import", ["-window", "root", _shotPath])?.WaitForExit();
            _window.Close();
        }
    }

    private void OnClosing()
    {
        _device.WaitForIdle();
        foreach (DemoScene scene in _scenes.Values)
            scene.Dispose();
        _demo.Dispose();
        _device.Dispose();
    }
}


public static class Program
{
    private static void Main(string[] args) => new DemoWindow(args).Run();
}
