using Silk.NET.Input;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

public static class Input
{
    public static IInputContext Context { get; internal set; }

    public static IReadOnlyList<IKeyboard> Keyboards => Context.Keyboards;
    public static IReadOnlyList<IMouse> Mice => Context.Mice;
    public static IReadOnlyList<IJoystick> Joysticks => Context.Joysticks;


    private static Vector2 _currentMousePos;
    private static Vector2 _prevMousePos;

    public static Vector2 PrevMousePosition => _prevMousePos;
    public static Vector2 MousePosition {
        get => _currentMousePos;
        set {
            _prevMousePos = value;
            _currentMousePos = value;
            Mice[0].Position = value;
        }
    }
    public static Vector2 MouseDelta => _currentMousePos - _prevMousePos;
    public static float MouseWheelDelta => Mice[0].ScrollWheels[0].Y;

    private static Dictionary<Key, bool> previousKeyStates = new Dictionary<Key, bool>();
    private static Dictionary<MouseButton, bool> previousMouseStates = new Dictionary<MouseButton, bool>();

    internal static void Initialize()
    {
        Context = Window.InternalWindow.CreateInput();
        _prevMousePos = Mice[0].Position;
        _currentMousePos = Mice[0].Position;
        UpdateKeyStates();
    }

    internal static void Dispose()
    {
        Context.Dispose();
    }

    internal static void LateUpdate()
    {
        _prevMousePos = _currentMousePos;
        _currentMousePos = Mice[0].Position;
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
        foreach (var keyboard in Keyboards)
            if (keyboard.IsKeyPressed(key))
                return true;
        return false;
    }

    public static bool GetKeyDown(Key key) => GetKey(key) && !previousKeyStates[key];

    public static bool GetKeyUp(Key key) => !GetKey(key) && previousKeyStates[key];

    public static bool GetMouseButton(int button)
    {
        foreach (var mouse in Mice)
            if (mouse.IsButtonPressed((MouseButton)button))
                return true;
        return false;
    }

    public static bool GetMouseButtonDown(int button) => GetMouseButton(button) && !previousMouseStates[(MouseButton)button];

    public static bool GetMouseButtonUp(int button) => !GetMouseButton(button) && previousMouseStates[(MouseButton)button];
}