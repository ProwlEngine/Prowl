using Veldrid.Sdl2;
using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime;

public static class Input
{
    public static bool Enabled { get; set; } = true;
    
    public static InputSnapshot InputSnapshot { get; set; }

    public static IReadOnlyList<KeyEvent> KeyEvents => InputSnapshot.KeyEvents;
    public static IReadOnlyList<MouseEvent> MouseEvents => InputSnapshot.MouseEvents;

    public static string Clipboard {
        get => Sdl2Native.SDL_GetClipboardText();
        set {
           Sdl2Native.SDL_SetClipboardText(value);
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
                Screen.InternalWindow.SetMousePosition(new Vector2(value.x, value.y));
            }
        }
    }

    public static Vector2 MouseDelta => Enabled ? (_currentMousePos - _prevMousePos) : Vector2.zero;

    private static float _mouseWheelDelta;
    public static float MouseWheelDelta => Enabled ? _mouseWheelDelta : 0f;

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
        InputSnapshot = Screen.LatestInputSnapshot;

        Screen.InternalWindow.MouseWheel += (mouseWheelEvent) => { _mouseWheelDelta = mouseWheelEvent.WheelDelta; };

        _prevMousePos = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);
        _currentMousePos = _prevMousePos;

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
            wasMousePressed[button] = false;
            isMousePressed[button] = false;
        }

        UpdateKeyStates();
    }

    internal static void LateUpdate()
    {
        InputSnapshot = Screen.LatestInputSnapshot;

        _prevMousePos = _currentMousePos;
        _currentMousePos = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);

        if (_prevMousePos != _currentMousePos)
        {
            if (isMousePressed[MouseButton.Left])
                OnMouseEvent?.Invoke(MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
            else if (isMousePressed[MouseButton.Right])
                OnMouseEvent?.Invoke(MouseButton.Right, MousePosition.x, MousePosition.y, false, true);
            else if (isMousePressed[MouseButton.Middle])
                OnMouseEvent?.Invoke(MouseButton.Middle, MousePosition.x, MousePosition.y, false, true);
            #warning Veldrid change
            //else
            //    OnMouseEvent?.Invoke(MouseButton.Unknown, MousePosition.x, MousePosition.y, false, true);
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

                foreach (var keyEvent in KeyEvents)
                    if (keyEvent.Down && keyEvent.Key == key)
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
            //if (button != MouseButton.Unknown)
            //{
                wasMousePressed[button] = isMousePressed[button];
                isMousePressed[button] = false;

                foreach (var mouseEvent in MouseEvents)
                    if (mouseEvent.Down && mouseEvent.MouseButton == button)
                    {
                        isMousePressed[button] = true;
                        break;
                    }

                if (wasMousePressed[button] != isMousePressed[button])
                    OnMouseEvent?.Invoke(button, MousePosition.x, MousePosition.y, isMousePressed[button], false);
            //}
        }
    }

    public static bool GetKey(Key key) => Enabled && isKeyPressed[key];

    public static bool GetKeyDown(Key key) => Enabled && isKeyPressed[key] && !wasKeyPressed[key];

    public static bool GetKeyUp(Key key) => Enabled && !isKeyPressed[key] && wasKeyPressed[key];

    public static bool GetMouseButton(int button) => Enabled && isMousePressed[(MouseButton)button];

    public static bool GetMouseButtonDown(int button) => Enabled && isMousePressed[(MouseButton)button] && !wasMousePressed[(MouseButton)button];

    public static bool GetMouseButtonUp(int button) => Enabled && isMousePressed[(MouseButton)button] && wasMousePressed[(MouseButton)button];
    
    public static void SetCursorVisible(bool visible) => Screen.InternalWindow.CursorVisible = visible;
}