using System;

using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Input handler for play mode that only forwards keyboard/mouse input
/// when the Game View panel is hovered AND gameplay code is executing.
/// Editor code (camera, gizmos, UI) always gets full input.
/// Gamepads always pass through.
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

    private bool ShouldFilter => Runtime.Application.IsGameplayExecuting && !IsGameViewFocused;

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

    // Keyboard — filtered only during gameplay execution outside game view
    public bool IsAnyKeyDown => ShouldFilter ? false : _real.IsAnyKeyDown;
    public char? GetPressedChar() => ShouldFilter ? null : _real.GetPressedChar();
    public bool GetKey(KeyCode key) => ShouldFilter ? false : _real.GetKey(key);
    public bool GetKeyDown(KeyCode key) => ShouldFilter ? false : _real.GetKeyDown(key);
    public bool GetKeyUp(KeyCode key) => ShouldFilter ? false : _real.GetKeyUp(key);

    // Mouse — filtered only during gameplay execution outside game view
    public Int2 PrevMousePosition => _real.PrevMousePosition;
    public Int2 MousePosition
    {
        get => _real.MousePosition;
        set => _real.MousePosition = value;
    }
    public Float2 MouseDelta => ShouldFilter ? Float2.Zero : _real.MouseDelta;
    public float MouseWheelDelta => ShouldFilter ? 0f : _real.MouseWheelDelta;
    public bool GetMouseButton(int button) => ShouldFilter ? false : _real.GetMouseButton(button);
    public bool GetMouseButtonDown(int button) => ShouldFilter ? false : _real.GetMouseButtonDown(button);
    public bool GetMouseButtonUp(int button) => ShouldFilter ? false : _real.GetMouseButtonUp(button);
    public void SetCursorVisible(bool visible, int miceIndex = 0)
    {
        _real.SetCursorVisible(visible, miceIndex);
    }

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
