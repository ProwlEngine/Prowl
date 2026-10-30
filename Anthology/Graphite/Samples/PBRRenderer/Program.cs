using System;
using System.Collections.Generic;
using System.IO;

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
    private int _sceneIndex;
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
    private bool _noBloom;
    private bool _noTonemap;
    private bool _noVsync;
    private int _debugView = -1;
    private PassTimer? _timer;
    private Texture? _shotTexture;
    private Framebuffer? _shotTarget;

    private static readonly string[] DebugViews = ["DIFFUSE", "SPECULAR", "SPECULAR_ALBEDO", "COAT", "FUZZ", "THIN_FILM", "DARKENING", "SUBSURFACE", "BACK", "TRANSMISSION", "THICKNESS", "DIRECT", "INDIRECT", "FURNACE", "NORMALS", "ROUGHNESS"];

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
            else if (args[i] == "--nobloom")
                _noBloom = true;
            else if (args[i] == "--notonemap")
                _noTonemap = true;
            else if (args[i] == "--novsync")
                _noVsync = true;
            else if (args[i] == "--profile")
                _timer = new PassTimer();
            else if (args[i] == "--debug" && i + 1 < args.Length)
                _debugView = Array.IndexOf(DebugViews, args[++i].ToUpperInvariant());
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
            IsVisible = _shotPath == null,
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
        _device = DeviceCreateUtilities.CreateDevice(_window, new GraphicsDeviceOptions { PreferDepthRangeZeroToOne = true }, new SwapchainDescription { SyncToVerticalBlank = true }, GraphicsBackend.Vulkan);
        _window.FramebufferResize += size =>
        {
            if (_demo != null)
                _demo.Renderer.Resize((uint)size.X, (uint)size.Y);
            else
                _device.ResizeMainWindow((uint)size.X, (uint)size.Y);
        };

        _device.SyncToVerticalBlank = !_noVsync;
        if (_shotPath != null)
        {
            _shotTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1600, 900, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm_SRgb, TextureUsage.RenderTarget | TextureUsage.Sampled));
            _shotTarget = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _shotTexture));
        }
        _demo = new Demo(_device);
        _demo.Bloom.Enabled = !_noBloom;
        _demo.Tonemap.Enabled = !_noTonemap;
        SelectScene(Math.Clamp(_startScene, 0, 4));
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
                2 => new ShadowsScene(_demo),
                3 => new InstancingScene(_demo),
                _ => new MaterialsScene(_demo),
            };
            _scenes[index] = scene;
        }

        _scene = scene;
        _sceneIndex = index;
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
            case Key.Number4: SelectScene(3); break;
            case Key.Number5: SelectScene(4); break;
            case Key.F: _debugView = _debugView + 1 < DebugViews.Length ? _debugView + 1 : -1; break;
            case Key.Space: _paused = !_paused; break;
            case Key.B: _demo.Bloom.Enabled = !_demo.Bloom.Enabled; break;
            case Key.V: _device.SyncToVerticalBlank = !_device.SyncToVerticalBlank; break;
            case Key.T: _demo.Tonemap.Enabled = !_demo.Tonemap.Enabled; break;
            case Key.X: _demo.Taa.Enabled = !_demo.Taa.Enabled; break;
            case Key.Minus: _demo.AutoExposure.ExposureCompensation -= 0.5f; break;
            case Key.Equal: _demo.AutoExposure.ExposureCompensation += 0.5f; break;
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

        Framebuffer target = _shotTarget ?? _device.MainSwapchain.Framebuffer;
        ViewDesc view = new()
        {
            World = _scene.World,
            ViewId = _sceneIndex,
            EV100 = _scene.StartEV100,
            Target = target,
            ClearColor = new Float4(0.01f, 0.012f, 0.02f, 1),
            Near = FlyCamera.Near,
            Kind = ViewKind.Game,
            Features = _demo.Post,
            Keywords = _debugView < 0 ? null : [new Prowl.Graphite.ShaderDef.Keyword("GRAPHITE_DEBUG", DebugViews[_debugView])],
        };
        view.Layers[0] = _camera.Layer(target.Width / (float)Math.Max(target.Height, 1u));

        _demo.Renderer.SubmitFrame(new FrameDesc
        {
            Views = new[] { view },
            Time = new FrameTime { Time = _time, DeltaTime = dt, FrameIndex = _frame++ },
            Profilers = _timer == null ? null : [_timer.Begin()],
        });

        _titleTimer += deltaTime;
        _titleFrames++;
        if (_titleTimer >= 0.5)
        {
            _timer?.Print(100);
            _window.Title = $"Graphite Renderer - {_scene.Name} - {_scene.Status} - {_titleFrames / _titleTimer:0} fps - 1 to 5 scene, B bloom, T tonemap, X TAA, V vsync, +/- exposure, space pause, {_scene.Controls}{(_debugView < 0 ? "" : $" - debug {DebugViews[_debugView]}")}";
            _titleTimer = 0;
            _titleFrames = 0;
        }

        if (_shotPath != null && _frame >= (ulong)_shotFrames)
        {
            _demo.Renderer.WaitForRenderThread();
            _device.WaitForIdle();
            SaveShot(_shotPath);
            _window.Close();
        }
    }

    // Writes the offscreen target as a 24 bit BMP
    private void SaveShot(string path)
    {
        uint width = _shotTexture!.Width, height = _shotTexture.Height;
        using DeviceBuffer staging = _device.ResourceFactory.CreateBuffer(new BufferDescription(width * height * 4, BufferUsage.Staging));
        _device.Record(cmd => cmd.CopyTextureToBuffer(_shotTexture, staging, 0, TextureRegion.Whole(_shotTexture, 0, 0))).Wait();
        byte[] pixels = _device.Map<byte>(staging).ToArray();
        _device.Unmap(staging);

        int row = ((int)width * 3 + 3) & ~3;
        using BinaryWriter writer = new(File.Create(path));
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + row * (int)height);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write((int)width);
        writer.Write((int)height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(new byte[24]);
        byte[] line = new byte[row];
        for (int y = (int)height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * (int)width + x) * 4;
                line[x * 3] = pixels[i + 2];
                line[x * 3 + 1] = pixels[i + 1];
                line[x * 3 + 2] = pixels[i];
            }
            writer.Write(line);
        }
    }

    private void OnClosing()
    {
        _device.WaitForIdle();
        _shotTarget?.Dispose();
        _shotTexture?.Dispose();
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
