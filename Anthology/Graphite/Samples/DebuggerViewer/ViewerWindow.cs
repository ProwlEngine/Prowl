using DebuggerViewer.Rendering;
using DebuggerViewer.Scene;

using Prowl.Graphite;
using Prowl.Graphite.Debugger.Viewer;
using Prowl.Graphite.Samples;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;
using Prowl.Vector;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

using Color = System.Drawing.Color;

namespace DebuggerViewer;

internal sealed class ViewerWindow : IDisposable
{
    private static readonly Dictionary<Key, PaperKey> KeyOverrides = new()
    {
        [Key.Number0] = PaperKey.Num0, [Key.Number1] = PaperKey.Num1, [Key.Number2] = PaperKey.Num2, [Key.Number3] = PaperKey.Num3, [Key.Number4] = PaperKey.Num4,
        [Key.Number5] = PaperKey.Num5, [Key.Number6] = PaperKey.Num6, [Key.Number7] = PaperKey.Num7, [Key.Number8] = PaperKey.Num8, [Key.Number9] = PaperKey.Num9,
        [Key.Equal] = PaperKey.Equals, [Key.BackSlash] = PaperKey.Backslash, [Key.GraveAccent] = PaperKey.Grave,
        [Key.KeypadSubtract] = PaperKey.KeypadMinus, [Key.KeypadAdd] = PaperKey.KeypadPlus, [Key.KeypadEqual] = PaperKey.KeypadEquals,
        [Key.ShiftLeft] = PaperKey.LeftShift, [Key.ControlLeft] = PaperKey.LeftControl, [Key.AltLeft] = PaperKey.LeftAlt, [Key.SuperLeft] = PaperKey.LeftSuper,
        [Key.ShiftRight] = PaperKey.RightShift, [Key.ControlRight] = PaperKey.RightControl, [Key.AltRight] = PaperKey.RightAlt, [Key.SuperRight] = PaperKey.RightSuper,
    };

    private readonly Options _options;
    private readonly IWindow _window;
    private IInputContext _input = null!;
    private GraphicsDevice _device = null!;
    private GraphiteRenderer _renderer = null!;
    private PbrScenes _scene = null!;
    private SceneProfilerSource _source = null!;
    private Paper _paper = null!;
    private DockSpace _dock = null!;
    private RenderProfilerWindows _windows = null!;
    private FontFile _wordmark = null!;
    private bool _showPreview = true;
    private float _fps;
    private int _frame;

    public ViewerWindow(Options options)
    {
        _options = options;
        DeviceCreateUtilities.InitializeWindowing();

        WindowOptions windowOptions = WindowOptions.Default with
        {
            Title = "Graphite Debugger Viewer",
            Size = new Vector2D<int>(1600, 1000),
            IsVisible = options.ShotPath == null,
            VSync = false,
            ShouldSwapAutomatically = false,
            API = new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(2, 1)),
        };

        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _device = DeviceCreateUtilities.CreateDevice(_window, new GraphicsDeviceOptions { PreferDepthRangeZeroToOne = true }, new SwapchainDescription { SyncToVerticalBlank = true }, GraphicsBackend.Vulkan);
        _device.SyncToVerticalBlank = true;
        _window.FramebufferResize += size => _device.ResizeMainWindow((uint)size.X, (uint)size.Y);

        _renderer = new GraphiteRenderer(_device);
        _renderer.Initialize(_window.FramebufferSize.X, _window.FramebufferSize.Y);

        _paper = new Paper(_renderer, _window.Size.X, _window.Size.Y, new Prowl.Quill.FontAtlasSettings());
        _input = _window.CreateInput();
        _paper.OnCursorChange += c => _input.Mice[0].Cursor.StandardCursor = MapCursor(c);
        SetupInput();
        LoadFonts();

        _scene = new PbrScenes(_device);
        _source = new SceneProfilerSource(_device, _scene) { IsRecording = !_options.Paused };
        _dock = new DockSpace(DockNode.Leaf());
        _windows = new RenderProfilerWindows(_source, _dock);
        _dock.Root = DockNode.Leaf(_windows.Live);
    }

    private void LoadFonts()
    {
        string fonts = Path.Combine(AppContext.BaseDirectory, "Fonts");
        FontFile Load(string file)
        {
            using FileStream stream = File.OpenRead(Path.Combine(fonts, file));
            return new FontFile(stream);
        }

        _wordmark = Load("SpaceGrotesk-Bold.ttf");
    }

    private void SetupInput()
    {
        IKeyboard keyboard = _input.Keyboards[0];
        keyboard.KeyDown += (_, key, _) => SetKey(key, true);
        keyboard.KeyUp += (_, key, _) => SetKey(key, false);
        keyboard.KeyChar += (_, c) => _paper.AddInputCharacter(c.ToString());

        IMouse mouse = _input.Mice[0];
        mouse.MouseDown += (m, button) => _paper.SetPointerState(Translate(button), m.Position.X, m.Position.Y, true, false);
        mouse.MouseUp += (m, button) => _paper.SetPointerState(Translate(button), m.Position.X, m.Position.Y, false, false);
        mouse.MouseMove += (m, position) =>
        {
            if (_paper.TryWrapPointer(new Float2(position.X, position.Y), _window.Size.X, _window.Size.Y, out Float2 wrapped))
            {
                m.Position = new System.Numerics.Vector2((float)wrapped.X, (float)wrapped.Y);
                position = m.Position;
            }
            _paper.SetPointerState(PaperMouseBtn.Unknown, position.X, position.Y, false, true);
        };
        mouse.Scroll += (_, wheel) => _paper.SetPointerWheel(wheel.Y);
    }

    private void SetKey(Key key, bool down)
    {
        PaperKey mapped = KeyOverrides.TryGetValue(key, out PaperKey o) ? o : Enum.TryParse(key.ToString(), out PaperKey p) ? p : PaperKey.Unknown;
        if (mapped != PaperKey.Unknown)
            _paper.SetKeyState(mapped, down);
    }

    private void OnRender(double deltaTime)
    {
        foreach (ScriptStep step in _options.Script.Where(s => s.Frame == _frame))
        {
            _paper.SetPointerState(PaperMouseBtn.Unknown, step.X, step.Y, false, true);
            if (step.Click)
                _paper.SetPointerState(PaperMouseBtn.Left, step.X, step.Y, true, false);
        }
        foreach (ScriptStep step in _options.Script.Where(s => s.Click && s.Frame + 1 == _frame))
            _paper.SetPointerState(PaperMouseBtn.Left, step.X, step.Y, false, false);

        float dt = (float)deltaTime;
        _source.Update(dt);

        float dpiScale = (float)_window.FramebufferSize.X / _window.Size.X;
        _paper.BeginFrame(dt, dpiScale);
        Origami.BeginFrame(_paper, dt);
        if (dt > 0.0001f)
            _fps = _fps <= 0f ? 1f / dt : _fps * 0.92f + (1f / dt) * 0.08f;

        const float gap = 8f, top = 44f;
        float width = _window.Size.X, height = _window.Size.Y;
        using (_paper.Column("host").Width(width).Height(height).BackgroundColor(Color.FromArgb(17, 17, 19)).Enter())
        {
            TopBar(width);
            _dock.Draw(_paper, gap, top, width - gap * 2, height - top - gap);
            if (_showPreview)
                ScenePreview(width, height);
        }
        Origami.EndFrame(_paper);

        bool shoot = _options.ShotPath != null && _frame + 1 >= _options.ShotFrames;
        if (shoot)
            _renderer.Screenshot(_options.ShotPath!);

        _paper.EndFrame();
        _frame++;

        if (shoot)
            _window.Close();
    }

    private void TopBar(float width)
    {
        OrigamiTheme theme = Origami.Current;
        using (_paper.Row("topbar").PositionType(PositionType.SelfDirected).Position(16, 0).Width(width - 32).Height(44)
            .Gap(9).AlignItems(LayoutAlignment.Center).Enter())
        {
            using (_paper.Box("logo").Size(20).Rounded(6).BackgroundLinearGradient(0, 0, 1, 1, theme.Primary.C500, Color.FromArgb(189, 107, 255)).Enter())
                _paper.Draw((vg, r) => OrigamiGlyphs.Cube_I.Draw(vg, new Rect(new Float2((float)r.Min.X + 4, (float)r.Min.Y + 4), new Float2((float)r.Min.X + 16, (float)r.Min.Y + 16)), Color.White, 1.2f));

            _paper.Box("brand").Width(UnitValue.Auto).Height(UnitValue.Auto)
                .Text("PROWL", _wordmark).FontSize(18).LetterSpacing(3f).TextColor(theme.Ink.C500).IsNotInteractable();
            _paper.Box("product").Width(UnitValue.Auto).Height(UnitValue.Auto)
                .Text("Graphite Debugger", theme.Mono!).FontSize(14).TextColor(theme.Ink.C200).IsNotInteractable();

            Origami.MenuBar(_paper, "menu")
                .Menu("File", m => m
                    .Item("Load snapshot...", _windows.LoadSnapshot)
                    .Separator()
                    .Item("Exit", _window.Close))
                .Menu("Capture", m => m
                    .Item(_source.IsRecording ? "Stop recording" : "Start recording", () => _source.IsRecording = !_source.IsRecording))
                .Menu("Scene", m =>
                {
                    for (int i = 0; i < PbrScenes.Names.Length; i++)
                    {
                        int index = i;
                        m.Item((_scene.Selected == i ? "> " : "") + PbrScenes.Names[i], () => _scene.Select(index));
                    }
                    m.Separator().Item(_showPreview ? "Hide preview" : "Show preview", () => _showPreview = !_showPreview);
                })
                .Menu("Window", m => m
                    .Item("Render Profiler", _windows.ShowLive)
                    .Item("Snapshots", _windows.ShowSnapshots))
                .Transparent()
                .Show();

            _paper.Box("spacer").Width(UnitValue.Stretch()).Height(1);
            _paper.Box("fps").Width(UnitValue.Auto).Height(UnitValue.Auto)
                .Text($"{_fps:F0} FPS", theme.Mono!).FontSize(15).TextColor(theme.Ink.C200).IsNotInteractable();
        }
    }

    private void ScenePreview(float width, float height)
    {
        const float previewWidth = 320f, previewHeight = previewWidth * PbrScenes.Height / PbrScenes.Width, margin = 20f, header = 26f;
        OrigamiTheme theme = Origami.Current;
        object? image = _renderer.Preview(_scene.Target.ColorTextures[0]);
        using (_paper.Column("preview").PositionType(PositionType.SelfDirected)
            .Position(width - previewWidth - margin, height - previewHeight - header - margin)
            .Width(previewWidth).Height(previewHeight + header).Rounded(8).BackgroundColor(Color.FromArgb(230, 24, 24, 28)).Enter())
        {
            _paper.Box("title").Width(UnitValue.Stretch()).Height(header).Margin(10, 0, 0, 0)
                .Text($"Scene / {PbrScenes.Names[_scene.Selected]}", theme.Mono!).FontSize(13).TextColor(theme.Ink.C300).IsNotInteractable();
            using (_paper.Box("image").Width(previewWidth).Height(previewHeight).IsNotInteractable().Enter())
            {
                _paper.Draw((canvas, r) =>
                {
                    if (image != null)
                        canvas.DrawImage(image, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y);
                });
            }
        }
    }

    private void OnResize(Vector2D<int> size)
    {
        _paper.SetResolution(_window.Size.X, _window.Size.Y);
        _renderer.UpdateProjection(_window.FramebufferSize.X, _window.FramebufferSize.Y);
    }

    private void OnClosing()
    {
        _device.WaitForIdle();
        _scene.Dispose();
        _renderer.Dispose();
    }

    public void Dispose()
    {
        _device?.Dispose();
        _input?.Dispose();
        _window.Dispose();
    }

    private static StandardCursor MapCursor(PaperCursor c) => c switch
    {
        PaperCursor.Pointer or PaperCursor.Grab or PaperCursor.Grabbing => StandardCursor.Hand,
        PaperCursor.Text => StandardCursor.IBeam,
        PaperCursor.Crosshair => StandardCursor.Crosshair,
        PaperCursor.ResizeHorizontal => StandardCursor.HResize,
        PaperCursor.ResizeVertical => StandardCursor.VResize,
        PaperCursor.ResizeNWSE => StandardCursor.NwseResize,
        PaperCursor.ResizeNESW => StandardCursor.NeswResize,
        PaperCursor.ResizeAll => StandardCursor.ResizeAll,
        PaperCursor.NotAllowed => StandardCursor.NotAllowed,
        PaperCursor.Wait => StandardCursor.Wait,
        _ => StandardCursor.Default,
    };

    private static PaperMouseBtn Translate(MouseButton button) => button switch
    {
        MouseButton.Left => PaperMouseBtn.Left,
        MouseButton.Right => PaperMouseBtn.Right,
        MouseButton.Middle => PaperMouseBtn.Middle,
        _ => PaperMouseBtn.Unknown,
    };
}
