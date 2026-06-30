// SilkWindow.cs - Simplified
using Common;
using Prowl.Graphite;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Scribe.Internal;
using Prowl.Vector;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

using Texture = Prowl.Graphite.Texture;
using Sampler = Prowl.Graphite.Sampler;
using Silk.NET.Windowing.Sdl;
using Silk.NET.Input.Sdl;
using System.Runtime.InteropServices;
using Silk.NET.SDL;


namespace GraphiteExample;


public class SilkWindow : IDisposable
{
    // Core components
    private IWindow _window;
    private IInputContext _input;

    private GraphicsDeviceOptions _deviceOptions;
    private GraphicsBackend _backend;
    private GraphicsDevice _device;
    private GraphiteRenderer _renderer;

    private Canvas _canvas;

    private List<IDemo> _demos;
    private int _currentDemoIndex;

    // View properties
    private Float2 _viewOffset = Float2.Zero;
    private float _zoom = 1.0f;
    private float _rotation = 0.0f;

    // Resources
    private TextureGraphite _whiteTexture;
    private TextureGraphite _demoTexture;
    private FontFile RobotoFont;
    private FontFile AlamakFont;

    // Input tracking
    private bool _isDragging = false;
    private Int2 _lastMousePos;


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


    public SilkWindow(WindowOptions windowOptions, GraphicsDeviceOptions deviceOptions, GraphicsBackend backend)
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
        _window.Update += OnUpdate;
        _window.Resize += OnResize;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        SetupInputHandlers();

        // Load textures
        _whiteTexture = TextureGraphite.LoadFromFile(_device, "Textures/white.png");
        _demoTexture = TextureGraphite.LoadFromFile(_device, "Textures/wall.png");

        // Set up renderer and canvas
        _renderer = new GraphiteRenderer(_device);
        _renderer.Initialize(_window.Size.X, _window.Size.Y, _whiteTexture);
        _canvas = new Canvas(_renderer, new FontAtlasSettings());

        // Load fonts
        RobotoFont = new FontFile(Path.Join(AppContext.BaseDirectory, "Fonts/Roboto.ttf"));
        AlamakFont = new FontFile(Path.Join(AppContext.BaseDirectory, "Fonts/Alamak.ttf"));

        // Initialize demos
        _demos = new List<IDemo>
            {
                new CanvasDemo(_canvas, _demoTexture, RobotoFont, AlamakFont),
                new SVGDemo(_canvas),
                new BenchmarkScene(_canvas, RobotoFont),
            };
    }


    private void SetupInputHandlers()
    {
        // Set up keyboard handlers
        var keyboard = _input.Keyboards[0];
        keyboard.KeyDown += (kb, key, scancode) =>
        {
            if (key == Key.Escape) _window.Close();
            if (key == Key.Left)
                _currentDemoIndex = _currentDemoIndex - 1 < 0 ? _demos.Count - 1 : _currentDemoIndex - 1;
            if (key == Key.Right)
                _currentDemoIndex = _currentDemoIndex + 1 == _demos.Count ? 0 : _currentDemoIndex + 1;
            if (key == Key.Space)
                if (_demos[_currentDemoIndex] is SVGDemo svgDemo)
                    svgDemo.ParseSVG();
        };

        // Set up mouse handlers
        var mouse = _input.Mice[0];
        mouse.MouseDown += (_, button) =>
        {
            if (button == MouseButton.Left)
            {
                _isDragging = true;
                _lastMousePos = new Int2((int)mouse.Position.X, (int)mouse.Position.Y);
            }
        };

        mouse.MouseUp += (_, button) =>
        {
            if (button == MouseButton.Left) _isDragging = false;
        };

        mouse.MouseMove += (_, position) =>
        {
            if (_isDragging)
            {
                var delta = new Int2((int)position.X, (int)position.Y) - _lastMousePos;
                _viewOffset.X += delta.X * (1.0f / _zoom);
                _viewOffset.Y += delta.Y * (1.0f / _zoom);
                _lastMousePos = new Int2((int)position.X, (int)position.Y);
            }
        };

        mouse.Scroll += (_, scrollWheel) =>
        {
            _zoom = Maths.Max(0.1f, _zoom + scrollWheel.Y * 0.1f);
        };
    }


    private void OnRender(double deltaTime)
    {
        Frame frame = _device.BeginFrame();

        // Prepare canvas and record demo content. Canvas.Render() drives the renderer, which records
        // the whole pass into its command buffer (clear, draws, present) and leaves it completed.
        _canvas.BeginFrame(_window.Size.X, _window.Size.Y);
        _demos[_currentDemoIndex].RenderFrame((float)deltaTime, _viewOffset, _zoom, _rotation);
        _canvas.Render();

        frame.SubmitCommands(_renderer.CommandBuffer);
        _device.EndFrame(frame);
        _device.SwapBuffers();
    }


    private void OnUpdate(double deltaTime)
    {
        // Handle keyboard rotation
        var keyboard = _input.Keyboards[0];
        if (keyboard.IsKeyPressed(Key.Q)) _rotation += 10.0f * (float)deltaTime;
        if (keyboard.IsKeyPressed(Key.E)) _rotation -= 10.0f * (float)deltaTime;
    }


    private void OnResize(Silk.NET.Maths.Vector2D<int> newSize)
    {
        _renderer.UpdateProjection(newSize.X, newSize.Y);
    }


    private void OnClosing()
    {
        _demoTexture?.Dispose();
        _whiteTexture.Dispose();

        _renderer?.Cleanup();
    }


    public void Dispose()
    {
        _device?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}