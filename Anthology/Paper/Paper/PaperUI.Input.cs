using Prowl.Vector;

namespace Prowl.PaperUI
{
    public static partial class Paper
    {
        #region Fields & Properties

        private static PaperContext.InputState I => Current.Input;

        private static bool _capturedKeyboard { get => I.CapturedKeyboard; set => I.CapturedKeyboard = value; } // Whether keyboard input is captured by an element
        public static bool WantsCaptureKeyboard { get => I.WantsCaptureKeyboard; private set => I.WantsCaptureKeyboard = value; }

        // Enums
        public static readonly PaperKey[] KeyValues = Enum.GetValues<PaperKey>();
        public static readonly PaperMouseBtn[] MouseValues = Enum.GetValues<PaperMouseBtn>();

        // Events
        public static event Action<Vector2> OnPointerPosSet
        {
            add => I.OnPointerPosSet += value;
            remove => I.OnPointerPosSet -= value;
        }
        public static event Action<bool> OnCursorVisibilitySet
        {
            add => I.OnCursorVisibilitySet += value;
            remove => I.OnCursorVisibilitySet -= value;
        }

        #region Keyboard State

        // Keyboard state tracking
        private static bool[] _keyCurState => I.KeyCurState;
        private static bool[] _keyPrevState => I.KeyPrevState;
        private static double[] _keyPressedTime => I.KeyPressedTime;
        public static PaperKey LastKeyPressed { get => I.LastKeyPressed; private set => I.LastKeyPressed = value; }

        #region Auto-Repeat Settings

        // Auto-repeat configuration
        private static bool _keyAutoRepeatEnabled { get => I.KeyAutoRepeatEnabled; set => I.KeyAutoRepeatEnabled = value; }
        private static double _autoRepeatDelay { get => I.AutoRepeatDelay; set => I.AutoRepeatDelay = value; }
        private static double _autoRepeatRate { get => I.AutoRepeatRate; set => I.AutoRepeatRate = value; }

        // Auto-repeat state tracking
        private static double[] _keyRepeatTimer => I.KeyRepeatTimer;
        private static bool[] _keyRepeating => I.KeyRepeating;

        // Public properties for configuration
        public static bool KeyAutoRepeatEnabled
        {
            get => I.KeyAutoRepeatEnabled;
            set => I.KeyAutoRepeatEnabled = value;
        }

        public static double AutoRepeatDelay
        {
            get => I.AutoRepeatDelay;
            set => I.AutoRepeatDelay = Math.Max(0.1, value); // Minimum safe delay
        }

        public static double AutoRepeatRate
        {
            get => I.AutoRepeatRate;
            set => I.AutoRepeatRate = Math.Max(0.01, value); // Maximum rate of 100 per second
        }

        #endregion

        #endregion

        #region Mouse State

        // Mouse state tracking
        private static bool[] _pointerCurState => I.PointerCurState;
        private static bool[] _pointerPrevState => I.PointerPrevState;
        private static double[] _pointerPressedTime => I.PointerPressedTime;
        private static Vector2[] _pointerClickPos => I.PointerClickPos;
        public static PaperMouseBtn LastButtonPressed { get => I.LastButtonPressed; private set => I.LastButtonPressed = value; }
        public static Vector2 PreviousPointerPos { get => I.PreviousPointerPos; private set => I.PreviousPointerPos = value; }

        // Current pointer position
        private static Vector2 _pointerPos
        {
            get => I.PointerPos;
            set
            {
                I.PointerPos = value;
                I.InvokePointerPos(I.PointerPos / I.FrameBufferScale);
            }
        }
        public static Vector2 PointerPos
        {
            get => _pointerPos;
            set => _pointerPos = value;
        }

        // Mouse wheel
        public static double PointerWheel { get => I.PointerWheel; private set => I.PointerWheel = value; }

        // Derived properties
        public static Vector2 PointerDelta => PointerPos - PreviousPointerPos;
        public static bool IsPointerMoving => PointerDelta.sqrMagnitude > 0;

        // Double-click tracking
        private static double[] _pointerLastClickTime => I.PointerLastClickTime;
        private static Vector2[] _pointerLastClickPos => I.PointerLastClickPos;
        private const double MaxDoubleClickTime = 0.25f;

        #endregion

        #region Text Input

        // Text input
        public static Queue<char> InputString => I.InputString;

        #endregion

        #region Timing & Scaling

        // Frame timing
        private static double _deltaTime { get => I.DeltaTime; set => I.DeltaTime = value; }
        private static double _time { get => I.Time; set => I.Time = value; }
        public static double DeltaTime => I.DeltaTime;
        public static double Time => I.Time;

        // Scaling
        private static Vector2 _frameBufferScale { get => I.FrameBufferScale; set => I.FrameBufferScale = value; }

        #endregion

        // Clipboard handling
        private static IClipboardHandler _clipboardHandler { get => I.ClipboardHandler; set => I.ClipboardHandler = value; }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the input system.
        /// </summary>
        private static void InitializeInput()
        {
            // Initialize keyboard arrays
            I.KeyCurState = new bool[KeyValues.Length];
            I.KeyPrevState = new bool[KeyValues.Length];
            I.KeyPressedTime = new double[KeyValues.Length];
            I.KeyRepeatTimer = new double[KeyValues.Length];
            I.KeyRepeating = new bool[KeyValues.Length];

            // Initialize mouse arrays
            I.PointerCurState = new bool[MouseValues.Length];
            I.PointerPrevState = new bool[MouseValues.Length];
            I.PointerPressedTime = new double[MouseValues.Length];
            I.PointerClickPos = new Vector2[MouseValues.Length];
            I.PointerLastClickTime = new double[MouseValues.Length];
            I.PointerLastClickPos = new Vector2[MouseValues.Length];

            // Initialize clipboard handler
            I.ClipboardHandler = null;

            I.Time = 0;
        }

        #endregion

        #region Clipboard Handling

        /// <summary>
        /// Sets the clipboard handler for text operations.
        /// </summary>
        /// <param name="handler">The clipboard handler implementation</param>
        public static void SetClipboardHandler(IClipboardHandler handler)
        {
            _clipboardHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Gets the current clipboard text.
        /// </summary>
        /// <returns>The text from the clipboard</returns>
        public static string GetClipboard()
        {
            if (_clipboardHandler == null)
            {
                Console.WriteLine("Warning: Clipboard handler not initialized.");
                return "";
            }

            return _clipboardHandler.GetClipboardText();
        }

        /// <summary>
        /// Sets the clipboard text.
        /// </summary>
        /// <param name="text">The text to set</param>
        public static void SetClipboard(string text)
        {
            if (_clipboardHandler == null)
            {
                Console.WriteLine("Warning: Clipboard handler not initialized.");
                return;
            }

            _clipboardHandler.SetClipboardText(text);
        }

        #endregion

        #region Text Input Handling

        public static void CaptureKeyboard()
        {
            _capturedKeyboard = true;
        }

        /// <summary>
        /// Adds a character to the input queue.
        /// </summary>
        /// <param name="character">The character to add</param>
        public static void PushInputText(char character) => InputString.Enqueue(character);

        #endregion

        #region Frame Management

        /// <summary>
        /// Updates the timing information.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame</param>
        public static void SetTime(double deltaTime)
        {
            _time += deltaTime;
            _deltaTime = deltaTime;
        }

        /// <summary>
        /// Begins the input processing for a new frame.
        /// </summary>
        /// <param name="frameBufferScale">The framebuffer scale factor</param>
        private static void StartInputFrame(Vector2 frameBufferScale)
        {
            _frameBufferScale = frameBufferScale;

            // Update key pressed times
            for (var i = 0; i < _keyPressedTime.Length; ++i)
                if (_keyCurState[i])
                {
                    _keyPressedTime[i] += _deltaTime;

                    // Handle auto-repeat for keys
                    if (_keyAutoRepeatEnabled)
                    {
                        if (_keyRepeating[i])
                        {
                            _keyRepeatTimer[i] += _deltaTime;
                            if (_keyRepeatTimer[i] >= _autoRepeatRate)
                            {
                                // Trigger a key press event
                                _keyPrevState[i] = false;
                                _keyRepeatTimer[i] = 0;
                            }
                        }
                        else if (_keyPressedTime[i] >= _autoRepeatDelay)
                        {
                            _keyRepeating[i] = true;
                            _keyRepeatTimer[i] = 0;
                        }
                    }
                }

            // Update pointer pressed times
            for (var i = 0; i < _pointerPressedTime.Length; ++i)
                if (_pointerCurState[i])
                    _pointerPressedTime[i] += _deltaTime;

            _capturedKeyboard = false;

        }

        /// <summary>
        /// Finalizes the input processing for the current frame.
        /// </summary>
        private static void EndInputFrame()
        {
            // Update keyboard state
            for (var i = 0; i < _keyCurState.Length; ++i)
            {
                _keyPrevState[i] = _keyCurState[i];

                if (!_keyCurState[i])
                {
                    _keyPressedTime[i] = 0.0f;

                    if (!_keyCurState[i])
                    {
                        _keyPressedTime[i] = 0.0f;
                        _keyRepeatTimer[i] = 0.0f;
                        _keyRepeating[i] = false;
                    }
                }
            }

            // Update mouse state
            for (var i = 0; i < _pointerCurState.Length; ++i)
            {
                if (_pointerPrevState[i] && !_pointerCurState[i]) // Just released
                {
                    _pointerLastClickTime[i] = _time + MaxDoubleClickTime;
                    _pointerLastClickPos[i] = PointerPos;
                }

                _pointerPrevState[i] = _pointerCurState[i];

                if (!_pointerCurState[i])
                    _pointerPressedTime[i] = 0.0f;
            }

            // Reset transient values
            PointerWheel = 0;
            PreviousPointerPos = PointerPos;
            InputString.Clear();

            WantsCaptureKeyboard = _capturedKeyboard;
        }

        /// <summary>
        /// Clears all input state.
        /// </summary>
        public static void ClearInput()
        {
            // Clear keyboard state
            for (var i = 0; i < _keyCurState.Length; ++i)
            {
                _keyCurState[i] = false;
                _keyPrevState[i] = false;
                _keyPressedTime[i] = 0;
            }

            LastKeyPressed = PaperKey.Unknown;

            // Clear mouse state
            for (var i = 0; i < _pointerCurState.Length; ++i)
            {
                _pointerCurState[i] = false;
                _pointerPrevState[i] = false;
                _pointerPressedTime[i] = 0;
                _pointerClickPos[i] = Vector2.zero;
            }

            LastButtonPressed = PaperMouseBtn.Unknown;
            PreviousPointerPos = _pointerPos;
            _pointerPos = Vector2.zero;
            PointerWheel = 0;
        }

        #endregion

        #region Input State Management

        /// <summary>
        /// Sets the state of a keyboard key.
        /// </summary>
        /// <param name="key">The key to update</param>
        /// <param name="isKeyDown">Whether the key is pressed</param>
        public static void SetKeyState(PaperKey key, bool isKeyDown)
        {
            var index = (int)key;

            // If the key is being released, we need to reset the auto-repeat state
            if (!isKeyDown)
            {
                _keyRepeating[index] = false;
                _keyRepeatTimer[index] = 0;
            }

            _keyPrevState[index] = _keyCurState[index];
            _keyCurState[index] = isKeyDown;

            if (isKeyDown)
                LastKeyPressed = key;
        }

        /// <summary>
        /// Sets the position of the pointer (mouse cursor).
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        public static void SetPointerPosition(double x, double y)
        {
            x *= _frameBufferScale.x;
            y *= _frameBufferScale.y;
            _pointerPos = new Vector2(x, y);
        }

        /// <summary>
        /// Sets the state of a mouse button or updates pointer position.
        /// </summary>
        /// <param name="btn">The mouse button</param>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="isPointerBtnDown">Whether the button is pressed</param>
        /// <param name="isPointerMove">Whether this is a movement event</param>
        public static void SetPointerState(PaperMouseBtn btn, double x, double y, bool isPointerBtnDown, bool isPointerMove)
        {
            var index = (int)btn;
            LastButtonPressed = btn;

            x *= _frameBufferScale.x;
            y *= _frameBufferScale.y;

            if (!isPointerMove)
            {
                _pointerPrevState[index] = _pointerCurState[index];
                _pointerCurState[index] = isPointerBtnDown;
                _pointerClickPos[index] = new Vector2(x, y);
            }
            else
            {
                _pointerPos = new Vector2(x, y);
            }
        }

        /// <summary>
        /// Sets the mouse wheel value.
        /// </summary>
        /// <param name="wheel">The wheel delta</param>
        public static void SetPointerWheel(double wheel)
        {
            PointerWheel = wheel;
        }

        /// <summary>
        /// Adds text input characters.
        /// </summary>
        /// <param name="text">The text to add</param>
        public static void AddInputCharacter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (text.Length == 1)
            {
                var c = text[0];
                if (c == '\r')
                    c = '\n';

                InputString.Enqueue(c);
            }
            else
            {
                foreach (var c in text)
                {
                    if (c == '\r')
                        InputString.Enqueue('\n');
                    else
                        InputString.Enqueue(c);
                }
            }
        }

        #endregion

        #region Input State Queries

        #region Keyboard Queries

        /// <summary>
        /// Checks if a key is currently down.
        /// </summary>
        public static bool IsKeyDown(PaperKey key) => _keyCurState[(int)key];

        /// <summary>
        /// Checks if a key is currently up.
        /// </summary>
        public static bool IsKeyUp(PaperKey key) => !_keyCurState[(int)key];

        /// <summary>
        /// Checks if a key was just pressed this frame.
        /// </summary>
        public static bool IsKeyPressed(PaperKey key) => !_keyPrevState[(int)key] && _keyCurState[(int)key];

        /// <summary>
        /// Checks if a key was just released this frame.
        /// </summary>
        public static bool IsKeyReleased(PaperKey key) => _keyPrevState[(int)key] && !_keyCurState[(int)key];

        /// <summary>
        /// Checks if a key has been held down for a while.
        /// </summary>
        public static bool IsKeyHeld(PaperKey key) => IsKeyDown(key) && _keyPressedTime[(int)key] >= 0.5f;

        /// <summary>
        /// Checks if a key is auto-repeating this frame.
        /// </summary>
        public static bool IsKeyRepeating(PaperKey key) =>
            _keyAutoRepeatEnabled && _keyCurState[(int)key] && _keyRepeating[(int)key];

        /// <summary>
        /// Checks if a key was just pressed or is auto-repeating this frame.
        /// </summary>
        public static bool IsKeyPressedOrRepeating(PaperKey key) =>
            IsKeyPressed(key) || (_keyAutoRepeatEnabled && _keyRepeating[(int)key] && _keyRepeatTimer[(int)key] < _autoRepeatRate * 0.5);

        #endregion

        #region Mouse Queries

        /// <summary>
        /// Checks if a mouse button is currently down.
        /// </summary>
        public static bool IsPointerDown(PaperMouseBtn btn) => _pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button is currently up.
        /// </summary>
        public static bool IsPointerUp(PaperMouseBtn btn) => !_pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button was just pressed this frame.
        /// </summary>
        public static bool IsPointerPressed(PaperMouseBtn btn) => !_pointerPrevState[(int)btn] && _pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button was just released this frame.
        /// </summary>
        public static bool IsPointerReleased(PaperMouseBtn btn) => _pointerPrevState[(int)btn] && !_pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button has been held down for a while.
        /// </summary>
        public static bool IsPointerHeld(PaperMouseBtn btn) => IsPointerDown(btn) && _pointerPressedTime[(int)btn] >= 0.5f;

        /// <summary>
        /// Checks if a mouse button was double-clicked.
        /// </summary>
        public static bool IsPointerDoubleClick(PaperMouseBtn btn) =>
            IsPointerPressed(btn) && _time < _pointerLastClickTime[(int)btn] &&
            (PointerPos - _pointerLastClickPos[(int)btn]).sqrMagnitude < 2; // 5^2 = 25

        /// <summary>
        /// Gets the position where a mouse button was clicked.
        /// </summary>
        public static Vector2 GetPointerClickPos(PaperMouseBtn btn) => _pointerClickPos[(int)btn];

        /// <summary>
        /// Checks if the pointer is over a specified rectangle.
        /// </summary>
        public static bool IsPointerOverRect(double x, double y, double width, double height)
        {
            return _pointerPos.x >= x && _pointerPos.x <= x + width &&
                   _pointerPos.y >= y && _pointerPos.y <= y + height;
        }

        #endregion

        #endregion

        #region UI Control

        /// <summary>
        /// Sets the visibility of the cursor.
        /// </summary>
        /// <param name="visible">Whether the cursor should be visible</param>
        public static void SetCursorVisibility(bool visible) => I.InvokeCursorVisibility(visible);

        #endregion
    }

    /// <summary>
    /// Interface for clipboard handling operations.
    /// </summary>
    public interface IClipboardHandler
    {
        /// <summary>
        /// Gets text from the clipboard.
        /// </summary>
        string GetClipboardText();

        /// <summary>
        /// Sets text to the clipboard.
        /// </summary>
        void SetClipboardText(string text);
    }

    public enum PaperKey
    {
        Unknown = 0,

        // Alphanumeric keys
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9, Num0,

        // Function keys
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

        // Special keys
        Enter, Escape, Backspace, Tab, Space,
        Minus, Equals, LeftBracket, RightBracket, Backslash,
        Semicolon, Apostrophe, Grave, Comma, Period, Slash,

        CapsLock, PrintScreen, ScrollLock, Pause,
        Insert, Home, PageUp, Delete, End, PageDown,
        Right, Left, Down, Up,

        // Keypad
        NumLock, KeypadDivide, KeypadMultiply, KeypadMinus, KeypadPlus, KeypadEnter, KeypadEquals,
        Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9, Keypad0,
        KeypadDecimal,

        // Modifier keys
        LeftControl, LeftShift, LeftAlt, LeftSuper,
        RightControl, RightShift, RightAlt, RightSuper,

        // Media keys
        AudioNext, AudioPrevious, AudioStop, AudioPlay, AudioMute,

        // Application control keys
        Application, Menu, Select, Help
    }

    /// <summary>
    /// Enumeration of mouse buttons.
    /// </summary>
    public enum PaperMouseBtn
    {
        Unknown = 0,
        Left,
        Middle,
        Right,
        Button4,
        Button5,
        Button6,
        Button7,
        Button8
    }
}
