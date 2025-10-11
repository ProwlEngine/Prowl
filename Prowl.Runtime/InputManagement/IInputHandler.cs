// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

public interface IInputHandler
{
    string Clipboard { get; set; }
    bool IsAnyKeyDown { get; }
    Double2 MouseDelta { get; }
    Int2 MousePosition { get; set; }
    float MouseWheelDelta { get; }
    Int2 PrevMousePosition { get; }

    event Action<KeyCode, bool> OnKeyEvent;
    event Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    // Keyboard methods
    char? GetPressedChar();
    bool GetKey(KeyCode key);
    bool GetKeyDown(KeyCode key);
    bool GetKeyUp(KeyCode key);

    // Mouse methods
    bool GetMouseButton(int button);
    bool GetMouseButtonDown(int button);
    bool GetMouseButtonUp(int button);
    void SetCursorVisible(bool visible, int miceIndex = 0);

    // Gamepad methods
    int GetGamepadCount();
    bool IsGamepadConnected(int gamepadIndex);
    bool GetGamepadButton(int gamepadIndex, GamepadButton button);
    bool GetGamepadButtonDown(int gamepadIndex, GamepadButton button);
    bool GetGamepadButtonUp(int gamepadIndex, GamepadButton button);
    Float2 GetGamepadAxis(int gamepadIndex, int axisIndex);
    float GetGamepadTrigger(int gamepadIndex, int triggerIndex);
    void SetGamepadVibration(int gamepadIndex, float leftMotor, float rightMotor);
}
