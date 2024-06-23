using Veldrid.Sdl2;
using System;
using System.Collections.Generic;
using Veldrid;
using System.Text;

namespace Prowl.Runtime;

public static class Input
{   
    public static readonly Key[] KeyValues = Enum.GetValues<Key>();
    public static readonly MouseButton[] MouseValues = Enum.GetValues<MouseButton>();

    public static bool Enabled { get; set; } = true;
    
    public static InputSnapshot InputSnapshot => Screen.LatestInputSnapshot;

    public static IReadOnlyList<KeyEvent> KeyEvents => InputSnapshot.KeyEvents;
    public static IReadOnlyList<MouseEvent> MouseEvents => InputSnapshot.MouseEvents;

    private enum InputState
    {
        Pressed,
        Released,
        Unset
    }


    private static Dictionary<Key, InputState> keyState = new();
    private static Dictionary<Key, InputState> newKeyState = new();

    private static Dictionary<MouseButton, InputState> buttonState = new();
    private static Dictionary<MouseButton, InputState> newButtonState = new();

    private static Vector2Int _currentMousePos;
    private static Vector2Int _prevMousePos;


    private static bool _receivedDeltaEvent = false;
    private static float _mouseWheelDelta;
    public static float MouseWheelDelta => Enabled ? _mouseWheelDelta : 0f;

    public static bool CursorHidden;
    public static bool CursorLocked;

    public static bool Locked { get; private set; }
    public static bool Hidden => !Screen.InternalWindow.CursorVisible;

    public static IReadOnlyList<char> InputString => InputSnapshot.KeyCharPresses;


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

                if (!Locked)
                    Screen.InternalWindow.SetMousePosition(new Vector2(value.x, value.y));
            }
        }
    }

    public static Vector2 MouseDelta => Enabled ? MousePosition - PrevMousePosition : Vector2.zero;
    public static event Action<Key, bool> OnKeyEvent;
    public static Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    public static bool IsAnyKeyDown => Enabled && newKeyState.Count > 0;



    internal static void Initialize()
    {
        Screen.InternalWindow.MouseWheel += (mouseWheelEvent) => { 
            _receivedDeltaEvent = true;
            _mouseWheelDelta = mouseWheelEvent.WheelDelta; 
        };

        _prevMousePos = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);
        _currentMousePos = _prevMousePos;

        foreach (Key key in KeyValues)
        {
            keyState[key] = InputState.Released;
            newKeyState[key] = InputState.Unset;
        }

        foreach (MouseButton button in MouseValues)
        {
            buttonState[button] = InputState.Released;
            newButtonState[button] = InputState.Unset;
        }

        UpdateKeyStates();
    }

    internal static void EarlyUpdate()
    {
        if (!_receivedDeltaEvent)
            _mouseWheelDelta = 0.0f;

        if (_receivedDeltaEvent)
            _receivedDeltaEvent = false;

        UpdateCursorState();
        UpdateKeyStates();

        if (_prevMousePos != _currentMousePos)
        {
            OnMouseEvent?.Invoke(MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
        }
    }

    // Update cursor locking and position
    private static void UpdateCursorState()
    {
        Vector2Int mousePosition = new Vector2Int((int)InputSnapshot.MousePosition.X, (int)InputSnapshot.MousePosition.Y);

        if ((GetKey(Key.Escape, false) || !Screen.InternalWindow.Focused || !Screen.ScreenRect.Contains(mousePosition)) && (Locked || !Screen.InternalWindow.CursorVisible))
        {
            Screen.InternalWindow.CursorVisible = true;
            Locked = false;

            Screen.InternalWindow.SetMousePosition(new Vector2(_currentMousePos.x, _currentMousePos.y));
        } 
        else if (GetMouseButton(MouseButton.Left, false))
        {
            Screen.InternalWindow.CursorVisible = !CursorHidden;
            Locked = CursorLocked;
        }

        if (!Locked)
        {
            _prevMousePos = _currentMousePos;
            _currentMousePos = mousePosition;

            return;
        }
        
        Vector2Int center = Screen.Position + (Screen.Size / new Vector2Int(2, 2));
        Vector2Int centerDelta = mousePosition - center;

        Screen.InternalWindow.SetMousePosition(new Vector2(center.x, center.y));

        _prevMousePos = _currentMousePos;
        _currentMousePos += centerDelta;
    }

    // Update the state of each key
    private static void UpdateKeyStates()
    {
        foreach (var pair in newKeyState)
            newKeyState[pair.Key] = InputState.Unset;

        foreach (var pair in newButtonState)
            newButtonState[pair.Key] = InputState.Unset;

        foreach (var keyEvent in InputSnapshot.KeyEvents)
        {
            Key key = (Key)keyEvent.Key;
            InputState state = keyEvent.Down ? InputState.Pressed : InputState.Released;

            if (keyState[key] == state)
                continue;

            newKeyState[key] = state;
            keyState[key] = state;

            OnKeyEvent?.Invoke(key, keyEvent.Down);
        }

        foreach (var mouseEvent in InputSnapshot.MouseEvents)
        {
            MouseButton button = (MouseButton)mouseEvent.MouseButton;
            InputState state = mouseEvent.Down ? InputState.Pressed : InputState.Released;

            if (buttonState[button] == state)
                continue;

            newButtonState[button] = state;
            buttonState[button] = state;

            OnMouseEvent?.Invoke(button, MousePosition.x, MousePosition.y, mouseEvent.Down, false);
        }
    }

    public static bool GetKey(Key key, bool respectEnabled = true) =>           
        (!respectEnabled || Enabled) && keyState[key] == InputState.Pressed;

    public static bool GetKeyDown(Key key, bool respectEnabled = true) =>       
        (!respectEnabled || Enabled) && newKeyState[key] == InputState.Pressed;

    public static bool GetKeyUp(Key key, bool respectEnabled = true) =>         
        (!respectEnabled || Enabled) && newKeyState[key] == InputState.Released;


    public static bool GetMouseButton(MouseButton button, bool respectEnabled = true) =>        
        (!respectEnabled || Enabled) && buttonState[button] == InputState.Pressed;

    public static bool GetMouseButtonDown(MouseButton button, bool respectEnabled = true) =>    
        (!respectEnabled || Enabled) && newButtonState[button] == InputState.Released;

    public static bool GetMouseButtonUp(MouseButton button, bool respectEnabled = true) =>      
        (!respectEnabled || Enabled) && newButtonState[button] == InputState.Released;
        

    public static bool GetMouseButton(int button, bool respectEnabled = true) =>        
        GetMouseButton((MouseButton)button, respectEnabled);

    public static bool GetMouseButtonDown(int button, bool respectEnabled = true) =>    
        GetMouseButtonDown((MouseButton)button, respectEnabled);

    public static bool GetMouseButtonUp(int button, bool respectEnabled = true) =>      
        GetMouseButtonUp((MouseButton)button, respectEnabled);
}