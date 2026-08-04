using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Fields & Properties

        private bool _capturedKeyboard = false; // Whether keyboard input is captured by an element
        /// <summary> Gets whether a UI element is currently requesting keyboard capture. </summary>
        public bool WantsCaptureKeyboard { get; private set; }

        private bool _wrapPointer = false;      // Whether an element is mid-drag and wants unbounded motion
        private Float2 _pointerWrap = Float2.Zero;

        /// <summary>
        /// Gets whether a UI element is mid-drag and wants unbounded pointer motion. Hosts honour this
        /// by wrapping the pointer to the opposite edge when it leaves the window, and reporting the
        /// jump through <see cref="NotifyPointerWrapped"/>.
        /// <para>Unrelated to <c>WantsCapturePointer</c> (which merely says the UI is under the
        /// cursor) and to cursor locking (which pins the pointer and discards its position). Wrapping
        /// keeps the pointer real and on-screen; only its travel becomes unbounded.</para>
        /// </summary>
        public bool WantsPointerWrap { get; private set; }

        // Enums
        public readonly PaperKey[] KeyValues = Enum.GetValues(typeof(PaperKey)).Cast<PaperKey>().ToArray();
        /// <summary> All defined PaperMouseBtn values, cached for iteration. </summary>
        public readonly PaperMouseBtn[] MouseValues = Enum.GetValues(typeof(PaperMouseBtn)).Cast<PaperMouseBtn>().ToArray();

        // Events
        /// <summary> Raised when the pointer position is set. The Float2 parameter contains the new pointer coordinates. </summary>
        public event Action<Float2> OnPointerPosSet;
        /// <summary> Raised when the cursor visibility changes. The boolean parameter is true when the cursor becomes visible and false when it becomes hidden. </summary>
        public event Action<bool> OnCursorVisibilitySet;

        #region Keyboard State

        // Keyboard state tracking
        private bool[] _keyCurState;
        private bool[] _keyPrevState;
        private float[] _keyPressedTime;
        /// <summary> Gets the most recently pressed key, or PaperKey.Unknown if no key has been pressed since the last reset. </summary>
        public PaperKey LastKeyPressed { get; private set; } = PaperKey.Unknown;

        #region Auto-Repeat Settings

        // Auto-repeat configuration
        private bool _keyAutoRepeatEnabled = true;
        private float _autoRepeatDelay = 0.8f; // Initial delay in seconds before repeating starts
        private float _autoRepeatRate = 0.05f; // Time between repeats once started (20 repeats per second)

        // Auto-repeat state tracking
        private float[] _keyRepeatTimer;
        private bool[] _keyRepeating;

        // Public properties for configuration
        /// <summary> Gets or sets whether holding a key down automatically repeats key-press events. Defaults to true. </summary>
        public bool KeyAutoRepeatEnabled
        {
            get => _keyAutoRepeatEnabled;
            set => _keyAutoRepeatEnabled = value;
        }

        /// <summary> Gets or sets the initial delay in seconds before a held key begins repeating. The minimum value is 0.1. </summary>
        public float AutoRepeatDelay
        {
            get => _autoRepeatDelay;
            set => _autoRepeatDelay = Maths.Max(0.1f, value); // Minimum safe delay
        }

        /// <summary> Gets or sets the time in seconds between auto-repeated key events after the initial delay. Clamped to a minimum of 0.01 (max 100 repeats per second). </summary>
        public float AutoRepeatRate
        {
            get => _autoRepeatRate;
            set => _autoRepeatRate = Maths.Max(0.01f, value); // Maximum rate of 100 per second
        }

        #endregion

        #endregion

        #region Mouse State

        // Mouse state tracking
        private bool[] _pointerCurState;
        private bool[] _pointerPrevState;
        private float[] _pointerPressedTime;
        private Float2[] _pointerClickPos;
        public PaperMouseBtn LastButtonPressed { get; private set; } = PaperMouseBtn.Unknown;
        public Float2 PreviousPointerPos { get; private set; } = Float2.Zero;

        // Current pointer position
        private Float2 _pointerPos;
        /// <summary> Gets or sets the current pointer position. Setting the value raises OnPointerPosSet. </summary>
        public Float2 PointerPos {
            get => _pointerPos;
            set {
                _pointerPos = value;
                OnPointerPosSet?.Invoke(_pointerPos);
            }
        }

        // Mouse wheel
        /// <summary> Cumulative mouse wheel scroll value since the last reset. Positive values indicate forward/up scrolling, negative indicates backward/down. </summary>
        public float PointerWheel { get; private set; } = 0;

        // Derived properties
        /// <summary>
        /// The change in pointer position since the last update. Continuous across any wrap the host
        /// performed for <see cref="WrapPointer"/>, so a wrapping drag keeps reporting real motion
        /// instead of a full-screen jump when the pointer crosses an edge.
        /// <para>Identical to <see cref="PointerDeltaRaw"/> whenever nothing is wrapping, which is the
        /// normal case.</para>
        /// </summary>
        public Float2 PointerDelta => (PointerPos - PreviousPointerPos) - _pointerWrap;

        /// <summary> The literal difference between this frame's and last frame's pointer position,
        /// including any wrap. Use only when you specifically want the un-corrected value. </summary>
        public Float2 PointerDeltaRaw => PointerPos - PreviousPointerPos;

        public bool IsPointerMoving => Float2.LengthSquared(PointerDelta) > 0;

        // Double-click tracking
        private float[] _pointerLastClickTime;
        private Float2[] _pointerLastClickPos;
        // Set when a press completes a double-click, so the release that follows does not re-arm the
        // window; without this a third click keeps chaining into further double-clicks.
        private bool[] _pointerDoubleClickConsumed;
        private const float MaxDoubleClickTime = 0.25f;

        #endregion

        #region Text Input

        // Text input
        /// <summary> Queue of characters buffered for text input processing. Characters are enqueued from keyboard events and cleared after each frame's input handling. </summary>
        public readonly Queue<char> InputString = new Queue<char>();

        #endregion

        #region Timing & Scaling

        // Frame timing
        private float _deltaTime = 0.016f; // Default to 60 FPS
        private float _time = 0f;
        /// <summary> Time in seconds since the last frame update, used for frame-rate-independent logic. </summary>
        public float DeltaTime => _deltaTime;
        /// <summary> Gets the accumulated time in seconds since the paper instance was initialized. </summary>
        public float Time => _time;

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
            _keyPressedTime = new float[KeyValues.Length];
            _keyRepeatTimer = new float[KeyValues.Length];
            _keyRepeating = new bool[KeyValues.Length];

            // Initialize keyboard arrays
            _keyCurState = new bool[KeyValues.Length];
            _keyPrevState = new bool[KeyValues.Length];
            _keyPressedTime = new float[KeyValues.Length];

            // Initialize mouse arrays
            _pointerCurState = new bool[MouseValues.Length];
            _pointerPrevState = new bool[MouseValues.Length];
            _pointerPressedTime = new float[MouseValues.Length];
            _pointerClickPos = new Float2[MouseValues.Length];
            _pointerLastClickTime = new float[MouseValues.Length];
            _pointerLastClickPos = new Float2[MouseValues.Length];
            _pointerDoubleClickConsumed = new bool[MouseValues.Length];

            // Initialize clipboard handler
            _clipboardHandler = null;

            _time = 0;
        }

        #endregion

        #region Clipboard Handling

        /// <summary> Sets the clipboard handler for text operations. Throws ArgumentNullException if handler is null. </summary>
        public void SetClipboardHandler(IClipboardHandler handler)
        {
            _clipboardHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary> Returns the current clipboard text, or an empty string if no clipboard handler is set. </summary>
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

        /// <summary> Marks the currently active element as the exclusive receiver of keyboard input until the next frame begins. </summary>
        public void CaptureKeyboard()
        {
            _capturedKeyboard = true;
        }

        /// <summary>
        /// Marks the currently active element as wanting unbounded pointer motion for this frame - a
        /// drag that must not stop at the screen edge. Call it every frame the drag continues.
        /// <para>The host wraps the pointer to the opposite edge when it leaves the window and reports
        /// the jump via <see cref="NotifyPointerWrapped"/>, so <see cref="PointerDelta"/> stays
        /// continuous. A widget does nothing beyond calling this and reading the delta as usual.</para>
        /// </summary>
        public void WrapPointer()
        {
            _wrapPointer = true;
        }

        /// <summary>
        /// Told by the host that it teleported the pointer by <paramref name="warpDelta"/> this frame
        /// to wrap it back into the window. <see cref="PointerDelta"/> subtracts it so the motion reads
        /// as continuous; <see cref="PointerDeltaRaw"/> keeps the jump.
        /// </summary>
        public void NotifyPointerWrapped(Float2 warpDelta) => _pointerWrap += warpDelta;

        /// <summary>
        /// Work out where the pointer should be teleported to keep a <see cref="WrapPointer"/> drag
        /// going, and record the jump. Hosts call this each frame and, when it returns true, move the
        /// real cursor to <paramref name="wrapped"/>.
        /// <para>Centralised here so every backend wraps identically; a host only has to know how to
        /// set the OS cursor position.</para>
        /// </summary>
        /// <param name="pos">Current pointer position, in the same space fed to Paper.</param>
        /// <param name="width">Window width in that space.</param>
        /// <param name="height">Window height in that space.</param>
        /// <param name="wrapped">Position to teleport to; equals <paramref name="pos"/> when false.</param>
        /// <param name="margin">How far inside the edge to land, so the pointer does not immediately
        /// re-wrap on the following frame.</param>
        public bool TryWrapPointer(Float2 pos, float width, float height, out Float2 wrapped, float margin = 4f)
        {
            wrapped = pos;
            if (!WantsPointerWrap || width <= margin * 4 || height <= margin * 4)
                return false;

            if (pos.X <= margin) wrapped.X = width - margin * 2;
            else if (pos.X >= width - margin) wrapped.X = margin * 2;

            if (pos.Y <= margin) wrapped.Y = height - margin * 2;
            else if (pos.Y >= height - margin) wrapped.Y = margin * 2;

            if (wrapped.X == pos.X && wrapped.Y == pos.Y)
                return false;

            NotifyPointerWrapped(wrapped - pos);
            return true;
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
        public void SetTime(float deltaTime)
        {
            _time += deltaTime;
            _deltaTime = deltaTime;
        }

        /// <summary>
        /// Begins the input processing for a new frame.
        /// </summary>
        private void StartInputFrame()
        {
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
            _wrapPointer = false;
            _pointerWrap = Float2.Zero;

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
                bool justPressed = !_pointerPrevState[i] && _pointerCurState[i];
                bool justReleased = _pointerPrevState[i] && !_pointerCurState[i];

                // If this press falls inside the armed window near the last click, it completes a
                // double-click. Flag it and disarm now so the matching release does not re-arm.
                if (justPressed && _time < _pointerLastClickTime[i] &&
                    Float2.LengthSquared(PointerPos - _pointerLastClickPos[i]) < 2)
                {
                    _pointerDoubleClickConsumed[i] = true;
                    _pointerLastClickTime[i] = 0.0f;
                }

                if (justReleased)
                {
                    if (_pointerDoubleClickConsumed[i])
                        // Second click of a pair: reset so the next click starts a fresh single click.
                        _pointerDoubleClickConsumed[i] = false;
                    else
                    {
                        _pointerLastClickTime[i] = _time + MaxDoubleClickTime;
                        _pointerLastClickPos[i] = PointerPos;
                    }
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
            WantsPointerWrap = _wrapPointer;
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
                _pointerClickPos[i] = Float2.Zero;
                _pointerLastClickTime[i] = 0;
                _pointerDoubleClickConsumed[i] = false;
            }

            LastButtonPressed = PaperMouseBtn.Unknown;
            PreviousPointerPos = _pointerPos;
            _pointerPos = Float2.Zero;
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
        public void SetPointerPosition(float x, float y)
        {
            _pointerPos = new Float2(x, y);
        }

        /// <summary>
        /// Sets the state of a mouse button or updates pointer position.
        /// </summary>
        /// <param name="btn">The mouse button</param>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="isPointerBtnDown">Whether the button is pressed</param>
        /// <param name="isPointerMove">Whether this is a movement event</param>
        public void SetPointerState(PaperMouseBtn btn, float x, float y, bool isPointerBtnDown, bool isPointerMove)
        {
            var index = (int)btn;
            LastButtonPressed = btn;

            if (!isPointerMove)
            {
                _pointerPrevState[index] = _pointerCurState[index];
                _pointerCurState[index] = isPointerBtnDown;
                _pointerClickPos[index] = new Float2(x, y);
            }
            else
            {
                _pointerPos = new Float2(x, y);
            }
        }

        /// <summary> Sets the mouse wheel scroll delta for the current frame. </summary>
        public void SetPointerWheel(float wheel)
        {
            PointerWheel = wheel;
        }

        /// <summary> Adds each character of the text to the input queue, converting carriage returns to newlines. </summary>
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
        public bool IsKeyHeld(PaperKey key, float holdDuration = 0.5f) => IsKeyDown(key) && _keyPressedTime[(int)key] >= holdDuration;

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
        public bool IsPointerHeld(PaperMouseBtn btn, float holdDuration = 0.5f) => IsPointerDown(btn) && _pointerPressedTime[(int)btn] >= holdDuration;

        /// <summary> Checks whether the specified mouse button was double-clicked within the time and distance thresholds. </summary>
        public bool IsPointerDoubleClick(PaperMouseBtn btn) =>
            IsPointerPressed(btn) && _time < _pointerLastClickTime[(int)btn] &&
            Float2.LengthSquared(PointerPos - _pointerLastClickPos[(int)btn]) < 2; // squared distance threshold of 2 pixels

        /// <summary>
        /// Gets the position where a mouse button was clicked.
        /// </summary>
        /// <param name="btn">The mouse button to query.</param>
        public Float2 GetPointerClickPos(PaperMouseBtn btn) => _pointerClickPos[(int)btn];

        /// <summary> Returns whether the pointer position lies within the axis-aligned rectangle (x, y, width, height), inclusive of its edges. </summary>
        public bool IsPointerOverRect(float x, float y, float width, float height)
        {
            return _pointerPos.X >= x && _pointerPos.X <= x + width &&
                   _pointerPos.Y >= y && _pointerPos.Y <= y + height;
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

    /// <summary> Identifies a key on the Paper input device. </summary>
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
