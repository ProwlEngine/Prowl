using Silk.NET.Input;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

public static class Input
{
    public static IInputContext Context { get; internal set; }

    public static IReadOnlyList<IKeyboard> Keyboards => Context.Keyboards;
    public static IReadOnlyList<IMouse> Mice => Context.Mice;
    public static IReadOnlyList<IJoystick> Joysticks => Context.Joysticks;

    public static Vector2D<float> PreviousMousePosition { get; private set; }
    public static Vector2D<float> MouseDelta => MousePosition - PreviousMousePosition;
    public static Vector2D<float> MousePosition {
        get {
            return Mice[0].Position.ToGeneric();
        }
        set {
            Mice[0].Position = value.ToSystem();
        }
    }

    private static Dictionary<Key, bool> previousKeyStates = new Dictionary<Key, bool>();
    private static Dictionary<MouseButton, bool> previousMouseStates = new Dictionary<MouseButton, bool>();

    internal static void Initialize()
    {
        Context = Window.InternalWindow.CreateInput();
        PreviousMousePosition = MousePosition;
        UpdateKeyStates();
    }

    internal static void Dispose()
    {
        Context.Dispose();
    }

    internal static void LateUpdate()
    {
        PreviousMousePosition = MousePosition;
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