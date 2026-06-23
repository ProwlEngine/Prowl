using System.Runtime.InteropServices;

using Prowl.Graphite;
using Prowl.PaperUI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Shared;

using Silk.NET.Input;
using Silk.NET.Input.Sdl;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

namespace GraphiteSample;


public class GraphiteWindow : IDisposable
{
    private IWindow _window;
    private IInputContext _input;

    private GraphicsDeviceOptions _deviceOptions;
    private GraphicsBackend _backend;
    private GraphicsDevice _device;
    private GraphiteRenderer _renderer;
    private TextureGraphite _whiteTexture;

    private Paper _paper;


    private static void MoltenVKMacWorkaround(GraphicsBackend backend)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || backend != GraphicsBackend.Vulkan)
            return;

        SdlWindowing.RegisterPlatform();
        SdlWindowing.Use();
        SdlInput.Use();

        Sdl? sdl = Sdl.GetApi();

        if (sdl.Init(Sdl.InitVideo) != 0)
            Console.WriteLine($"SDL video initialization failed: {sdl.GetErrorS()}");

        string basePath = Environment.ProcessPath != null ? AppContext.BaseDirectory :
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        string libraryPath = Path.Join(basePath, "runtimes/osx/native/libMoltenVK.dylib");

        if (sdl.VulkanLoadLibrary(libraryPath) != 0)
            Console.WriteLine($"SDL VulkanLoadLibrary failed for '{libraryPath}': {sdl.GetErrorS()}");
    }


    public GraphiteWindow(WindowOptions windowOptions, GraphicsDeviceOptions deviceOptions, GraphicsBackend backend)
    {
        _deviceOptions = deviceOptions;
        _backend = backend;

        MoltenVKMacWorkaround(backend);
        _window = Silk.NET.Windowing.Window.Create(windowOptions);
        _window.Load += OnLoad;
    }


    public void Run()
    {
        _window.Run();
    }


    private void OnLoad()
    {
        _device = DeviceCreateUtilities.CreateDevice(_window, _deviceOptions, _backend);

        _input = _window.CreateInput();
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;

        SetupInputHandlers();

        _whiteTexture = TextureGraphite.CreateTexture(_device, 1, 1);
        _whiteTexture.SetTextureData(_device, new IntRect(0, 0, 1, 1), new byte[] { 255, 255, 255, 255 });

        _renderer = new GraphiteRenderer(_device);
        _renderer.Initialize(_window.FramebufferSize.X, _window.FramebufferSize.Y, _whiteTexture);

        _paper = new Paper(_renderer, _window.Size.X, _window.Size.Y, new Prowl.Quill.FontAtlasSettings());
        PaperDemo.Initialize(_paper);
    }


    private void SetupInputHandlers()
    {
        var keyboard = _input.Keyboards[0];
        keyboard.KeyDown += (_, key, _) => _paper.SetKeyState(TranslateKey(key), true);
        keyboard.KeyUp += (_, key, _) => _paper.SetKeyState(TranslateKey(key), false);
        keyboard.KeyChar += (_, c) => _paper.AddInputCharacter(c.ToString());

        var mouse = _input.Mice[0];
        mouse.MouseDown += (m, button) => _paper.SetPointerState(TranslateMouseButton(button), m.Position.X, m.Position.Y, true, false);
        mouse.MouseUp += (m, button) => _paper.SetPointerState(TranslateMouseButton(button), m.Position.X, m.Position.Y, false, false);
        mouse.MouseMove += (_, position) => _paper.SetPointerState(PaperMouseBtn.Unknown, position.X, position.Y, false, true);
        mouse.Scroll += (_, wheel) => _paper.SetPointerWheel(wheel.Y);
    }


    private void OnRender(double deltaTime)
    {
        float dpiScale = (float)_window.FramebufferSize.X / _window.Size.X;

        Frame frame = _device.BeginFrame();

        _paper.BeginFrame((float)deltaTime, dpiScale);
        PaperDemo.RenderUI();
        _paper.EndFrame();

        frame.SubmitCommands(_renderer.CommandBuffer);
        _device.EndFrame(frame);
        _device.SwapBuffers();
    }


    private void OnResize(Silk.NET.Maths.Vector2D<int> newSize)
    {
        _paper.SetResolution(_window.Size.X, _window.Size.Y);
        _renderer.UpdateProjection(_window.FramebufferSize.X, _window.FramebufferSize.Y);
    }


    private void OnClosing()
    {
        _whiteTexture?.Dispose();
        _renderer?.Cleanup();
    }


    public void Dispose()
    {
        _device?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }


    private static PaperMouseBtn TranslateMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => PaperMouseBtn.Left,
            MouseButton.Right => PaperMouseBtn.Right,
            MouseButton.Middle => PaperMouseBtn.Middle,
            _ => PaperMouseBtn.Unknown
        };
    }


    public static PaperKey TranslateKey(Key key)
    {
        return key switch
        {
            Key.Space => PaperKey.Space,
            Key.Apostrophe => PaperKey.Apostrophe,
            Key.Comma => PaperKey.Comma,
            Key.Minus => PaperKey.Minus,
            Key.Period => PaperKey.Period,
            Key.Slash => PaperKey.Slash,
            Key.Number0 => PaperKey.Num0,
            Key.Number1 => PaperKey.Num1,
            Key.Number2 => PaperKey.Num2,
            Key.Number3 => PaperKey.Num3,
            Key.Number4 => PaperKey.Num4,
            Key.Number5 => PaperKey.Num5,
            Key.Number6 => PaperKey.Num6,
            Key.Number7 => PaperKey.Num7,
            Key.Number8 => PaperKey.Num8,
            Key.Number9 => PaperKey.Num9,
            Key.Semicolon => PaperKey.Semicolon,
            Key.Equal => PaperKey.Equals,
            Key.A => PaperKey.A,
            Key.B => PaperKey.B,
            Key.C => PaperKey.C,
            Key.D => PaperKey.D,
            Key.E => PaperKey.E,
            Key.F => PaperKey.F,
            Key.G => PaperKey.G,
            Key.H => PaperKey.H,
            Key.I => PaperKey.I,
            Key.J => PaperKey.J,
            Key.K => PaperKey.K,
            Key.L => PaperKey.L,
            Key.M => PaperKey.M,
            Key.N => PaperKey.N,
            Key.O => PaperKey.O,
            Key.P => PaperKey.P,
            Key.Q => PaperKey.Q,
            Key.R => PaperKey.R,
            Key.S => PaperKey.S,
            Key.T => PaperKey.T,
            Key.U => PaperKey.U,
            Key.V => PaperKey.V,
            Key.W => PaperKey.W,
            Key.X => PaperKey.X,
            Key.Y => PaperKey.Y,
            Key.Z => PaperKey.Z,
            Key.LeftBracket => PaperKey.LeftBracket,
            Key.BackSlash => PaperKey.Backslash,
            Key.RightBracket => PaperKey.RightBracket,
            Key.GraveAccent => PaperKey.Grave,
            Key.Escape => PaperKey.Escape,
            Key.Enter => PaperKey.Enter,
            Key.Tab => PaperKey.Tab,
            Key.Backspace => PaperKey.Backspace,
            Key.Insert => PaperKey.Insert,
            Key.Delete => PaperKey.Delete,
            Key.Right => PaperKey.Right,
            Key.Left => PaperKey.Left,
            Key.Down => PaperKey.Down,
            Key.Up => PaperKey.Up,
            Key.PageUp => PaperKey.PageUp,
            Key.PageDown => PaperKey.PageDown,
            Key.Home => PaperKey.Home,
            Key.End => PaperKey.End,
            Key.CapsLock => PaperKey.CapsLock,
            Key.ScrollLock => PaperKey.ScrollLock,
            Key.NumLock => PaperKey.NumLock,
            Key.PrintScreen => PaperKey.PrintScreen,
            Key.Pause => PaperKey.Pause,
            Key.F1 => PaperKey.F1,
            Key.F2 => PaperKey.F2,
            Key.F3 => PaperKey.F3,
            Key.F4 => PaperKey.F4,
            Key.F5 => PaperKey.F5,
            Key.F6 => PaperKey.F6,
            Key.F7 => PaperKey.F7,
            Key.F8 => PaperKey.F8,
            Key.F9 => PaperKey.F9,
            Key.F10 => PaperKey.F10,
            Key.F11 => PaperKey.F11,
            Key.F12 => PaperKey.F12,
            Key.Keypad0 => PaperKey.Keypad0,
            Key.Keypad1 => PaperKey.Keypad1,
            Key.Keypad2 => PaperKey.Keypad2,
            Key.Keypad3 => PaperKey.Keypad3,
            Key.Keypad4 => PaperKey.Keypad4,
            Key.Keypad5 => PaperKey.Keypad5,
            Key.Keypad6 => PaperKey.Keypad6,
            Key.Keypad7 => PaperKey.Keypad7,
            Key.Keypad8 => PaperKey.Keypad8,
            Key.Keypad9 => PaperKey.Keypad9,
            Key.KeypadDecimal => PaperKey.KeypadDecimal,
            Key.KeypadDivide => PaperKey.KeypadDivide,
            Key.KeypadMultiply => PaperKey.KeypadMultiply,
            Key.KeypadSubtract => PaperKey.KeypadMinus,
            Key.KeypadAdd => PaperKey.KeypadPlus,
            Key.KeypadEnter => PaperKey.KeypadEnter,
            Key.KeypadEqual => PaperKey.KeypadEquals,
            Key.ShiftLeft => PaperKey.LeftShift,
            Key.ControlLeft => PaperKey.LeftControl,
            Key.AltLeft => PaperKey.LeftAlt,
            Key.SuperLeft => PaperKey.LeftSuper,
            Key.ShiftRight => PaperKey.RightShift,
            Key.ControlRight => PaperKey.RightControl,
            Key.AltRight => PaperKey.RightAlt,
            Key.SuperRight => PaperKey.RightSuper,
            Key.Menu => PaperKey.Menu,
            _ => PaperKey.Unknown
        };
    }
}
