// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.Input;

namespace Prowl.Runtime;

public interface IInputHandler
{
    string Clipboard { get; set; }
    bool IsAnyKeyDown { get; }
    Double2 MouseDelta { get; }
    Int2 MousePosition { get; set; }
    float MouseWheelDelta { get; }
    Int2 PrevMousePosition { get; }

    event Action<Key, bool> OnKeyEvent;
    event Action<MouseButton, double, double, bool, bool> OnMouseEvent;

    char? GetPressedChar();
    bool GetKey(Key key);
    bool GetKeyDown(Key key);
    bool GetKeyUp(Key key);
    bool GetMouseButton(int button);
    bool GetMouseButtonDown(int button);
    bool GetMouseButtonUp(int button);
    void SetCursorVisible(bool visible, int miceIndex = 0);
}
