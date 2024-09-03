// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public interface IInputHandler
    {
        string Clipboard { get; set; }
        bool IsAnyKeyDown { get; }

        IReadOnlyList<char> InputString { get; set; }

        Vector2 MouseDelta { get; }
        Vector2Int MousePosition { get; set; }
        float MouseWheelDelta { get; }
        Vector2Int PrevMousePosition { get; }

        bool CursorVisible { get; set; }
        bool CursorLocked { get; set; }

        event Action<Key, bool> OnKeyEvent;
        event Action<MouseButton, double, double, bool, bool> OnMouseEvent;

        bool GetKey(Key key);
        bool GetKeyDown(Key key);
        bool GetKeyUp(Key key);
        bool GetMouseButton(MouseButton button);
        bool GetMouseButtonDown(MouseButton button);
        bool GetMouseButtonUp(MouseButton button);
    }
}
