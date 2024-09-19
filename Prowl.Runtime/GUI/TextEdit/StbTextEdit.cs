// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using System.Text;

using static Prowl.Runtime.GUI.TextEdit.StbTextEdit;

namespace Prowl.Runtime.GUI.TextEdit;

public class StbTextEditState
{
    public TextEditRow LayoutRow(int startIndex)
    {
        TextEditRow r = new();
        int? text_remaining = 0;
        Vector2? offset = null;
        Vector2 size = font.InputTextCalcTextSizeW(Text, startIndex, Text.Length, ref text_remaining, ref offset, true);
        r.x0 = 0.0f;
        r.x1 = (float)size.x;
        r.baseline_y_delta = (float)size.y;
        r.ymin = 0.0f;
        r.ymax = (float)size.y;
        if (text_remaining is not null)
            r.num_chars = text_remaining.Value - startIndex;
        return r;
    }

    public float GetWidth(int index)
    {
        var c = Text[index];
        if (c == '\n')
            return -1f;
        return font.GetCharAdvance(c) * (fontSize / (float)font.FontSize);
    }

    public float cursorAnim;
    public float ScrollX;
    public bool CursorFollow;

    public Font font;
    public float fontSize => (float)font.DisplayFontSize;

    public string Text;
    public int Length => Text?.Length ?? 0;

    public ulong ID;

    public int CursorIndex;
    public bool CursorAtEndOfLine;
    public bool HasPreferredX;
    public bool InsertMode;
    public float PreferredX;
    public int SelectEnd;
    public int SelectStart;
    public bool SingleLine;
    public UndoState UndoData = new();
}

public static class StbTextEdit
{
    private static void SortSelection(StbTextEditState state)
    {
        if (state.SelectEnd < state.SelectStart)
        {
            (state.SelectStart, state.SelectEnd) = (state.SelectEnd, state.SelectStart);
        }
    }

    private static void MoveToFirst(StbTextEditState state)
    {
        if (state.SelectStart != state.SelectEnd)
        {
            SortSelection(state);
            state.CursorIndex = state.SelectStart;
            state.SelectEnd = state.SelectStart;
            state.HasPreferredX = false;
        }
    }

    private static void PrepareSelectionAtCursor(StbTextEditState state)
    {
        if (!(state.SelectStart != state.SelectEnd))
            state.SelectStart = state.SelectEnd = state.CursorIndex;
        else
            state.CursorIndex = state.SelectEnd;
    }

    private static void MakeUndoInsert(StbTextEditState state, int where, int length)
    {
        state.UndoData.CreateUndo(where, 0, length);
    }

    public static void ClearState(StbTextEditState state, bool is_single_line)
    {
        state.UndoData.undo_point = 0;
        state.UndoData.undo_char_point = 0;
        state.UndoData.redo_point = 99;
        state.UndoData.redo_char_point = 999;
        state.SelectEnd = state.SelectStart = 0;
        state.CursorIndex = 0;
        state.HasPreferredX = false;
        state.PreferredX = 0;
        state.CursorAtEndOfLine = false;
        state.SingleLine = is_single_line;
        state.InsertMode = false;
    }

    private static void DeleteChars(StbTextEditState state, int pos, int l)
    {
        if (l == 0)
            return;

        state.Text = state.Text[..pos] + state.Text[(pos + l)..];
    }

    private static int InsertChars(StbTextEditState state, int pos, int[] codepoints, int start, int length)
    {
        var sb = new StringBuilder();
        for (int i = start; i < start + length; ++i)
        {
            sb.Append(char.ConvertFromUtf32(codepoints[i]));
        }

        InsertChars(state, pos, sb.ToString());

        return length;
    }

    private static int InsertChars(StbTextEditState state, int pos, string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;

        if (state.Text == null)
            state.Text = s;
        else
            state.Text = state.Text[..pos] + s + state.Text[pos..];

        return s.Length;
    }

    private static int InsertChar(StbTextEditState state, int pos, int codepoint)
    {
        string s = char.ConvertFromUtf32(codepoint);
        return InsertChars(state, pos, s);
    }

    private static int LocateCoord(StbTextEditState state, float x, float y)
    {
        TextEditRow r = new();
        int n = state.Length;
        float base_y = 0.0f;
        int i = 0;
        r.x0 = r.x1 = 0;
        r.ymin = r.ymax = 0;
        r.num_chars = 0;
        while (i < n)
        {
            r = state.LayoutRow(i);
            if (r.num_chars <= 0)
                return n;
            if (i == 0 && y < base_y + r.ymin)
                return 0;
            if (y < base_y + r.ymax)
                break;
            i += r.num_chars;
            base_y += r.baseline_y_delta;
        }

        if (i >= n)
            return n;
        if (x < r.x0)
            return i;
        if (x < r.x1)
        {
            var prev_x = r.x0;
            for (int k = 0; k < r.num_chars; ++k)
            {
                var w = state.GetWidth(i + k);
                if (x < prev_x + w)
                {
                    if (x < prev_x + w / 2)
                        return k + i;
                    return k + i + 1;
                }

                prev_x += w;
            }
        }

        if (state.Text[i + r.num_chars - 1] == '\n')
            return i + r.num_chars - 1;
        return i + r.num_chars;
    }

    public static void Click(StbTextEditState state, float x, float y)
    {
        if (state.SingleLine)
        {
            var r = state.LayoutRow(0);
            y = r.ymin;
        }

        state.CursorIndex = LocateCoord(state, x, y);
        state.SelectStart = state.CursorIndex;
        state.SelectEnd = state.CursorIndex;
        state.HasPreferredX = false;
    }

    public static void Drag(StbTextEditState state, float x, float y)
    {
        if (state.SingleLine)
        {
            var r = state.LayoutRow(0);
            y = r.ymin;
        }

        if (state.SelectStart == state.SelectEnd)
            state.SelectStart = state.CursorIndex;
        state.CursorIndex = state.SelectEnd = LocateCoord(state, x, y);
    }

    private static void Clamp(StbTextEditState state)
    {
        var n = state.Length;
        if (state.SelectStart != state.SelectEnd)
        {
            if (state.SelectStart > n)
                state.SelectStart = n;
            if (state.SelectEnd > n)
                state.SelectEnd = n;
            if (state.SelectStart == state.SelectEnd)
                state.CursorIndex = state.SelectStart;
        }

        if (state.CursorIndex > n)
            state.CursorIndex = n;
    }

    private static void Delete(StbTextEditState state, int where, int len)
    {
        MakeUndoDelete(state, where, len);
        DeleteChars(state, where, len);
        state.HasPreferredX = false;
    }

    public static void DeleteSelection(StbTextEditState state)
    {
        Clamp(state);
        if (state.SelectStart != state.SelectEnd)
        {
            if (state.SelectStart < state.SelectEnd)
            {
                Delete(state, state.SelectStart, state.SelectEnd - state.SelectStart);
                state.SelectEnd = state.CursorIndex = state.SelectStart;
            }
            else
            {
                Delete(state, state.SelectEnd, state.SelectStart - state.SelectEnd);
                state.SelectStart = state.CursorIndex = state.SelectEnd;
            }

            state.HasPreferredX = false;
        }
    }

    private static void MoveToLast(StbTextEditState state)
    {
        if (state.SelectStart != state.SelectEnd)
        {
            SortSelection(state);
            Clamp(state);
            state.CursorIndex = state.SelectEnd;
            state.SelectStart = state.SelectEnd;
            state.HasPreferredX = false;
        }
    }

    private static bool IsWordBoundary(StbTextEditState state, int idx) => idx <= 0 || char.IsWhiteSpace(state.Text[idx - 1]) && !char.IsWhiteSpace(state.Text[idx]);

    private static int MoveToPreviousWord(StbTextEditState state, int c)
    {
        --c;
        while (c >= 0 && !IsWordBoundary(state, c))
            --c;
        if (c < 0)
            c = 0;
        return c;
    }

    private static int MoveToNextWord(StbTextEditState state, int c)
    {
        ++c;
        while (c < state.Length && !IsWordBoundary(state, c))
            ++c;
        if (c > state.Length)
            c = state.Length;
        return c;
    }

    public static int Cut(StbTextEditState state)
    {
        if (state.SelectStart != state.SelectEnd)
        {
            DeleteSelection(state);
            state.HasPreferredX = false;
            return 1;
        }

        return 0;
    }

    public static int Paste(StbTextEditState state, string text)
    {
        Clamp(state);
        DeleteSelection(state);
        if (InsertChars(state, state.CursorIndex, text) != 0)
        {
            MakeUndoInsert(state, state.CursorIndex, text.Length);
            state.CursorIndex += text.Length;
            state.HasPreferredX = false;
            return 1;
        }

        if (state.UndoData.undo_point != 0)
            --state.UndoData.undo_point;
        return 0;
    }

    public static void InputChar(StbTextEditState state, char ch)
    {
        if (ch == '\n' && state.SingleLine)
            return;
        if (state.InsertMode && !(state.SelectStart != state.SelectEnd) && state.CursorIndex < state.Length)
        {
            MakeUndoReplace(state, state.CursorIndex, 1, 1);
            DeleteChars(state, state.CursorIndex, 1);
            if (InsertChar(state, state.CursorIndex, ch) != 0)
            {
                ++state.CursorIndex;
                state.HasPreferredX = false;
            }
        }
        else
        {
            DeleteSelection(state);
            if (InsertChar(state, state.CursorIndex, ch) != 0)
            {
                MakeUndoInsert(state, state.CursorIndex, 1);
                ++state.CursorIndex;
                state.HasPreferredX = false;
            }
        }
    }

    public static void Key(StbTextEditState state, ControlKeys key)
    {
        retry:
        switch (key)
        {
            case ControlKeys.InsertMode:
                state.InsertMode = !state.InsertMode;
                break;
            case ControlKeys.Undo:
                Undo(state);
                state.HasPreferredX = false;
                break;
            case ControlKeys.Redo:
                Redo(state);
                state.HasPreferredX = false;
                break;
            case ControlKeys.Left:
                if (state.SelectStart != state.SelectEnd)
                    MoveToFirst(state);
                else if (state.CursorIndex > 0)
                    --state.CursorIndex;
                state.HasPreferredX = false;
                break;
            case ControlKeys.Right:
                if (state.SelectStart != state.SelectEnd)
                    MoveToLast(state);
                else
                    ++state.CursorIndex;
                Clamp(state);
                state.HasPreferredX = false;
                break;
            case ControlKeys.Left | ControlKeys.Shift:
                Clamp(state);
                PrepareSelectionAtCursor(state);
                if (state.SelectEnd > 0)
                    --state.SelectEnd;
                state.CursorIndex = state.SelectEnd;
                state.HasPreferredX = false;
                break;
            case ControlKeys.WordLeft:
                if (state.SelectStart != state.SelectEnd)
                {
                    MoveToFirst(state);
                }
                else
                {
                    state.CursorIndex = MoveToPreviousWord(state, state.CursorIndex);
                    Clamp(state);
                }

                break;
            case ControlKeys.WordLeft | ControlKeys.Shift:
                if (state.SelectStart == state.SelectEnd)
                    PrepareSelectionAtCursor(state);
                state.CursorIndex = MoveToPreviousWord(state, state.CursorIndex);
                state.SelectEnd = state.CursorIndex;
                Clamp(state);
                break;
            case ControlKeys.WordRight:
                if (state.SelectStart != state.SelectEnd)
                {
                    MoveToLast(state);
                }
                else
                {
                    state.CursorIndex = MoveToNextWord(state, state.CursorIndex);
                    Clamp(state);
                }

                break;
            case ControlKeys.WordRight | ControlKeys.Shift:
                if (state.SelectStart == state.SelectEnd)
                    PrepareSelectionAtCursor(state);
                state.CursorIndex = MoveToNextWord(state, state.CursorIndex);
                state.SelectEnd = state.CursorIndex;
                Clamp(state);
                break;
            case ControlKeys.Right | ControlKeys.Shift:
                PrepareSelectionAtCursor(state);
                ++state.SelectEnd;
                Clamp(state);
                state.CursorIndex = state.SelectEnd;
                state.HasPreferredX = false;
                break;
            case ControlKeys.Down:
            case ControlKeys.Down | ControlKeys.Shift:
                {
                    var sel = (key & ControlKeys.Shift) != 0;
                    if (state.SingleLine)
                    {
                        key = ControlKeys.Right | (key & ControlKeys.Shift);
                        goto retry;
                    }

                    if (sel)
                        PrepareSelectionAtCursor(state);
                    else if (state.SelectStart != state.SelectEnd)
                        MoveToLast(state);
                    Clamp(state);
                    var find = new FindState();
                    FindCharPosition(state, ref find, state.CursorIndex, state.SingleLine);
                    if (find.length != 0)
                    {
                        var goal_x = state.HasPreferredX ? state.PreferredX : find.x;
                        var start = find.first_char + find.length;
                        state.CursorIndex = start;
                        var row = state.LayoutRow(state.CursorIndex);
                        float x = row.x0;
                        for (var i = 0; i < row.num_chars; ++i)
                        {
                            var dx = (float)1;
                            x += dx;
                            if (x > goal_x)
                                break;
                            ++state.CursorIndex;
                        }

                        Clamp(state);
                        state.HasPreferredX = true;
                        state.PreferredX = goal_x;
                        if (sel)
                            state.SelectEnd = state.CursorIndex;
                    }

                    break;
                }
            case ControlKeys.Up:
            case ControlKeys.Up | ControlKeys.Shift:
                {
                    var sel = (key & ControlKeys.Shift) != 0;
                    if (state.SingleLine)
                    {
                        key = ControlKeys.Left | (key & ControlKeys.Shift);
                        goto retry;
                    }

                    if (sel)
                        PrepareSelectionAtCursor(state);
                    else if (state.SelectStart != state.SelectEnd)
                        MoveToFirst(state);
                    Clamp(state);
                    var find = new FindState();
                    FindCharPosition(state, ref find, state.CursorIndex, state.SingleLine);
                    if (find.prev_first != find.first_char)
                    {
                        var goal_x = state.HasPreferredX ? state.PreferredX : find.x;
                        state.CursorIndex = find.prev_first;
                        var row = state.LayoutRow(state.CursorIndex);
                        float x = row.x0;
                        for (int i = 0; i < row.num_chars; ++i)
                        {
                            var dx = (float)1;
                            x += dx;
                            if (x > goal_x)
                                break;
                            ++state.CursorIndex;
                        }

                        Clamp(state);
                        state.HasPreferredX = true;
                        state.PreferredX = goal_x;
                        if (sel)
                            state.SelectEnd = state.CursorIndex;
                    }

                    break;
                }
            case ControlKeys.Delete:
            case ControlKeys.Delete | ControlKeys.Shift:
                if (state.SelectStart != state.SelectEnd)
                {
                    DeleteSelection(state);
                }
                else
                {
                    var n = state.Length;
                    if (state.CursorIndex < n)
                        Delete(state, state.CursorIndex, 1);
                }

                state.HasPreferredX = false;
                break;
            case ControlKeys.BackSpace:
            case ControlKeys.BackSpace | ControlKeys.Shift:
                if (state.SelectStart != state.SelectEnd)
                {
                    DeleteSelection(state);
                }
                else
                {
                    Clamp(state);
                    if (state.CursorIndex > 0)
                    {
                        Delete(state, state.CursorIndex - 1, 1);
                        --state.CursorIndex;
                    }
                }

                state.HasPreferredX = false;
                break;
            case ControlKeys.TextStart:
                state.CursorIndex = state.SelectStart = state.SelectEnd = 0;
                state.HasPreferredX = false;
                break;
            case ControlKeys.TextEnd:
                state.CursorIndex = state.Length;
                state.SelectStart = state.SelectEnd = 0;
                state.HasPreferredX = false;
                break;
            case ControlKeys.TextStart | ControlKeys.Shift:
                PrepareSelectionAtCursor(state);
                state.CursorIndex = state.SelectEnd = 0;
                state.HasPreferredX = false;
                break;
            case ControlKeys.TextEnd | ControlKeys.Shift:
                PrepareSelectionAtCursor(state);
                state.CursorIndex = state.SelectEnd = state.Length;
                state.HasPreferredX = false;
                break;
            case ControlKeys.LineStart:
                Clamp(state);
                MoveToFirst(state);
                if (state.SingleLine)
                    state.CursorIndex = 0;
                else
                    while (state.CursorIndex > 0 && state.Text[state.CursorIndex - 1] != '\n')
                        --state.CursorIndex;
                state.HasPreferredX = false;
                break;
            case ControlKeys.LineEnd:
                {
                    var n = state.Length;
                    Clamp(state);
                    MoveToFirst(state);
                    if (state.SingleLine)
                        state.CursorIndex = n;
                    else
                        while (state.CursorIndex < n && state.Text[state.CursorIndex] != '\n')
                            ++state.CursorIndex;
                    state.HasPreferredX = false;
                    break;
                }
            case ControlKeys.LineStart | ControlKeys.Shift:
                Clamp(state);
                PrepareSelectionAtCursor(state);
                if (state.SingleLine)
                    state.CursorIndex = 0;
                else
                    while (state.CursorIndex > 0 && state.Text[state.CursorIndex - 1] != '\n')
                        --state.CursorIndex;
                state.SelectEnd = state.CursorIndex;
                state.HasPreferredX = false;
                break;
            case ControlKeys.LineEnd | ControlKeys.Shift:
                {
                    var n = state.Length;
                    Clamp(state);
                    PrepareSelectionAtCursor(state);
                    if (state.SingleLine)
                        state.CursorIndex = n;
                    else
                        while (state.CursorIndex < n && state.Text[state.CursorIndex] != '\n')
                            ++state.CursorIndex;
                    state.SelectEnd = state.CursorIndex;
                    state.HasPreferredX = false;
                    break;
                }
        }
    }

    private static void Undo(StbTextEditState state)
    {
        var s = state.UndoData;
        if (s.undo_point == 0)
            return;
        var u = s.undo_rec[s.undo_point - 1];
        var rpos = s.redo_point - 1;
        s.undo_rec[rpos].char_storage = -1;
        s.undo_rec[rpos].insert_length = u.delete_length;
        s.undo_rec[rpos].delete_length = u.insert_length;
        s.undo_rec[rpos].where = u.where;
        if (u.delete_length != 0)
        {
            if (s.undo_char_point + u.delete_length >= 999)
            {
                s.undo_rec[rpos].insert_length = 0;
            }
            else
            {
                while (s.undo_char_point + u.delete_length > s.redo_char_point)
                {
                    if (s.redo_point == 99)
                        return;
                    s.DiscardRedo();
                }

                rpos = s.redo_point - 1;
                s.undo_rec[rpos].char_storage = s.redo_char_point - u.delete_length;
                s.redo_char_point -= u.delete_length;
                for (int i = 0; i < u.delete_length; ++i)
                    s.undo_char[s.undo_rec[rpos].char_storage + i] = (sbyte)state.Text[u.where + i];
            }

            DeleteChars(state, u.where, u.delete_length);
        }

        if (u.insert_length != 0)
        {
            InsertChars(state, u.where, s.undo_char, u.char_storage, u.insert_length);
            s.undo_char_point -= u.insert_length;
        }

        state.CursorIndex = u.where + u.insert_length;
        s.undo_point--;
        s.redo_point--;
    }

    private static void Redo(StbTextEditState state)
    {
        var s = state.UndoData;
        if (s.redo_point == 99)
            return;
        int upos = s.undo_point;
        var r = s.undo_rec[s.redo_point];
        s.undo_rec[upos].delete_length = r.insert_length;
        s.undo_rec[upos].insert_length = r.delete_length;
        s.undo_rec[upos].where = r.where;
        s.undo_rec[upos].char_storage = -1;

        var u = s.undo_rec[upos];
        if (r.delete_length != 0)
        {
            if (s.undo_char_point + u.insert_length > s.redo_char_point)
            {
                s.undo_rec[upos].insert_length = 0;
                s.undo_rec[upos].delete_length = 0;
            }
            else
            {
                s.undo_rec[upos].char_storage = s.undo_char_point;
                s.undo_char_point += u.insert_length;
                u = s.undo_rec[upos];
                for (int i = 0; i < u.insert_length; ++i)
                {
                    s.undo_char[u.char_storage + i] = state.Text[u.where + i];
                }
            }

            DeleteChars(state, r.where, r.delete_length);
        }

        if (r.insert_length != 0)
        {
            InsertChars(state, r.where, s.undo_char, r.char_storage, r.insert_length);
            s.redo_char_point += r.insert_length;
        }

        state.CursorIndex = r.where + r.insert_length;
        s.undo_point++;
        s.redo_point++;
    }

    private static void MakeUndoDelete(StbTextEditState state, int where, int length)
    {
        int i;
        var p = state.UndoData.CreateUndo(where, length, 0);
        if (p != null)
            for (i = 0; i < length; ++i)
                state.UndoData.undo_char[p.Value + i] = state.Text[where + i];
    }

    private static void MakeUndoReplace(StbTextEditState state, int where, int old_length, int new_length)
    {
        int i;
        var p = state.UndoData.CreateUndo(where, old_length, new_length);
        if (p != null)
            for (i = 0; i < old_length; ++i)
                state.UndoData.undo_char[p.Value + i] = state.Text[where + i];
    }

    private static void FindCharPosition(StbTextEditState state, ref FindState f, int n, bool single_line)
    {
        TextEditRow r;
        var prev_start = 0;
        var z = state.Length;
        var i = 0;
        int first;
        if (n == z)
        {
            if (single_line)
            {
                r = state.LayoutRow(0);
                f.y = 0;
                f.first_char = 0;
                f.length = z;
                f.height = r.ymax - r.ymin;
                f.x = r.x1;
            }
            else
            {
                f.y = 0;
                f.x = 0;
                f.height = 1;
                while (i < z)
                {
                    r = state.LayoutRow(i);
                    prev_start = i;
                    i += r.num_chars;
                }

                f.first_char = i;
                f.length = 0;
                f.prev_first = prev_start;
            }

            return;
        }

        f.y = 0;
        for (; ; )
        {
            r = state.LayoutRow(i);
            if (n < i + r.num_chars)
                break;
            prev_start = i;
            i += r.num_chars;
            f.y += r.baseline_y_delta;
        }

        f.first_char = first = i;
        f.length = r.num_chars;
        f.height = r.ymax - r.ymin;
        f.prev_first = prev_start;
        f.x = r.x0;
        for (i = 0; first + i < n; ++i) f.x += 1;
    }

    [Flags]
    public enum ControlKeys
    {
        None = 0,
        Shift = 0x20000,
        Left = 0x10000,
        Right = 0x10001,
        Up = 0x10002,
        Down = 0x10003,
        LineStart = 0x10004,
        LineEnd = 0x10005,
        TextStart = 0x10006,
        TextEnd = 0x10007,
        Delete = 0x10008,
        BackSpace = 0x10009,
        Undo = 0x1000A,
        Redo = 0x1000B,
        InsertMode = 0x1000C,
        WordLeft = 0x1000D,
        WordRight = 0x1000E,
        PageUp = 0x1000F,
        PageDown = 0x10010
    }

    public struct TextEditRow
    {
        public float x0;
        public float x1;
        public float baseline_y_delta;
        public float ymin;
        public float ymax;
        public int num_chars;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UndoRecord
    {
        public int where;
        public int insert_length;
        public int delete_length;
        public int char_storage;
    }

    public struct FindState
    {
        public float x;
        public float y;
        public float height;
        public int first_char;
        public int length;
        public int prev_first;
    }

    public class UndoState
    {
        public int redo_char_point;
        public short redo_point;
        public int[] undo_char = new int[999];
        public int undo_char_point;
        public short undo_point;
        public UndoRecord[] undo_rec = new UndoRecord[99];

        public void FlushRedo()
        {
            redo_point = 99;
            redo_char_point = 999;
        }

        public void DiscardUndo()
        {
            if (undo_point > 0)
            {
                if (undo_rec[0].char_storage >= 0)
                {
                    var n = undo_rec[0].insert_length;
                    undo_char_point -= n;

                    Array.Copy(undo_char, n, undo_char, 0, undo_char_point);
                    for (var i = 0; i < undo_point; ++i)
                        if (undo_rec[i].char_storage >= 0)
                            undo_rec[i].char_storage -= n;
                }

                --undo_point;

                Array.Copy(undo_rec, 1, undo_rec, 0, undo_point);
            }
        }

        public void DiscardRedo()
        {
            int num;
            var k = 99 - 1;
            if (redo_point <= k)
            {
                if (undo_rec[k].char_storage >= 0)
                {
                    var n = undo_rec[k].insert_length;
                    int i;
                    redo_char_point += n;
                    num = 999 - redo_char_point;

                    Array.Copy(undo_char, redo_char_point - n, undo_char, redo_char_point, num);
                    for (i = redo_point; i < k; ++i)
                        if (undo_rec[i].char_storage >= 0)
                            undo_rec[i].char_storage += n;
                }

                ++redo_point;
                num = 99 - redo_point;
                if (num != 0) Array.Copy(undo_rec, redo_point, undo_rec, redo_point - 1, num);
            }
        }

        public int? CreateUndoRecord(int numchars)
        {
            FlushRedo();
            if (undo_point == 99)
                DiscardUndo();
            if (numchars > 999)
            {
                undo_point = 0;
                undo_char_point = 0;
                return null;
            }

            while (undo_char_point + numchars > 999) DiscardUndo();
            return undo_point++;
        }

        public int? CreateUndo(int pos, int insert_len, int delete_len)
        {
            var rpos = CreateUndoRecord(insert_len);
            if (rpos == null)
                return null;

            var rposv = rpos.Value;

            undo_rec[rposv].where = pos;
            undo_rec[rposv].insert_length = (short)insert_len;
            undo_rec[rposv].delete_length = (short)delete_len;
            if (insert_len == 0)
            {
                undo_rec[rposv].char_storage = -1;
                return null;
            }

            undo_rec[rposv].char_storage = (short)undo_char_point;
            undo_char_point = (short)(undo_char_point + insert_len);
            return undo_rec[rposv].char_storage;
        }
    }
}
