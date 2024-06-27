using Prowl.Editor;
using Silk.NET.Input;

namespace Prowl.Runtime;

// Same Implementation as Runtimes DefaultInputHandler, Except for adjusting mouse values to fit inside the GameView
// TODO: Make DefaultInputHandler use virtual so we can override it here instead of copying the whole class

public class GameViewInputHandler : IInputHandler, IDisposable
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
    private GameWindow _window;

    public Vector2Int PrevMousePosition => _prevMousePos;
    public Vector2Int MousePosition
    {
        get => _currentMousePos;
        set
        {
            _prevMousePos = value;
            _currentMousePos = value;
            Mice[0].Position = (Vector2)value + new Vector2((float)GameWindow.FocusedPosition.x, (float)GameWindow.FocusedPosition.y);
        }
    }
    public Vector2 MouseDelta => _currentMousePos - _prevMousePos;
    public float MouseWheelDelta => Mice[0].ScrollWheels[0].Y;

    private Dictionary<Key, bool> wasKeyPressed = new Dictionary<Key, bool>();
    private Dictionary<Key, bool> isKeyPressed = new Dictionary<Key, bool>();
    private Dictionary<MouseButton, bool> wasMousePressed = new Dictionary<MouseButton, bool>();
    private Dictionary<MouseButton, bool> isMousePressed = new Dictionary<MouseButton, bool>();

    public char? LastPressedChar { get; set; }

    public event Action<Key, bool> OnKeyEvent;
    public event Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    public bool IsAnyKeyDown => isKeyPressed.ContainsValue(true);

    public GameViewInputHandler(IInputContext context, GameWindow window)
    {
        Context = context;
        _window = window;
        _prevMousePos = (Vector2Int)InternalMousePosition();
        _currentMousePos = (Vector2Int)InternalMousePosition();

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

    private Vector2 InternalMousePosition()
    {
        return Mice[0].Position - new System.Numerics.Vector2((float)GameWindow.FocusedPosition.x, (float)GameWindow.FocusedPosition.y);
    }

    private bool InternalKeyPressed(Key key, IKeyboard board)
    {
        // Only allow key presses if the window is focused
        if(GameWindow.LastFocused.Target != _window) return false;

        return board.IsKeyPressed(key);
    }

    private bool InternalMousePressed(MouseButton button, IMouse mouse)
    {
        // Only allow key presses if the window is focused
        if (GameWindow.LastFocused.Target != _window) return false;

        return mouse.IsButtonPressed(button);
    }

    internal void LateUpdate()
    {
        _prevMousePos = _currentMousePos;
        _currentMousePos = (Vector2Int)InternalMousePosition();
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
    private void UpdateKeyStates()
    {
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            if (key != Key.Unknown)
            {
                wasKeyPressed[key] = isKeyPressed[key];
                isKeyPressed[key] = false;
                foreach (var keyboard in Keyboards)
                    if (InternalKeyPressed(key, keyboard))
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
                    if (InternalMousePressed(button, mouse))
                    {
                        isMousePressed[button] = true;
                        break;
                    }
                if (wasMousePressed[button] != isMousePressed[button])
                    OnMouseEvent?.Invoke(button, MousePosition.x, MousePosition.y, isMousePressed[button], false);
            }
        }
    }

    public bool GetKey(Key key) => isKeyPressed[key];

    public bool GetKeyDown(Key key) => isKeyPressed[key] && !wasKeyPressed[key];

    public bool GetKeyUp(Key key) => !isKeyPressed[key] && wasKeyPressed[key];

    public bool GetMouseButton(int button) => isMousePressed[(MouseButton)button];

    public bool GetMouseButtonDown(int button) => isMousePressed[(MouseButton)button] && !wasMousePressed[(MouseButton)button];

    public bool GetMouseButtonUp(int button) => isMousePressed[(MouseButton)button] && wasMousePressed[(MouseButton)button];

    public void SetCursorVisible(bool visible, int miceIndex = 0) => Mice[miceIndex].Cursor.CursorMode = visible ? CursorMode.Normal : CursorMode.Hidden;

    public void Dispose() => Context.Dispose();
}