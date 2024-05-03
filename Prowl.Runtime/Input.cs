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

    public static string Clipboard {
        get => Context.Keyboards[0].ClipboardText;
        set {
           Context.Keyboards[0].ClipboardText = value;
        }
    }


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

    private static Dictionary<Key, bool> wasKeyPressed = new Dictionary<Key, bool>();
    private static Dictionary<Key, bool> isKeyPressed = new Dictionary<Key, bool>();
    private static Dictionary<MouseButton, bool> wasMousePressed = new Dictionary<MouseButton, bool>();
    private static Dictionary<MouseButton, bool> isMousePressed = new Dictionary<MouseButton, bool>();

    public static char? LastPressedChar;

    public static event Action<Key, bool> OnKeyEvent;
    public static Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    public static bool IsAnyKeyDown => Enabled && isKeyPressed.ContainsValue(true);

    internal static void Initialize()
    {
        Context = Window.InternalWindow.CreateInput();
        _prevMousePos = (Vector2Int)Mice[0].Position.ToDouble();
        _currentMousePos = (Vector2Int)Mice[0].Position.ToDouble();

        // initialize key states
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            if (key != Key.Unknown)
            {
                wasKeyPressed[key] = false;
                isKeyPressed[key] = false;
            }
        }

        foreach (MouseButton button in Enum.GetValues(typeof(MouseButton)))
        {
            if (button != MouseButton.Unknown)
            {
                wasMousePressed[button] = false;
                isMousePressed[button] = false;
            }
        }

        foreach (var keyboard in Keyboards)
            keyboard.KeyChar += (keyboard, c) => LastPressedChar = c;

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
        if (_prevMousePos != _currentMousePos)
        {
            if (isMousePressed[MouseButton.Left])
                OnMouseEvent?.Invoke(MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
            else if (isMousePressed[MouseButton.Right])
                OnMouseEvent?.Invoke(MouseButton.Right, MousePosition.x, MousePosition.y, false, true);
            else if (isMousePressed[MouseButton.Middle])
                OnMouseEvent?.Invoke(MouseButton.Middle, MousePosition.x, MousePosition.y, false, true);
            else
                OnMouseEvent?.Invoke(MouseButton.Unknown, MousePosition.x, MousePosition.y, false, true);
        }
        UpdateKeyStates();
    }

    // Update the state of each key
    private static void UpdateKeyStates()
    {
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            if (key != Key.Unknown)
            {
                wasKeyPressed[key] = isKeyPressed[key];
                isKeyPressed[key] = false;
                foreach (var keyboard in Keyboards)
                    if (keyboard.IsKeyPressed(key))
                    {
                        isKeyPressed[key] = true;
                        break;
                    }

                if (wasKeyPressed[key] != isKeyPressed[key])
                    OnKeyEvent?.Invoke(key, isKeyPressed[key]);
            }
        }

        foreach (MouseButton button in Enum.GetValues(typeof(MouseButton)))
        {
            if (button != MouseButton.Unknown)
            {
                wasMousePressed[button] = isMousePressed[button];
                isMousePressed[button] = false;
                foreach (var mouse in Mice)
                    if (mouse.IsButtonPressed((MouseButton)button))
                    {
                        isMousePressed[button] = true;
                        break;
                    }
                if (wasMousePressed[button] != isMousePressed[button])
                    OnMouseEvent?.Invoke(button, MousePosition.x, MousePosition.y, isMousePressed[button], false);
            }
        }
    }

    public static bool GetKey(Key key) => Enabled && isKeyPressed[key];

    public static bool GetKeyDown(Key key) => Enabled && isKeyPressed[key] && !wasKeyPressed[key];

    public static bool GetKeyUp(Key key) => Enabled && !isKeyPressed[key] && wasKeyPressed[key];

    public static bool GetMouseButton(int button) => Enabled && isMousePressed[(MouseButton)button];

    public static bool GetMouseButtonDown(int button) => Enabled && isMousePressed[(MouseButton)button] && !wasMousePressed[(MouseButton)button];

    public static bool GetMouseButtonUp(int button) => Enabled && isMousePressed[(MouseButton)button] && wasMousePressed[(MouseButton)button];
}