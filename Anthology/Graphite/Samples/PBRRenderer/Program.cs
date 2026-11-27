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
    private readonly double[]? _at;
    private readonly Vector2D<int> _size = new(1600, 900);
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
    private bool _noBloom;
    private bool _noTonemap;
    private bool _noVsync;
    private string? _tier;
    private int _lightCount = -1;
    private float _lightRange = -1;
    private bool _lightSpread;
    private int _voxelView = -1;
    private int _gbufferView = -1;
    private bool _deferred = true;
    private bool _decal;
    private bool _contactShadows;
    private int _displacementCell = 2;
    private bool _gi;
    private bool _reflections;
    private bool _giScreenTrace = true;
    private bool _giOcclusion = true;
    private int _giSpatialPasses = -1;
    private int _voxelFrames = -1;
    private EffectResolution? _giResolution;
    private int _forcedLod = -1;
    private readonly System.Diagnostics.Stopwatch _shotClock = new();
    private PassTimer? _timer;
    private Texture? _shotTexture;
    private Framebuffer? _shotTarget;


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
            else if (args[i] == "--tier" && i + 1 < args.Length)
                _tier = args[++i].ToUpperInvariant();
            else if (args[i] == "--noscreentrace")
                _giScreenTrace = false;
            else if (args[i] == "--gi-noocclusion")
                _giOcclusion = false;
            else if (args[i] == "--voxel-frames" && i + 1 < args.Length)
                _voxelFrames = int.Parse(args[++i]);
            else if (args[i] == "--gi-spatial" && i + 1 < args.Length)
                _giSpatialPasses = int.Parse(args[++i]);
            else if (args[i] == "--gi-res" && i + 1 < args.Length)
                _giResolution = Enum.Parse<EffectResolution>(args[++i], true);
            else if (args[i] == "--gi")
                _gi = true;
            else if (args[i] == "--deferred")
                _deferred = true;
            else if (args[i] == "--forward")
                _deferred = false;
            else if (args[i] == "--decal")
                _decal = true;
            else if (args[i] == "--ssr")
                _reflections = true;
            else if (args[i] == "--contact")
                _contactShadows = true;
            else if (args[i] == "--displace" && i + 1 < args.Length)
                _displacementCell = int.Parse(args[++i]);
            else if (args[i] == "--gbuffer" && i + 1 < args.Length)
            {
                _deferred = true;
                _gbufferView = (int)Enum.Parse<GBufferDebugMode>(args[++i], ignoreCase: true);
            }
            else if (args[i] == "--voxels" && i + 1 < args.Length)
                _voxelView = (int)Enum.Parse<VoxelDebugMode>(args[++i], ignoreCase: true);
            else if (args[i] == "--lights" && i + 1 < args.Length)
                _lightCount = int.Parse(args[++i]);
            else if (args[i] == "--range" && i + 1 < args.Length)
                _lightRange = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--spread")
                _lightSpread = true;
            else if (args[i] == "--lod" && i + 1 < args.Length)
                _forcedLod = int.Parse(args[++i]);
            else if (args[i] == "--freeze")
                _freeze = true;
            else if (args[i] == "--frames" && i + 1 < args.Length)
                _shotFrames = int.Parse(args[++i]);
            else if (args[i] == "--size" && i + 1 < args.Length)
            {
                string[] parts = args[++i].Split('x');
                _size = new Vector2D<int>(int.Parse(parts[0]), int.Parse(parts[1]));
            }
            else if (args[i] == "--at" && i + 1 < args.Length)
                _at = Array.ConvertAll(args[++i].Split(','), s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
        }

        DeviceCreateUtilities.InitializeWindowing();
        WindowOptions options = WindowOptions.Default with
        {
            Title = "Graphite Renderer",
            Size = _size,
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
        _device = DeviceCreateUtilities.CreateDevice(_window, new GraphicsDeviceOptions { PreferDepthRangeZeroToOne = true }, new SwapchainDescription { VSync = VSyncMode.Adaptive }, GraphicsBackend.Vulkan);
        _window.FramebufferResize += size =>
        {
            if (_demo != null)
                _demo.Renderer.Resize((uint)size.X, (uint)size.Y);
            else
                _device.ResizeMainWindow((uint)size.X, (uint)size.Y);
        };

        _device.VSync = _noVsync ? VSyncMode.Mailbox : VSyncMode.Adaptive;
        if (_shotPath != null)
        {
            _shotTexture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)_size.X, (uint)_size.Y, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm_SRgb, TextureUsage.RenderTarget | TextureUsage.Sampled));
            _shotTarget = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _shotTexture));
        }
        _demo = new Demo(_device);
        _demo.Bloom.Enabled = !_noBloom;
        _demo.Tonemap.Enabled = !_noTonemap;
        SelectScene(Math.Clamp(_startScene, 0, 8));
        if (_decal)
            _scene.World.AddDecal(new DecalDesc { Transform = Double4x4.CreateTranslation(new Double3(0, 0, 0)) * Double4x4.CreateScale(6, 6, 6) * Double4x4.RotateX(Math.PI / 2), Color = new Float4(1f, 0.25f, 0.1f, 0.8f) });
        _paused = _freeze;
        _scene.View.ForcedLod = _forcedLod;
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
                5 => new LodScene(_demo),
                6 => new TerrainScene(_demo),
                8 => new CornellScene(_demo),
                7 => new ManyLightsScene(_demo, _lightCount >= 0 ? _lightCount : 1000, _lightRange > 0 ? _lightRange : 8f, _lightSpread),
                _ => new MaterialsScene(_demo),
            };
            _scenes[index] = scene;
        }

        _scene = scene;
        scene.Camera = _camera;
        _camera.Position = scene.StartPosition;
        _camera.Yaw = scene.StartYaw;
        _camera.Pitch = scene.StartPitch;
        if (_at is { Length: 5 })
        {
            _camera.Position = new Double3(_at[0], _at[1], _at[2]);
            _camera.Yaw = (float)_at[3];
            _camera.Pitch = (float)_at[4];
        }
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
            case Key.Number6: SelectScene(5); break;
            case Key.Number7: SelectScene(6); break;
            case Key.Number8: SelectScene(7); break;
            case Key.Number9: SelectScene(8); break;
            case Key.F:
                _gbufferView = _gbufferView + 1 < Enum.GetValues<GBufferDebugMode>().Length ? _gbufferView + 1 : -1;
                _deferred |= _gbufferView >= 0;
                break;
            case Key.G: _voxelView = _voxelView + 1 < Enum.GetValues<VoxelDebugMode>().Length ? _voxelView + 1 : -1; break;
            case Key.I: _gi = !_gi; break;
            case Key.R: _reflections = !_reflections; break;
            case Key.Comma: _contactShadows = !_contactShadows; break;
            case Key.H: _giScreenTrace = !_giScreenTrace; break;
            case Key.Space: _paused = !_paused; break;
            case Key.B: _demo.Bloom.Enabled = !_demo.Bloom.Enabled; break;
            case Key.V: _device.VSync = _device.VSync == VSyncMode.Adaptive ? VSyncMode.Mailbox : VSyncMode.Adaptive; break;
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
        float speed = keyboard.IsKeyPressed(Key.ControlLeft) ? 400f : keyboard.IsKeyPressed(Key.ShiftLeft) ? 40f : 10f;
        float right = (keyboard.IsKeyPressed(Key.D) ? 1 : 0) - (keyboard.IsKeyPressed(Key.A) ? 1 : 0);
        float forward = (keyboard.IsKeyPressed(Key.W) ? 1 : 0) - (keyboard.IsKeyPressed(Key.S) ? 1 : 0);
        float up = (keyboard.IsKeyPressed(Key.E) ? 1 : 0) - (keyboard.IsKeyPressed(Key.Q) ? 1 : 0);
        _camera.Move(right, up, forward, speed * dt);

        _scene.Update((float)_time, _paused ? 0f : dt);

        RenderView view = _scene.View;
        view.Target = _shotTarget ?? _device.MainSwapchain.Framebuffer;
        view.EV100 = _scene.StartEV100;
        view.Camera = _camera.Camera;
        List<GlobalKeyword> keywords = new();
        if (_tier != null)
            keywords.Add(GlobalAxis.PbrTier[_tier]);
        view.Keywords = keywords.Count == 0 ? null : [.. keywords];
        view.Path = _deferred ? RenderPath.Deferred : RenderPath.Forward;
        view.DisplacementCell = _displacementCell;
        _demo.GBuffer.Enabled = _gbufferView >= 0;
        _demo.GBuffer.Mode = (GBufferDebugMode)Math.Max(_gbufferView, 0);
        _demo.Voxels.Enabled = _voxelView >= 0;
        _demo.Voxels.Mode = (VoxelDebugMode)Math.Max(_voxelView, 0);
        _demo.Gi.Enabled = _gi;
        _demo.Reflections.Enabled = _reflections;
        _demo.Contact.Enabled = _contactShadows;
        _demo.Gi.ScreenTrace = _giScreenTrace;
        _demo.Gi.DirectionalOcclusion = _giOcclusion;
        if (_giSpatialPasses >= 0)
            _demo.Gi.SpatialPasses = _giSpatialPasses;
        if (_giResolution is { } giResolution)
            _demo.Gi.Resolution = giResolution;
        _scene.World.SetVoxelVolume(_voxelView < 0 && !_gi ? null : new VoxelVolumeDesc { Center = _camera.Position, UpdateFrames = _voxelFrames > 0 ? _voxelFrames : 8 });

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
            _window.Title = $"Graphite Renderer - {_scene.Name} - {_scene.Status} - {_titleFrames / _titleTimer:0} fps - 1 to 9 scene, B bloom, T tonemap, X TAA, V vsync, +/- exposure, space pause, {_scene.Controls}{(_gbufferView < 0 ? "" : $" - GBuffer {(GBufferDebugMode)_gbufferView}")}, G voxels, I GI, H GI screen trace{(_voxelView < 0 ? "" : $" - voxels {(VoxelDebugMode)_voxelView}")}{(_gi ? _giScreenTrace ? " - GI" : " - GI, voxels only" : "")}";
            _titleTimer = 0;
            _titleFrames = 0;
        }

        if (_shotPath != null && _frame == 10)
            _shotClock.Restart();
        if (_shotPath != null && _frame >= (ulong)_shotFrames)
        {
            Console.WriteLine($"{_shotClock.Elapsed.TotalMilliseconds / Math.Max(_shotFrames - 10, 1):0.00} ms a frame over the last {_shotFrames - 10}");
            Console.WriteLine(_scene.Status);
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
