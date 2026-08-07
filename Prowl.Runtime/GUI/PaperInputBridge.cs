// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Runtime.GUI;

/// <summary>
/// Feeds engine <see cref="Input"/> state into a <see cref="Paper"/> instance. Call once per frame
/// per Paper, before <c>BeginFrame</c>. Multiple Paper instances can be fed in the same frame (the
/// editor's own UI and the game's UI rendered into the Game View render target, for example).
/// </summary>
public static class PaperInputBridge
{
    private static readonly (KeyCode Key, PaperKey Mapped)[] s_keyMap = BuildKeyMap();

    public static void Pump(Paper paper, Float2 pointerPos, bool receivesInput = true)
    {
        if (!receivesInput)
        {
            paper.SetPointerState(PaperMouseBtn.Unknown, -1f, -1f, false, true);
            paper.SetPointerState(PaperMouseBtn.Left, -1f, -1f, false, false);
            paper.SetPointerState(PaperMouseBtn.Right, -1f, -1f, false, false);
            paper.SetPointerState(PaperMouseBtn.Middle, -1f, -1f, false, false);
            return;
        }

        pointerPos = ApplyPointerWrap(paper, pointerPos);
        float x = pointerPos.X;
        float y = pointerPos.Y;
        paper.SetPointerState(PaperMouseBtn.Unknown, x, y, false, true);

        PumpButton(paper, 0, PaperMouseBtn.Left, x, y);
        PumpButton(paper, 1, PaperMouseBtn.Right, x, y);
        PumpButton(paper, 2, PaperMouseBtn.Middle, x, y);

        float wheelDelta = Input.MouseWheelDelta;
        if (wheelDelta != 0)
            paper.SetPointerWheel(wheelDelta);

        // InputString is read non-destructively, so every Paper and any user UI sees the same characters.
        foreach (char ch in Input.InputString)
            paper.AddInputCharacter(ch.ToString());

        foreach ((KeyCode key, PaperKey mapped) in s_keyMap)
        {
            if (Input.GetKeyDown(key))
                paper.SetKeyState(mapped, true);
            else if (Input.GetKeyUp(key))
                paper.SetKeyState(mapped, false);
        }
    }

    /// <summary>
    /// Honour <see cref="Paper.WantsPointerWrap"/>: while a widget is mid-drag, teleport the OS cursor
    /// to the opposite window edge instead of letting it run out of screen. Paper is told the jump so
    /// <see cref="Paper.PointerDelta"/> stays continuous, and the widget never learns it happened.
    /// </summary>
    private static Float2 ApplyPointerWrap(Paper paper, Float2 pointerPos)
    {
        var size = Window.Size;
        if (!paper.TryWrapPointer(pointerPos, size.X, size.Y, out Float2 wrapped))
            return pointerPos;

        Input.MousePosition = new Int2((int)wrapped.X, (int)wrapped.Y);
        return wrapped;
    }

    private static void PumpButton(Paper paper, int button, PaperMouseBtn btn, float x, float y)
    {
        if (Input.GetMouseButtonDown(button))
            paper.SetPointerState(btn, x, y, true, false);
        if (Input.GetMouseButtonUp(button))
            paper.SetPointerState(btn, x, y, false, false);
    }

    // Nearly every KeyCode name matches a PaperKey name, so the map is built by name once and the
    // handful of names that differ are overridden afterwards.
    private static (KeyCode, PaperKey)[] BuildKeyMap()
    {
        var map = new Dictionary<KeyCode, PaperKey>();

        foreach (KeyCode k in Enum.GetValues<KeyCode>())
            if (k != KeyCode.Unknown && Enum.TryParse(k.ToString(), out PaperKey mapped))
                map[k] = mapped;

        map[KeyCode.Equal] = PaperKey.Equals;
        map[KeyCode.BackSlash] = PaperKey.Backslash;
        map[KeyCode.GraveAccent] = PaperKey.Grave;
        map[KeyCode.KeypadEqual] = PaperKey.KeypadEquals;

        map[KeyCode.Number0] = PaperKey.Num0;
        map[KeyCode.Number1] = PaperKey.Num1;
        map[KeyCode.Number2] = PaperKey.Num2;
        map[KeyCode.Number3] = PaperKey.Num3;
        map[KeyCode.Number4] = PaperKey.Num4;
        map[KeyCode.Number5] = PaperKey.Num5;
        map[KeyCode.Number6] = PaperKey.Num6;
        map[KeyCode.Number7] = PaperKey.Num7;
        map[KeyCode.Number8] = PaperKey.Num8;
        map[KeyCode.Number9] = PaperKey.Num9;

        map[KeyCode.KeypadSubtract] = PaperKey.KeypadMinus;
        map[KeyCode.KeypadAdd] = PaperKey.KeypadPlus;

        map[KeyCode.ShiftLeft] = PaperKey.LeftShift;
        map[KeyCode.ShiftRight] = PaperKey.RightShift;
        map[KeyCode.AltLeft] = PaperKey.LeftAlt;
        map[KeyCode.AltRight] = PaperKey.RightAlt;
        map[KeyCode.ControlLeft] = PaperKey.LeftControl;
        map[KeyCode.ControlRight] = PaperKey.RightControl;
        map[KeyCode.SuperLeft] = PaperKey.LeftSuper;
        map[KeyCode.SuperRight] = PaperKey.RightSuper;

        return map.Select(kv => (kv.Key, kv.Value)).ToArray();
    }
}
