using Silk.NET.Input;
using System;

namespace Prowl.Runtime
{
    public interface IInputHandler
    {
        string Clipboard { get; set; }
        bool IsAnyKeyDown { get; }
        char? LastPressedChar { get; set; }
        Vector2 MouseDelta { get; }
        Vector2Int MousePosition { get; set; }
        float MouseWheelDelta { get; }
        Vector2Int PrevMousePosition { get; }

        event Action<Key, bool> OnKeyEvent;
        event Action<MouseButton, double, double, bool, bool> OnMouseEvent;

        bool GetKey(Key key);
        bool GetKeyDown(Key key);
        bool GetKeyUp(Key key);
        bool GetMouseButton(int button);
        bool GetMouseButtonDown(int button);
        bool GetMouseButtonUp(int button);
        void SetCursorVisible(bool visible, int miceIndex = 0);
    }
}