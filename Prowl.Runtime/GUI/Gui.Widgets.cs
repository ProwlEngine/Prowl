using Microsoft.VisualBasic;
using Prowl.Runtime.GUI.Layout;
using SharpFont.Fnt;
using Silk.NET.SDL;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection.Emit;
using System;
using System.Numerics;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.InputField;
using System.Collections.Generic;
using Silk.NET.Input;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public void ScrollV()
        {
            const int width = 6;
            const int padding = 2;

            var n = CurrentNode;
            CurrentNode.VScroll = GetStorage<double>("VScroll");

            using (Node().Width(width).Height(Size.Percentage(1f, -(padding * 2))).Left(Offset.Percentage(1f, -(width + padding))).Top(padding).IgnoreLayout().Enter())
            {

                Rect scrollRect = CurrentNode.LayoutData.Rect;
                if (n.HasLayoutData)
                {
                    if (n.LayoutData.ContentRect.height > n.LayoutData.Rect.height)
                    {
                        double overflowHeight = n.LayoutData.ContentRect.height - n.LayoutData.Rect.height;

                        double scrollRatio = n.LayoutData.Rect.height / n.LayoutData.ContentRect.height;
                        double scrollBarHeight = scrollRatio * scrollRect.height;

                        double scrollBarY = (n.VScroll / overflowHeight) * (scrollRect.height - scrollBarHeight);

                        Rect barRect = new(scrollRect.x, scrollRect.y + scrollBarY, scrollRect.width, scrollBarHeight);

                        Interactable interact = GetInteractable(barRect);

                        if (interact.TakeFocus() || interact.IsActive())
                        {
                            DrawRectFilled(barRect, Color.green, 20f);
                            {
                                n.VScroll += Input.MouseDelta.y * 2f;
                                layoutDirty = true;
                            }
                        }
                        else if (interact.IsHovered()) DrawRectFilled(barRect, Color.blue, 20f);
                        else DrawRectFilled(barRect, Color.red, 20f);

                        if (IsHovering(n.LayoutData.Rect) && Input.MouseWheelDelta != 0)
                        {
                            n.VScroll -= Input.MouseWheelDelta * 10;
                            layoutDirty = true;
                        }

                        n.VScroll = Mathf.Clamp(n.VScroll, 0, overflowHeight);
                    }
                    else if (n.VScroll != 0)
                    {
                        n.VScroll = 0;
                        layoutDirty = true;
                    }
                }
            }

            SetStorage("VScroll", CurrentNode.VScroll);
        }


        public static bool Button(string? label, Offset x, Offset y, Size width, Size height, float roundness = 2f, bool invisible = false, bool repeat = false) => Button(label, x, y, width, height, out _, roundness, invisible, repeat);
        public static bool Button(string? label, Offset x, Offset y, Size width, Size height, out LayoutNode node, float roundness = 2f, bool invisible = false, bool repeat = false)
        {
            var g = Gui.ActiveGUI;
            using ((node = g.Node()).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();

                interact.UpdateContext();

                if (!invisible)
                {
                    var col = g.ActiveID == interact.ID ? Color.green :
                              g.HoveredID == interact.ID ? Color.blue : Color.red;

                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, roundness);
                    g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.yellow, roundness);
                    
                    g.DrawText(label, 20, g.CurrentNode.LayoutData.InnerRect, Color.black);
                }

                if (repeat)
                    return interact.IsActive();
                return interact.TakeFocus();
            }
        }

        // TODO: STB_TextEdit
        // Based on: https://github.com/UnSkyToo/LiteGui - MIT License
        [Flags]
        public enum InputFieldFlags : uint
        {
            None = 0,

            CharsDecimal = 1 << 0, // 0123456789.+-*/
            CharsScientific = 1 << 1, // 0123456789.+-*/eE
            CharsHexadecimal = 1 << 2, // 0123456789ABCDEFabcdef
            CharsUppercase = 1 << 3, // A..Z
            CharsNoBlank = 1 << 4, // no \n \t
            CharsCallback = 1 << 5, // filter with callback

            AutoSelectAll = 1 << 6, // first focus select all
            Multiline = 1 << 7,
            Readonly = 1 << 8,
            InsertMode = 1 << 9,
            NoUndoRedo = 1 << 10,
            Password = 1 << 11,

            OnlyDisplay = 1 << 12,
            EnterReturnsTrue = 1 << 13,
        }
        private static InputFieldState InputFieldState;
        public static bool InputField(ref string value, uint maxLength, InputFieldFlags flags, Offset x, Offset y, Size width, Size height, float roundness = 2f)
        {
            var g = Gui.ActiveGUI;
            using (g.Node().Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();

                interact.UpdateContext();

                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.red, roundness);
                g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.yellow, roundness);

                interact.TakeFocus();

                var ValueChanged = false;
                if (g.FocusID == interact.ID || g.ActiveID == interact.ID)
                {
                    ValueChanged = OnProcess(interact, ref value, g.CurrentNode.LayoutData.Rect, maxLength, flags, null);
                }
                else
                {
                    OnProcess(interact, ref value, g.CurrentNode.LayoutData.Rect, maxLength, flags | InputFieldFlags.OnlyDisplay, null);
                }

                return ValueChanged;
            }

        }

        internal static bool OnProcess(Interactable interact, ref string Text, Rect Rect, uint MaxLength, InputFieldFlags Flags, Func<char, bool> Callback)
        {
            var g = Gui.ActiveGUI;
            var ID = interact.ID;
            var RenderPos = new Vector2(Rect.x + 3, Rect.y + 1);

            var Info = new LGuiTextRenderInfo();
            Info.TextColor = Color.blue;
            Info.Font = UIDrawList.DefaultFont;
            Info.IsHidden = (Flags & InputFieldFlags.Password) == InputFieldFlags.Password;
            Info.HiddenChar = '*';
            Info.CountX = (uint)Mathf.Floor((Rect.width - 6) / Info.Font.FontSize);
            Info.CountY = (Flags & InputFieldFlags.Multiline) == InputFieldFlags.Multiline
                ? (uint)Mathf.Floor((Rect.height - 2) / Info.Font.FontSize)
                : 1u;

            Info.OnlyShowText = false;
            if ((Flags & InputFieldFlags.OnlyDisplay) == InputFieldFlags.OnlyDisplay)
            {
                Info.Text = Text;
                Info.OnlyShowText = true;

                Render(Info, RenderPos);
                return false;
            }

            if (InputFieldState == null || InputFieldState.ID != ID)
            {
                InputFieldState = new InputFieldState(ID, Text, (Flags & InputFieldFlags.Multiline) != InputFieldFlags.Multiline);
                if ((Flags & InputFieldFlags.AutoSelectAll) == InputFieldFlags.AutoSelectAll)
                    InputFieldState.SelectAll();
            }
            HandleKeyEvent(InputFieldState, Flags, Callback);
            HandleMouseEvent(InputFieldState, Info.Font, RenderPos);

            InputFieldState.MaxLength = MaxLength;
            Info.CursorColor = Info.TextColor;
            if ((Flags & InputFieldFlags.InsertMode) == InputFieldFlags.InsertMode)
            {
                InputFieldState.InsertMode = true;
                Info.CursorWidth = 1u;
                Info.CursorColor.a = 0.5f;
            }
            else
            {
                Info.CursorWidth = 0u;
            }

            var Cursor = (uint)InputFieldState.GetCursor();
            if (Cursor < InputFieldState.OffsetX)
            {
                InputFieldState.OffsetX = Cursor;
            }
            else if (Cursor > InputFieldState.OffsetX + Info.CountX)
            {
                InputFieldState.OffsetX = Cursor - Info.CountX;
            }

            Info.Text = InputFieldState.Text;
            Info.SelectStart = (uint)InputFieldState.GetSelectStart();
            Info.SelectEnd = (uint)InputFieldState.GetSelectEnd();
            Info.SelectColor = Color.blue;
            Info.Spacing = InputFieldState.Spacing;

            Info.Cursor = Cursor;
            Info.OffsetX = InputFieldState.OffsetX;
            Info.OffsetY = InputFieldState.OffsetY;

            Render(Info, RenderPos);

            if ((Flags & InputFieldFlags.EnterReturnsTrue) == InputFieldFlags.EnterReturnsTrue)
            {
                Text = InputFieldState.Text;
                if (g.IsKeyPressed(Silk.NET.Input.Key.Enter))
                {
                    g.FocusID = 0;
                    return true;
                }
                return false;
            }
            else
            {
                var oldText = Text;
                Text = InputFieldState.Text;
                return oldText != Text;
            }
        }

        private static void HandleKeyEvent(InputFieldState State, InputFieldFlags Flags, Func<char, bool> Callback)
        {
            var g = Gui.ActiveGUI;
            var KeyCode = g.KeyCode;
            if (KeyCode == Key.Unknown)
            {
                return;
            }

            if (!g.IsKeyPressed(KeyCode))
            {
                return;
            }

            var CmdKey = LGuiTextFieldCmdKey.None;
            var Ctrl = g.IsKeyDown(Key.ControlLeft);
            var Shift = g.IsKeyDown(Key.ShiftLeft);
            var Alt = g.IsKeyDown(Key.AltLeft);

            var ReadOnly = (Flags & InputFieldFlags.Readonly) == InputFieldFlags.Readonly;
            var NoUndoRedo = (Flags & InputFieldFlags.NoUndoRedo) == InputFieldFlags.NoUndoRedo;

            switch (KeyCode)
            {
                case Key.Left:
                    CmdKey = LGuiTextFieldCmdKey.Left;
                    break;
                case Key.Right:
                    CmdKey = LGuiTextFieldCmdKey.Right;
                    break;
                case Key.Up:
                    CmdKey = LGuiTextFieldCmdKey.Up;
                    break;
                case Key.Down:
                    CmdKey = LGuiTextFieldCmdKey.Down;
                    break;
                case Key.Home:
                    CmdKey = LGuiTextFieldCmdKey.Home;
                    break;
                case Key.End:
                    CmdKey = LGuiTextFieldCmdKey.End;
                    break;
                case Key.CapsLock:
                    State.CapsLock = !State.CapsLock;
                    break;
                case Key.Insert:
                    State.InsertMode = !State.InsertMode;
                    break;
                case Key.Backspace:
                    if (!ReadOnly)
                    {
                        CmdKey = LGuiTextFieldCmdKey.Backspace;
                    }
                    break;
                case Key.Delete:
                    if (!ReadOnly)
                    {
                        CmdKey = LGuiTextFieldCmdKey.Delete;
                    }
                    break;
                case Key.A:
                    if (Ctrl)
                    {
                        State.SelectAll();
                    }
                    else
                    {
                        CmdKey = LGuiTextFieldCmdKey.Character;
                    }
                    break;
                case Key.C:
                    if (!ReadOnly)
                    {
                        CmdKey = Ctrl ? LGuiTextFieldCmdKey.Copy : LGuiTextFieldCmdKey.Character;
                    }
                    break;
                case Key.V:
                    if (!ReadOnly)
                    {
                        CmdKey = Ctrl ? LGuiTextFieldCmdKey.Paste : LGuiTextFieldCmdKey.Character;
                    }
                    break;
                case Key.X:
                    if (!ReadOnly)
                    {
                        CmdKey = Ctrl ? LGuiTextFieldCmdKey.Cut : LGuiTextFieldCmdKey.Character;
                    }
                    break;
                case Key.Y:
                    if (!ReadOnly && !NoUndoRedo)
                    {
                        CmdKey = Ctrl ? LGuiTextFieldCmdKey.Redo : LGuiTextFieldCmdKey.Character;
                    }
                    break;
                case Key.Z:
                    if (!ReadOnly && !NoUndoRedo)
                    {
                        CmdKey = Ctrl ? LGuiTextFieldCmdKey.Undo : LGuiTextFieldCmdKey.Character;
                    }
                    break;
                case Key.ControlLeft:
                case Key.ShiftLeft:
                case Key.AltLeft:
                    break;
                default:
                    if (!ReadOnly)
                    {
                        CmdKey = LGuiTextFieldCmdKey.Character;
                    }
                    break;
            }

            if (CmdKey == LGuiTextFieldCmdKey.None)
            {
                return;
            }

            if (Ctrl)
            {
                CmdKey |= LGuiTextFieldCmdKey.Ctrl;
            }

            if (Shift)
            {
                CmdKey |= LGuiTextFieldCmdKey.Shift;
            }

            if (Alt)
            {
                CmdKey |= LGuiTextFieldCmdKey.Alt;
            }

            var Ch = Input.LastPressedChar;
            //var Filter = new LGuiTextFieldInputFilter(Flags, Callback);
            //if (!Filter.Parse(ref Ch))
            //    Ch = (char)0;

            State.OnCmdKey(CmdKey, Ch);
        }

        private static void HandleMouseEvent(InputFieldState State, Font Font, Vector2 RenderPos)
        {
            var g = Gui.ActiveGUI;
            if (g.IsPointerClick(MouseButton.Left))
            {
                var Pos = g.PointerPos - RenderPos + new Vector2(State.OffsetX * Font.FontSize, State.OffsetY * Font.FontSize);
                if (State.SingleLine)
                {
                    Pos.y = 0;
                }

                var Cursor = LocateCoord(State, Font, Pos);
                State.OnClick(Cursor);
            }
            else if (g.IsPointerDown(MouseButton.Left) && g.IsPointerMoving)
            {
                var Pos = g.PointerPos - RenderPos + new Vector2(State.OffsetX * Font.FontSize, State.OffsetY * Font.FontSize);
                if (State.SingleLine)
                {
                    Pos.y = 0;
                }

                var Cursor = LocateCoord(State, Font, Pos);
                State.OnDrag(Cursor);
            }
        }

        private static int LayoutRow(InputFieldState State, Font Font, int StartIndex, ref Vector2 TextSize)
        {
            var Index = StartIndex;
            var LineWidth = 0.0;
            TextSize.x = 0;
            TextSize.y = 0;

            while (Index < State.TextLength)
            {
                var Ch = State.GetCharacter(Index++);

                if (Ch == '\n')
                {
                    TextSize.x = Mathf.Max(TextSize.x, LineWidth);
                    TextSize.y = TextSize.y + Font.FontSize + State.Spacing.y;
                    LineWidth = 0.0f;
                    break;
                }

                if (Ch == '\r')
                    continue;

                LineWidth = LineWidth + Font.FontSize + State.Spacing.x;
            }

            if (TextSize.x < LineWidth)
                TextSize.x = LineWidth;

            if (LineWidth > 0 || TextSize.y == 0.0f)
                TextSize.y = TextSize.y + Font.FontSize + State.Spacing.y;

            return Index - StartIndex;
        }

        private static int LocateCoord(InputFieldState State, Font Font, Vector2 Pos)
        {
            var Length = State.TextLength;
            var Index = 0;
            var BaseY = 0.0;
            var Size = Vector2.zero;
            var NumChars = 0;

            while (Index < Length)
            {
                NumChars = LayoutRow(State, Font, Index, ref Size);
                if (NumChars <= 0)
                {
                    return Length;
                }

                if (Index == 0 && Pos.y < BaseY)
                {
                    return 0;
                }

                if (Pos.y < BaseY + Size.y)
                {
                    break;
                }

                Index += NumChars;
                BaseY += Size.y;
            }

            if (Index >= Length)
            {
                return Length;
            }

            if (Pos.x < 0)
            {
                return Index;
            }

            if (Pos.x < Size.x)
            {
                var PrevX = 0.0;
                for (var N = 0; N < NumChars; ++N)
                {
                    var Width = Font.FontSize + State.Spacing.x;
                    if (Pos.x < PrevX + Width)
                    {
                        if (Pos.x < PrevX + Width / 2.0f)
                        {
                            return Index + N;
                        }
                        else
                        {
                            return Index + N + 1;
                        }
                    }

                    PrevX += Width;
                }
            }

            if (State.GetCharacter(Index + NumChars - 1) == '\n')
            {
                return Index + NumChars - 1;
            }

            return Index + NumChars;
        }

        internal class LGuiTextRenderInfo
        {
            internal string Text;
            internal Color TextColor;
            internal Font Font;
            internal bool OnlyShowText;

            internal uint Cursor;
            internal uint CursorWidth;
            internal Color CursorColor;

            internal uint SelectStart;
            internal uint SelectEnd;
            internal Color SelectColor;

            internal bool IsHidden;
            internal char HiddenChar;

            internal Vector2 Spacing;
            internal uint OffsetX;
            internal uint OffsetY;
            internal uint CountX;
            internal uint CountY;
        }

        internal static void Render(LGuiTextRenderInfo Info, Vector2 RenderPos)
        {
            var g = Gui.ActiveGUI;
            var FontWidth = Info.Font.FontSize + Info.Spacing.x;
            var FontHeight = Info.Font.FontSize + Info.Spacing.y;
            var Padding = new Vector2(Info.Spacing.x / 2.0f, Info.Spacing.y / 2.0f);

            var SelectRects = new List<Rect>();
            var HasSelect = Info.SelectStart != Info.SelectEnd && !Info.OnlyShowText;
            var SelectStart = Mathf.Min(Info.SelectStart, Info.SelectEnd);
            var SelectEnd = Mathf.Min((uint)Info.Text.Length, Mathf.Max(Info.SelectStart, Info.SelectEnd));
            var InSelect = false;

            var BeginPos = RenderPos - new Vector2(Info.OffsetX * FontWidth, Info.OffsetY * FontHeight);
            var Pos = BeginPos;
            var CursorPos = Pos;
            var SelectPos = Pos;

            var CharX = 0u;
            var CharY = 0u;

            var Index = 0u;
            foreach (var Ch in Info.Text)
            {
                if (Index == Info.Cursor)
                {
                    CursorPos = Pos;
                }

                if (HasSelect)
                {
                    if (Index == SelectStart)
                    {
                        SelectPos = Pos;
                        InSelect = true;
                    }

                    if (Index == SelectEnd)
                    {
                        SelectRects.Add(new Rect(SelectPos, new Vector2(Pos.x - SelectPos.x, FontHeight)));
                        InSelect = false;
                        HasSelect = false;
                    }

                    if (InSelect && Ch == '\n')
                    {
                        SelectRects.Add(new Rect(SelectPos, new Vector2(Pos.x - SelectPos.x + FontWidth, FontHeight)));
                        SelectPos = new Vector2(BeginPos.x, Pos.y + FontHeight);
                    }
                }

                if (CharX >= Info.OffsetX && CharY >= Info.OffsetY && CharX < (Info.OffsetX + Info.CountX) && CharY < (Info.OffsetY + Info.CountY))
                {
                    g.DrawText(Info.Font, (Info.IsHidden ? Info.HiddenChar : Ch).ToString(), Info.Font.FontSize, Pos + Padding, Info.TextColor);
                }

                if (Ch == '\n')
                {
                    Pos.x = BeginPos.x;
                    Pos.y = Pos.y + FontHeight;
                    CharX = 0u;
                    CharY++;
                }
                else
                {
                    Pos.x = Pos.x + FontWidth;
                    CharX++;
                }

                Index++;
            }

            if (InSelect)
            {
                if (Info.Text[Info.Text.Length - 1] != '\n')
                {
                    SelectRects.Add(new Rect(SelectPos, new Vector2(Pos.x - SelectPos.x, FontHeight)));
                }
            }

            var ViewRect = new Rect(RenderPos, new Vector2(Info.CountX * FontWidth + 1.0f, Info.CountY * FontHeight));
            var SelectResult = Rect.Zero;
            foreach (var Rect in SelectRects)
            {
                if (!Rect.IntersectRect(Rect, ViewRect, ref SelectResult))
                    continue;

                g.DrawRectFilled(SelectResult, Info.SelectColor, 0);
            }

            if (!Info.OnlyShowText && ((g.frameCount >> 4) & 1) == 1)
            {
                if (Info.Cursor == Info.Text.Length)
                {
                    CursorPos = Pos;
                }

                if (Info.CursorWidth > 0)
                {
                    var CursorRect = new Rect(CursorPos, new Vector2(Info.CursorWidth * FontWidth, FontHeight));
                    if (ViewRect.Overlaps(CursorRect))
                    {
                        g.DrawRectFilled(CursorRect, Info.CursorColor, 0);
                    }
                }
                else if (ViewRect.Contains(CursorPos))
                {
                    g.DrawLine(CursorPos, CursorPos + new Vector2(0, FontHeight), Info.CursorColor);
                }
            }
        }


    }
}