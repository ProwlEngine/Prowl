using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;
using SilkWindowState = Silk.NET.Windowing.WindowState;

namespace Veldrid.StartupUtilities
{
    /// <summary>
    /// A window backed by Silk.NET.Windowing, providing a Veldrid-compatible API surface.
    /// Replaces the former Sdl2Window.
    /// </summary>
    public class VeldridWindow : IDisposable
    {
        private readonly IWindow _window;
        private readonly IInputContext _input;
        private readonly SimpleInputSnapshot _snapshot = new();

        private bool _resizable = true;
        private bool _borderVisible = true;
        private bool _focused = true;
        private bool _previousIsVisible = true;
        private Vector2 _previousMousePosition;
        private Vector2 _mouseDelta;
        private bool _firstPumpEvents = true;
        private bool _disposed;
        private bool _cursorRelativeMode;
        private bool _cursorVisible = true;
        private Func<bool> _closeRequestedHandler;

        private bool _borderlessFullScreen;
        private bool _savedBorderVisible;
        private bool _savedResizable;
        private readonly HashSet<Key> _pressedKeys = new();
        private readonly bool[] _cachedMouseButtons = new bool[(int)MouseButton.LastButton + 1];
        private readonly Silk.NET.GLFW.Glfw _glfw;
        private readonly unsafe Silk.NET.GLFW.WindowHandle* _glfwHandle;

        /// <summary>
        /// Whether the window is still open. Becomes false once the window begins closing or has been disposed.
        /// </summary>
        public bool Exists => !_disposed && !_window.IsClosing;

        /// <summary>
        /// The window's X position in screen coordinates.
        /// </summary>
        public int X
        {
            get => _window.Position.X;
            set => _window.Position = new Vector2D<int>(value, _window.Position.Y);
        }

        /// <summary>
        /// The window's Y position in screen coordinates.
        /// </summary>
        public int Y
        {
            get => _window.Position.Y;
            set => _window.Position = new Vector2D<int>(_window.Position.X, value);
        }

        /// <summary>
        /// The window's client area width in pixels.
        /// </summary>
        public int Width
        {
            get => _window.Size.X;
            set => _window.Size = new Vector2D<int>(value, _window.Size.Y);
        }

        /// <summary>
        /// The window's client area height in pixels.
        /// </summary>
        public int Height
        {
            get => _window.Size.Y;
            set => _window.Size = new Vector2D<int>(_window.Size.X, value);
        }

        /// <summary>
        /// The window title.
        /// </summary>
        public string Title
        {
            get => _window.Title;
            set => _window.Title = value;
        }

        /// <summary>
        /// The platform-specific native window handle (HWND on Windows, X11 Window on Linux, etc.).
        /// </summary>
        public IntPtr Handle
        {
            get
            {
                var native = _window.Native;
                if (native == null)
                    return IntPtr.Zero;
                if (native.Win32.HasValue)
                    return native.Win32.Value.Hwnd;
                if (native.X11.HasValue)
                    return (IntPtr)native.X11.Value.Window;
                if (native.Wayland.HasValue)
                    return native.Wayland.Value.Surface;
                if (native.Cocoa.HasValue)
                    return native.Cocoa.Value;
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// The current window state (normal, fullscreen, maximized, minimized, etc.).
        /// </summary>
        public WindowState WindowState
        {
            get
            {
                if (!_window.IsVisible)
                    return WindowState.Hidden;
                if (_borderlessFullScreen)
                    return WindowState.BorderlessFullScreen;
                return MapWindowStateReverse(_window.WindowState);
            }
            set
            {
                if (value == WindowState.Hidden)
                {
                    _window.IsVisible = false;
                    _previousIsVisible = false;
                    return;
                }

                _window.IsVisible = true;
                _previousIsVisible = true;

                if (value == WindowState.BorderlessFullScreen)
                {
                    if (!_borderlessFullScreen)
                    {
                        _savedBorderVisible = _borderVisible;
                        _savedResizable = _resizable;
                    }
                    _borderlessFullScreen = true;
                    _borderVisible = false;
                    _window.WindowBorder = WindowBorder.Hidden;
                    _window.WindowState = SilkWindowState.Maximized;
                }
                else
                {
                    if (_borderlessFullScreen)
                    {
                        _borderVisible = _savedBorderVisible;
                        _resizable = _savedResizable;
                        _borderlessFullScreen = false;
                    }
                    _window.WindowState = MapWindowStateToSilk(value);
                    UpdateWindowBorder();
                }
            }
        }

        /// <summary>
        /// Whether the window is visible.
        /// </summary>
        public bool Visible
        {
            get => _window.IsVisible;
            set
            {
                _window.IsVisible = value;
                _previousIsVisible = value;
            }
        }

        /// <summary>
        /// Whether the window currently has input focus.
        /// </summary>
        public bool Focused => _focused;

        /// <summary>
        /// Whether the window can be resized by the user.
        /// </summary>
        public bool Resizable
        {
            get => _resizable;
            set
            {
                _resizable = value;
                if (!_borderlessFullScreen)
                    UpdateWindowBorder();
            }
        }

        /// <summary>
        /// Whether the window border (title bar and frame) is visible.
        /// </summary>
        public bool BorderVisible
        {
            get => _borderVisible;
            set
            {
                _borderVisible = value;
                if (!_borderlessFullScreen)
                    UpdateWindowBorder();
            }
        }

        /// <summary>
        /// Whether relative mouse mode is active. When enabled, the cursor is hidden and
        /// confined to the window, and only movement deltas are reported.
        /// While active, <see cref="CursorVisible"/> writes update the cached state but are
        /// not applied until relative mode is disabled.
        /// Equivalent to SDL's SDL_SetRelativeMouseMode.
        /// </summary>
        public bool CursorRelativeMode
        {
            get => _cursorRelativeMode;
            set
            {
                _cursorRelativeMode = value;
                if (_input?.Mice.Count > 0)
                    _input.Mice[0].Cursor.CursorMode = value
                        ? CursorMode.Disabled
                        : (_cursorVisible ? CursorMode.Normal : CursorMode.Hidden);
            }
        }

        /// <summary>
        /// Whether the mouse cursor is visible over the window. Defaults to true.
        /// While <see cref="CursorRelativeMode"/> is active, reads return the cached state
        /// and writes update the cached state without applying it.
        /// </summary>
        public bool CursorVisible
        {
            get => _cursorVisible;
            set
            {
                _cursorVisible = value;
                if (_cursorRelativeMode) return;
                if (_input?.Mice.Count > 0)
                    _input.Mice[0].Cursor.CursorMode = value ? CursorMode.Normal : CursorMode.Hidden;
            }
        }

        /// <summary>
        /// The window opacity (0.0 to 1.0).
        /// Returns float.NaN if the platform does not support opacity.
        /// </summary>
        public unsafe float Opacity
        {
            get
            {
                if (_glfw != null && _glfwHandle != null)
                    return _glfw.GetWindowOpacity(_glfwHandle);
                return float.NaN;
            }
            set
            {
                if (_glfw != null && _glfwHandle != null)
                    _glfw.SetWindowOpacity(_glfwHandle, value);
            }
        }

        /// <summary>
        /// The DPI scale factor. Currently always returns (1, 1).
        /// </summary>
        public Vector2 ScaleFactor => Vector2.One;

        /// <summary>
        /// The window bounds (position and size) as a Rectangle.
        /// </summary>
        public Rectangle Bounds => new Rectangle(X, Y, Width, Height);

        /// <summary>
        /// The mouse movement delta since the last call to PumpEvents.
        /// </summary>
        public Vector2 MouseDelta => _mouseDelta;

        /// <summary>
        /// Whether to throttle event polling. Stored but has no effect (threaded mode not supported).
        /// </summary>
        public bool LimitPollRate { get; set; }

        /// <summary>
        /// Poll rate interval in milliseconds. Stored but has no effect (threaded mode not supported).
        /// </summary>
        public float PollIntervalInMs { get; set; }

        /// <summary>
        /// Fires when the window is resized.
        /// </summary>
        public event Action Resized;

        /// <summary>
        /// Fires before the window closes.
        /// </summary>
        public event Action Closing;

        /// <summary>
        /// Fires when the window is being disposed, before resources are released.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// Fires when the window loses input focus.
        /// </summary>
        public event Action FocusLost;

        /// <summary>
        /// Fires when the window gains input focus.
        /// </summary>
        public event Action FocusGained;

        /// <summary>
        /// Fires when the window becomes visible.
        /// </summary>
        public event Action Shown;

        /// <summary>
        /// Fires when the window becomes hidden.
        /// </summary>
        public event Action Hidden;

        /// <summary>
        /// Fires when the window is moved, providing the new position.
        /// </summary>
        public event Action<Point> Moved;

        /// <summary>
        /// Fires when the mouse wheel is scrolled.
        /// </summary>
        public event Action<MouseWheelEventArgs> MouseWheel;

        /// <summary>
        /// Fires when the mouse is moved.
        /// </summary>
        public event Action<MouseMoveEventArgs> MouseMove;

        /// <summary>
        /// Fires when a key is pressed.
        /// </summary>
        public event Action<KeyEvent> KeyDown;

        /// <summary>
        /// Fires when a key is released.
        /// </summary>
        public event Action<KeyEvent> KeyUp;

        /// <summary>
        /// Fires when a mouse button is pressed.
        /// </summary>
        public event Action<MouseEvent> MouseDown;

        /// <summary>
        /// Fires when a mouse button is released.
        /// </summary>
        public event Action<MouseEvent> MouseUp;

        /// <summary>
        /// Fires when a file is dropped onto the window.
        /// </summary>
        public event Action<DragDropEvent> DragDrop;

        /// <summary>
        /// The underlying Silk.NET window. For internal use only.
        /// </summary>
        internal IWindow SilkWindow => _window;

        /// <summary>
        /// Creates a new window. Probes for the highest available OpenGL version and
        /// forwards framebuffer hints (depth, stencil, sRGB) from <paramref name="options"/>
        /// so the window can be used with any backend including OpenGL.
        /// </summary>
        /// <param name="wci">Window creation parameters (position, size, title, state).</param>
        /// <param name="options">Graphics device options. Depth/stencil/sRGB/debug settings
        /// are applied to the GL context at window creation time (GLFW requires this upfront,
        /// unlike SDL2 which could set them later).</param>
        public VeldridWindow(WindowCreateInfo wci, GraphicsDeviceOptions options)
            : this(wci, GetGraphicsAPI(options), options)
        {
        }

        private static GraphicsAPI GetGraphicsAPI(GraphicsDeviceOptions options)
        {
#if !EXCLUDE_OPENGL_BACKEND
            if (OpenGLVersionProbe.TryGetMaxVersion(gles: false, out var version))
            {
                ContextFlags flags = options.Debug
                    ? ContextFlags.Debug | ContextFlags.ForwardCompatible
                    : ContextFlags.ForwardCompatible;
                return new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, flags, new APIVersion(version.Major, version.Minor));
            }
#endif
            return GraphicsAPI.None;
        }

        internal VeldridWindow(WindowCreateInfo wci, GraphicsAPI api, GraphicsDeviceOptions deviceOptions = default)
        {
            GetDepthStencilBits(deviceOptions, out int? depthBits, out int? stencilBits);

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
                WindowState = MapWindowStateToSilk(wci.WindowInitialState),
                ShouldSwapAutomatically = false,
                VSync = false,
                PreferredDepthBufferBits = depthBits,
                PreferredStencilBufferBits = stencilBits,
            };

            GlfwWindowing.Use();
            var glfw = Silk.NET.GLFW.GlfwProvider.GLFW.Value;
            glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.SrgbCapable, deviceOptions.SwapchainSrgbFormat);

            _window = Window.Create(options);

            _window.Resize += OnWindowResize;
            _window.Closing += OnWindowClosing;
            _window.Move += OnWindowMoved;
            _window.FocusChanged += OnFocusChanged;
            _window.StateChanged += OnStateChanged;
            _window.FileDrop += OnFileDrop;

            _window.Initialize();
            _previousIsVisible = _window.IsVisible;

            // Cache GLFW handles for direct access to features Silk.NET doesn't surface
            _glfw = GlfwWindowing.GetExistingApi(_window);
            unsafe { _glfwHandle = (Silk.NET.GLFW.WindowHandle*)_window.Handle; }

            _input = _window.CreateInput();
            SetupInputCallbacks();

            if (_input?.Mice.Count > 0)
            {
                var mouse = _input.Mice[0];
                _previousMousePosition = new Vector2(mouse.Position.X, mouse.Position.Y);
            }
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
                mouse.MouseMove += OnMouseMoveCallback;
            }
        }

        /// <summary>
        /// Processes all pending window and input events, returning an InputSnapshot
        /// of events accumulated this frame.
        /// </summary>
        public InputSnapshot PumpEvents()
        {
            _snapshot.Clear();
            if (_disposed)
                return _snapshot;
            _window.DoEvents();

            if (_input?.Mice.Count > 0)
            {
                var mouse = _input.Mice[0];
                var currentPos = new Vector2(mouse.Position.X, mouse.Position.Y);
                _snapshot.MousePosition = currentPos;

                // Suppress the first-frame delta to avoid a large spurious jump
                if (_firstPumpEvents)
                {
                    _previousMousePosition = currentPos;
                    _firstPumpEvents = false;
                    _mouseDelta = Vector2.Zero;
                }
                else
                {
                    _mouseDelta = currentPos - _previousMousePosition;
                    _previousMousePosition = currentPos;
                }

                for (int i = 0; i <= (int)MouseButton.LastButton; i++)
                    _snapshot.SetMouseButton((MouseButton)i, _cachedMouseButtons[i]);
            }

            return _snapshot;
        }

        /// <summary>
        /// Requests the window to close.
        /// </summary>
        public void Close() => _window.Close();

        /// <summary>
        /// Warps the mouse cursor to the given position in client coordinates.
        /// </summary>
        public void SetMousePosition(Vector2 position)
        {
            if (_input?.Mice.Count > 0)
            {
                _input.Mice[0].Position = position;
                _previousMousePosition = position;
            }
        }

        /// <summary>
        /// Warps the mouse cursor to the given position in client coordinates.
        /// </summary>
        public void SetMousePosition(int x, int y)
            => SetMousePosition(new Vector2(x, y));

        /// <summary>
        /// Sets a handler that is called when the user requests the window to close.
        /// If the handler returns true, the close is cancelled and the Closing event is suppressed.
        /// </summary>
        public void SetCloseRequestedHandler(Func<bool> handler)
            => _closeRequestedHandler = handler;

        /// <summary>
        /// Converts a point from client (window) coordinates to screen coordinates.
        /// </summary>
        public Point ClientToScreen(Point p)
        {
            var result = _window.PointToScreen(new Vector2D<int>(p.X, p.Y));
            return new Point(result.X, result.Y);
        }

        /// <summary>
        /// Converts a point from screen coordinates to client (window) coordinates.
        /// </summary>
        public Point ScreenToClient(Point p)
        {
            var result = _window.PointToClient(new Vector2D<int>(p.X, p.Y));
            return new Point(result.X, result.Y);
        }

        private void UpdateWindowBorder()
        {
            if (!_borderVisible)
                _window.WindowBorder = WindowBorder.Hidden;
            else if (_resizable)
                _window.WindowBorder = WindowBorder.Resizable;
            else
                _window.WindowBorder = WindowBorder.Fixed;
        }

        private MouseState CreateMouseState(IMouse mouse)
        {
            var b = _cachedMouseButtons;
            return new MouseState(
                (int)mouse.Position.X, (int)mouse.Position.Y,
                b[0], b[1], b[2], b[3], b[4], b[5], b[6],
                b[7], b[8], b[9], b[10], b[11], b[12]);
        }

        private void OnWindowResize(Vector2D<int> size)
        {
            if (_disposed) return;
            Resized?.Invoke();
        }

        private void OnWindowClosing()
        {
            if (_disposed) return;
            if (_closeRequestedHandler != null && _closeRequestedHandler())
            {
                // Cancel the close by resetting the GLFW should-close flag
                _window.IsClosing = false;
                return;
            }

            Closing?.Invoke();
        }

        private void OnWindowMoved(Vector2D<int> position)
        {
            if (_disposed) return;
            Moved?.Invoke(new Point(position.X, position.Y));
        }

        private void OnFocusChanged(bool focused)
        {
            if (_disposed) return;
            _focused = focused;
            if (focused)
            {
                FocusGained?.Invoke();
            }
            else
            {
                // Keys may be released while unfocused without generating key-up events.
                // Clear tracked state to avoid false repeat detection when focus returns.
                _pressedKeys.Clear();
                FocusLost?.Invoke();
            }
        }

        private void OnStateChanged(SilkWindowState state)
        {
            if (_disposed) return;
            bool currentlyVisible = _window.IsVisible;
            if (currentlyVisible && !_previousIsVisible)
                Shown?.Invoke();
            else if (!currentlyVisible && _previousIsVisible)
                Hidden?.Invoke();
            _previousIsVisible = currentlyVisible;
        }

        private void OnFileDrop(string[] files)
        {
            if (_disposed) return;
            if (DragDrop != null)
            {
                foreach (var file in files)
                    DragDrop.Invoke(new DragDropEvent(file));
            }
        }

        private void OnKeyDown(IKeyboard keyboard, SilkKey key, int scancode)
        {
            if (_disposed) return;
            var vKey = MapKey(key);
            if (vKey == Key.Unknown && key != SilkKey.Unknown)
                return;
            var mods = GetModifiers(keyboard);
            // Silk.NET drops GLFW's repeat flag, so we detect repeats ourselves:
            // if the key is already in the pressed set, this is a repeat event.
            bool repeat = !_pressedKeys.Add(vKey);
            var ev = new KeyEvent(vKey, down: true, mods, repeat);
            _snapshot.AddKeyEvent(ev);
            KeyDown?.Invoke(ev);
        }

        private void OnKeyUp(IKeyboard keyboard, SilkKey key, int scancode)
        {
            if (_disposed) return;
            var vKey = MapKey(key);
            if (vKey == Key.Unknown && key != SilkKey.Unknown)
                return;
            _pressedKeys.Remove(vKey);
            var mods = GetModifiers(keyboard);
            var ev = new KeyEvent(vKey, down: false, mods);
            _snapshot.AddKeyEvent(ev);
            KeyUp?.Invoke(ev);
        }

        private void OnKeyChar(IKeyboard keyboard, char c)
        {
            if (_disposed) return;
            _snapshot.AddKeyChar(c);
        }

        private void OnMouseDown(IMouse mouse, SilkMouseButton button)
        {
            if (_disposed) return;
            var vButton = MapMouseButton(button);
            _cachedMouseButtons[(int)vButton] = true;
            var ev = new MouseEvent(vButton, down: true);
            _snapshot.AddMouseEvent(ev);
            MouseDown?.Invoke(ev);
        }

        private void OnMouseUp(IMouse mouse, SilkMouseButton button)
        {
            if (_disposed) return;
            var vButton = MapMouseButton(button);
            _cachedMouseButtons[(int)vButton] = false;
            var ev = new MouseEvent(vButton, down: false);
            _snapshot.AddMouseEvent(ev);
            MouseUp?.Invoke(ev);
        }

        private void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            if (_disposed) return;
            _snapshot.WheelDelta += wheel.Y;
            if (MouseWheel != null)
            {
                var state = CreateMouseState(mouse);
                MouseWheel.Invoke(new MouseWheelEventArgs(state, wheel.Y));
            }
        }

        private void OnMouseMoveCallback(IMouse mouse, Vector2 position)
        {
            if (_disposed) return;
            if (MouseMove != null)
            {
                var state = CreateMouseState(mouse);
                MouseMove.Invoke(new MouseMoveEventArgs(state, position));
            }
        }

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

        private static SilkWindowState MapWindowStateToSilk(WindowState state) => state switch
        {
            WindowState.Normal => SilkWindowState.Normal,
            WindowState.FullScreen => SilkWindowState.Fullscreen,
            WindowState.Maximized => SilkWindowState.Maximized,
            WindowState.Minimized => SilkWindowState.Minimized,
            WindowState.BorderlessFullScreen => SilkWindowState.Maximized,
            WindowState.Hidden => SilkWindowState.Normal,
            _ => SilkWindowState.Normal,
        };

        private static WindowState MapWindowStateReverse(SilkWindowState state) => state switch
        {
            SilkWindowState.Normal => WindowState.Normal,
            SilkWindowState.Fullscreen => WindowState.FullScreen,
            SilkWindowState.Maximized => WindowState.Maximized,
            SilkWindowState.Minimized => WindowState.Minimized,
            _ => WindowState.Normal,
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

        private static void GetDepthStencilBits(GraphicsDeviceOptions options, out int? depthBits, out int? stencilBits)
        {
            if (!options.SwapchainDepthFormat.HasValue)
            {
                depthBits = null;
                stencilBits = null;
                return;
            }

            (depthBits, stencilBits) = options.SwapchainDepthFormat.Value switch
            {
                PixelFormat.R16_UNorm => ((int?)16, (int?)0),
                PixelFormat.D24_UNorm_S8_UInt => (24, 8),
                PixelFormat.R32_Float => (32, 0),
                PixelFormat.D32_Float_S8_UInt => (32, 8),
                _ => throw new VeldridException("Invalid depth format: " + options.SwapchainDepthFormat.Value),
            };
        }

        /// <summary>
        /// Disposes the window and its resources. Fires the Closed event.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Closed?.Invoke();
            _input?.Dispose();
            _window?.Dispose();
        }
    }
}
