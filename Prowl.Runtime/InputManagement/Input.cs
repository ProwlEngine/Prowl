// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Central input management system for Prowl Engine.
/// Provides both low-level direct input queries and high-level action-based input.
/// </summary>
public static class Input
{
    private static Stack<IInputHandler> _handlers = [];
    private static List<InputActionMap> _actionMaps = [];
    private static float _currentTime = 0;

    public static Stack<IInputHandler> Handlers => _handlers;
    private static readonly NullInputHandler _nullHandler = new();
    public static IInputHandler Current => _handlers.Count > 0 ? _handlers.Peek() : _nullHandler;

    public static void PushHandler(IInputHandler handler) => _handlers.Push(handler);
    public static void PopHandler() => _handlers.Pop();

    #region Low-Level Direct Input API (Backward Compatible)

    // Clipboard
    public static string Clipboard
    {
        get => Current.Clipboard;
        set => Current.Clipboard = value;
    }

    // Events
    public static event Action<KeyCode, bool> OnKeyEvent
    {
        add => Current.OnKeyEvent += value;
        remove => Current.OnKeyEvent -= value;
    }

    public static event Action<MouseButton, float, float, bool, bool> OnMouseEvent
    {
        add => Current.OnMouseEvent += value;
        remove => Current.OnMouseEvent -= value;
    }

    // Mouse
    public static Int2 PrevMousePosition => CursorLocked ? LockedMousePosition : Current.PrevMousePosition;
    public static Int2 MousePosition
    {
        get => CursorLocked ? LockedMousePosition : Current.MousePosition;
        set => Current.MousePosition = value;
    }

    // CursorLockCenter is window-space; the handler maps it into whatever space it reports positions
    // in, so a locked cursor stays consistent with an unlocked one (render-target pixels in the editor's
    // Game View, window pixels everywhere else).
    private static Int2 LockedMousePosition => Current.MapWindowPosition(CursorLockCenter);
    public static Float2 MouseDelta => Current.MouseDelta;
    public static float MouseWheelDelta => Current.MouseWheelDelta;

    // Keyboard
    public static char? GetPressedChar() => Current.GetPressedChar();

    /// <summary>The characters typed this frame, read non-destructively. Prefer this over
    /// <see cref="GetPressedChar"/> for text input - multiple consumers can read it in the same frame.</summary>
    public static string InputString => Current.InputString;
    public static bool GetKey(KeyCode key) => Current.GetKey(key);
    public static bool GetKeyDown(KeyCode key) => Current.GetKeyDown(key);
    public static bool GetKeyUp(KeyCode key) => Current.GetKeyUp(key);

    // Mouse Buttons
    public static bool GetMouseButton(int button) => Current.GetMouseButton(button);
    public static bool GetMouseButtonDown(int button) => Current.GetMouseButtonDown(button);
    public static bool GetMouseButtonUp(int button) => Current.GetMouseButtonUp(button);
    public static void SetCursorVisible(bool visible)
    {
        _cursorVisible = visible;
        Current.ApplyCursorState(_cursorVisible, _lockMode);
    }

    /// <summary>Sets the hardware cursor shape. Hook this to <c>Paper.OnCursorChange</c> so hovered
    /// elements can request a shape (pointer, resize, text, ...).</summary>
    public static void SetCursorShape(PaperCursor shape, int miceIndex = 0) => Current.SetCursorShape(shape, miceIndex);

    /// <summary>
    /// Whether the cursor is currently locked (hidden + recentered each frame).
    /// </summary>
    public static bool CursorLocked => _lockMode == CursorLockMode.Locked;

    /// <summary>
    /// How the cursor is constrained, independently of <see cref="CursorVisible"/>. A mode the topmost
    /// context disallows is rejected: <see cref="OnCursorLockFailed"/> fires and the mode is unchanged.
    /// </summary>
    public static CursorLockMode CursorLockState
    {
        get => _lockMode;
        set
        {
            if (value == _lockMode) return;

            if (value != CursorLockMode.None && !TopLockContext.AllowLock)
            {
                OnCursorLockFailed?.Invoke();
                return;
            }

            _lockMode = value;
            Current.ApplyCursorState(_cursorVisible, _lockMode);

            // Confined traps the pointer as effectively as Locked, so it needs the same escape prompt.
            if (value != CursorLockMode.None)
                OnCursorLocked?.Invoke();
        }
    }

    /// <summary>
    /// Whether the hardware cursor is drawn. <see cref="CursorLockMode.Locked"/> hides it regardless.
    /// </summary>
    public static bool CursorVisible
    {
        get => _cursorVisible;
        set => SetCursorVisible(value);
    }

    /// <summary>
    /// The screen-space center point where the cursor is locked to,
    /// as defined by the topmost lock context.
    /// </summary>
    public static Int2 CursorLockCenter => TopLockContext.GetLockCenter();

    /// <summary>
    /// The inclusive window-space rect <see cref="CursorLockMode.Confined"/> holds the cursor inside,
    /// as defined by the topmost lock context.
    /// </summary>
    public static IntRect CursorConfineBounds => TopLockContext.GetConfineBounds();

    private static CursorLockMode _lockMode = CursorLockMode.None;
    private static bool _cursorVisible = true;

    private static readonly Stack<CursorLockContext> _lockContextStack = new();
    private static readonly CursorLockContext _defaultLockContext = new();

    private static CursorLockContext TopLockContext => _lockContextStack.Count > 0
        ? _lockContextStack.Peek()
        : _defaultLockContext;

    /// <summary>Fired when the cursor is successfully constrained, locked or confined. Editor hooks this to show a toast.</summary>
    public static event Action? OnCursorLocked;

    /// <summary>Fired when a lock attempt is rejected by the current context. Editor hooks this for a toast.</summary>
    public static event Action? OnCursorLockFailed;

    /// <summary>
    /// Push a lock context that defines where the cursor centers and whether locking is allowed.
    /// The topmost context controls behavior. Does not lock the cursor by itself.
    /// </summary>
    public static void PushLockContext(CursorLockContext context) => _lockContextStack.Push(context);

    /// <summary>
    /// Pop the topmost lock context. If the cursor is locked and the stack becomes empty
    /// or the new top disallows locks, the cursor is automatically unlocked.
    /// </summary>
    public static void PopLockContext()
    {
        if (_lockContextStack.Count == 0) return;
        _lockContextStack.Pop();

        // If constrained but no context remains, or new top disallows it, release the cursor
        if (_lockMode != CursorLockMode.None && (_lockContextStack.Count == 0 || !TopLockContext.AllowLock))
            UnlockCursor();
    }

    /// <summary>
    /// Lock the cursor hides it and reports CursorLockCenter as the mouse position.
    /// Uses the topmost lock context to determine the center position.
    /// If the topmost context disallows locking, fires OnCursorLockFailed and returns.
    /// </summary>
    public static void LockCursor()
    {
        CursorLockState = CursorLockMode.Locked;
        if (CursorLocked) // the context can refuse; don't hide a cursor that never locked
            SetCursorVisible(false);
    }

    /// <summary>
    /// Release any cursor constraint and show the cursor again.
    /// Overrides <see cref="CursorVisible"/> for back-compat, unlike setting <see cref="CursorLockState"/> directly
    /// </summary>
    public static void UnlockCursor()
    {
        CursorLockState = CursorLockMode.None;
        SetCursorVisible(true);
    }

    // Gamepad
    public static int GetGamepadCount() => Current.GetGamepadCount();
    public static bool IsGamepadConnected(int gamepadIndex = 0) => Current.IsGamepadConnected(gamepadIndex);
    public static bool GetGamepadButton(GamepadButton button, int gamepadIndex = 0) => Current.GetGamepadButton(gamepadIndex, button);
    public static bool GetGamepadButtonDown(GamepadButton button, int gamepadIndex = 0) => Current.GetGamepadButtonDown(gamepadIndex, button);
    public static bool GetGamepadButtonUp(GamepadButton button, int gamepadIndex = 0) => Current.GetGamepadButtonUp(gamepadIndex, button);
    public static Float2 GetGamepadLeftStick(int gamepadIndex = 0) => Current.GetGamepadAxis(gamepadIndex, 0);
    public static Float2 GetGamepadRightStick(int gamepadIndex = 0) => Current.GetGamepadAxis(gamepadIndex, 1);
    public static float GetGamepadLeftTrigger(int gamepadIndex = 0) => Current.GetGamepadTrigger(gamepadIndex, 0);
    public static float GetGamepadRightTrigger(int gamepadIndex = 0) => Current.GetGamepadTrigger(gamepadIndex, 1);
    public static void SetGamepadVibration(float leftMotor, float rightMotor, int gamepadIndex = 0) => Current.SetGamepadVibration(gamepadIndex, leftMotor, rightMotor);

    #endregion

    #region High-Level Action-Based Input API

    /// <summary>
    /// Registers an input action map with the input system.
    /// The map's actions will be updated each frame.
    /// </summary>
    public static void RegisterActionMap(InputActionMap map)
    {
        if (!_actionMaps.Contains(map))
            _actionMaps.Add(map);
    }

    /// <summary>
    /// Unregisters an input action map from the input system.
    /// </summary>
    public static void UnregisterActionMap(InputActionMap map)
    {
        _actionMaps.Remove(map);
    }

    /// <summary>
    /// Gets all registered action maps.
    /// </summary>
    public static IReadOnlyList<InputActionMap> ActionMaps => _actionMaps.AsReadOnly();

    /// <summary>
    /// Finds an action across all registered maps.
    /// </summary>
    public static InputAction? FindAction(string actionName)
    {
        foreach (InputActionMap map in _actionMaps)
        {
            InputAction? action = map.FindAction(actionName);
            if (action != null)
                return action;
        }
        return null;
    }

    /// <summary>
    /// Finds an action in a specific map using "mapName/actionName" syntax.
    /// </summary>
    public static InputAction? FindAction(string mapName, string actionName)
    {
        InputActionMap? map = _actionMaps.FirstOrDefault(m => m.Name == mapName);
        return map.IsValid() ? map.FindAction(actionName) : null;
    }

    /// <summary>
    /// Updates all registered action maps. Should be called once per frame.
    /// </summary>
    internal static void UpdateActions(float deltaTime)
    {
        _currentTime += deltaTime;

        if (_handlers.Count == 0)
            return;

        foreach (InputActionMap map in _actionMaps)
        {
            if (map.Enabled)
                map.UpdateActions(Current, _currentTime);
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Checks if any key is currently pressed.
    /// </summary>
    public static bool AnyKey => Current.IsAnyKeyDown;

    /// <summary>
    /// Checks if a specific key combination is pressed (e.g., Ctrl+S).
    /// </summary>
    public static bool GetKeyCombo(KeyCode modifier, KeyCode key)
    {
        return GetKey(modifier) && GetKeyDown(key);
    }

    /// <summary>
    /// Checks if Ctrl is held (handles both left and right Ctrl).
    /// </summary>
    public static bool IsCtrlPressed => GetKey(KeyCode.ControlLeft) || GetKey(KeyCode.ControlRight);

    /// <summary>
    /// Checks if Shift is held (handles both left and right Shift).
    /// </summary>
    public static bool IsShiftPressed => GetKey(KeyCode.ShiftLeft) || GetKey(KeyCode.ShiftRight);

    /// <summary>
    /// Checks if Alt is held (handles both left and right Alt).
    /// </summary>
    public static bool IsAltPressed => GetKey(KeyCode.AltLeft) || GetKey(KeyCode.AltRight);

    /// <summary>
    /// Gets a Vector2 from WASD keys (normalized).
    /// </summary>
    public static Float2 GetWASD()
    {
        Float2 input = Float2.Zero;
        if (GetKey(KeyCode.W)) input.Y += 1;
        if (GetKey(KeyCode.S)) input.Y -= 1;
        if (GetKey(KeyCode.A)) input.X -= 1;
        if (GetKey(KeyCode.D)) input.X += 1;

        // Normalize diagonal movement
        float magnitude = Maths.Sqrt(input.X * input.X + input.Y * input.Y);
        if (magnitude > 1.0)
            input /= magnitude;

        return input;
    }

    /// <summary>
    /// Gets a Vector2 from arrow keys (normalized).
    /// </summary>
    public static Float2 GetArrowKeys()
    {
        Float2 input = Float2.Zero;
        if (GetKey(KeyCode.Up)) input.Y += 1;
        if (GetKey(KeyCode.Down)) input.Y -= 1;
        if (GetKey(KeyCode.Left)) input.X -= 1;
        if (GetKey(KeyCode.Right)) input.X += 1;

        // Normalize diagonal movement
        float magnitude = Maths.Sqrt(input.X * input.X + input.Y * input.Y);
        if (magnitude > 1.0)
            input /= magnitude;

        return input;
    }

    #endregion
}
