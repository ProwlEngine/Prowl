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

                if (!isLocked)
                    Screen.InternalWindow.SetMousePosition(new Vector2(value.x, value.y));
            }
        }
    }

    public static Vector2 MouseDelta => Enabled ? Screen.InternalWindow.MouseDelta : Vector2.zero;

    private static float _mouseWheelDelta;
    public static float MouseWheelDelta => Enabled ? _mouseWheelDelta : 0f;

    private static Dictionary<Key, bool> previousKeyState = new();
    private static Dictionary<Key, bool> newKeyState = new();

    private static Dictionary<MouseButton, bool> previousButtonState = new();
    private static Dictionary<MouseButton, bool> newButtonState = new();

    private static bool wantsHidden;
    private static bool wantsLock;

    private static bool isLocked;

    public static char? LastPressedChar = null;

    public static event Action<Key, bool> OnKeyEvent;
    public static Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    public static bool IsAnyKeyDown => Enabled && newKeyState.Count > 0;

    internal static void Initialize()
    {
        InputSnapshot = Screen.LatestInputSnapshot;

        Screen.InternalWindow.MouseWheel += (mouseWheelEvent) => { _mouseWheelDelta = mouseWheelEvent.WheelDelta; };

        _prevMousePos = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);
        _currentMousePos = _prevMousePos;

        foreach (Key key in Enum.GetValues<Key>())
        {
            previousKeyState[key] = false;
            newKeyState[key] = false;
        }

        foreach (MouseButton button in Enum.GetValues<MouseButton>())
        {
            previousButtonState[button] = false;
            newButtonState[button] = false;
        }

        UpdateKeyStates();
    }

    internal static void EarlyUpdate()
    {
        InputSnapshot = Screen.LatestInputSnapshot;

        Vector2Int mousePosition = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);

        if ((GetKey(Key.Escape) || !Screen.InternalWindow.Focused || !new Rect(Screen.Position, Screen.Size).Contains(mousePosition)) && // Hit esc or unfocused the window or the mouse is outside window
            (isLocked || !Screen.InternalWindow.CursorVisible))
        {
            Screen.InternalWindow.CursorVisible = true;
            
            isLocked = false;
            Screen.InternalWindow.SetMousePosition(new Vector2(_currentMousePos.x, _currentMousePos.y));
        } 
        else if (GetMouseButton(0))
        {
            Screen.InternalWindow.CursorVisible = !wantsHidden;
            isLocked = wantsLock;
        }

        if (!isLocked)
        {
            _prevMousePos = _currentMousePos;
            _currentMousePos = mousePosition;
        }
        else
        {
            Vector2Int size = Screen.Size;
            Vector2Int pos = Screen.Position;

            Vector2Int center = pos + (size / new Vector2Int(2, 2));
            Vector2Int centerDelta = mousePosition - center;

            Screen.InternalWindow.SetMousePosition(new Vector2(center.x, center.y));

            _prevMousePos = _currentMousePos;
            _currentMousePos += centerDelta;
        }

        UpdateKeyStates();

        if (_prevMousePos != _currentMousePos)
        {
            if (GetMouseButton((int)MouseButton.Left))
                OnMouseEvent?.Invoke(MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
            else if (GetMouseButton((int)MouseButton.Right))
                OnMouseEvent?.Invoke(MouseButton.Right, MousePosition.x, MousePosition.y, false, true);
            else if (GetMouseButton((int)MouseButton.Middle))
                OnMouseEvent?.Invoke(MouseButton.Middle, MousePosition.x, MousePosition.y, false, true);
        }
    }

    // Update the state of each key
    private static void UpdateKeyStates()
    {
        foreach (var key in InputSnapshot.KeyEvents)
        {
            bool stateDiffers = key.Down != previousKeyState[key.Key];

            if (newKeyState[key.Key])
                newKeyState[key.Key] = false;

            if (stateDiffers)
            {
                previousKeyState[key.Key] = newKeyState[key.Key];
                newKeyState[key.Key] = key.Down;
            }
        }

        foreach (var mouse in InputSnapshot.MouseEvents)
        {
            bool stateDiffers = mouse.Down != previousButtonState[mouse.MouseButton];

            if (newButtonState[mouse.MouseButton])
                newButtonState[mouse.MouseButton] = false;

            if (stateDiffers)
            {
                previousButtonState[mouse.MouseButton] = newButtonState[mouse.MouseButton];
                newButtonState[mouse.MouseButton] = mouse.Down;
            }
        }
    }


    public static bool GetKey(Key key) => Enabled && 
        (previousKeyState[key] || 
        newKeyState[key]);

    public static bool GetKeyDown(Key key) => Enabled && 
        newKeyState[key] && 
        !previousKeyState[key];

    public static bool GetKeyUp(Key key) => Enabled && 
        !newKeyState[key] && 
        previousKeyState[key];

    public static bool GetMouseButton(int button) => Enabled && 
        (newButtonState[(MouseButton)button] || 
        previousButtonState[(MouseButton)button]);

    public static bool GetMouseButtonDown(int button) => Enabled && 
        newButtonState[(MouseButton)button] && 
        !previousButtonState[(MouseButton)button];

    public static bool GetMouseButtonUp(int button) => Enabled && 
        !newButtonState[(MouseButton)button] && 
        previousButtonState[(MouseButton)button];


    public static void SetCursorVisible(bool visible) => wantsHidden = !visible;
    public static void LockCursor(bool isLocked) => wantsLock = isLocked;
}