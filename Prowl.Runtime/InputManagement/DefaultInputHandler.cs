// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Silk.NET.Input;

namespace Prowl.Runtime;

public class DefaultInputHandler : IInputHandler, IDisposable
{
    public IInputContext Context { get; internal set; }

    public IReadOnlyList<IKeyboard> Keyboards => Context.Keyboards;
    public IReadOnlyList<IMouse> Mice => Context.Mice;
    public IReadOnlyList<IJoystick> Joysticks => Context.Joysticks;

    public string Clipboard
    {
        get => Context.Keyboards[0].ClipboardText;
        set
        {
            Context.Keyboards[0].ClipboardText = value;
        }
    }


    private Vector2Int _currentMousePos;
    private Vector2Int _prevMousePos;

    public Vector2Int PrevMousePosition => _prevMousePos;
    public Vector2Int MousePosition
    {
        get => _currentMousePos;
        set
        {
            _prevMousePos = value;
            _currentMousePos = value;
            Mice[0].Position = (Vector2)value;
        }
    }
    public Vector2 MouseDelta => _currentMousePos - _prevMousePos;
    public float MouseWheelDelta => Mice[0].ScrollWheels[0].Y;

    private Dictionary<Silk.NET.Input.Key, bool> wasKeyPressed = new Dictionary<Silk.NET.Input.Key, bool>();
    private Dictionary<Silk.NET.Input.Key, bool> isKeyPressed = new Dictionary<Silk.NET.Input.Key, bool>();
    private Dictionary<Silk.NET.Input.MouseButton, bool> wasMousePressed = new Dictionary<Silk.NET.Input.MouseButton, bool>();
    private Dictionary<Silk.NET.Input.MouseButton, bool> isMousePressed = new Dictionary<Silk.NET.Input.MouseButton, bool>();

    private Queue<char> pressedChars { get; set; } = new();

    public event Action<Silk.NET.Input.Key, bool> OnKeyEvent;
    public event Action<Silk.NET.Input.MouseButton, double, double, bool, bool> OnMouseEvent;

    public bool IsAnyKeyDown => isKeyPressed.ContainsValue(true);

    public DefaultInputHandler(IInputContext context)
    {
        Context = context;
        _prevMousePos = (Vector2Int)(Vector2)Mice[0].Position;
        _currentMousePos = (Vector2Int)(Vector2)Mice[0].Position;

        // initialize key states
        foreach (Silk.NET.Input.Key key in Enum.GetValues(typeof(Silk.NET.Input.Key)))
        {
            if (key != Silk.NET.Input.Key.Unknown)
            {
                wasKeyPressed[key] = false;
                isKeyPressed[key] = false;
            }
        }

        foreach (Silk.NET.Input.MouseButton button in Enum.GetValues(typeof(Silk.NET.Input.MouseButton)))
        {
            if (button != Silk.NET.Input.MouseButton.Unknown)
            {
                wasMousePressed[button] = false;
                isMousePressed[button] = false;
            }
        }

        foreach (var keyboard in Keyboards)
            keyboard.KeyChar += (keyboard, c) => pressedChars.Enqueue(c);

        UpdateKeyStates();
    }

    internal void LateUpdate()
    {
        _prevMousePos = _currentMousePos;
        _currentMousePos = (Vector2Int)(Vector2)Mice[0].Position;
        if (!_prevMousePos.Equals(_currentMousePos))
        {
            if (isMousePressed[Silk.NET.Input.MouseButton.Left])
                OnMouseEvent?.Invoke(Silk.NET.Input.MouseButton.Left, MousePosition.x, MousePosition.y, false, true);
            else if (isMousePressed[Silk.NET.Input.MouseButton.Right])
                OnMouseEvent?.Invoke(Silk.NET.Input.MouseButton.Right, MousePosition.x, MousePosition.y, false, true);
            else if (isMousePressed[Silk.NET.Input.MouseButton.Middle])
                OnMouseEvent?.Invoke(Silk.NET.Input.MouseButton.Middle, MousePosition.x, MousePosition.y, false, true);
            else
                OnMouseEvent?.Invoke(Silk.NET.Input.MouseButton.Unknown, MousePosition.x, MousePosition.y, false, true);
        }
        UpdateKeyStates();
    }

    // Update the state of each key
    private void UpdateKeyStates()
    {
        foreach (Silk.NET.Input.Key key in Enum.GetValues(typeof(Silk.NET.Input.Key)))
        {
            if (key != Silk.NET.Input.Key.Unknown)
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

        foreach (Silk.NET.Input.MouseButton button in Enum.GetValues(typeof(Silk.NET.Input.MouseButton)))
        {
            if (button != Silk.NET.Input.MouseButton.Unknown)
            {
                wasMousePressed[button] = isMousePressed[button];
                isMousePressed[button] = false;
                foreach (var mouse in Mice)
                    if (mouse.IsButtonPressed(button))
                    {
                        isMousePressed[button] = true;
                        break;
                    }
                if (wasMousePressed[button] != isMousePressed[button])
                    OnMouseEvent?.Invoke(button, MousePosition.x, MousePosition.y, isMousePressed[button], false);
            }
        }
    }

    public char? GetPressedChar()
    {
        if (pressedChars.TryDequeue(out char c))
            return c;
        return null;
    }

    public bool GetKey(Silk.NET.Input.Key key) => isKeyPressed[key];

    public bool GetKeyDown(Silk.NET.Input.Key key) => isKeyPressed[key] && !wasKeyPressed[key];

    public bool GetKeyUp(Silk.NET.Input.Key key) => !isKeyPressed[key] && wasKeyPressed[key];

    public bool GetMouseButton(int button) => isMousePressed[(Silk.NET.Input.MouseButton)button];

    public bool GetMouseButtonDown(int button) => isMousePressed[(Silk.NET.Input.MouseButton)button] && !wasMousePressed[(Silk.NET.Input.MouseButton)button];

    public bool GetMouseButtonUp(int button) => !isMousePressed[(Silk.NET.Input.MouseButton)button] && wasMousePressed[(Silk.NET.Input.MouseButton)button];

    public void SetCursorVisible(bool visible, int miceIndex = 0) => Mice[miceIndex].Cursor.CursorMode = visible ? CursorMode.Normal : CursorMode.Disabled;

    public void Dispose() => Context.Dispose();
}
