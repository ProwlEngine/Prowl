using Prowl.Runtime;
using Silk.NET.Input;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

public class Input
{
    public static Stack<IInputHandler> Handlers { get; private set; } = [];
    public static IInputHandler Current => Handlers.Peek();

    public static void PushHandler(IInputHandler handler) => Handlers.Push(handler);
    public static void PopHandler() => Handlers.Pop();

    public static string Clipboard
    {
        get => Current.Clipboard;
        set => Current.Clipboard = value;
    }

    public static char? LastPressedChar
    {
        get => Current.LastPressedChar;
        set => Current.LastPressedChar = value;
    }

    public static event Action<Key, bool> OnKeyEvent
    {
        add => Current.OnKeyEvent += value;
        remove => Current.OnKeyEvent -= value;
    }

    public static Vector2Int PrevMousePosition => Current.PrevMousePosition;
    public static Vector2Int MousePosition
    {
        get => Current.MousePosition;
        set => Current.MousePosition = value;
    }
    public static Vector2 MouseDelta => Current.MouseDelta;
    public static float MouseWheelDelta => Current.MouseWheelDelta;

    public static event Action<MouseButton, double, double, bool, bool> OnMouseEvent
    {
        add => Current.OnMouseEvent += value;
        remove => Current.OnMouseEvent -= value;
    }

    public static bool GetKey(Key key) => Current.GetKey(key);
    public static bool GetKeyDown(Key key) => Current.GetKeyDown(key);
    public static bool GetKeyUp(Key key) => Current.GetKeyUp(key);
    public static bool GetMouseButton(int button) => Current.GetMouseButton(button);
    public static bool GetMouseButtonDown(int button) => Current.GetMouseButtonDown(button);
    public static bool GetMouseButtonUp(int button) => Current.GetMouseButtonUp(button);
    public static void SetCursorVisible(bool visible, int miceIndex = 0) => Current.SetCursorVisible(visible, miceIndex);
}
