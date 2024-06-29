using Veldrid.Sdl2;
using System;
using System.Collections.Generic;
using Veldrid;
using System.Text;

namespace Prowl.Runtime;

public class DefaultInputHandler : IInputHandler, IDisposable
{   
    public static readonly Key[] KeyValues = Enum.GetValues<Key>();
    public static readonly MouseButton[] MouseValues = Enum.GetValues<MouseButton>();

    private enum InputState
    {
        Pressed,
        Released,
        Unset
    }

    private Dictionary<Key, InputState> keyState = new();
    private Dictionary<Key, InputState> newKeyState = new();

    private Dictionary<MouseButton, InputState> buttonState = new();
    private Dictionary<MouseButton, InputState> newButtonState = new();

    private Vector2Int _currentMousePos;
    private Vector2Int _prevMousePos;


    private bool _receivedDeltaEvent = false;
    private float _mouseWheelDelta;
    public float MouseWheelDelta => _mouseWheelDelta;

    public bool CursorVisible { get; set; }
    public bool CursorLocked { get; set; }

    public bool Locked { get; private set; }
    public bool Hidden => !Screen.InternalWindow.CursorVisible;

    public IReadOnlyList<char> InputString { get; set; }


    public string Clipboard 
    {
        get => Sdl2Native.SDL_GetClipboardText();
        set => Sdl2Native.SDL_SetClipboardText(value);
    }

    public Vector2Int PrevMousePosition => _prevMousePos;
    public Vector2Int MousePosition 
    {
        get => _currentMousePos;
        set 
        {
            _prevMousePos = value;
            _currentMousePos = value;

            if (!Locked)
                Screen.InternalWindow.SetMousePosition(new Vector2(value.x, value.y));
        }
    }

    public Vector2 MouseDelta => MousePosition - PrevMousePosition;
    public event Action<Key, bool> OnKeyEvent;
    public event Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    public bool IsAnyKeyDown => newKeyState.Count > 0;


    public DefaultInputHandler()
    {   
        var snapshot = Screen.LatestInputSnapshot;

        InputString = snapshot.KeyCharPresses;

        Screen.InternalWindow.MouseWheel += (mouseWheelEvent) => { 
            _receivedDeltaEvent = true;
            _mouseWheelDelta = mouseWheelEvent.WheelDelta; 
        };

        _prevMousePos = new Vector2Int((int)snapshot.MousePosition.X, (int)snapshot.MousePosition.Y);
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

        UpdateKeyStates(snapshot);
    }

    internal void EarlyUpdate()
    {
        var snapshot = Screen.LatestInputSnapshot;

        InputString = snapshot.KeyCharPresses;

        if (!_receivedDeltaEvent)
            _mouseWheelDelta = 0.0f;

        if (_receivedDeltaEvent)
            _receivedDeltaEvent = false;

        UpdateCursorState(snapshot);
        UpdateKeyStates(snapshot);

        if (_prevMousePos != _currentMousePos)
        {
            OnMouseEvent?.Invoke(MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
        }
    }

    // Update cursor locking and position
    private void UpdateCursorState(InputSnapshot snapshot)
    {
        Vector2Int mousePosition = new Vector2Int((int)snapshot.MousePosition.X, (int)snapshot.MousePosition.Y);

        if ((GetKey(Key.Escape) || !Screen.InternalWindow.Focused || !Screen.ScreenRect.Contains(mousePosition)) && (Locked || !Screen.InternalWindow.CursorVisible))
        {
            Screen.InternalWindow.CursorVisible = true;
            Locked = false;

            Screen.InternalWindow.SetMousePosition(new Vector2(_currentMousePos.x, _currentMousePos.y));
        } 
        else if (GetMouseButton(0))
        {
            Screen.InternalWindow.CursorVisible = CursorVisible;
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
    private void UpdateKeyStates(InputSnapshot snapshot)
    {
        foreach (var pair in newKeyState)
            newKeyState[pair.Key] = InputState.Unset;

        foreach (var pair in newButtonState)
            newButtonState[pair.Key] = InputState.Unset;

        foreach (var keyEvent in snapshot.KeyEvents)
        {
            Key key = (Key)keyEvent.Key;
            InputState state = keyEvent.Down ? InputState.Pressed : InputState.Released;

            if (keyState[key] == state)
                continue;

            newKeyState[key] = state;
            keyState[key] = state;

            OnKeyEvent?.Invoke(key, keyEvent.Down);
        }

        foreach (var mouseEvent in snapshot.MouseEvents)
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

    
    public bool GetKey(Key key) => keyState[key] == InputState.Pressed;
    public bool GetKeyDown(Key key) => newKeyState[key] == InputState.Pressed;
    public bool GetKeyUp(Key key) => newKeyState[key] == InputState.Released;

    public bool GetMouseButton(int button) => buttonState[(MouseButton)button] == InputState.Pressed;
    public bool GetMouseButtonDown(int button) => newButtonState[(MouseButton)button] == InputState.Released;
    public bool GetMouseButtonUp(int button) => newButtonState[(MouseButton)button] == InputState.Released;

    public void Dispose()
    {

    }
}