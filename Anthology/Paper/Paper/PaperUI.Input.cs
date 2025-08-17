using Prowl.Vector;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Fields & Properties

        private bool _capturedKeyboard = false; // Whether keyboard input is captured by an element
        public bool WantsCaptureKeyboard { get; private set; }

        // Enums
        public readonly PaperKey[] KeyValues = Enum.GetValues<PaperKey>();
        public readonly PaperMouseBtn[] MouseValues = Enum.GetValues<PaperMouseBtn>();

        // Events
        public event Action<Vector2> OnPointerPosSet;
        public event Action<bool> OnCursorVisibilitySet;

        #region Keyboard State

        // Keyboard state tracking
        private bool[] _keyCurState;
        private bool[] _keyPrevState;
        private double[] _keyPressedTime;
        public PaperKey LastKeyPressed { get; private set; } = PaperKey.Unknown;

        #region Auto-Repeat Settings

        // Auto-repeat configuration
        private bool _keyAutoRepeatEnabled = true;
        private double _autoRepeatDelay = 0.8; // Initial delay in seconds before repeating starts
        private double _autoRepeatRate = 0.05; // Time between repeats once started (20 repeats per second)

        // Auto-repeat state tracking
        private double[] _keyRepeatTimer;
        private bool[] _keyRepeating;

        // Public properties for configuration
        public bool KeyAutoRepeatEnabled
        {
            get => _keyAutoRepeatEnabled;
            set => _keyAutoRepeatEnabled = value;
        }

        public double AutoRepeatDelay
        {
            get => _autoRepeatDelay;
            set => _autoRepeatDelay = Math.Max(0.1, value); // Minimum safe delay
        }

        public double AutoRepeatRate
        {
            get => _autoRepeatRate;
            set => _autoRepeatRate = Math.Max(0.01, value); // Maximum rate of 100 per second
        }

        #endregion

        #endregion

        #region Mouse State

        // Mouse state tracking
        private bool[] _pointerCurState;
        private bool[] _pointerPrevState;
        private double[] _pointerPressedTime;
        private Vector2[] _pointerClickPos;
        public PaperMouseBtn LastButtonPressed { get; private set; } = PaperMouseBtn.Unknown;
        public Vector2 PreviousPointerPos { get; private set; } = Vector2.zero;

        // Current pointer position
        private Vector2 _pointerPos;
        public Vector2 PointerPos {
            get => _pointerPos;
            set {
                _pointerPos = value;
                OnPointerPosSet?.Invoke(_pointerPos / _frameBufferScale);
            }
        }

        // Mouse wheel
        public double PointerWheel { get; private set; } = 0;

        // Derived properties
        public Vector2 PointerDelta => PointerPos - PreviousPointerPos;
        public bool IsPointerMoving => PointerDelta.sqrMagnitude > 0;

        // Double-click tracking
        private double[] _pointerLastClickTime;
        private Vector2[] _pointerLastClickPos;
        private const double MaxDoubleClickTime = 0.25f;

        #endregion

        #region Text Input

        // Text input
        public readonly Queue<char> InputString = new Queue<char>();

        #endregion

        #region Timing & Scaling

        // Frame timing
        private double _deltaTime = 0.016f; // Default to 60 FPS
        private double _time = 0f;
        public double DeltaTime => _deltaTime;
        public double Time => _time;

        // Scaling
        private Vector2 _frameBufferScale = Vector2.one;

        #endregion

        // Clipboard handling
        private IClipboardHandler _clipboardHandler;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the input system.
        /// </summary>
        private void InitializeInput()
        {
            // Initialize keyboard arrays
            _keyCurState = new bool[KeyValues.Length];
            _keyPrevState = new bool[KeyValues.Length];
            _keyPressedTime = new double[KeyValues.Length];
            _keyRepeatTimer = new double[KeyValues.Length];
            _keyRepeating = new bool[KeyValues.Length];
            
            // Initialize keyboard arrays
            _keyCurState = new bool[KeyValues.Length];
            _keyPrevState = new bool[KeyValues.Length];
            _keyPressedTime = new double[KeyValues.Length];

            // Initialize mouse arrays
            _pointerCurState = new bool[MouseValues.Length];
            _pointerPrevState = new bool[MouseValues.Length];
            _pointerPressedTime = new double[MouseValues.Length];
            _pointerClickPos = new Vector2[MouseValues.Length];
            _pointerLastClickTime = new double[MouseValues.Length];
            _pointerLastClickPos = new Vector2[MouseValues.Length];

            // Initialize clipboard handler
            _clipboardHandler = null;

            _time = 0;
        }

        #endregion

        #region Clipboard Handling

        /// <summary>
        /// Sets the clipboard handler for text operations.
        /// </summary>
        /// <param name="handler">The clipboard handler implementation</param>
        public void SetClipboardHandler(IClipboardHandler handler)
        {
            _clipboardHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Gets the current clipboard text.
        /// </summary>
        /// <returns>The text from the clipboard</returns>
        public string GetClipboard()
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
        public void SetClipboard(string text)
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

        public void CaptureKeyboard()
        {
            _capturedKeyboard = true;
        }

        /// <summary>
        /// Adds a character to the input queue.
        /// </summary>
        /// <param name="character">The character to add</param>
        public void PushInputText(char character) => InputString.Enqueue(character);

        #endregion

        #region Frame Management

        /// <summary>
        /// Updates the timing information.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame</param>
        public void SetTime(double deltaTime)
        {
            _time += deltaTime;
            _deltaTime = deltaTime;
        }

        /// <summary>
        /// Begins the input processing for a new frame.
        /// </summary>
        /// <param name="frameBufferScale">The framebuffer scale factor</param>
        private void StartInputFrame(Vector2 frameBufferScale)
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
        private void EndInputFrame()
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
        public void ClearInput()
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
        public void SetKeyState(PaperKey key, bool isKeyDown)
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
        public void SetPointerPosition(double x, double y)
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
        public void SetPointerState(PaperMouseBtn btn, double x, double y, bool isPointerBtnDown, bool isPointerMove)
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
        public void SetPointerWheel(double wheel)
        {
            PointerWheel = wheel;
        }

        /// <summary>
        /// Adds text input characters.
        /// </summary>
        /// <param name="text">The text to add</param>
        public void AddInputCharacter(string text)
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
        /// <param name="key">The key to query.</param>
        public bool IsKeyDown(PaperKey key) => _keyCurState[(int)key];

        /// <summary>
        /// Checks if a key is currently up.
        /// </summary>
        /// <param name="key">The key to query.</param>
        public bool IsKeyUp(PaperKey key) => !_keyCurState[(int)key];

        /// <summary>
        /// Checks if a key was just pressed this frame.
        /// </summary>
        /// <param name="key">The key to query.</param>
        public bool IsKeyPressed(PaperKey key) => !_keyPrevState[(int)key] && _keyCurState[(int)key];

        /// <summary>
        /// Checks if a key was just released this frame.
        /// </summary>
        /// <param name="key">The key to query.</param>
        public bool IsKeyReleased(PaperKey key) => _keyPrevState[(int)key] && !_keyCurState[(int)key];

        /// <summary>
        /// Checks if a key has been held down for the specified duration.
        /// </summary>
        /// <param name="key">The key to query.</param>
        /// <param name="holdDuration">Minimum time in seconds the key must be held.</param>
        /// <returns><c>true</c> if the key has been held for at least <paramref name="holdDuration"/> seconds.</returns>
        public bool IsKeyHeld(PaperKey key, double holdDuration = 0.5f) => IsKeyDown(key) && _keyPressedTime[(int)key] >= holdDuration;

        /// <summary>
        /// Checks if a key is auto-repeating this frame.
        /// </summary>
        /// <param name="key">The key to query.</param>
        public bool IsKeyRepeating(PaperKey key) =>
            _keyAutoRepeatEnabled && _keyCurState[(int)key] && _keyRepeating[(int)key];

        /// <summary>
        /// Checks if a key was just pressed or is auto-repeating this frame.
        /// </summary>
        /// <param name="key">The key to query.</param>
        public bool IsKeyPressedOrRepeating(PaperKey key) =>
            IsKeyPressed(key) || (_keyAutoRepeatEnabled && _keyRepeating[(int)key] && _keyRepeatTimer[(int)key] < _autoRepeatRate * 0.5);

        #endregion

        #region Mouse Queries

        /// <summary>
        /// Checks if a mouse button is currently down.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public bool IsPointerDown(PaperMouseBtn btn) => _pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button is currently up.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public bool IsPointerUp(PaperMouseBtn btn) => !_pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button was just pressed this frame.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public bool IsPointerPressed(PaperMouseBtn btn) => !_pointerPrevState[(int)btn] && _pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button was just released this frame.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public bool IsPointerReleased(PaperMouseBtn btn) => _pointerPrevState[(int)btn] && !_pointerCurState[(int)btn];

        /// <summary>
        /// Checks if a mouse button has been held down for the specified duration.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        /// <param name="holdDuration">Minimum time in seconds the button must be held.</param>
        /// <returns><c>true</c> if the button has been held for at least <paramref name="holdDuration"/> seconds.</returns>
        public bool IsPointerHeld(PaperMouseBtn btn, double holdDuration = 0.5f) => IsPointerDown(btn) && _pointerPressedTime[(int)btn] >= holdDuration;

        /// <summary>
        /// Checks if a mouse button was double-clicked.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public bool IsPointerDoubleClick(PaperMouseBtn btn) =>
            IsPointerPressed(btn) && _time < _pointerLastClickTime[(int)btn] &&
            (PointerPos - _pointerLastClickPos[(int)btn]).sqrMagnitude < 2; // 5^2 = 25

        /// <summary>
        /// Gets the position where a mouse button was clicked.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public Vector2 GetPointerClickPos(PaperMouseBtn btn) => _pointerClickPos[(int)btn];

        /// <summary>
        /// Checks if the pointer is over a specified rectangle.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public bool IsPointerOverRect(double x, double y, double width, double height)
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
        public void SetCursorVisibility(bool visible) => OnCursorVisibilitySet?.Invoke(visible);

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
