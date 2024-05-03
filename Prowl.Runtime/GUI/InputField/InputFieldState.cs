using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.GUI.InputField
{
    [Flags]
    internal enum LGuiTextFieldCmdKey : uint
    {
        None = 0,
        Character = 1 << 0,
        Left = 1 << 1,
        Right = 1 << 2,
        Up = 1 << 3,
        Down = 1 << 4,
        Home = 1 << 5,
        End = 1 << 6,
        Delete = 1 << 7,
        Backspace = 1 << 8,
        Undo = 1 << 9,
        Redo = 1 << 10,
        Copy = 1 << 11,
        Paste = 1 << 12,
        Cut = 1 << 13,
        Ctrl = 1 << 14,
        Shift = 1 << 15,
        Alt = 1 << 16,
    }

    internal class InputFieldState
    {
        internal ulong ID { get; }

        internal bool InsertMode { get; set; } = false;
        internal bool CapsLock { get; set; } = false;
        internal bool SingleLine { get; private set; } = false;
        internal Vector2 Spacing { get; set; } = Vector2.zero;
        internal uint OffsetX { get; set; } = 0u;
        internal uint OffsetY { get; set; } = 0u;

        internal string Text => Buffer_.ToString();
        internal int TextLength => Buffer_.Length;
        internal uint MaxLength { get; set; } = int.MaxValue;

        private readonly StringBuilder Buffer_ = new StringBuilder();
        private readonly Stack<LGuiIUndoTextCommand> UndoCommands_ = new Stack<LGuiIUndoTextCommand>();
        private readonly Stack<LGuiIUndoTextCommand> RedoCommands_ = new Stack<LGuiIUndoTextCommand>();

        private int Cursor_ = 0;
        private int SelectStart_ = 0;
        private int SelectEnd_ = 0;

        internal InputFieldState(ulong ID, string Text, bool SingleLine)
        {
            this.ID = ID;
            this.SingleLine = SingleLine;
            this.Buffer_.Append(Text);
        }

        private bool HasSelection()
        {
            return SelectStart_ != SelectEnd_;
        }

        private void ClearSelection()
        {
            SelectStart_ = SelectEnd_ = Cursor_;
        }

        private void SortSelection()
        {
            if (SelectEnd_ < SelectStart_)
            {
                var Temp = SelectStart_;
                SelectStart_ = SelectEnd_;
                SelectEnd_ = Temp;
            }
        }

        private void DeleteSelection()
        {
            if (HasSelection())
            {
                SortSelection();
                Cursor_ = SelectStart_;
                Execute(new LGuiRemoveStringCommand(this, SelectStart_, SelectEnd_ - SelectStart_));
                ClearSelection();
            }
        }

        private string GetSelectionString()
        {
            if (SelectStart_ < SelectEnd_)
            {
                return GetString(SelectStart_, SelectEnd_ - SelectStart_);
            }
            else
            {
                return GetString(SelectEnd_, SelectStart_ - SelectEnd_);
            }
        }

        private void Execute(LGuiITextCommand Cmd)
        {
            Cmd.Execute();
            RedoCommands_.Clear();

            if (Cmd is LGuiIUndoTextCommand UndoCmd)
            {
                UndoCommands_.Push(UndoCmd);
            }
            else
            {
                UndoCommands_.Clear();
            }
        }

        private void Undo()
        {
            if (UndoCommands_.Count == 0)
            {
                return;
            }

            var Cmd = UndoCommands_.Pop();
            Cmd.Undo();
            RedoCommands_.Push(Cmd);
        }

        private void Redo()
        {
            if (RedoCommands_.Count == 0)
            {
                return;
            }

            var Cmd = RedoCommands_.Pop();
            Cmd.Execute();
            UndoCommands_.Push(Cmd);
        }

        private int GetLineStartPos(int Index)
        {
            while (Index > 0)
            {
                if (Buffer_[Index] == '\n')
                {
                    return Index + 1;
                }

                Index--;
            }

            return 0;
        }

        private int GetLineEndPos(int Index)
        {
            while (Index < Buffer_.Length)
            {
                if (Buffer_[Index] == '\n')
                {
                    return Index;
                }

                Index++;
            }

            return Buffer_.Length;
        }

        private int FindPrevLinePos(int Index)
        {
            var LineStart = GetLineStartPos(Index - 1);
            if (LineStart == 0)
            {
                return Index;
            }

            var Count = Index - LineStart;
            var PrevLineEnd = GetLineEndPos(LineStart - 1);
            var PrevLineStart = GetLineStartPos(PrevLineEnd - 1);

            while (Count > 0 && PrevLineStart < PrevLineEnd)
            {
                PrevLineStart++;
                Count--;
            }

            return PrevLineStart;
        }

        private int FindNextLinePos(int Index)
        {
            var LineStart = GetLineStartPos(Index - 1);
            if (LineStart == Buffer_.Length)
            {
                return Index;
            }

            var LineEnd = GetLineEndPos(Index);
            if (LineEnd == Buffer_.Length)
            {
                return Index;
            }

            var Count = Index - LineStart;
            var NextLineEnd = GetLineEndPos(LineEnd + 1);
            var NextLineStart = GetLineStartPos(NextLineEnd - 1);

            while (Count > 0 && NextLineStart < NextLineEnd)
            {
                NextLineStart++;
                Count--;
            }

            return NextLineStart;
        }

        internal void SetCursor(int Cursor)
        {
            if (Cursor < 0 || Cursor > Buffer_.Length)
            {
                return;
            }

            Cursor_ = Cursor;
        }

        internal int GetCursor()
        {
            return Cursor_;
        }

        internal int GetSelectStart()
        {
            return SelectStart_;
        }

        internal int GetSelectEnd()
        {
            return SelectEnd_;
        }

        internal char GetCharacter(int Index)
        {
            if (Index < 0 || Index >= Buffer_.Length)
            {
                return (char)0;
            }

            return Buffer_[Index];
        }

        internal string GetString(int Index, int Length)
        {
            if (Index < 0 || Index + Length > Buffer_.Length)
            {
                return string.Empty;
            }

            return Buffer_.ToString().Substring(Index, Length);
        }

        internal void InsertCharacter(int Index, char Ch)
        {
            if (Index < 0 || Index > Buffer_.Length || Ch == 0)
            {
                return;
            }

            Buffer_.Insert(Index, Ch);
        }

        internal void RemoveCharacter(int Index)
        {
            if (Index < 0 || Index > Buffer_.Length)
            {
                return;
            }

            Buffer_.Remove(Index, 1);
        }

        internal void InsertString(int Index, string Value)
        {
            if (Index < 0 || Index > Buffer_.Length || string.IsNullOrEmpty(Value))
            {
                return;
            }

            Buffer_.Insert(Index, Value);
        }

        internal void RemoveString(int Index, int Length)
        {
            if (Index < 0 || Index + Length > Buffer_.Length)
            {
                return;
            }

            Buffer_.Remove(Index, Length);
        }

        internal void SelectAll()
        {
            SelectStart_ = 0;
            SelectEnd_ = Cursor_ = Buffer_.Length;
        }

        internal void OnClick(int Cursor)
        {
            Cursor_ = Cursor;
            ClearSelection();
        }

        internal void OnDrag(int Cursor)
        {
            if (!HasSelection())
            {
                SelectStart_ = Cursor_;
            }

            Cursor_ = SelectEnd_ = Cursor;
        }

        internal void OnCmdKey(LGuiTextFieldCmdKey CmdKey, char Ch)
        {
            if ((CmdKey & LGuiTextFieldCmdKey.Character) == LGuiTextFieldCmdKey.Character)
            {
                if (Ch == (char)0 || (SingleLine && Ch == '\n'))
                {
                }
                else
                {
                    if (HasSelection())
                    {
                        SortSelection();
                        Cursor_ = SelectStart_;
                        Execute(new LGuiReplaceStringCommand(this, SelectStart_, SelectEnd_ - SelectStart_, Ch.ToString()));
                        ClearSelection();
                    }
                    else
                    {
                        if (InsertMode && Cursor_ < Buffer_.Length && GetCharacter(Cursor_) != '\n')
                        {
                            Execute(new LGuiReplaceCharacterCommand(this, Cursor_, Ch));
                        }
                        else if (Buffer_.Length + 1 <= MaxLength)
                        {
                            Execute(new LGuiInsertCharacterCommand(this, Cursor_, Ch));
                        }
                    }
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Backspace) == LGuiTextFieldCmdKey.Backspace)
            {
                if (HasSelection())
                {
                    DeleteSelection();
                }
                else
                {
                    if (Cursor_ > 0)
                    {
                        Execute(new LGuiRemoveCharacterCommand(this, Cursor_));
                    }
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Delete) == LGuiTextFieldCmdKey.Delete)
            {
                if (HasSelection())
                {
                    DeleteSelection();
                }
                else
                {
                    if (Cursor_ < Buffer_.Length)
                    {
                        Cursor_++;
                        Execute(new LGuiRemoveCharacterCommand(this, Cursor_));
                    }
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Left) == LGuiTextFieldCmdKey.Left)
            {
                if ((CmdKey & LGuiTextFieldCmdKey.Shift) == LGuiTextFieldCmdKey.Shift)
                {
                    if (SelectEnd_ > 0)
                    {
                        SelectEnd_--;
                    }

                    Cursor_ = SelectEnd_;
                }
                else
                {
                    if (HasSelection())
                    {
                        Cursor_ = SelectStart_;
                        ClearSelection();
                    }
                    else if (Cursor_ > 0)
                    {
                        Cursor_--;
                    }
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Right) == LGuiTextFieldCmdKey.Right)
            {
                if ((CmdKey & LGuiTextFieldCmdKey.Shift) == LGuiTextFieldCmdKey.Shift)
                {
                    if (SelectEnd_ < Buffer_.Length)
                    {
                        SelectEnd_++;
                    }

                    Cursor_ = SelectEnd_;
                }
                else
                {
                    if (HasSelection())
                    {
                        Cursor_ = SelectEnd_;
                        ClearSelection();
                    }
                    else if (Cursor_ < Buffer_.Length)
                    {
                        Cursor_++;
                    }
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Up) == LGuiTextFieldCmdKey.Up && !SingleLine)
            {
                Cursor_ = FindPrevLinePos(Cursor_);

                if ((CmdKey & LGuiTextFieldCmdKey.Shift) == LGuiTextFieldCmdKey.Shift)
                {
                    SelectEnd_ = Cursor_;
                }
                else
                {
                    ClearSelection();
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Down) == LGuiTextFieldCmdKey.Down && !SingleLine)
            {
                Cursor_ = FindNextLinePos(Cursor_);

                if ((CmdKey & LGuiTextFieldCmdKey.Shift) == LGuiTextFieldCmdKey.Shift)
                {
                    SelectEnd_ = Cursor_;
                }
                else
                {
                    ClearSelection();
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Home) == LGuiTextFieldCmdKey.Home)
            {
                Cursor_ = (CmdKey & LGuiTextFieldCmdKey.Ctrl) == LGuiTextFieldCmdKey.Ctrl ? 0 : GetLineStartPos(Cursor_ - 1);

                if ((CmdKey & LGuiTextFieldCmdKey.Shift) == LGuiTextFieldCmdKey.Shift)
                {
                    SelectEnd_ = Cursor_;
                }
                else
                {
                    ClearSelection();
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.End) == LGuiTextFieldCmdKey.End)
            {
                Cursor_ = (CmdKey & LGuiTextFieldCmdKey.Ctrl) == LGuiTextFieldCmdKey.Ctrl ? Buffer_.Length : GetLineEndPos(Cursor_);

                if ((CmdKey & LGuiTextFieldCmdKey.Shift) == LGuiTextFieldCmdKey.Shift)
                {
                    SelectEnd_ = Cursor_;
                }
                else
                {
                    ClearSelection();
                }
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Undo) == LGuiTextFieldCmdKey.Undo)
            {
                Undo();
            }
            else if ((CmdKey & LGuiTextFieldCmdKey.Redo) == LGuiTextFieldCmdKey.Redo)
            {
                Redo();
            }
            //else if ((CmdKey & LGuiTextFieldCmdKey.Copy) == LGuiTextFieldCmdKey.Copy)
            //{
            //    if (HasSelection())
            //    {
            //        var ClipboardText = GetSelectionString();
            //        LGuiConvert.SetClipboardText(ClipboardText);
            //    }
            //}
            //else if ((CmdKey & LGuiTextFieldCmdKey.Paste) == LGuiTextFieldCmdKey.Paste)
            //{
            //    var ClipboardText = LGuiConvert.GetClipboardText();
            //    if (SingleLine)
            //    {
            //        ClipboardText = ClipboardText.Trim('\n');
            //    }
            //
            //    if (!string.IsNullOrEmpty(ClipboardText) && Buffer_.Length + ClipboardText.Length <= MaxLength)
            //    {
            //        if (HasSelection())
            //        {
            //            SortSelection();
            //            Cursor_ = SelectStart_;
            //            Execute(new LGuiReplaceStringCommand(this, SelectStart_, SelectEnd_ - SelectStart_, ClipboardText));
            //            ClearSelection();
            //        }
            //        else
            //        {
            //            Execute(new LGuiInsertStringCommand(this, Cursor_, ClipboardText));
            //        }
            //    }
            //}
            //else if ((CmdKey & LGuiTextFieldCmdKey.Cut) == LGuiTextFieldCmdKey.Cut)
            //{
            //    if (HasSelection())
            //    {
            //        var ClipboardText = GetSelectionString();
            //        LGuiConvert.SetClipboardText(ClipboardText);
            //        DeleteSelection();
            //    }
            //}
        }

        internal interface LGuiITextCommand
        {
            void Execute();
        }

        internal interface LGuiIUndoTextCommand : LGuiITextCommand
        {
            void Undo();
        }

        internal class LGuiInsertCharacterCommand : LGuiIUndoTextCommand
        {
            private InputFieldState State { get; }
            private int Cursor { get; }
            private char Ch { get; }

            public LGuiInsertCharacterCommand(InputFieldState State, int Cursor, char Ch)
            {
                this.State = State;
                this.Cursor = Cursor;
                this.Ch = Ch;
            }

            public void Execute()
            {
                State.InsertCharacter(Cursor, Ch);
                State.SetCursor(Cursor + 1);
            }

            public void Undo()
            {
                State.RemoveCharacter(Cursor);
                State.SetCursor(Cursor);
            }
        }

        internal class LGuiRemoveCharacterCommand : LGuiIUndoTextCommand
        {
            private InputFieldState State { get; }
            private int Cursor { get; }
            private char OldCh;

            public LGuiRemoveCharacterCommand(InputFieldState State, int Cursor)
            {
                this.State = State;
                this.Cursor = Cursor;
            }

            public void Execute()
            {
                OldCh = State.GetCharacter(Cursor - 1);
                State.RemoveCharacter(Cursor - 1);
                State.SetCursor(Cursor - 1);
            }

            public void Undo()
            {
                State.InsertCharacter(Cursor - 1, OldCh);
                State.SetCursor(Cursor);
            }
        }

        internal class LGuiReplaceCharacterCommand : LGuiIUndoTextCommand
        {
            private InputFieldState State { get; }
            private int Cursor { get; }
            private char NewCh { get; }
            private char OldCh;

            public LGuiReplaceCharacterCommand(InputFieldState State, int Cursor, char NewCh)
            {
                this.State = State;
                this.Cursor = Cursor;
                this.NewCh = NewCh;
            }

            public void Execute()
            {
                OldCh = State.GetCharacter(Cursor);
                State.RemoveCharacter(Cursor);
                State.InsertCharacter(Cursor, NewCh);
                State.SetCursor(Cursor + 1);
            }

            public void Undo()
            {
                State.RemoveCharacter(Cursor);
                State.InsertCharacter(Cursor, OldCh);
                State.SetCursor(Cursor);
            }
        }

        internal class LGuiInsertStringCommand : LGuiIUndoTextCommand
        {
            private InputFieldState State { get; }
            private int Cursor { get; }
            private string Value { get; }

            public LGuiInsertStringCommand(InputFieldState State, int Cursor, string Value)
            {
                this.State = State;
                this.Cursor = Cursor;
                this.Value = Value;
            }

            public void Execute()
            {
                State.InsertString(Cursor, Value);
                State.SetCursor(Cursor + Value.Length);
            }

            public void Undo()
            {
                State.RemoveString(Cursor, Value.Length);
                State.SetCursor(Cursor);
            }
        }

        internal class LGuiRemoveStringCommand : LGuiIUndoTextCommand
        {
            private InputFieldState State { get; }
            private int Cursor { get; }
            private int Length { get; }
            private string OldValue;

            public LGuiRemoveStringCommand(InputFieldState State, int Cursor, int Length)
            {
                this.State = State;
                this.Cursor = Cursor;
                this.Length = Length;
            }

            public void Execute()
            {
                OldValue = State.GetString(Cursor, Length);
                State.RemoveString(Cursor, Length);
                State.SetCursor(Cursor);
            }

            public void Undo()
            {
                State.InsertString(Cursor, OldValue);
                State.SetCursor(Cursor + OldValue.Length);
            }
        }

        internal class LGuiReplaceStringCommand : LGuiIUndoTextCommand
        {
            private InputFieldState State { get; }
            private int Cursor { get; }
            private int Length { get; }
            private string NewValue { get; }
            private string OldValue;

            public LGuiReplaceStringCommand(InputFieldState State, int Cursor, int Length, string NewValue)
            {
                this.State = State;
                this.Cursor = Cursor;
                this.Length = Length;
                this.NewValue = NewValue;
            }

            public void Execute()
            {
                OldValue = State.GetString(Cursor, Length);
                State.RemoveString(Cursor, Length);
                State.InsertString(Cursor, NewValue);
                State.SetCursor(Cursor + NewValue.Length);
            }

            public void Undo()
            {
                State.RemoveString(Cursor, NewValue.Length);
                State.InsertString(Cursor, OldValue);
                State.SetCursor(Cursor + OldValue.Length);
            }
        }
    }
}
