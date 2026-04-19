using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using Silk.NET.SDL;
using NeoVeldrid.StartupUtilities;

namespace NeoVeldrid.Sdl2
{
    public unsafe class Sdl2Window
    {
        private static readonly Sdl _sdl = Sdl.GetApi();

        private readonly List<Event> _events = new List<Event>();
        private Silk.NET.SDL.Window* _window;
        internal uint WindowID { get; private set; }
        private bool _exists;

        private SimpleInputSnapshot _publicSnapshot = new SimpleInputSnapshot();
        private SimpleInputSnapshot _privateSnapshot = new SimpleInputSnapshot();
        private SimpleInputSnapshot _privateBackbuffer = new SimpleInputSnapshot();

        // Threaded Sdl2Window flags
        private readonly bool _threadedProcessing;

        private bool _shouldClose;
        public bool LimitPollRate { get; set; }
        public float PollIntervalInMs { get; set; }

        // Current input states
        private int _currentMouseX;
        private int _currentMouseY;
        private bool[] _currentMouseButtonStates = new bool[13];
        private Vector2 _currentMouseDelta;

        // Cached Sdl2Window state (for threaded processing)
        private Point _cachedPosition;
        private Point _cachedSize;
        private string _cachedWindowTitle;
        private bool _newWindowTitleReceived;
        private bool _firstMouseEvent = true;
        private Func<bool> _closeRequestedHandler;

        // Cursor state
        private bool _cursorRelativeMode;

        private const int SDL_QUERY = -1;
        private const int SDL_DISABLE = 0;
        private const int SDL_ENABLE = 1;

        public Sdl2Window(string title, int x, int y, int width, int height, SDL_WindowFlags flags, bool threadedProcessing)
        {
            _sdl.SetHint("SDL_MOUSE_FOCUS_CLICKTHROUGH", "1");
            _threadedProcessing = threadedProcessing;
            if (threadedProcessing)
            {
                using (ManualResetEvent mre = new ManualResetEvent(false))
                {
                    WindowParams wp = new WindowParams()
                    {
                        Title = title,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        WindowFlags = flags,
                        ResetEvent = mre
                    };

                    Task.Factory.StartNew(WindowOwnerRoutine, wp, TaskCreationOptions.LongRunning);
                    mre.WaitOne();
                }
            }
            else
            {
                _window = _sdl.CreateWindow(title, x, y, width, height, (uint)flags);
                WindowID = _sdl.GetWindowID(_window);
                Sdl2WindowRegistry.RegisterWindow(this);
                PostWindowCreated(flags);
            }
        }

        public Sdl2Window(IntPtr windowHandle, bool threadedProcessing)
        {
            _threadedProcessing = threadedProcessing;
            if (threadedProcessing)
            {
                using (ManualResetEvent mre = new ManualResetEvent(false))
                {
                    WindowParams wp = new WindowParams()
                    {
                        WindowHandle = windowHandle,
                        WindowFlags = 0,
                        ResetEvent = mre
                    };

                    Task.Factory.StartNew(WindowOwnerRoutine, wp, TaskCreationOptions.LongRunning);
                    mre.WaitOne();
                }
            }
            else
            {
                _window = _sdl.CreateWindowFrom((void*)windowHandle);
                WindowID = _sdl.GetWindowID(_window);
                Sdl2WindowRegistry.RegisterWindow(this);
                PostWindowCreated(0);
            }
        }

        public int X { get => _cachedPosition.X; set => SetWindowPosition(value, Y); }
        public int Y { get => _cachedPosition.Y; set => SetWindowPosition(X, value); }

        public int Width { get => GetWindowSize().X; set => SetWindowSize(value, Height); }
        public int Height { get => GetWindowSize().Y; set => SetWindowSize(Width, value); }

        public IntPtr Handle => GetUnderlyingWindowHandle();

        public string Title { get => _cachedWindowTitle; set => SetWindowTitle(value); }

        private void SetWindowTitle(string value)
        {
            _cachedWindowTitle = value;
            _newWindowTitleReceived = true;
        }

        public WindowState WindowState
        {
            get
            {
                uint flags = _sdl.GetWindowFlags(_window);
                if (((flags & (uint)WindowFlags.FullscreenDesktop) == (uint)WindowFlags.FullscreenDesktop)
                    || ((flags & (uint)(WindowFlags.Borderless | WindowFlags.Fullscreen)) == (uint)(WindowFlags.Borderless | WindowFlags.Fullscreen)))
                {
                    return WindowState.BorderlessFullScreen;
                }
                else if ((flags & (uint)WindowFlags.Minimized) == (uint)WindowFlags.Minimized)
                {
                    return WindowState.Minimized;
                }
                else if ((flags & (uint)WindowFlags.Fullscreen) == (uint)WindowFlags.Fullscreen)
                {
                    return WindowState.FullScreen;
                }
                else if ((flags & (uint)WindowFlags.Maximized) == (uint)WindowFlags.Maximized)
                {
                    return WindowState.Maximized;
                }
                else if ((flags & (uint)WindowFlags.Hidden) == (uint)WindowFlags.Hidden)
                {
                    return WindowState.Hidden;
                }

                return WindowState.Normal;
            }
            set
            {
                switch (value)
                {
                    case WindowState.Normal:
                        _sdl.SetWindowFullscreen(_window, 0);
                        break;
                    case WindowState.FullScreen:
                        _sdl.SetWindowFullscreen(_window, (uint)WindowFlags.Fullscreen);
                        break;
                    case WindowState.Maximized:
                        _sdl.MaximizeWindow(_window);
                        break;
                    case WindowState.Minimized:
                        _sdl.MinimizeWindow(_window);
                        break;
                    case WindowState.BorderlessFullScreen:
                        _sdl.SetWindowFullscreen(_window, (uint)WindowFlags.FullscreenDesktop);
                        break;
                    case WindowState.Hidden:
                        _sdl.HideWindow(_window);
                        break;
                    default:
                        throw new InvalidOperationException("Illegal WindowState value: " + value);
                }
            }
        }

        public bool Exists => _exists;

        public bool Visible
        {
            get => (_sdl.GetWindowFlags(_window) & (uint)WindowFlags.Shown) != 0;
            set
            {
                if (value)
                {
                    _sdl.ShowWindow(_window);
                }
                else
                {
                    _sdl.HideWindow(_window);
                }
            }
        }

        public Vector2 ScaleFactor => Vector2.One;

        public Rectangle Bounds => new Rectangle(_cachedPosition, GetWindowSize());

        public bool CursorVisible
        {
            get
            {
                return _sdl.ShowCursor(SDL_QUERY) == 1;
            }
            set
            {
                int toggle = value ? SDL_ENABLE : SDL_DISABLE;
                _sdl.ShowCursor(toggle);
            }
        }

        /// <summary>
        /// Whether relative mouse mode is active. When enabled, the cursor is hidden and
        /// confined to the window, and only movement deltas are reported.
        /// </summary>
        public bool CursorRelativeMode
        {
            get => _cursorRelativeMode;
            set
            {
                _cursorRelativeMode = value;
                _sdl.SetRelativeMouseMode(value ? SdlBool.True : SdlBool.False);
            }
        }

        public float Opacity
        {
            get
            {
                float opacity = float.NaN;
                if (_sdl.GetWindowOpacity(_window, &opacity) == 0)
                {
                    return opacity;
                }
                return float.NaN;
            }
            set
            {
                _sdl.SetWindowOpacity(_window, value);
            }
        }

        public bool Focused => (_sdl.GetWindowFlags(_window) & (uint)WindowFlags.InputFocus) != 0;

        public bool Resizable
        {
            get => (_sdl.GetWindowFlags(_window) & (uint)WindowFlags.Resizable) != 0;
            set => _sdl.SetWindowResizable(_window, value ? SdlBool.True : SdlBool.False);
        }

        public bool BorderVisible
        {
            get => (_sdl.GetWindowFlags(_window) & (uint)WindowFlags.Borderless) == 0;
            set => _sdl.SetWindowBordered(_window, value ? SdlBool.True : SdlBool.False);
        }

        public IntPtr SdlWindowHandle => (IntPtr)_window;

        public event Action Resized;
        public event Action Closing;
        public event Action Closed;
        public event Action FocusLost;
        public event Action FocusGained;
        public event Action Shown;
        public event Action Hidden;
        public event Action MouseEntered;
        public event Action MouseLeft;
        public event Action Exposed;
        public event Action<Point> Moved;
        public event Action<MouseWheelEventArgs> MouseWheel;
        public event Action<MouseMoveEventArgs> MouseMove;
        public event Action<MouseEvent> MouseDown;
        public event Action<MouseEvent> MouseUp;
        public event Action<KeyEvent> KeyDown;
        public event Action<KeyEvent> KeyUp;
        public event Action<DragDropEvent> DragDrop;

        public Point ClientToScreen(Point p)
        {
            Point position = _cachedPosition;
            return new Point(p.X + position.X, p.Y + position.Y);
        }

        public void SetMousePosition(Vector2 position) => SetMousePosition((int)position.X, (int)position.Y);
        public void SetMousePosition(int x, int y)
        {
            if (!_exists)
                return;

            _sdl.WarpMouseInWindow(_window, x, y);
            _currentMouseX = x;
            _currentMouseY = y;
            _privateSnapshot.MousePosition = new Vector2(x, y);
        }

        public Vector2 MouseDelta => _currentMouseDelta;

        public void SetCloseRequestedHandler(Func<bool> handler)
        {
            _closeRequestedHandler = handler;
        }

        public void Close()
        {
            if (_threadedProcessing)
            {
                _shouldClose = true;
            }
            else
            {
                CloseCore();
            }
        }

        private bool CloseCore()
        {
            if (_closeRequestedHandler?.Invoke() ?? false)
            {
                _shouldClose = false;
                return false;
            }

            Sdl2WindowRegistry.RemoveWindow(this);
            Closing?.Invoke();
            _sdl.DestroyWindow(_window);
            _exists = false;
            Closed?.Invoke();

            return true;
        }

        private void WindowOwnerRoutine(object state)
        {
            WindowParams wp = (WindowParams)state;
            _window = wp.Create();
            WindowID = _sdl.GetWindowID(_window);
            Sdl2WindowRegistry.RegisterWindow(this);
            PostWindowCreated(wp.WindowFlags);
            wp.ResetEvent.Set();

            double previousPollTimeMs = 0;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (_exists)
            {
                if (_shouldClose && CloseCore())
                {
                    return;
                }

                double currentTick = sw.ElapsedTicks;
                double currentTimeMs = sw.ElapsedTicks * (1000.0 / Stopwatch.Frequency);
                if (LimitPollRate && currentTimeMs - previousPollTimeMs < PollIntervalInMs)
                {
                    System.Threading.Thread.Sleep(0);
                }
                else
                {
                    previousPollTimeMs = currentTimeMs;
                    ProcessEvents(null);
                }
            }
        }

        private void PostWindowCreated(SDL_WindowFlags flags)
        {
            RefreshCachedPosition();
            RefreshCachedSize();
            if ((flags & SDL_WindowFlags.Shown) == SDL_WindowFlags.Shown)
            {
                _sdl.ShowWindow(_window);
            }

            _exists = true;
        }

        // Called by Sdl2WindowRegistry when an event for this window is encountered.
        internal void AddEvent(Event ev)
        {
            _events.Add(ev);
        }

        public InputSnapshot PumpEvents()
        {
            _currentMouseDelta = new Vector2();
            if (_threadedProcessing)
            {
                SimpleInputSnapshot snapshot = Interlocked.Exchange(ref _privateSnapshot, _privateBackbuffer);
                snapshot.CopyTo(_publicSnapshot);
                snapshot.Clear();
            }
            else
            {
                ProcessEvents(null);
                _privateSnapshot.CopyTo(_publicSnapshot);
                _privateSnapshot.Clear();
            }

            return _publicSnapshot;
        }

        private void ProcessEvents(SDLEventHandler eventHandler)
        {
            CheckNewWindowTitle();

            Sdl2Events.ProcessEvents();
            for (int i = 0; i < _events.Count; i++)
            {
                Event ev = _events[i];
                if (eventHandler == null)
                {
                    HandleEvent(&ev);
                }
                else
                {
                    eventHandler(ref ev);
                }
            }
            _events.Clear();
        }

        public void PumpEvents(SDLEventHandler eventHandler)
        {
            ProcessEvents(eventHandler);
        }

        private unsafe void HandleEvent(Event* ev)
        {
            switch ((EventType)ev->Type)
            {
                case EventType.Quit:
                    Close();
                    break;
                case EventType.AppTerminating:
                    Close();
                    break;
                case EventType.Windowevent:
                    WindowEvent windowEvent = Unsafe.Read<WindowEvent>(ev);
                    HandleWindowEvent(windowEvent);
                    break;
                case EventType.Keydown:
                case EventType.Keyup:
                    KeyboardEvent keyboardEvent = Unsafe.Read<KeyboardEvent>(ev);
                    HandleKeyboardEvent(keyboardEvent);
                    break;
                case EventType.Textediting:
                    break;
                case EventType.Textinput:
                    TextInputEvent textInputEvent = Unsafe.Read<TextInputEvent>(ev);
                    HandleTextInputEvent(textInputEvent);
                    break;
                case EventType.Keymapchanged:
                    break;
                case EventType.Mousemotion:
                    MouseMotionEvent mouseMotionEvent = Unsafe.Read<MouseMotionEvent>(ev);
                    HandleMouseMotionEvent(mouseMotionEvent);
                    break;
                case EventType.Mousebuttondown:
                case EventType.Mousebuttonup:
                    MouseButtonEvent mouseButtonEvent = Unsafe.Read<MouseButtonEvent>(ev);
                    HandleMouseButtonEvent(mouseButtonEvent);
                    break;
                case EventType.Mousewheel:
                    Silk.NET.SDL.MouseWheelEvent mouseWheelEvent = Unsafe.Read<Silk.NET.SDL.MouseWheelEvent>(ev);
                    HandleMouseWheelEvent(mouseWheelEvent);
                    break;
                case EventType.Dropfile:
                case EventType.Dropbegin:
                case EventType.Droptext:
                    DropEvent dropEvent = Unsafe.Read<DropEvent>(ev);
                    HandleDropEvent(dropEvent);
                    break;
                default:
                    // Ignore
                    break;
            }
        }

        private void CheckNewWindowTitle()
        {
            if (WindowState != WindowState.Minimized && _newWindowTitleReceived)
            {
                _newWindowTitleReceived = false;
                _sdl.SetWindowTitle(_window, _cachedWindowTitle);
            }
        }

        private void HandleTextInputEvent(TextInputEvent textInputEvent)
        {
            const int MaxTextSize = 32;
            uint byteCount = 0;
            // Loop until the null terminator is found or the max size is reached.
            while (byteCount < MaxTextSize && textInputEvent.Text[byteCount++] != 0)
            { }

            if (byteCount > 1)
            {
                // We don't want the null terminator.
                byteCount -= 1;
                int charCount = Encoding.UTF8.GetCharCount(textInputEvent.Text, (int)byteCount);
                char* charsPtr = stackalloc char[charCount];
                Encoding.UTF8.GetChars(textInputEvent.Text, (int)byteCount, charsPtr, charCount);
                for (int i = 0; i < charCount; i++)
                {
                    _privateSnapshot.AddKeyChar(charsPtr[i]);
                }
            }
        }

        private void HandleMouseWheelEvent(Silk.NET.SDL.MouseWheelEvent mouseWheelEvent)
        {
            // PreciseY carries sub-detent resolution from precision touchpads and high-end
            // mice. The integer Y field rounds those down to 0, making slow scroll feel dead.
            float delta = mouseWheelEvent.PreciseY;
            _privateSnapshot.WheelDelta += delta;
            MouseWheel?.Invoke(new MouseWheelEventArgs(GetCurrentMouseState(), delta));
        }

        private void HandleDropEvent(DropEvent dropEvent)
        {
            string file = Marshal.PtrToStringUTF8((nint)dropEvent.File);
            _sdl.Free(dropEvent.File);

            if ((EventType)dropEvent.Type == EventType.Dropfile)
            {
                DragDrop?.Invoke(new DragDropEvent(file));
            }
        }

        private void HandleMouseButtonEvent(MouseButtonEvent mouseButtonEvent)
        {
            MouseButton button = MapMouseButton(mouseButtonEvent.Button);
            bool down = mouseButtonEvent.State == 1;
            _currentMouseButtonStates[(int)button] = down;
            _privateSnapshot.SetMouseButton(button, down);
            MouseEvent mouseEvent = new MouseEvent(button, down);
            _privateSnapshot.AddMouseEvent(mouseEvent);
            if (down)
            {
                MouseDown?.Invoke(mouseEvent);
            }
            else
            {
                MouseUp?.Invoke(mouseEvent);
            }
        }

        private MouseButton MapMouseButton(byte button)
        {
            switch (button)
            {
                case 1: // SDL_BUTTON_LEFT
                    return MouseButton.Left;
                case 2: // SDL_BUTTON_MIDDLE
                    return MouseButton.Middle;
                case 3: // SDL_BUTTON_RIGHT
                    return MouseButton.Right;
                case 4: // SDL_BUTTON_X1
                    return MouseButton.Button1;
                case 5: // SDL_BUTTON_X2
                    return MouseButton.Button2;
                default:
                    return MouseButton.Left;
            }
        }

        private void HandleMouseMotionEvent(MouseMotionEvent mouseMotionEvent)
        {
            // Skip spurious zero-delta motion events. SDL 2.24+ on Windows emits these
            // continuously via raw input. Only filter when the absolute position is also
            // unchanged: events with Xrel=Yrel=0 but a fresh X/Y are legitimate position
            // updates SDL generates on focus-gain and mouse-enter-window, and must not be
            // dropped or the MouseMove stream silently desyncs from the real cursor.
            if (mouseMotionEvent.Xrel == 0 && mouseMotionEvent.Yrel == 0
                && mouseMotionEvent.X == _currentMouseX && mouseMotionEvent.Y == _currentMouseY)
            {
                return;
            }

            Vector2 mousePos = new Vector2(mouseMotionEvent.X, mouseMotionEvent.Y);
            Vector2 delta = new Vector2(mouseMotionEvent.Xrel, mouseMotionEvent.Yrel);

            _currentMouseX = (int)mousePos.X;
            _currentMouseY = (int)mousePos.Y;
            _privateSnapshot.MousePosition = mousePos;

            if (!_firstMouseEvent)
            {
                _currentMouseDelta += delta;
                MouseMove?.Invoke(new MouseMoveEventArgs(GetCurrentMouseState(), mousePos));
            }

            _firstMouseEvent = false;
        }

        private void HandleKeyboardEvent(KeyboardEvent keyboardEvent)
        {
            SimpleInputSnapshot snapshot = _privateSnapshot;
            KeyEvent keyEvent = new KeyEvent(MapKey(keyboardEvent.Keysym), keyboardEvent.State == 1, MapModifierKeys(keyboardEvent.Keysym.Mod), keyboardEvent.Repeat == 1);
            snapshot.AddKeyEvent(keyEvent);
            if (keyboardEvent.State == 1)
            {
                KeyDown?.Invoke(keyEvent);
            }
            else
            {
                KeyUp?.Invoke(keyEvent);
            }
        }

        private Key MapKey(Keysym keysym)
        {
            switch (keysym.Scancode)
            {
                case Scancode.ScancodeA:
                    return Key.A;
                case Scancode.ScancodeB:
                    return Key.B;
                case Scancode.ScancodeC:
                    return Key.C;
                case Scancode.ScancodeD:
                    return Key.D;
                case Scancode.ScancodeE:
                    return Key.E;
                case Scancode.ScancodeF:
                    return Key.F;
                case Scancode.ScancodeG:
                    return Key.G;
                case Scancode.ScancodeH:
                    return Key.H;
                case Scancode.ScancodeI:
                    return Key.I;
                case Scancode.ScancodeJ:
                    return Key.J;
                case Scancode.ScancodeK:
                    return Key.K;
                case Scancode.ScancodeL:
                    return Key.L;
                case Scancode.ScancodeM:
                    return Key.M;
                case Scancode.ScancodeN:
                    return Key.N;
                case Scancode.ScancodeO:
                    return Key.O;
                case Scancode.ScancodeP:
                    return Key.P;
                case Scancode.ScancodeQ:
                    return Key.Q;
                case Scancode.ScancodeR:
                    return Key.R;
                case Scancode.ScancodeS:
                    return Key.S;
                case Scancode.ScancodeT:
                    return Key.T;
                case Scancode.ScancodeU:
                    return Key.U;
                case Scancode.ScancodeV:
                    return Key.V;
                case Scancode.ScancodeW:
                    return Key.W;
                case Scancode.ScancodeX:
                    return Key.X;
                case Scancode.ScancodeY:
                    return Key.Y;
                case Scancode.ScancodeZ:
                    return Key.Z;
                case Scancode.Scancode1:
                    return Key.Number1;
                case Scancode.Scancode2:
                    return Key.Number2;
                case Scancode.Scancode3:
                    return Key.Number3;
                case Scancode.Scancode4:
                    return Key.Number4;
                case Scancode.Scancode5:
                    return Key.Number5;
                case Scancode.Scancode6:
                    return Key.Number6;
                case Scancode.Scancode7:
                    return Key.Number7;
                case Scancode.Scancode8:
                    return Key.Number8;
                case Scancode.Scancode9:
                    return Key.Number9;
                case Scancode.Scancode0:
                    return Key.Number0;
                case Scancode.ScancodeReturn:
                    return Key.Enter;
                case Scancode.ScancodeEscape:
                    return Key.Escape;
                case Scancode.ScancodeBackspace:
                    return Key.BackSpace;
                case Scancode.ScancodeTab:
                    return Key.Tab;
                case Scancode.ScancodeSpace:
                    return Key.Space;
                case Scancode.ScancodeMinus:
                    return Key.Minus;
                case Scancode.ScancodeEquals:
                    return Key.Plus;
                case Scancode.ScancodeLeftbracket:
                    return Key.BracketLeft;
                case Scancode.ScancodeRightbracket:
                    return Key.BracketRight;
                case Scancode.ScancodeBackslash:
                    return Key.BackSlash;
                case Scancode.ScancodeSemicolon:
                    return Key.Semicolon;
                case Scancode.ScancodeApostrophe:
                    return Key.Quote;
                case Scancode.ScancodeGrave:
                    return Key.Grave;
                case Scancode.ScancodeComma:
                    return Key.Comma;
                case Scancode.ScancodePeriod:
                    return Key.Period;
                case Scancode.ScancodeSlash:
                    return Key.Slash;
                case Scancode.ScancodeCapslock:
                    return Key.CapsLock;
                case Scancode.ScancodeF1:
                    return Key.F1;
                case Scancode.ScancodeF2:
                    return Key.F2;
                case Scancode.ScancodeF3:
                    return Key.F3;
                case Scancode.ScancodeF4:
                    return Key.F4;
                case Scancode.ScancodeF5:
                    return Key.F5;
                case Scancode.ScancodeF6:
                    return Key.F6;
                case Scancode.ScancodeF7:
                    return Key.F7;
                case Scancode.ScancodeF8:
                    return Key.F8;
                case Scancode.ScancodeF9:
                    return Key.F9;
                case Scancode.ScancodeF10:
                    return Key.F10;
                case Scancode.ScancodeF11:
                    return Key.F11;
                case Scancode.ScancodeF12:
                    return Key.F12;
                case Scancode.ScancodePrintscreen:
                    return Key.PrintScreen;
                case Scancode.ScancodeScrolllock:
                    return Key.ScrollLock;
                case Scancode.ScancodePause:
                    return Key.Pause;
                case Scancode.ScancodeInsert:
                    return Key.Insert;
                case Scancode.ScancodeHome:
                    return Key.Home;
                case Scancode.ScancodePageup:
                    return Key.PageUp;
                case Scancode.ScancodeDelete:
                    return Key.Delete;
                case Scancode.ScancodeEnd:
                    return Key.End;
                case Scancode.ScancodePagedown:
                    return Key.PageDown;
                case Scancode.ScancodeRight:
                    return Key.Right;
                case Scancode.ScancodeLeft:
                    return Key.Left;
                case Scancode.ScancodeDown:
                    return Key.Down;
                case Scancode.ScancodeUp:
                    return Key.Up;
                case Scancode.ScancodeNumlockclear:
                    return Key.NumLock;
                case Scancode.ScancodeKPDivide:
                    return Key.KeypadDivide;
                case Scancode.ScancodeKPMultiply:
                    return Key.KeypadMultiply;
                case Scancode.ScancodeKPMinus:
                    return Key.KeypadMinus;
                case Scancode.ScancodeKPPlus:
                    return Key.KeypadPlus;
                case Scancode.ScancodeKPEnter:
                    return Key.KeypadEnter;
                case Scancode.ScancodeKP1:
                    return Key.Keypad1;
                case Scancode.ScancodeKP2:
                    return Key.Keypad2;
                case Scancode.ScancodeKP3:
                    return Key.Keypad3;
                case Scancode.ScancodeKP4:
                    return Key.Keypad4;
                case Scancode.ScancodeKP5:
                    return Key.Keypad5;
                case Scancode.ScancodeKP6:
                    return Key.Keypad6;
                case Scancode.ScancodeKP7:
                    return Key.Keypad7;
                case Scancode.ScancodeKP8:
                    return Key.Keypad8;
                case Scancode.ScancodeKP9:
                    return Key.Keypad9;
                case Scancode.ScancodeKP0:
                    return Key.Keypad0;
                case Scancode.ScancodeKPPeriod:
                    return Key.KeypadPeriod;
                case Scancode.ScancodeNonusbackslash:
                    return Key.NonUSBackSlash;
                case Scancode.ScancodeKPEquals:
                    return Key.KeypadPlus;
                case Scancode.ScancodeF13:
                    return Key.F13;
                case Scancode.ScancodeF14:
                    return Key.F14;
                case Scancode.ScancodeF15:
                    return Key.F15;
                case Scancode.ScancodeF16:
                    return Key.F16;
                case Scancode.ScancodeF17:
                    return Key.F17;
                case Scancode.ScancodeF18:
                    return Key.F18;
                case Scancode.ScancodeF19:
                    return Key.F19;
                case Scancode.ScancodeF20:
                    return Key.F20;
                case Scancode.ScancodeF21:
                    return Key.F21;
                case Scancode.ScancodeF22:
                    return Key.F22;
                case Scancode.ScancodeF23:
                    return Key.F23;
                case Scancode.ScancodeF24:
                    return Key.F24;
                case Scancode.ScancodeMenu:
                    return Key.Menu;
                case Scancode.ScancodeLctrl:
                    return Key.ControlLeft;
                case Scancode.ScancodeLshift:
                    return Key.ShiftLeft;
                case Scancode.ScancodeLalt:
                    return Key.AltLeft;
                case Scancode.ScancodeRctrl:
                    return Key.ControlRight;
                case Scancode.ScancodeRshift:
                    return Key.ShiftRight;
                case Scancode.ScancodeRalt:
                    return Key.AltRight;
                case Scancode.ScancodeLgui:
                    return Key.LWin;
                case Scancode.ScancodeRgui:
                    return Key.RWin;
                default:
                    return Key.Unknown;
            }
        }

        private ModifierKeys MapModifierKeys(ushort mod)
        {
            ModifierKeys mods = ModifierKeys.None;
            if ((mod & ((int)Keymod.Lshift | (int)Keymod.Rshift)) != 0)
            {
                mods |= ModifierKeys.Shift;
            }
            if ((mod & ((int)Keymod.Lalt | (int)Keymod.Ralt)) != 0)
            {
                mods |= ModifierKeys.Alt;
            }
            if ((mod & ((int)Keymod.Lctrl | (int)Keymod.Rctrl)) != 0)
            {
                mods |= ModifierKeys.Control;
            }
            if ((mod & ((int)Keymod.Lgui | (int)Keymod.Rgui)) != 0)
            {
                mods |= ModifierKeys.Gui;
            }

            return mods;
        }

        private void HandleWindowEvent(WindowEvent windowEvent)
        {
            switch ((WindowEventID)windowEvent.Event)
            {
                case WindowEventID.Resized:
                case WindowEventID.SizeChanged:
                case WindowEventID.Minimized:
                case WindowEventID.Maximized:
                case WindowEventID.Restored:
                    HandleResizedMessage();
                    break;
                case WindowEventID.FocusGained:
                    FocusGained?.Invoke();
                    break;
                case WindowEventID.FocusLost:
                    FocusLost?.Invoke();
                    break;
                case WindowEventID.Close:
                    Close();
                    break;
                case WindowEventID.Shown:
                    Shown?.Invoke();
                    break;
                case WindowEventID.Hidden:
                    Hidden?.Invoke();
                    break;
                case WindowEventID.Enter:
                    MouseEntered?.Invoke();
                    break;
                case WindowEventID.Leave:
                    MouseLeft?.Invoke();
                    break;
                case WindowEventID.Exposed:
                    Exposed?.Invoke();
                    break;
                case WindowEventID.Moved:
                    _cachedPosition = new Point(windowEvent.Data1, windowEvent.Data2);
                    Moved?.Invoke(new Point(windowEvent.Data1, windowEvent.Data2));
                    break;
                default:
                    Debug.WriteLine("Unhandled SDL WindowEvent: " + (WindowEventID)windowEvent.Event);
                    break;
            }
        }

        private void HandleResizedMessage()
        {
            RefreshCachedSize();
            Resized?.Invoke();
        }

        private void RefreshCachedSize()
        {
            int w, h;
            _sdl.GetWindowSize(_window, &w, &h);
            _cachedSize = new Point(w, h);
        }

        private void RefreshCachedPosition()
        {
            int x, y;
            _sdl.GetWindowPosition(_window, &x, &y);
            _cachedPosition = new Point(x, y);
        }

        private MouseState GetCurrentMouseState()
        {
            return new MouseState(
                _currentMouseX, _currentMouseY,
                _currentMouseButtonStates[0], _currentMouseButtonStates[1],
                _currentMouseButtonStates[2], _currentMouseButtonStates[3],
                _currentMouseButtonStates[4], _currentMouseButtonStates[5],
                _currentMouseButtonStates[6], _currentMouseButtonStates[7],
                _currentMouseButtonStates[8], _currentMouseButtonStates[9],
                _currentMouseButtonStates[10], _currentMouseButtonStates[11],
                _currentMouseButtonStates[12]);
        }

        public Point ScreenToClient(Point p)
        {
            Point position = _cachedPosition;
            return new Point(p.X - position.X, p.Y - position.Y);
        }

        private void SetWindowPosition(int x, int y)
        {
            _sdl.SetWindowPosition(_window, x, y);
            _cachedPosition = new Point(x, y);
        }

        private Point GetWindowSize()
        {
            return _cachedSize;
        }

        private void SetWindowSize(int width, int height)
        {
            _sdl.SetWindowSize(_window, width, height);
            _cachedSize = new Point(width, height);
        }

        private IntPtr GetUnderlyingWindowHandle()
        {
            SysWMInfo wmInfo;
            _sdl.GetVersion(&wmInfo.Version);
            _sdl.GetWindowWMInfo(_window, &wmInfo);
            switch (wmInfo.Subsystem)
            {
                case SysWMType.Windows:
                    return wmInfo.Info.Win.Hwnd;
                case SysWMType.X11:
                    return (IntPtr)wmInfo.Info.X11.Window;
                case SysWMType.Wayland:
                    return (IntPtr)wmInfo.Info.Wayland.Surface;
                case SysWMType.Cocoa:
                    return (IntPtr)wmInfo.Info.Cocoa.Window;
                case SysWMType.Android:
                    return (IntPtr)wmInfo.Info.Android.Window;
                default:
                    return (IntPtr)_window;
            }
        }

        public static Sdl SdlInstance => _sdl;

        private class WindowParams
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Title { get; set; }
            public SDL_WindowFlags WindowFlags { get; set; }

            public IntPtr WindowHandle { get; set; }

            public ManualResetEvent ResetEvent { get; set; }

            public Silk.NET.SDL.Window* Create()
            {
                if (WindowHandle != IntPtr.Zero)
                {
                    return _sdl.CreateWindowFrom((void*)WindowHandle);
                }
                else
                {
                    return _sdl.CreateWindow(Title, X, Y, Width, Height, (uint)WindowFlags);
                }
            }
        }
    }

    /// <summary>
    /// SDL2 window flags, mapped to the same values as SDL_WindowFlags.
    /// Used by Sdl2Window constructors for API compatibility.
    /// </summary>
    [Flags]
    public enum SDL_WindowFlags : uint
    {
        Fullscreen = 0x00000001,
        OpenGL = 0x00000002,
        Shown = 0x00000004,
        Hidden = 0x00000008,
        Borderless = 0x00000010,
        Resizable = 0x00000020,
        Minimized = 0x00000040,
        Maximized = 0x00000080,
        InputGrabbed = 0x00000100,
        InputFocus = 0x00000200,
        MouseFocus = 0x00000400,
        FullScreenDesktop = (Fullscreen | 0x00001000),
        Foreign = 0x00000800,
        AllowHighDpi = 0x00002000,
        MouseCapture = 0x00004000,
        AlwaysOnTop = 0x00008000,
        SkipTaskbar = 0x00010000,
        Utility = 0x00020000,
        Tooltip = 0x00040000,
        PopupMenu = 0x00080000,
        Vulkan = 0x10000000,
        Metal = 0x20000000,
    }

    public unsafe delegate void SDLEventHandler(ref Event ev);
}
