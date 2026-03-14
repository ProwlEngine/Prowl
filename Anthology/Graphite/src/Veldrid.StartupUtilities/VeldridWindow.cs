using System;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Veldrid.StartupUtilities
{
    // TODO: Sdl2Window 1:1 parity checklist. Everything below existed on the upstream Sdl2Window
    // and must be implemented before the port is complete.
    //
    // --- Properties (missing) ---
    // int X { get; set; }                    — window position X, backed by IWindow.Position.X
    // int Y { get; set; }                    — window position Y, backed by IWindow.Position.Y
    // int Width { set; }                     — setter missing (getter exists), use IWindow.Size
    // int Height { set; }                    — setter missing (getter exists), use IWindow.Size
    // IntPtr Handle                          — native window handle (HWND/X11 Window/etc.), extract from IWindow.Native
    // WindowState WindowState { get; set; }  — get/set window state (normal/fullscreen/maximized/minimized)
    //                                          Silk.NET: IWindow.WindowState, but need to map between Veldrid and Silk enums
    // bool Visible { get; set; }             — show/hide window, IWindow.IsVisible
    // bool Focused { get; }                  — whether window has input focus
    // bool Resizable { get; set; }           — IWindow.WindowBorder = Resizable vs Fixed
    // bool BorderVisible { get; set; }       — IWindow.WindowBorder = Hidden vs Resizable
    // bool CursorVisible { get; set; }       — Silk.NET: IMouse.Cursor.CursorMode = Normal/Hidden/Disabled
    // float Opacity { get; set; }            — window opacity (0-1), may not be supported by Silk.NET/GLFW
    // Vector2 ScaleFactor { get; }           — upstream always returns Vector2.One, can stub
    // Rectangle Bounds { get; }              — computed from position + size
    // Vector2 MouseDelta { get; }            — per-frame mouse delta, track via IMouse.MouseMove callback
    // bool LimitPollRate { get; set; }       — upstream feature for threaded windows, may not be needed
    // float PollIntervalInMs { get; set; }   — upstream feature for threaded windows, may not be needed
    // IntPtr SdlWindowHandle                 — remove (SDL2-specific), replace with Handle or SilkWindow
    //
    // --- Events (missing) ---
    // event Action Closed                    — fires after window is destroyed (vs Closing which fires before)
    // event Action FocusLost                 — IWindow.FocusChanged (check value)
    // event Action FocusGained               — IWindow.FocusChanged (check value)
    // event Action Shown                     — IWindow.StateChanged or visibility change
    // event Action Hidden                    — IWindow.StateChanged or visibility change
    // event Action MouseEntered              — Silk.NET: no direct equivalent, may need cursor tracking
    // event Action MouseLeft                 — Silk.NET: no direct equivalent, may need cursor tracking
    // event Action Exposed                   — window needs redraw (rare, may stub)
    // event Action<Point> Moved              — IWindow.Move event
    // event Action<MouseWheelEventArgs> MouseWheel — need MouseWheelEventArgs type (contains MouseState + WheelDelta)
    // event Action<MouseMoveEventArgs> MouseMove   — need MouseMoveEventArgs type (contains MouseState + position)
    // event Action<DragDropEvent> DragDrop         — file drag-drop, need DragDropEvent type + Silk.NET equivalent
    //
    // --- Methods (missing) ---
    // void SetMousePosition(Vector2 position)       — IMouse.Position setter
    // void SetMousePosition(int x, int y)           — same, int overload
    // void SetCloseRequestedHandler(Func<bool> h)   — handler returns true to cancel close
    // Point ClientToScreen(Point p)                 — coordinate transform, may need platform-specific impl
    // Point ScreenToClient(Point p)                 — coordinate transform, may need platform-specific impl
    //
    // --- Supporting types (missing) ---
    // MouseState struct       — holds mouse position + all button states, used by MouseWheel/MouseMove events
    // MouseWheelEventArgs     — { MouseState State, float WheelDelta }
    // MouseMoveEventArgs      — { MouseState State, Vector2 MousePosition }
    // DragDropEvent           — { DragDropType Type, string File } for file drag-drop
    //
    // --- Behavioral differences ---
    // - KeyEvent.Repeat is always false (Silk.NET KeyDown callback doesn't expose GLFW repeat flag)
    // - BorderlessFullScreen throws NotSupportedException (needs monitor query + borderless window workaround)
    // - PumpEvents returns same mutable object (upstream triple-buffers for threaded mode)
    // - No threaded window processing mode (upstream supports threadedProcessing=true with ManualResetEvent)

    /// <summary>
    /// A window backed by Silk.NET.Windowing, providing a Veldrid-compatible API surface.
    /// Replaces the former Sdl2Window.
    /// </summary>
    public class VeldridWindow : IDisposable
    {
        private readonly IWindow _window;
        private IInputContext _input;
        private readonly SimpleInputSnapshot _snapshot = new();

        public bool Exists => !_window.IsClosing;
        public int Width => _window.Size.X;
        public int Height => _window.Size.Y;
        public string Title { get => _window.Title; set => _window.Title = value; }

        public event Action Resized;
        public event Action Closing;
        public event Action<KeyEvent> KeyDown;
        public event Action<KeyEvent> KeyUp;
        public event Action<MouseEvent> MouseDown;
        public event Action<MouseEvent> MouseUp;

        internal IWindow SilkWindow => _window;

        public VeldridWindow(WindowCreateInfo wci)
            : this(wci, GraphicsAPI.None)
        {
        }

        internal VeldridWindow(WindowCreateInfo wci, GraphicsAPI api)
        {
            var options = new WindowOptions
            {
                Title = wci.WindowTitle ?? "Veldrid",
                Position = new Vector2D<int>(wci.X, wci.Y),
                Size = new Vector2D<int>(
                    wci.WindowWidth > 0 ? wci.WindowWidth : 960,
                    wci.WindowHeight > 0 ? wci.WindowHeight : 540),
                API = api,
                WindowBorder = WindowBorder.Resizable,
                IsVisible = wci.WindowInitialState != WindowState.Hidden,
                WindowState = MapWindowState(wci.WindowInitialState),
                ShouldSwapAutomatically = false,
                VSync = false,
            };

            _window = Window.Create(options);
            _window.Resize += OnWindowResize;
            _window.Closing += OnWindowClosing;
            _window.Initialize();

            _input = _window.CreateInput();
            SetupInputCallbacks();
        }

        private void SetupInputCallbacks()
        {
            foreach (var kb in _input.Keyboards)
            {
                kb.KeyDown += OnKeyDown;
                kb.KeyUp += OnKeyUp;
                kb.KeyChar += OnKeyChar;
            }
            foreach (var mouse in _input.Mice)
            {
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.Scroll += OnScroll;
            }
        }

        /// <summary>
        /// Processes all pending window and input events, returning an InputSnapshot of events accumulated this frame.
        /// </summary>
        public InputSnapshot PumpEvents()
        {
            _snapshot.Clear();
            _window.DoEvents();

            // Snapshot current mouse state after events
            if (_input?.Mice.Count > 0)
            {
                var mouse = _input.Mice[0];
                _snapshot.MousePosition = new Vector2(mouse.Position.X, mouse.Position.Y);

                _snapshot.SetMouseButton(MouseButton.Left, mouse.IsButtonPressed(SilkMouseButton.Left));
                _snapshot.SetMouseButton(MouseButton.Right, mouse.IsButtonPressed(SilkMouseButton.Right));
                _snapshot.SetMouseButton(MouseButton.Middle, mouse.IsButtonPressed(SilkMouseButton.Middle));
                for (int i = 3; i <= (int)SilkMouseButton.Button12; i++)
                {
                    _snapshot.SetMouseButton(MapMouseButton((SilkMouseButton)i), mouse.IsButtonPressed((SilkMouseButton)i));
                }
            }

            return _snapshot;
        }

        public void Close() => _window.Close();

        // --- Event handlers ---

        private void OnWindowResize(Vector2D<int> size)
        {
            Resized?.Invoke();
        }

        private void OnWindowClosing()
        {
            Closing?.Invoke();
        }

        private void OnKeyDown(IKeyboard keyboard, SilkKey key, int scancode)
        {
            var vKey = MapKey(key);
            if (vKey == Key.Unknown && key != SilkKey.Unknown) return;
            var mods = GetModifiers(keyboard);
            var ev = new KeyEvent(vKey, down: true, mods);
            _snapshot.AddKeyEvent(ev);
            KeyDown?.Invoke(ev);
        }

        private void OnKeyUp(IKeyboard keyboard, SilkKey key, int scancode)
        {
            var vKey = MapKey(key);
            if (vKey == Key.Unknown && key != SilkKey.Unknown) return;
            var mods = GetModifiers(keyboard);
            var ev = new KeyEvent(vKey, down: false, mods);
            _snapshot.AddKeyEvent(ev);
            KeyUp?.Invoke(ev);
        }

        private void OnKeyChar(IKeyboard keyboard, char c)
        {
            _snapshot.AddKeyChar(c);
        }

        private void OnMouseDown(IMouse mouse, SilkMouseButton button)
        {
            var vButton = MapMouseButton(button);
            var ev = new MouseEvent(vButton, down: true);
            _snapshot.AddMouseEvent(ev);
            MouseDown?.Invoke(ev);
        }

        private void OnMouseUp(IMouse mouse, SilkMouseButton button)
        {
            var vButton = MapMouseButton(button);
            var ev = new MouseEvent(vButton, down: false);
            _snapshot.AddMouseEvent(ev);
            MouseUp?.Invoke(ev);
        }

        private void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            _snapshot.WheelDelta += wheel.Y;
        }

        // --- Mapping helpers ---

        private static ModifierKeys GetModifiers(IKeyboard keyboard)
        {
            ModifierKeys mods = ModifierKeys.None;
            if (keyboard.IsKeyPressed(SilkKey.ShiftLeft) || keyboard.IsKeyPressed(SilkKey.ShiftRight))
                mods |= ModifierKeys.Shift;
            if (keyboard.IsKeyPressed(SilkKey.ControlLeft) || keyboard.IsKeyPressed(SilkKey.ControlRight))
                mods |= ModifierKeys.Control;
            if (keyboard.IsKeyPressed(SilkKey.AltLeft) || keyboard.IsKeyPressed(SilkKey.AltRight))
                mods |= ModifierKeys.Alt;
            if (keyboard.IsKeyPressed(SilkKey.SuperLeft) || keyboard.IsKeyPressed(SilkKey.SuperRight))
                mods |= ModifierKeys.Gui;
            return mods;
        }

        private static Silk.NET.Windowing.WindowState MapWindowState(WindowState state) => state switch
        {
            WindowState.Normal => Silk.NET.Windowing.WindowState.Normal,
            WindowState.FullScreen => Silk.NET.Windowing.WindowState.Fullscreen,
            WindowState.Maximized => Silk.NET.Windowing.WindowState.Maximized,
            WindowState.Minimized => Silk.NET.Windowing.WindowState.Minimized,
            WindowState.BorderlessFullScreen => throw new NotSupportedException(
                "BorderlessFullScreen is not yet implemented. Needs monitor query + borderless window workaround."),
            WindowState.Hidden => Silk.NET.Windowing.WindowState.Normal, // Hidden handled via IsVisible
            _ => Silk.NET.Windowing.WindowState.Normal,
        };

        private static MouseButton MapMouseButton(SilkMouseButton button) => button switch
        {
            SilkMouseButton.Left => MouseButton.Left,
            SilkMouseButton.Right => MouseButton.Right,
            SilkMouseButton.Middle => MouseButton.Middle,
            SilkMouseButton.Button4 => MouseButton.Button1,
            SilkMouseButton.Button5 => MouseButton.Button2,
            SilkMouseButton.Button6 => MouseButton.Button3,
            SilkMouseButton.Button7 => MouseButton.Button4,
            SilkMouseButton.Button8 => MouseButton.Button5,
            SilkMouseButton.Button9 => MouseButton.Button6,
            SilkMouseButton.Button10 => MouseButton.Button7,
            SilkMouseButton.Button11 => MouseButton.Button8,
            SilkMouseButton.Button12 => MouseButton.Button9,
            _ => MouseButton.Left,
        };

        private static Key MapKey(SilkKey key) => key switch
        {
            SilkKey.Space => Key.Space,
            SilkKey.Apostrophe => Key.Quote,
            SilkKey.Comma => Key.Comma,
            SilkKey.Minus => Key.Minus,
            SilkKey.Period => Key.Period,
            SilkKey.Slash => Key.Slash,
            SilkKey.Number0 => Key.Number0,
            SilkKey.Number1 => Key.Number1,
            SilkKey.Number2 => Key.Number2,
            SilkKey.Number3 => Key.Number3,
            SilkKey.Number4 => Key.Number4,
            SilkKey.Number5 => Key.Number5,
            SilkKey.Number6 => Key.Number6,
            SilkKey.Number7 => Key.Number7,
            SilkKey.Number8 => Key.Number8,
            SilkKey.Number9 => Key.Number9,
            SilkKey.Semicolon => Key.Semicolon,
            SilkKey.Equal => Key.Plus,
            SilkKey.A => Key.A,
            SilkKey.B => Key.B,
            SilkKey.C => Key.C,
            SilkKey.D => Key.D,
            SilkKey.E => Key.E,
            SilkKey.F => Key.F,
            SilkKey.G => Key.G,
            SilkKey.H => Key.H,
            SilkKey.I => Key.I,
            SilkKey.J => Key.J,
            SilkKey.K => Key.K,
            SilkKey.L => Key.L,
            SilkKey.M => Key.M,
            SilkKey.N => Key.N,
            SilkKey.O => Key.O,
            SilkKey.P => Key.P,
            SilkKey.Q => Key.Q,
            SilkKey.R => Key.R,
            SilkKey.S => Key.S,
            SilkKey.T => Key.T,
            SilkKey.U => Key.U,
            SilkKey.V => Key.V,
            SilkKey.W => Key.W,
            SilkKey.X => Key.X,
            SilkKey.Y => Key.Y,
            SilkKey.Z => Key.Z,
            SilkKey.LeftBracket => Key.BracketLeft,
            SilkKey.BackSlash => Key.BackSlash,
            SilkKey.RightBracket => Key.BracketRight,
            SilkKey.GraveAccent => Key.Grave,
            SilkKey.Escape => Key.Escape,
            SilkKey.Enter => Key.Enter,
            SilkKey.Tab => Key.Tab,
            SilkKey.Backspace => Key.BackSpace,
            SilkKey.Insert => Key.Insert,
            SilkKey.Delete => Key.Delete,
            SilkKey.Right => Key.Right,
            SilkKey.Left => Key.Left,
            SilkKey.Down => Key.Down,
            SilkKey.Up => Key.Up,
            SilkKey.PageUp => Key.PageUp,
            SilkKey.PageDown => Key.PageDown,
            SilkKey.Home => Key.Home,
            SilkKey.End => Key.End,
            SilkKey.CapsLock => Key.CapsLock,
            SilkKey.ScrollLock => Key.ScrollLock,
            SilkKey.NumLock => Key.NumLock,
            SilkKey.PrintScreen => Key.PrintScreen,
            SilkKey.Pause => Key.Pause,
            SilkKey.F1 => Key.F1,
            SilkKey.F2 => Key.F2,
            SilkKey.F3 => Key.F3,
            SilkKey.F4 => Key.F4,
            SilkKey.F5 => Key.F5,
            SilkKey.F6 => Key.F6,
            SilkKey.F7 => Key.F7,
            SilkKey.F8 => Key.F8,
            SilkKey.F9 => Key.F9,
            SilkKey.F10 => Key.F10,
            SilkKey.F11 => Key.F11,
            SilkKey.F12 => Key.F12,
            SilkKey.F13 => Key.F13,
            SilkKey.F14 => Key.F14,
            SilkKey.F15 => Key.F15,
            SilkKey.F16 => Key.F16,
            SilkKey.F17 => Key.F17,
            SilkKey.F18 => Key.F18,
            SilkKey.F19 => Key.F19,
            SilkKey.F20 => Key.F20,
            SilkKey.F21 => Key.F21,
            SilkKey.F22 => Key.F22,
            SilkKey.F23 => Key.F23,
            SilkKey.F24 => Key.F24,
            SilkKey.F25 => Key.F25,
            SilkKey.Keypad0 => Key.Keypad0,
            SilkKey.Keypad1 => Key.Keypad1,
            SilkKey.Keypad2 => Key.Keypad2,
            SilkKey.Keypad3 => Key.Keypad3,
            SilkKey.Keypad4 => Key.Keypad4,
            SilkKey.Keypad5 => Key.Keypad5,
            SilkKey.Keypad6 => Key.Keypad6,
            SilkKey.Keypad7 => Key.Keypad7,
            SilkKey.Keypad8 => Key.Keypad8,
            SilkKey.Keypad9 => Key.Keypad9,
            SilkKey.KeypadDecimal => Key.KeypadDecimal,
            SilkKey.KeypadDivide => Key.KeypadDivide,
            SilkKey.KeypadMultiply => Key.KeypadMultiply,
            SilkKey.KeypadSubtract => Key.KeypadSubtract,
            SilkKey.KeypadAdd => Key.KeypadAdd,
            SilkKey.KeypadEnter => Key.KeypadEnter,
            SilkKey.ShiftLeft => Key.ShiftLeft,
            SilkKey.ControlLeft => Key.ControlLeft,
            SilkKey.AltLeft => Key.AltLeft,
            SilkKey.SuperLeft => Key.WinLeft,
            SilkKey.ShiftRight => Key.ShiftRight,
            SilkKey.ControlRight => Key.ControlRight,
            SilkKey.AltRight => Key.AltRight,
            SilkKey.SuperRight => Key.WinRight,
            SilkKey.Menu => Key.Menu,
            _ => Key.Unknown,
        };

        public void Dispose()
        {
            _input?.Dispose();
            _window?.Dispose();
        }
    }
}
