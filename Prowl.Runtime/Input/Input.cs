using Veldrid.Sdl2;
using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime;

public static class Input
{
    public static bool Enabled { get; set; } = true;
    
    public static InputSnapshot InputSnapshot => Screen.LatestInputSnapshot;

    public static IReadOnlyList<KeyEvent> KeyEvents => InputSnapshot.KeyEvents;
    public static IReadOnlyList<MouseEvent> MouseEvents => InputSnapshot.MouseEvents;


    private static Dictionary<Key, bool> previousKeyState = new();
    private static Dictionary<Key, bool> newKeyState = new();

    private static Dictionary<MouseButton, bool> previousButtonState = new();
    private static Dictionary<MouseButton, bool> newButtonState = new();

    private static Vector2Int _currentMousePos;
    private static Vector2Int _prevMousePos;

    private static float _mouseWheelDelta;
    public static float MouseWheelDelta => Enabled ? _mouseWheelDelta : 0f;

    public static bool CursorHidden;
    public static bool CursorLocked;

    public static bool ActualLockState { get; private set; }
    public static bool ActualHideState => !Screen.InternalWindow.CursorVisible;

    public static char? LastPressedChar = null;


    public static string Clipboard 
    {
        get => Sdl2Native.SDL_GetClipboardText();
        set => Sdl2Native.SDL_SetClipboardText(value);
    }

    public static Vector2Int PrevMousePosition => Enabled ? _prevMousePos : Vector2Int.zero;
    public static Vector2Int MousePosition 
    {
        get => Enabled ? _currentMousePos : Vector2Int.zero;
        set 
        {
            if (Enabled)
            {
                _prevMousePos = value;
                _currentMousePos = value;

                if (!ActualLockState)
                    Screen.InternalWindow.SetMousePosition(new Vector2(value.x, value.y));
            }
        }
    }

    public static Vector2 MouseDelta => Enabled ? Screen.InternalWindow.MouseDelta : Vector2.zero;
    public static event Action<Key, bool> OnKeyEvent;
    public static Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    public static bool IsAnyKeyDown => Enabled && newKeyState.Count > 0;



    internal static void Initialize()
    {
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
        UpdateCursorState();
        UpdateKeyStates();

        if (_prevMousePos != _currentMousePos)
        {
            if (GetMouseButton(MouseButton.Left))
                OnMouseEvent?.Invoke(MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
            else if (GetMouseButton(MouseButton.Right))
                OnMouseEvent?.Invoke(MouseButton.Right, MousePosition.x, MousePosition.y, false, true);
            else if (GetMouseButton(MouseButton.Middle))
                OnMouseEvent?.Invoke(MouseButton.Middle, MousePosition.x, MousePosition.y, false, true);
        }
    }

    // Update cursor locking and position
    private static void UpdateCursorState()
    {
        Vector2Int mousePosition = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);

        if (GetKey(Key.Escape) || !Screen.InternalWindow.Focused || !Screen.ScreenRect.Contains(mousePosition))
        {
            if (!ActualLockState || !Screen.InternalWindow.CursorVisible)
            {
                Screen.InternalWindow.CursorVisible = true;
                ActualLockState = false;

                Screen.InternalWindow.SetMousePosition(new Vector2(_currentMousePos.x, _currentMousePos.y));
            }
        } 
        else if (GetMouseButton(0))
        {
            Screen.InternalWindow.CursorVisible = !CursorHidden;
            ActualLockState = CursorLocked;
        }

        if (!ActualLockState)
        {
            _prevMousePos = _currentMousePos;
            _currentMousePos = mousePosition;
        }
        else
        {
            Vector2Int center = Screen.Position + (Screen.Size / new Vector2Int(2, 2));
            Vector2Int centerDelta = mousePosition - center;

            Screen.InternalWindow.SetMousePosition(new Vector2(center.x, center.y));

            _prevMousePos = _currentMousePos;
            _currentMousePos += centerDelta;
        }
    }

    // Update the state of each key
    private static void UpdateKeyStates()
    {
        foreach (var keyEvent in InputSnapshot.KeyEvents)
        {
            Key key = (Key)keyEvent.Key;

            bool stateDiffers = keyEvent.Down != previousKeyState[key];

            if (newKeyState[key])
                newKeyState[key] = false;

            if (stateDiffers)
            {
                previousKeyState[key] = newKeyState[key];
                newKeyState[key] = keyEvent.Down;
            }
        }

        foreach (var mouseEvent in InputSnapshot.MouseEvents)
        {
            MouseButton button = (MouseButton)mouseEvent.MouseButton;

            bool stateDiffers = mouseEvent.Down != previousButtonState[button];

            if (newButtonState[button])
                newButtonState[button] = false;

            if (stateDiffers)
            {
                previousButtonState[button] = newButtonState[button];
                newButtonState[button] = mouseEvent.Down;
            }
        }
    }


    public static bool GetKey(Key key) => Enabled && (previousKeyState[key] || newKeyState[key]);
    public static bool GetKeyDown(Key key) => Enabled && newKeyState[key] && !previousKeyState[key];
    public static bool GetKeyUp(Key key) => Enabled && !newKeyState[key] && previousKeyState[key];

    public static bool GetMouseButton(MouseButton button) => Enabled && (newButtonState[button] || previousButtonState[button]);
    public static bool GetMouseButtonDown(MouseButton button) => Enabled && newButtonState[button] && !previousButtonState[button];
    public static bool GetMouseButtonUp(MouseButton button) => Enabled && !newButtonState[button] && previousButtonState[button];
        
    public static bool GetMouseButton(int button) => GetMouseButton((MouseButton)button);
    public static bool GetMouseButtonDpwn(int button) => GetMouseButtonDown((MouseButton)button);
    public static bool GetMouseButtonUp(int button) => GetMouseButtonUp((MouseButton)button);
}