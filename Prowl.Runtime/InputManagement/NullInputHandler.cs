using System;

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A no-op input handler that returns safe defaults for all queries.
/// Used as a fallback when no real input handler is on the stack.
/// </summary>
internal class NullInputHandler : IInputHandler
{
    public string Clipboard { get => ""; set { } }
    public bool IsAnyKeyDown => false;
    public Float2 MouseDelta => Float2.Zero;
    public Int2 MousePosition { get => Int2.Zero; set { } }
    public float MouseWheelDelta => 0f;
    public Int2 PrevMousePosition => Int2.Zero;

    public event Action<KeyCode, bool> OnKeyEvent { add { } remove { } }
    public event Action<MouseButton, float, float, bool, bool> OnMouseEvent { add { } remove { } }

    public char? GetPressedChar() => null;
    public bool GetKey(KeyCode key) => false;
    public bool GetKeyDown(KeyCode key) => false;
    public bool GetKeyUp(KeyCode key) => false;
    public bool GetMouseButton(int button) => false;
    public bool GetMouseButtonDown(int button) => false;
    public bool GetMouseButtonUp(int button) => false;
    public void SetCursorVisible(bool visible, int miceIndex = 0) { }
    public void SetCursorShape(PaperCursor shape, int miceIndex = 0) { }

    public int GetGamepadCount() => 0;
    public bool IsGamepadConnected(int gamepadIndex) => false;
    public bool GetGamepadButton(int gamepadIndex, GamepadButton button) => false;
    public bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button) => false;
    public bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button) => false;
    public Float2 GetGamepadAxis(int gamepadIndex, int axisIndex) => Float2.Zero;
    public float GetGamepadTrigger(int gamepadIndex, int triggerIndex) => 0f;
    public void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor) { }
}
