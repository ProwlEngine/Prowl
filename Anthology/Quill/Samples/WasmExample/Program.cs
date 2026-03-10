using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using Common;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using StbImageSharp;

namespace WasmExample;

public partial class App
{
    private static Canvas _canvas = null!;
    private static WebGLCanvasRenderer _renderer = null!;
    private static List<IDemo> _demos = new();
    private static int _currentDemoIndex;

    // Camera
    private static Float2 _offset = Float2.Zero;
    private static float _zoom = 1.0f;
    private static float _rotation = 0.0f;

    // Input
    private static double _mouseX, _mouseY;
    private static double _prevMouseX, _prevMouseY;
    private static bool _mouseDown;
    private static bool _keyQ, _keyE;

    static void Main()
    {
        // Entry point — actual init happens in Init() called from JS after module imports are ready.
    }

    [JSExport]
    internal static void Init()
    {
        var asm = Assembly.GetExecutingAssembly();

        WebGLInterop.InitWebGL("canvas");

        _renderer = new WebGLCanvasRenderer();
        var (cw, ch) = _renderer.GetCanvasSize();
        _canvas = new Canvas(_renderer, new FontAtlasSettings());

        // Load fonts from embedded resources
        var robotoFont = LoadFontResource(asm, "Fonts.Roboto.ttf");
        var alamakFont = LoadFontResource(asm, "Fonts.Alamak.ttf");

        // Load texture from embedded resource
        object? wallTexture = LoadTextureResource(asm, "Textures.wall.png");

        // Load SVGs from embedded resources
        // Common.projitems SVGs get embedded with names like "WasmExample.path.to.file.svg"
        var svgElements = LoadSVGResources(asm);

        _demos = new List<IDemo>
        {
            new CanvasDemo(_canvas, cw, ch, wallTexture!, robotoFont!, alamakFont!),
            new SVGDemo(_canvas, cw, ch),
            new BenchmarkScene(_canvas, robotoFont!, cw, ch),
        };

        Console.WriteLine($"Initialized: {cw}x{ch}, {svgElements.Count} SVGs, {_demos.Count} demos");
    }

    private static FontFile? LoadFontResource(Assembly asm, string logicalName)
    {
        using var stream = asm.GetManifestResourceStream(logicalName);
        if (stream == null)
        {
            Console.WriteLine($"Font resource not found: {logicalName}");
            return null;
        }
        return new FontFile(stream);
    }

    private static object? LoadTextureResource(Assembly asm, string logicalName)
    {
        using var stream = asm.GetManifestResourceStream(logicalName);
        if (stream == null)
        {
            Console.WriteLine($"Texture resource not found: {logicalName}");
            return null;
        }

        try
        {
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var texId = _renderer.CreateTexture((uint)image.Width, (uint)image.Height);
            _renderer.SetTextureData(texId, new IntRect(0, 0, image.Width, image.Height), image.Data);
            return texId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Texture load error: {ex.Message}");
            return null;
        }
    }

    private static List<SvgElement> LoadSVGResources(Assembly asm)
    {
        var elements = new List<SvgElement>();

        // SVGs are embedded resources. Write to temp dir so SVGParser.ParseSVGDocument can load them.
        var svgResources = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n);

        // Also write to "SVGs/" directory for the Common SVGDemo which calls Directory.GetFiles("SVGs/")
        Directory.CreateDirectory("SVGs");

        foreach (var name in svgResources)
        {
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;

                // Extract a simple filename from the resource name
                var parts = name.Split('.');
                var fileName = parts.Length >= 3
                    ? string.Join(".", parts.Skip(parts.Length - 2))  // "filename.svg"
                    : name;

                var tempPath = Path.Combine("SVGs", fileName);
                using (var fs = File.Create(tempPath))
                    stream.CopyTo(fs);

                var el = SVGParser.ParseSVGDocument(tempPath);
                if (el != null) elements.Add(el);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SVG error ({name}): {ex.Message}");
            }
        }

        return elements;
    }

    [JSExport]
    internal static void OnFrame(double deltaTimeD)
    {
        if (_demos.Count == 0) return;

        float deltaTime = Math.Clamp((float)deltaTimeD, 0.001f, 0.1f);

        if (_keyQ) _rotation += 10f * deltaTime;
        if (_keyE) _rotation -= 10f * deltaTime;

        _canvas.Clear();
        _demos[_currentDemoIndex].RenderFrame(deltaTime, _offset, _zoom, _rotation);
        _canvas.Render();
    }

    [JSExport]
    internal static void OnMouseMove(double x, double y)
    {
        _prevMouseX = _mouseX;
        _prevMouseY = _mouseY;
        _mouseX = x;
        _mouseY = y;

        if (_mouseDown)
        {
            float dx = (float)(_mouseX - _prevMouseX);
            float dy = (float)(_mouseY - _prevMouseY);
            _offset.X += dx * (1f / _zoom);
            _offset.Y += dy * (1f / _zoom);
        }
    }

    [JSExport]
    internal static void OnMouseDown() => _mouseDown = true;

    [JSExport]
    internal static void OnMouseUp() => _mouseDown = false;

    [JSExport]
    internal static void OnWheel(double deltaY)
    {
        _zoom += -(float)deltaY * 0.001f;
        if (_zoom < 0.1f) _zoom = 0.1f;
    }

    [JSExport]
    internal static void OnKeyDown(string key)
    {
        switch (key)
        {
            case "q": case "Q": _keyQ = true; break;
            case "e": case "E": _keyE = true; break;
            case "ArrowLeft":
                _currentDemoIndex = _currentDemoIndex - 1 < 0 ? _demos.Count - 1 : _currentDemoIndex - 1;
                break;
            case "ArrowRight":
                _currentDemoIndex = (_currentDemoIndex + 1) % _demos.Count;
                break;
        }
    }

    [JSExport]
    internal static void OnKeyUp(string key)
    {
        switch (key)
        {
            case "q": case "Q": _keyQ = false; break;
            case "e": case "E": _keyE = false; break;
        }
    }
}
