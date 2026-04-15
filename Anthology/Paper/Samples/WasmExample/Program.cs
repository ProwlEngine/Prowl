using System.Runtime.InteropServices.JavaScript;
using Prowl.PaperUI;
using Prowl.Quill;

namespace WasmExample;

public partial class App
{
    private static WebGLCanvasRenderer _renderer = null!;
    private static Paper P = null!;

    static void Main() { }

    [JSExport]
    internal static void Init()
    {
        WebGLInterop.InitWebGL("canvas");

        _renderer = new WebGLCanvasRenderer();
        var (cw, ch) = _renderer.GetCanvasSize();

        P = new Paper(_renderer, cw, ch, new FontAtlasSettings());
        P.SetReferenceResolution(1280, 720);
        Shared.PaperDemo.Initialize(P);

        Console.WriteLine($"Initialized: {cw}x{ch}");
    }

    [JSExport]
    internal static void OnFrame(double deltaTime)
    {
        float dt = Math.Clamp((float)deltaTime, 0.001f, 0.1f);
        float dpiScale = (float)WebGLInterop.GetDevicePixelRatio();

        P.BeginFrame(dt, dpiScale);
        Shared.PaperDemo.RenderUI();
        P.EndFrame();
    }

    [JSExport]
    internal static void OnResize(int width, int height)
    {
        P.SetResolution(width, height);
    }

    [JSExport]
    internal static void OnMouseMove(double x, double y)
    {
        P.SetPointerState(PaperMouseBtn.Unknown, (float)x, (float)y, false, true);
    }

    [JSExport]
    internal static void OnMouseDown(double x, double y, int button)
    {
        P.SetPointerState(TranslateMouseButton(button), (float)x, (float)y, true, false);
    }

    [JSExport]
    internal static void OnMouseUp(double x, double y, int button)
    {
        P.SetPointerState(TranslateMouseButton(button), (float)x, (float)y, false, false);
    }

    [JSExport]
    internal static void OnWheel(double deltaY)
    {
        P.SetPointerWheel(-(float)deltaY / 100f);
    }

    [JSExport]
    internal static void OnKeyDown(string code)
    {
        var key = TranslateKeyCode(code);
        if (key != PaperKey.Unknown)
            P.SetKeyState(key, true);
    }

    [JSExport]
    internal static void OnKeyUp(string code)
    {
        var key = TranslateKeyCode(code);
        if (key != PaperKey.Unknown)
            P.SetKeyState(key, false);
    }

    [JSExport]
    internal static void OnTextInput(string text)
    {
        P.AddInputCharacter(text);
    }

    private static PaperMouseBtn TranslateMouseButton(int button) => button switch
    {
        0 => PaperMouseBtn.Left,
        1 => PaperMouseBtn.Middle,
        2 => PaperMouseBtn.Right,
        3 => PaperMouseBtn.Button4,
        4 => PaperMouseBtn.Button5,
        _ => PaperMouseBtn.Unknown,
    };

    private static PaperKey TranslateKeyCode(string code) => code switch
    {
        "KeyA" => PaperKey.A, "KeyB" => PaperKey.B, "KeyC" => PaperKey.C,
        "KeyD" => PaperKey.D, "KeyE" => PaperKey.E, "KeyF" => PaperKey.F,
        "KeyG" => PaperKey.G, "KeyH" => PaperKey.H, "KeyI" => PaperKey.I,
        "KeyJ" => PaperKey.J, "KeyK" => PaperKey.K, "KeyL" => PaperKey.L,
        "KeyM" => PaperKey.M, "KeyN" => PaperKey.N, "KeyO" => PaperKey.O,
        "KeyP" => PaperKey.P, "KeyQ" => PaperKey.Q, "KeyR" => PaperKey.R,
        "KeyS" => PaperKey.S, "KeyT" => PaperKey.T, "KeyU" => PaperKey.U,
        "KeyV" => PaperKey.V, "KeyW" => PaperKey.W, "KeyX" => PaperKey.X,
        "KeyY" => PaperKey.Y, "KeyZ" => PaperKey.Z,

        "Digit0" => PaperKey.Num0, "Digit1" => PaperKey.Num1, "Digit2" => PaperKey.Num2,
        "Digit3" => PaperKey.Num3, "Digit4" => PaperKey.Num4, "Digit5" => PaperKey.Num5,
        "Digit6" => PaperKey.Num6, "Digit7" => PaperKey.Num7, "Digit8" => PaperKey.Num8,
        "Digit9" => PaperKey.Num9,

        "F1" => PaperKey.F1, "F2" => PaperKey.F2, "F3" => PaperKey.F3,
        "F4" => PaperKey.F4, "F5" => PaperKey.F5, "F6" => PaperKey.F6,
        "F7" => PaperKey.F7, "F8" => PaperKey.F8, "F9" => PaperKey.F9,
        "F10" => PaperKey.F10, "F11" => PaperKey.F11, "F12" => PaperKey.F12,

        "Enter" => PaperKey.Enter, "NumpadEnter" => PaperKey.KeypadEnter,
        "Escape" => PaperKey.Escape, "Backspace" => PaperKey.Backspace,
        "Tab" => PaperKey.Tab, "Space" => PaperKey.Space,

        "Minus" => PaperKey.Minus, "Equal" => PaperKey.Equals,
        "BracketLeft" => PaperKey.LeftBracket, "BracketRight" => PaperKey.RightBracket,
        "Backslash" => PaperKey.Backslash, "Semicolon" => PaperKey.Semicolon,
        "Quote" => PaperKey.Apostrophe, "Backquote" => PaperKey.Grave,
        "Comma" => PaperKey.Comma, "Period" => PaperKey.Period,
        "Slash" => PaperKey.Slash,

        "CapsLock" => PaperKey.CapsLock, "PrintScreen" => PaperKey.PrintScreen,
        "ScrollLock" => PaperKey.ScrollLock, "Pause" => PaperKey.Pause,
        "Insert" => PaperKey.Insert, "Home" => PaperKey.Home,
        "PageUp" => PaperKey.PageUp, "Delete" => PaperKey.Delete,
        "End" => PaperKey.End, "PageDown" => PaperKey.PageDown,

        "ArrowRight" => PaperKey.Right, "ArrowLeft" => PaperKey.Left,
        "ArrowDown" => PaperKey.Down, "ArrowUp" => PaperKey.Up,

        "NumLock" => PaperKey.NumLock,
        "NumpadDivide" => PaperKey.KeypadDivide, "NumpadMultiply" => PaperKey.KeypadMultiply,
        "NumpadSubtract" => PaperKey.KeypadMinus, "NumpadAdd" => PaperKey.KeypadPlus,
        "NumpadEqual" => PaperKey.KeypadEquals, "NumpadDecimal" => PaperKey.KeypadDecimal,
        "Numpad0" => PaperKey.Keypad0, "Numpad1" => PaperKey.Keypad1,
        "Numpad2" => PaperKey.Keypad2, "Numpad3" => PaperKey.Keypad3,
        "Numpad4" => PaperKey.Keypad4, "Numpad5" => PaperKey.Keypad5,
        "Numpad6" => PaperKey.Keypad6, "Numpad7" => PaperKey.Keypad7,
        "Numpad8" => PaperKey.Keypad8, "Numpad9" => PaperKey.Keypad9,

        "ShiftLeft" => PaperKey.LeftShift, "ShiftRight" => PaperKey.RightShift,
        "ControlLeft" => PaperKey.LeftControl, "ControlRight" => PaperKey.RightControl,
        "AltLeft" => PaperKey.LeftAlt, "AltRight" => PaperKey.RightAlt,
        "MetaLeft" => PaperKey.LeftSuper, "MetaRight" => PaperKey.RightSuper,
        "ContextMenu" => PaperKey.Menu,

        _ => PaperKey.Unknown,
    };
}
