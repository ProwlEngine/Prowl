using System;

using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Input handler for play mode that only forwards keyboard/mouse input
/// when the Game View panel is hovered. Gamepads always pass through.
/// Wraps the real DefaultInputHandler underneath.
/// </summary>
public class GameViewInputHandler : IInputHandler
{
    private readonly IInputHandler _real;

    /// <summary>
    /// Set to true each frame by GameViewPanel when it's hovered.
    /// Reset to false at the start of each frame.
    /// </summary>
    public static bool IsGameViewFocused { get; set; }

    public GameViewInputHandler(IInputHandler realHandler)
    {
        _real = realHandler;
    }

    // Clipboard always works
    public string Clipboard
    {
        get => _real.Clipboard;
        set => _real.Clipboard = value;
    }

    // Keyboard — only when Game View focused
    public bool IsAnyKeyDown => IsGameViewFocused && _real.IsAnyKeyDown;
    public char? GetPressedChar() => IsGameViewFocused ? _real.GetPressedChar() : null;
    public bool GetKey(KeyCode key) => IsGameViewFocused && _real.GetKey(key);
    public bool GetKeyDown(KeyCode key) => IsGameViewFocused && _real.GetKeyDown(key);
    public bool GetKeyUp(KeyCode key) => IsGameViewFocused && _real.GetKeyUp(key);

    // Mouse — only when Game View focused
    public Int2 PrevMousePosition => _real.PrevMousePosition;
    public Int2 MousePosition
    {
        get => _real.MousePosition;
        set => _real.MousePosition = value;
    }
    public Float2 MouseDelta => IsGameViewFocused ? _real.MouseDelta : Float2.Zero;
    public float MouseWheelDelta => IsGameViewFocused ? _real.MouseWheelDelta : 0f;
    public bool GetMouseButton(int button) => IsGameViewFocused && _real.GetMouseButton(button);
    public bool GetMouseButtonDown(int button) => IsGameViewFocused && _real.GetMouseButtonDown(button);
    public bool GetMouseButtonUp(int button) => IsGameViewFocused && _real.GetMouseButtonUp(button);
    public void SetCursorVisible(bool visible, int miceIndex = 0) => _real.SetCursorVisible(visible, miceIndex);

    // Events — always forward (editor needs these for its own input processing)
    public event Action<KeyCode, bool> OnKeyEvent
    {
        add => _real.OnKeyEvent += value;
        remove => _real.OnKeyEvent -= value;
    }
    public event Action<MouseButton, float, float, bool, bool> OnMouseEvent
    {
        add => _real.OnMouseEvent += value;
        remove => _real.OnMouseEvent -= value;
    }

    // Gamepads — always pass through (physical controllers work regardless of focus)
    public int GetGamepadCount() => _real.GetGamepadCount();
    public bool IsGamepadConnected(int gamepadIndex) => _real.IsGamepadConnected(gamepadIndex);
    public bool GetGamepadButton(int gamepadIndex, GamepadButton button) => _real.GetGamepadButton(gamepadIndex, button);
    public bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button) => _real.GetGamepadButtonDown(gamepadIndex, button);
    public bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button) => _real.GetGamepadButtonUp(gamepadIndex, button);
    public Float2 GetGamepadAxis(int gamepadIndex, int axisIndex) => _real.GetGamepadAxis(gamepadIndex, axisIndex);
    public float GetGamepadTrigger(int gamepadIndex, int triggerIndex) => _real.GetGamepadTrigger(gamepadIndex, triggerIndex);
    public void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) => _real.SetGamepadVibration(gamepadIndex, leftMotor, rightMotor);
}
