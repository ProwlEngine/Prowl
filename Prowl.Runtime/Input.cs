using Silk.NET.Input;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

public static class Input
{
    public static bool Enabled { get; set; } = true;


    public static IInputContext Context { get; internal set; }

    public static IReadOnlyList<IKeyboard> Keyboards => Context.Keyboards;
    public static IReadOnlyList<IMouse> Mice => Context.Mice;
    public static IReadOnlyList<IJoystick> Joysticks => Context.Joysticks;


    private static Vector2Int _currentMousePos;
    private static Vector2Int _prevMousePos;

    public static Vector2Int PrevMousePosition => Enabled ? _prevMousePos : Vector2Int.zero;
    public static Vector2Int MousePosition {
        get => Enabled ? _currentMousePos : Vector2Int.zero;
        set {
            if (Enabled)
            {
                _prevMousePos = value;
                _currentMousePos = value;
                Mice[0].Position = (Vector2)value;
            }
        }
    }
    public static Vector2 MouseDelta => Enabled ? (_currentMousePos - _prevMousePos) : Vector2.zero;
    public static float MouseWheelDelta => Enabled ? Mice[0].ScrollWheels[0].Y : 0f;

    private static Dictionary<Key, bool> previousKeyStates = new Dictionary<Key, bool>();
    private static Dictionary<MouseButton, bool> previousMouseStates = new Dictionary<MouseButton, bool>();

    internal static void Initialize()
    {
        Context = Window.InternalWindow.CreateInput();
        _prevMousePos = (Vector2Int)Mice[0].Position.ToDouble();
        _currentMousePos = (Vector2Int)Mice[0].Position.ToDouble();
        UpdateKeyStates();
    }

    internal static void Dispose()
    {
        Context.Dispose();
    }

    internal static void LateUpdate()
    {
        _prevMousePos = _currentMousePos;
        _currentMousePos = (Vector2Int)Mice[0].Position.ToDouble();
        UpdateKeyStates();
    }

    // Update the state of each key
    private static void UpdateKeyStates()
    {
        foreach (Key key in Enum.GetValues(typeof(Key)))
            if(key != Key.Unknown)
                previousKeyStates[key] = GetKey(key);

        foreach (MouseButton button in Enum.GetValues(typeof(MouseButton)))
            if (button != MouseButton.Unknown)
                previousMouseStates[button] = GetMouseButton((int)button);
    }

    public static bool GetKey(Key key)
    {
        if (!Enabled)
            return false;

        foreach (var keyboard in Keyboards)
            if (keyboard.IsKeyPressed(key))
                return true;
        return false;
    }

    public static bool GetKeyDown(Key key) => Enabled && GetKey(key) && !previousKeyStates[key];

    public static bool GetKeyUp(Key key) => Enabled && !GetKey(key) && previousKeyStates[key];

    public static bool GetMouseButton(int button)
    {
        if (!Enabled)
            return false;

        foreach (var mouse in Mice)
            if (mouse.IsButtonPressed((MouseButton)button))
                return true;
        return false;
    }

    public static bool GetMouseButtonDown(int button) => Enabled && GetMouseButton(button) && !previousMouseStates[(MouseButton)button];

    public static bool GetMouseButtonUp(int button) => Enabled && !GetMouseButton(button) && previousMouseStates[(MouseButton)button];
}