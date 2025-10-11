// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;

using Silk.NET.Input;

namespace Prowl.Runtime;

/// <summary>
/// Central input management system for Prowl Engine.
/// Provides both low-level direct input queries and high-level action-based input.
/// </summary>
public static class Input
{
    private static Stack<IInputHandler> _handlers = [];
    private static List<InputActionMap> _actionMaps = [];
    private static double _currentTime = 0;

    public static Stack<IInputHandler> Handlers => _handlers;
    public static IInputHandler Current => _handlers.Peek();

    public static void PushHandler(IInputHandler handler) => _handlers.Push(handler);
    public static void PopHandler() => _handlers.Pop();

    #region Low-Level Direct Input API (Backward Compatible)

    // Clipboard
    public static string Clipboard {
        get => Current.Clipboard;
        set => Current.Clipboard = value;
    }

    // Events
    public static event Action<KeyCode, bool> OnKeyEvent {
        add => Current.OnKeyEvent += value;
        remove => Current.OnKeyEvent -= value;
    }

    public static event Action<MouseButton, double, double, bool, bool> OnMouseEvent {
        add => Current.OnMouseEvent += value;
        remove => Current.OnMouseEvent -= value;
    }

    // Mouse
    public static Int2 PrevMousePosition => Current.PrevMousePosition;
    public static Int2 MousePosition {
        get => Current.MousePosition;
        set => Current.MousePosition = value;
    }
    public static Double2 MouseDelta => Current.MouseDelta;
    public static float MouseWheelDelta => Current.MouseWheelDelta;

    // Keyboard
    public static char? GetPressedChar() => Current.GetPressedChar();
    public static bool GetKey(KeyCode key) => Current.GetKey(key);
    public static bool GetKeyDown(KeyCode key) => Current.GetKeyDown(key);
    public static bool GetKeyUp(KeyCode key) => Current.GetKeyUp(key);

    // Mouse Buttons
    public static bool GetMouseButton(int button) => Current.GetMouseButton(button);
    public static bool GetMouseButtonDown(int button) => Current.GetMouseButtonDown(button);
    public static bool GetMouseButtonUp(int button) => Current.GetMouseButtonUp(button);
    public static void SetCursorVisible(bool visible, int miceIndex = 0) => Current.SetCursorVisible(visible, miceIndex);

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
        foreach (var map in _actionMaps)
        {
            var action = map.FindAction(actionName);
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
        var map = _actionMaps.FirstOrDefault(m => m.Name == mapName);
        return map?.FindAction(actionName);
    }

    /// <summary>
    /// Updates all registered action maps. Should be called once per frame.
    /// </summary>
    internal static void UpdateActions(double deltaTime)
    {
        _currentTime += deltaTime;

        if (_handlers.Count == 0)
            return;

        foreach (var map in _actionMaps)
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
        float magnitude = (float)Math.Sqrt(input.X * input.X + input.Y * input.Y);
        if (magnitude > 1f)
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
        float magnitude = (float)Math.Sqrt(input.X * input.X + input.Y * input.Y);
        if (magnitude > 1f)
            input /= magnitude;

        return input;
    }

    #endregion
}
