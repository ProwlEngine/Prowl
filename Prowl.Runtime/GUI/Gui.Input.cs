using Silk.NET.Input;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        internal bool[] KeyCurState = new bool[(int)Key.Menu];
        internal bool[] KeyPreState = new bool[(int)Key.Menu];
        internal double[] KeyPressedTime = new double[(int)Key.Menu];
        internal Key KeyCode = Key.Unknown;

        internal bool[] PointerCurState = new bool[(int)MouseButton.Button12];
        internal bool[] PointerPreState = new bool[(int)MouseButton.Button12];
        internal double[] PointerPressedTime = new double[(int)MouseButton.Button12];
        internal Vector2[] PointerClickPos = new Vector2[(int)MouseButton.Button12];
        internal Vector2 PointerCurDeltaPos = Vector2.zero;
        internal Vector2 PointerPreDeltaPos = Vector2.zero;
        internal MouseButton PointerButton = MouseButton.Unknown;
        public Vector2 PointerPos = Vector2.zero;
        public float PointerWheel = 0;

        public Vector2 PointerMovePos => PointerCurDeltaPos;
        public Vector2 PointerDelta => new(PointerCurDeltaPos.x - PointerPreDeltaPos.x, PointerCurDeltaPos.y - PointerPreDeltaPos.y);
        public bool IsPointerMoving => PointerDelta.sqrMagnitude > 0;

        void StartInputFrame()
        {
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
                PointerPreState[Index] = PointerCurState[Index];

                if (!PointerCurState[Index])
                    PointerPressedTime[Index] = 0.0f;
            }

            PointerPreDeltaPos = PointerCurDeltaPos;
            PointerWheel = 0;
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

            PointerCurDeltaPos = Vector2.zero;
            PointerPreDeltaPos = Vector2.zero;
            PointerButton = MouseButton.Unknown;
            PointerPos = Vector2.zero;
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

            if (!IsPointerMove)
            {
                PointerPreState[Index] = PointerCurState[Index];
                PointerCurState[Index] = IsPointerBtnDown;
                PointerClickPos[Index] = new Vector2(X, Y);

                PointerPreDeltaPos = PointerCurDeltaPos;
                PointerCurDeltaPos = Vector2.zero;
            }
            else
            {
                if (Index != -1 && PointerCurState[Index])
                {
                    PointerPreDeltaPos = PointerCurDeltaPos;
                    PointerCurDeltaPos.x = X - PointerClickPos[Index].x;
                    PointerCurDeltaPos.y = Y - PointerClickPos[Index].y;
                }

                PointerPos.x = X;
                PointerPos.y = Y;
            }
        }

        public bool IsKeyDown(Key Key) => KeyCurState[(int)Key];
        public bool IsKeyUp(Key Key) => !KeyCurState[(int)Key];
        public bool IsKeyClick(Key Key) => !KeyPreState[(int)Key] && KeyCurState[(int)Key];
        public bool IsKeyPressed(Key Key) => IsKeyClick(Key) || (IsKeyDown(Key) && KeyPressedTime[(int)Key] >= 0.5f);
        public bool IsPointerDown(MouseButton Btn) => PointerCurState[(int)Btn];
        public bool IsPointerUp(MouseButton Btn) => !PointerCurState[(int)Btn];
        public bool IsPointerClick(MouseButton Btn) => !PointerPreState[(int)Btn] && PointerCurState[(int)Btn];
        public bool IsPointerPressed(MouseButton Btn) => IsPointerClick(Btn) || (IsPointerDown(Btn) && PointerPressedTime[(int)Btn] >= 0.5f);
        public Vector2 GetPointerClickPos(MouseButton Btn) => PointerClickPos[(int)Btn];
    }

}
