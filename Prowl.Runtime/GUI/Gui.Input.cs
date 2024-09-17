// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.GUI;

public partial class Gui
{
    public static readonly Key[] KeyValues = Enum.GetValues<Key>();
    public static readonly MouseButton[] MouseValues = Enum.GetValues<MouseButton>();

    public event Action<Vector2> OnPointerPosSet;
    public event Action<bool> OnCursorVisibilitySet;

    internal readonly bool[] KeyCurState = new bool[KeyValues.Length];
    internal readonly bool[] KeyPreState = new bool[KeyValues.Length];

    internal readonly double[] KeyPressedTime = new double[KeyValues.Length];
    internal Key KeyCode = Key.Unknown;

    internal readonly bool[] PointerCurState = new bool[MouseValues.Length];
    internal readonly bool[] PointerPreState = new bool[MouseValues.Length];

    internal readonly double[] PointerPressedTime = new double[MouseValues.Length];
    internal readonly Vector2[] PointerClickPos = new Vector2[MouseValues.Length];
    internal MouseButton PointerButton = (MouseButton)(-1);
    public Vector2 PreviousPointerPos = Vector2.zero;

    private Vector2 _pointerPos;
    public Vector2 PointerPos
    {
        get => _pointerPos;
        set
        {
            _pointerPos = value;
            OnPointerPosSet?.Invoke(_pointerPos / frameBufferScale);
        }
    }
    public float PointerWheel = 0;

    public Vector2 PointerDelta => PointerPos - PreviousPointerPos;
    public bool IsPointerMoving => PointerDelta.sqrMagnitude > 0;

    public readonly double[] PointerLastClickTime = new double[MouseValues.Length];
    public readonly Vector2[] PointerLastClickPos = new Vector2[MouseValues.Length];
    public const double MaxDoubleClickTime = 0.25;

    private Vector2 frameBufferScale;

    void StartInputFrame(Vector2 frameBufferScale)
    {
        this.frameBufferScale = frameBufferScale;
        for (var Index = 0; Index < KeyPressedTime.Length; ++Index)
            KeyPressedTime[Index] += Time.deltaTime;

        for (var Index = 0; Index < PointerPressedTime.Length; ++Index)
            PointerPressedTime[Index] += Time.deltaTime;
    }

    void EndInputFrame()
    {
        for (var Index = 0; Index < KeyCurState.Length; ++Index)
        {
            KeyPreState[Index] = KeyCurState[Index];

            if (!KeyCurState[Index])
                KeyPressedTime[Index] = 0.0f;
        }

        for (var Index = 0; Index < PointerCurState.Length; ++Index)
        {
            if (PointerPreState[Index] && !PointerCurState[Index]) // Just released
            {
                PointerLastClickTime[Index] = Time.time + MaxDoubleClickTime;
                PointerLastClickPos[Index] = PointerPos;
            }

            PointerPreState[Index] = PointerCurState[Index];

            if (!PointerCurState[Index])
                PointerPressedTime[Index] = 0.0f;
        }


        PointerWheel = 0;
        PreviousPointerPos = PointerPos;
    }

    public void ClearInput()
    {
        for (var Index = 0; Index < KeyCurState.Length; ++Index)
        {
            KeyCurState[Index] = false;
            KeyPreState[Index] = false;
            KeyPressedTime[Index] = 0;
        }

        KeyCode = Key.Unknown;

        for (var Index = 0; Index < PointerCurState.Length; ++Index)
        {
            PointerCurState[Index] = false;
            PointerPreState[Index] = false;
            PointerPressedTime[Index] = 0;
            PointerClickPos[Index] = Vector2.zero;
        }

        PointerButton = MouseButton.Unknown;
        PreviousPointerPos = _pointerPos;
        _pointerPos = Vector2.zero;
        PointerWheel = 0;
    }

    public void SetKeyState(Key Key, bool IsKeyDown)
    {
        KeyPreState[(int)Key] = KeyCurState[(int)Key];
        KeyCurState[(int)Key] = IsKeyDown;
        KeyCode = IsKeyDown ? Key : Key.Unknown;
    }

    public void SetPointerState(MouseButton Btn, double X, double Y, bool IsPointerBtnDown, bool IsPointerMove)
    {
        var Index = (int)Btn;
        PointerButton = Btn;

        X *= frameBufferScale.x;
        Y *= frameBufferScale.y;

        if (!IsPointerMove)
        {
            PointerPreState[Index] = PointerCurState[Index];
            PointerCurState[Index] = IsPointerBtnDown;
            PointerClickPos[Index] = new Vector2(X, Y);
        }
        else
        {
            _pointerPos = new Vector2(X, Y);
        }
    }

    public bool IsKeyDown(Key Key) => KeyCurState[(int)Key];
    public bool IsKeyUp(Key Key) => !KeyCurState[(int)Key];
    public bool IsKeyClick(Key Key) => !KeyPreState[(int)Key] && KeyCurState[(int)Key];
    public bool IsKeyPressed(Key Key) => IsKeyClick(Key) || (IsKeyDown(Key) && KeyPressedTime[(int)Key] >= 0.5f);
    public bool IsPointerDown(MouseButton Btn) => PointerCurState[(int)Btn];
    public bool IsPointerUp(MouseButton Btn) => !PointerCurState[(int)Btn];
    public bool IsPointerClick(MouseButton Btn = MouseButton.Left, bool onrelease = false) => !onrelease ? (!PointerPreState[(int)Btn] && PointerCurState[(int)Btn]) : (PointerPreState[(int)Btn] && !PointerCurState[(int)Btn]);
    public bool IsPointerDoubleClick(MouseButton Btn = MouseButton.Left) => IsPointerClick(Btn) && Time.time < PointerLastClickTime[(int)Btn] && (PointerPos - PointerLastClickPos[(int)Btn]).sqrMagnitude < 5;
    public bool IsPointerPressed(MouseButton Btn = MouseButton.Left) => IsPointerClick(Btn) || (IsPointerDown(Btn) && PointerPressedTime[(int)Btn] >= 0.5f);
    public Vector2 GetPointerClickPos(MouseButton Btn = MouseButton.Left) => PointerClickPos[(int)Btn];
    public void SetCursorVisibility(bool Visible) => OnCursorVisibilitySet?.Invoke(Visible);
}
