// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;

using OpenTKSample;

using Prowl.PaperUI;
using Prowl.Vector;

namespace OrigamiSample;

public class PaperTKWindow : GameWindow
{
    private PaperRenderer _renderer;
    private Paper _paper;
    private NebulaEditor _editor;

    private readonly string _shotPath;
    private readonly int _shotFrames;
    private readonly bool _textCompare;
    private int _frame;

    public PaperTKWindow(GameWindowSettings gws, NativeWindowSettings nws, string shotPath, int shotFrames, int initialCat, bool textCompare = false)
        : base(gws, nws)
    {
        _shotPath = shotPath;
        _shotFrames = shotFrames;
        _textCompare = textCompare;
        _initialCat = initialCat;
    }

    private readonly int _initialCat;

    protected override void OnLoad()
    {
        base.OnLoad();
        _renderer = new PaperRenderer();
        _renderer.Initialize(FramebufferSize.X, FramebufferSize.Y);
        _paper = new Paper(_renderer, ClientSize.X, ClientSize.Y, new Prowl.Quill.FontAtlasSettings());
        _paper.OnCursorChange += c => Cursor = MapCursor(c);
        _paper.SetClipboardHandler(new WindowClipboard(this));
        _editor = new NebulaEditor(_paper);
        _editor.SetPlaygroundCategory(_initialCat);
    }

    [Obsolete]
    private static MouseCursor MapCursor(PaperCursor c) => c switch
    {
        PaperCursor.Pointer => MouseCursor.PointingHand,
        PaperCursor.Grab or PaperCursor.Grabbing => MouseCursor.Hand,
        PaperCursor.Text => MouseCursor.IBeam,
        PaperCursor.Crosshair => MouseCursor.Crosshair,
        PaperCursor.ResizeHorizontal => MouseCursor.ResizeEW,
        PaperCursor.ResizeVertical => MouseCursor.ResizeNS,
        PaperCursor.ResizeNWSE => MouseCursor.ResizeNWSE,
        PaperCursor.ResizeNESW => MouseCursor.ResizeNESW,
        PaperCursor.ResizeAll => MouseCursor.ResizeAll,
        PaperCursor.NotAllowed => MouseCursor.NotAllowed,
        _ => MouseCursor.Default,
    };

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.ClearColor(0.024f, 0.016f, 0.035f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        // Optional hover simulation for screenshot verification (env PROWL_HOVER="x,y")
        var hoverEnv = Environment.GetEnvironmentVariable("PROWL_HOVER");
        if (hoverEnv != null)
        {
            var parts = hoverEnv.Split(',');
            if (parts.Length == 2 && float.TryParse(parts[0], out var hx) && float.TryParse(parts[1], out var hy))
                _paper.SetPointerState(PaperMouseBtn.Unknown, hx, hy, false, true);
        }

        // Optional click simulation for screenshotting popovers: PROWL_CLICK="x,y" clicks once.
        var clickEnv = Environment.GetEnvironmentVariable("PROWL_CLICK");
        if (clickEnv != null)
        {
            var cp = clickEnv.Split(',');
            if (cp.Length == 2 && float.TryParse(cp[0], out var qx) && float.TryParse(cp[1], out var qy))
            {
                int clickFrame = int.TryParse(Environment.GetEnvironmentVariable("PROWL_CLICK_FRAME"), out int cf) ? cf : 3;
                if (_frame == clickFrame) _paper.SetPointerState(PaperMouseBtn.Left, qx, qy, true, false);
                else if (_frame == clickFrame + 1) _paper.SetPointerState(PaperMouseBtn.Left, qx, qy, false, false);
                else if (hoverEnv == null) _paper.SetPointerState(PaperMouseBtn.Unknown, qx, qy, false, true);
                // else: after the click, let PROWL_HOVER own the pointer (click-then-hover testing).
            }
        }

        // Optional drag simulation: PROWL_DRAG="x1,y1,x2,y2" presses at (x1,y1), drags to (x2,y2), releases.
        var dragEnv = Environment.GetEnvironmentVariable("PROWL_DRAG");
        if (dragEnv != null)
        {
            var dp = dragEnv.Split(',');
            if (dp.Length == 4 && float.TryParse(dp[0], out var ax) && float.TryParse(dp[1], out var ay)
                && float.TryParse(dp[2], out var bx) && float.TryParse(dp[3], out var by))
            {
                int downF = 3, upF = Math.Max(downF + 4, _shotFrames - 2);
                int reachF = upF - 2;  // reach the target early, then hold it so hover settles before release
                if (_frame < downF) _paper.SetPointerState(PaperMouseBtn.Unknown, ax, ay, false, true);
                else if (_frame == downF) _paper.SetPointerState(PaperMouseBtn.Left, ax, ay, true, false);
                else if (_frame < upF)
                {
                    float t = Math.Min(1f, (float)(_frame - downF) / Math.Max(1, reachF - downF));
                    _paper.SetPointerState(PaperMouseBtn.Left, ax + (bx - ax) * t, ay + (by - ay) * t, true, true);
                }
                else if (_frame == upF) _paper.SetPointerState(PaperMouseBtn.Left, bx, by, false, false);
                else _paper.SetPointerState(PaperMouseBtn.Unknown, bx, by, false, true);
            }
        }

        // Optional right-click simulation for context menus: PROWL_RCLICK="x,y" right-clicks once.
        var rclickEnv = Environment.GetEnvironmentVariable("PROWL_RCLICK");
        if (rclickEnv != null)
        {
            var cp = rclickEnv.Split(',');
            if (cp.Length == 2 && float.TryParse(cp[0], out var qx) && float.TryParse(cp[1], out var qy))
            {
                if (_frame == 3) _paper.SetPointerState(PaperMouseBtn.Right, qx, qy, true, false);
                else if (_frame == 4) _paper.SetPointerState(PaperMouseBtn.Right, qx, qy, false, false);
                else _paper.SetPointerState(PaperMouseBtn.Unknown, qx, qy, false, true);
            }
        }

        SimulateTyping();

        Bench();

        float dpiScale = (float)FramebufferSize.X / ClientSize.X;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _paper.BeginFrame((float)args.Time, dpiScale);
        if (_textCompare) TextCompare.Render(_paper, ClientSize.X, ClientSize.Y);
        else _editor.Render(ClientSize.X, ClientSize.Y);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _paper.EndFrame();
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_benchMode != null) BenchRecord(t0, t1, t2);

        if (_shotPath != null && ++_frame >= _shotFrames)
        {
            CaptureScreenshot(_shotPath);
            Close();
            return;
        }

        SwapBuffers();
    }

    private void CaptureScreenshot(string path)
    {
        int w = FramebufferSize.X, h = FramebufferSize.Y;
        var pixels = new byte[w * h * 4];
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        // OpenGL origin is bottom-left; flip vertically for image output.
        var flipped = new byte[pixels.Length];
        int stride = w * 4;
        for (int y = 0; y < h; y++)
            Array.Copy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);

        using var fs = File.OpenWrite(path);
        var writer = new StbImageWriteSharp.ImageWriter();
        writer.WritePng(flipped, w, h, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fs);
        Console.WriteLine($"Saved screenshot: {path} ({w}x{h})");
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        _paper.SetResolution(ClientSize.X, ClientSize.Y);
        _renderer.UpdateProjection(FramebufferSize.X, FramebufferSize.Y);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        _paper.SetPointerState(Translate(e.Button), MouseState.X, MouseState.Y, true, false);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _paper.SetPointerState(Translate(e.Button), MouseState.X, MouseState.Y, false, false);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);

        float mx = MouseState.X, my = MouseState.Y;

        // Infinite drag: a widget calling WrapPointer wants motion that never stops at an edge, so
        // teleport the cursor to the opposite side and let Paper discount the jump.
        if (_paper.TryWrapPointer(new Float2(mx, my), ClientSize.X, ClientSize.Y, out Float2 wrapped))
        {
            MousePosition = new global::OpenTK.Mathematics.Vector2((float)wrapped.X, (float)wrapped.Y);
            mx = (float)wrapped.X;
            my = (float)wrapped.Y;
        }

        _paper.SetPointerState(PaperMouseBtn.Unknown, mx, my, false, true);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _paper.SetPointerWheel(e.OffsetY);
        _paper.SetPointerWheelX(e.OffsetX);
    }

    // ── Keyboard + text input ────────────────────────────────────────────
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        var pk = MapKey(e.Key);
        if (pk != PaperKey.Unknown) _paper.SetKeyState(pk, true);
    }

    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        base.OnKeyUp(e);
        var pk = MapKey(e.Key);
        if (pk != PaperKey.Unknown) _paper.SetKeyState(pk, false);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        _paper.AddInputCharacter(e.AsString);
    }

    // OpenTK's Keys enum names line up with PaperKey for letters/nav/modifiers; only the
    // number row (D0..D9 -> Num0..Num9) needs remapping.
    private static PaperKey MapKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys k)
    {
        string s = k.ToString();
        if (s.Length == 2 && s[0] == 'D' && s[1] >= '0' && s[1] <= '9')
            s = "Num" + s[1];
        else if (s.StartsWith("KeyPad")) s = "Keypad" + s.Substring(6);
        else if (s == "Equal") s = "Equals";
        else if (s == "GraveAccent") s = "Grave";
        return Enum.TryParse<PaperKey>(s, out var pk) ? pk : PaperKey.Unknown;
    }

    private static PaperMouseBtn Translate(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton b) => b switch
    {
        OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left => PaperMouseBtn.Left,
        OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right => PaperMouseBtn.Right,
        OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle => PaperMouseBtn.Middle,
        _ => PaperMouseBtn.Unknown
    };

    private sealed class WindowClipboard : IClipboardHandler
    {
        private readonly GameWindow _window;
        public WindowClipboard(GameWindow window) => _window = window;
        public string GetClipboardText() => _window.ClipboardString ?? "";
        public void SetClipboardText(string text) => _window.ClipboardString = text;
    }

    // PROWL_TYPE="text{enter}{ctrl+space}" types into the focused element from frame 12, one token per two frames.
    private List<string> _typeTokens;
    private int _typeIndex;
    private readonly List<PaperKey> _heldKeys = new();

    private void SimulateTyping()
    {
        foreach (var k in _heldKeys) _paper.SetKeyState(k, false);
        _heldKeys.Clear();

        var env = Environment.GetEnvironmentVariable("PROWL_TYPE");
        if (env == null || _frame < 12 || _frame % 2 == 1) return;
        _typeTokens ??= Tokenize(env);
        if (_typeIndex >= _typeTokens.Count) return;

        string t = _typeTokens[_typeIndex++];
        if (t.Length > 2 && t[0] == '{' && t[^1] == '}')
        {
            foreach (var part in t.Substring(1, t.Length - 2).Split('+'))
            {
                PaperKey key = part.ToLowerInvariant() switch
                {
                    "ctrl" => PaperKey.LeftControl,
                    "shift" => PaperKey.LeftShift,
                    "alt" => PaperKey.LeftAlt,
                    "enter" => PaperKey.Enter,
                    "esc" => PaperKey.Escape,
                    "tab" => PaperKey.Tab,
                    "space" => PaperKey.Space,
                    "back" => PaperKey.Backspace,
                    "up" => PaperKey.Up,
                    "down" => PaperKey.Down,
                    "left" => PaperKey.Left,
                    "right" => PaperKey.Right,
                    "end" => PaperKey.End,
                    "home" => PaperKey.Home,
                    "wait" => PaperKey.Unknown,
                    _ => Enum.TryParse<PaperKey>(part, true, out var pk) ? pk : PaperKey.Unknown,
                };
                if (key == PaperKey.Unknown) continue;
                _paper.SetKeyState(key, true);
                _heldKeys.Add(key);
            }
            return;
        }
        _paper.AddInputCharacter(t);
    }

    private static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '{')
            {
                int end = s.IndexOf('}', i);
                if (end > i) { list.Add(s.Substring(i, end - i + 1)); i = end; continue; }
            }
            list.Add(s[i].ToString());
        }
        return list;
    }

    // PROWL_BENCH="scroll" or "type" drives the code editor every frame and prints CPU frame times.
    private string _benchMode;
    private bool _benchInit;
    private double _benchBuild, _benchEnd, _benchMax, _benchWallMax;
    private long _benchLast, _benchStart;
    private int _benchGc;
    private TimeSpan _benchPause;
    private int _benchFrames, _benchTick;

    private void Bench()
    {
        if (!_benchInit)
        {
            _benchInit = true;
            _benchMode = Environment.GetEnvironmentVariable("PROWL_BENCH");
            if (_benchMode != null) VSync = VSyncMode.Off;
        }
        if (_benchMode == null || _frame < 30) return;
        _benchTick++;
        _paper.SetPointerState(PaperMouseBtn.Unknown, 600, 330, false, true);
        if (_benchMode == "scroll") _paper.SetPointerWheel((_benchTick / 400) % 2 == 0 ? -1 : 1);
        else if (_benchMode == "type" && _benchTick % 2 == 0) _paper.AddInputCharacter(_benchTick % 40 == 0 ? " " : "a");
    }

    private void BenchRecord(long t0, long t1, long t2)
    {
        if (_frame < 30) { _frame++; return; }
        double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (_benchFrames == 0) { _benchStart = t0; _benchGc = GC.CollectionCount(0); _benchPause = GC.GetTotalPauseDuration(); }
        if (_benchLast != 0) _benchWallMax = Math.Max(_benchWallMax, (t0 - _benchLast) * f);
        _benchLast = t0;
        _benchBuild += (t1 - t0) * f;
        _benchEnd += (t2 - t1) * f;
        _benchMax = Math.Max(_benchMax, (t2 - t0) * f);
        if (++_benchFrames == 240)
        {
            double wall = (t2 - _benchStart) * f / 240;
            Console.WriteLine($"build {_benchBuild / 240:0.00}ms  endframe {_benchEnd / 240:0.00}ms  worst cpu {_benchMax:0.00}ms  wall avg {wall:0.00}ms  wall worst {_benchWallMax:0.00}ms  gen0 {GC.CollectionCount(0) - _benchGc}  gc pause {(GC.GetTotalPauseDuration() - _benchPause).TotalMilliseconds:0.0}ms  gen2 {GC.CollectionCount(2)}  heap {GC.GetTotalMemory(false) / 1048576}MB  alloc {GC.GetTotalAllocatedBytes() / 1048576}MB");
            _benchBuild = _benchEnd = _benchMax = _benchWallMax = 0;
            _benchFrames = 0;
        }
    }
}
