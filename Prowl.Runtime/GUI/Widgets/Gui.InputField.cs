// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.GUI.TextEdit;

namespace Prowl.Runtime.GUI;

public partial class Gui
{
    // Input fields based on Dear ImGui

    [Flags]
    public enum InputFieldFlags : uint
    {
        None = 0,

        NumbersOnly = 1 << 0,
        Multiline = 1 << 1,
        AllowTab = 1 << 2,
        NoSelection = 1 << 3,
        AutoSelectAll = 1 << 4,
        EnterReturnsTrue = 1 << 5,
        OnlyDisplay = 1 << 6,
        Readonly = 1 << 7,
        NoHorizontalScroll = 1 << 8,
    }

    public struct WidgetStyle
    {
        public Color TextColor;
        public Color ActiveColor;
        public Color HoveredColor;
        public Color BGColor;
        public Color BorderColor;
        public float BorderThickness;
        public float Roundness;
        public AssetRef<Font> Font;
        public float FontSize;
        public float ItemSize;

        public WidgetStyle(float itemSize)
        {
            ItemSize = itemSize;
            TextColor = Color.white;
            ActiveColor = new(84, 21, 241);
            HoveredColor = new Color(255, 255, 255) * 0.8f;
            BGColor = new(31, 33, 40);
            BorderColor = new(49, 52, 66);
            BorderThickness = 1;
            Roundness = 5;
            Font = Runtime.Font.DefaultFont;
            FontSize = 20;
        }
    }

    public bool InputField(string ID, ref string value, uint maxLength, InputFieldFlags flags, Offset x, Offset y, Size width, Size? height = null, WidgetStyle? inputstyle = null, bool invisible = false)
    {
        var style = inputstyle ?? new WidgetStyle(30);
        var g = ActiveGUI;
        bool multiline = ((flags & InputFieldFlags.Multiline) == InputFieldFlags.Multiline);
        Size h = (multiline ? style.FontSize * 8 : style.ItemSize);
        if (height != null) h = height.Value;
        using (g.Node(ID).Left(x).Top(y).Width(width).Height(h).Padding(5).Enter())
        {
            Interactable interact = g.GetInteractable();

            if (!invisible)
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, style.BGColor, style.Roundness);
                if (style.BorderThickness > 0)
                    g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, style.BorderColor, style.BorderThickness, style.Roundness);
            }

            interact.TakeFocus();

            g.Draw2D.PushClip(g.CurrentNode.LayoutData.InnerRect);
            var ValueChanged = false;
            if (g.FocusID == interact.ID || g.ActiveID == interact.ID)
            {
                ValueChanged = OnProcess(style, interact, ref value, maxLength, flags);
            }
            else
            {
                if (stb == null || stb.ID == interact.ID)
                {
                    // Were not focused but stb still is set, reset it
                    stb = null;
                }

                //OnProcess(style, interact, ref value, maxLength, flags | InputFieldFlags.OnlyDisplay);
                var font = style.Font.IsAvailable ? style.Font.Res : Font.DefaultFont;
                var fontsize = style.FontSize;
                var render_pos = new Vector2(g.CurrentNode.LayoutData.InnerRect.x, g.CurrentNode.LayoutData.InnerRect.y);
                // Center text vertically
                //render_pos.y += (g.CurrentNode.LayoutData.InnerRect.height - fontsize) / 2;
                render_pos.y += 3;
                render_pos.x += 5;

                if (multiline)
                    render_pos.y -= g.CurrentNode.VScroll;

                Color32 colb = style.TextColor;
                g.Draw2D.DrawList.AddText(font, fontsize, render_pos, colb, value, 0, value.Length, 0.0f, null);
            }
            g.Draw2D.PopClip();

            if (multiline)
            {
                Vector2 textSize = (style.Font.IsAvailable ? style.Font.Res : Font.DefaultFont).CalcTextSize(value, 0, g.CurrentNode.LayoutData.InnerRect.width);
                // Dummy node to update ContentRect
                g.Node(ID).Width(textSize.x).Height(textSize.y).IgnoreLayout();
                g.CurrentNode.Scroll();
            }

            return ValueChanged;
        }

    }

    static int ImStrbolW(string data, int bufMidLine, int bufBegin)
    {
        while (bufMidLine > bufBegin && data[bufMidLine - 1] != '\n')
        {
            bufMidLine--;
        }
        return bufMidLine;
    }

    private static StbTextEditState stb;

    internal static bool OnProcess(WidgetStyle style, Interactable interact, ref string Text, uint MaxLength, InputFieldFlags Flags)
    {
        var g = ActiveGUI;
        var font = style.Font.IsAvailable ? style.Font.Res : Font.DefaultFont;
        var fontsize = style.FontSize;
        var render_pos = new Vector2(g.CurrentNode.LayoutData.InnerRect.x, g.CurrentNode.LayoutData.InnerRect.y);
        // Center text vertically
        //render_pos.y += (g.CurrentNode.LayoutData.InnerRect.height - fontsize) / 2;
        render_pos.y += 3;
        render_pos.x += 5;

        bool justSelected = false;
        if (stb == null || stb.ID != interact.ID)
        {
            justSelected = true;
            stb = new();
            stb.ID = interact.ID;
            stb.SingleLine = !((Flags & InputFieldFlags.Multiline) == InputFieldFlags.Multiline);
            stb.font = font;
            stb.Text = Text;
        }

        HandleKeyEvent(stb, MaxLength, Flags);
        HandleMouseEvent(stb);

        if (justSelected && (Flags & InputFieldFlags.AutoSelectAll) == InputFieldFlags.AutoSelectAll)
        {
            stb.SelectStart = 0;
            stb.SelectEnd = Text.Length;
        }

        if (g.IsNodeHovered() && g.IsPointerDoubleClick(MouseButton.Left))
        {
            stb.SelectStart = 0;
            stb.SelectEnd = Text.Length;
        }

        //g.DrawText(font, Text, fontsize, render_pos, Color.black);

        // Render
        Rect clip_rect = g.CurrentNode.LayoutData.InnerRect;
        Vector2 text_size = new Vector2(0f, 0f);
        stb.cursorAnim += Time.deltaTimeF;
        bool is_multiline = !stb.SingleLine;
        Vector2 size = new Vector2(g.CurrentNode.LayoutData.InnerRect.width, g.CurrentNode.LayoutData.InnerRect.height);

        // We need to:
        // - Display the text (this can be more easily clipped)
        // - Handle scrolling, highlight selection, display cursor (those all requires some form of 1d.2d cursor position calculation)
        // - Measure text height (for scrollbar)
        // We are attempting to do most of that in **one main pass** to minimize the computation cost (non-negligible for large amount of text) + 2nd pass for selection rendering (we could merge them by an extra refactoring effort)
        int text_begin = 0;
        Vector2 cursor_offset = Vector2.zero, select_start_offset = Vector2.zero;

        {
            // Count lines + find lines numbers straddling 'cursor' and 'select_start' position.
            int[] searches_input_ptr = new int[2];
            searches_input_ptr[0] = text_begin + stb.CursorIndex;
            searches_input_ptr[1] = -1;
            int searches_remaining = 1;
            int[] searches_result_line_number = [-1, -999];
            if (stb.SelectStart != stb.SelectEnd)
            {
                searches_input_ptr[1] = text_begin + MathD.Min(stb.SelectStart, stb.SelectEnd);
                searches_result_line_number[1] = -1;
                searches_remaining++;
            }

            // Iterate all lines to find our line numbers
            // In multi-line mode, we never exit the loop until all lines are counted, so add one extra to the searches_remaining counter.
            searches_remaining += is_multiline ? 1 : 0;
            int line_count = 0;
            for (int s = text_begin; s < stb.Text.Length && stb.Text[s] != 0; s++)
                if (stb.Text[s] == '\n')
                {
                    line_count++;
                    if (searches_result_line_number[0] == -1 && s >= searches_input_ptr[0]) { searches_result_line_number[0] = line_count; if (--searches_remaining <= 0) break; }
                    if (searches_result_line_number[1] == -1 && s >= searches_input_ptr[1]) { searches_result_line_number[1] = line_count; if (--searches_remaining <= 0) break; }
                }
            line_count++;
            if (searches_result_line_number[0] == -1) searches_result_line_number[0] = line_count;
            if (searches_result_line_number[1] == -1) searches_result_line_number[1] = line_count;

            int? remaining = null;
            Vector2? out_offset = null;
            // Calculate 2d position by finding the beginning of the line and measuring distance
            cursor_offset.x = font.InputTextCalcTextSizeW(stb.Text, ImStrbolW(stb.Text, searches_input_ptr[0], text_begin), searches_input_ptr[0], ref remaining, ref out_offset).x;
            cursor_offset.y = searches_result_line_number[0] * fontsize;
            if (searches_result_line_number[1] >= 0)
            {
                select_start_offset.x = font.InputTextCalcTextSizeW(stb.Text, ImStrbolW(stb.Text, searches_input_ptr[1], text_begin), searches_input_ptr[1], ref remaining, ref out_offset).x;
                select_start_offset.y = searches_result_line_number[1] * fontsize;
            }

            // Calculate text height
            if (is_multiline)
                text_size = new Vector2(size.x, line_count * fontsize);
        }

        // Scroll
        if (stb.CursorFollow)
        {
            // Horizontal scroll in chunks of quarter width
            if ((Flags & InputFieldFlags.NoHorizontalScroll) == 0)
            {
                double scroll_increment_x = size.x * 0.25f;
                if (cursor_offset.x < stb.ScrollX)
                    stb.ScrollX = (int)MathD.Max(0.0f, cursor_offset.x - scroll_increment_x);
                else if (cursor_offset.x - size.x >= stb.ScrollX)
                    stb.ScrollX = (int)(cursor_offset.x - size.x + scroll_increment_x);
            }
            else
            {
                stb.ScrollX = 0.0f;
            }

            // Vertical scroll
            if (is_multiline)
            {
                double scroll_y = g.CurrentNode.VScroll;
                if (cursor_offset.y - fontsize < scroll_y)
                    scroll_y = MathD.Max(0.0f, cursor_offset.y - fontsize);
                else if (cursor_offset.y - size.y >= scroll_y)
                    scroll_y = cursor_offset.y - size.y;
                g.SetNodeStorage("VScroll", scroll_y);
            }
        }
        stb.CursorFollow = false;
        if (is_multiline)
            render_pos.y -= g.CurrentNode.VScroll;
        Vector2 render_scroll = new Vector2(stb.ScrollX, 0.0f);

        if ((Flags & InputFieldFlags.OnlyDisplay) == InputFieldFlags.OnlyDisplay)
        {
            Color32 colb = style.TextColor;
            g.Draw2D.DrawList.AddText(font, fontsize, render_pos - render_scroll, colb, stb.Text, 0, stb.Text.Length, 0.0f, (is_multiline ? null : (Vector4?)clip_rect));
            return false;
        }

        // Draw selection
        if (stb.SelectStart != stb.SelectEnd)
        {
            int text_selected_begin = text_begin + MathD.Min(stb.SelectStart, stb.SelectEnd);
            int text_selected_end = text_begin + MathD.Max(stb.SelectStart, stb.SelectEnd);

            float bg_offy_up = is_multiline ? 0.0f : -1.0f;    // FIXME: those offsets should be part of the style? they don't play so well with multi-line selection.
            float bg_offy_dn = is_multiline ? 0.0f : 2.0f;
            Color32 bg_color = style.ActiveColor;
            Vector2 rect_pos = render_pos + select_start_offset - render_scroll;
            for (int p = text_selected_begin; p < text_selected_end;)
            {
                if (rect_pos.y > clip_rect.y + clip_rect.height + fontsize)
                    break;
                if (rect_pos.y < clip_rect.y)
                {
                    while (p < text_selected_end)
                        if (Text[p++] == '\n') //TODO: what should we access here?
                            break;
                }
                else
                {
                    var temp = (int?)p;
                    Vector2? out_offset = null;
                    Vector2 rect_size = font.InputTextCalcTextSizeW(Text, p, text_selected_end, ref temp, ref out_offset, true); p = temp!.Value;
                    if (rect_size.x <= 0.0f) rect_size.x = (int)(font.GetCharAdvance(' ') * 0.50f); // So we can see selected empty lines
                    Rect rect = new Rect(rect_pos + new Vector2(0.0f, bg_offy_up - fontsize), new Vector2(rect_size.x, bg_offy_dn + fontsize));
                    rect.Clip(clip_rect);
                    if (rect.Overlaps(clip_rect))
                        g.Draw2D.DrawList.AddRectFilled(rect.Min, rect.Max, bg_color);
                }
                rect_pos.x = render_pos.x - render_scroll.x;
                rect_pos.y += fontsize;
            }
        }


        Color32 col = style.TextColor;
        g.Draw2D.DrawList.AddText(font, fontsize, render_pos - render_scroll, col, stb.Text, 0, stb.Text.Length, 0.0f, (is_multiline ? null : (Vector4?)clip_rect));
        //g.DrawText(font, fontsize, Text, render_pos - render_scroll, Color.black, 0, stb.CurLenA, 0.0f, (is_multiline ? null : (ImVec4?)clip_rect));

        // Draw blinking cursor
        Vector2 cursor_screen_pos = render_pos + cursor_offset - render_scroll;
        bool cursor_is_visible = (stb.cursorAnim <= 0.0f) || (stb.cursorAnim % 1.20f) <= 0.80f;
        if (cursor_is_visible)
            g.Draw2D.DrawList.AddLine(cursor_screen_pos + new Vector2(0.0f, -fontsize - 4f), cursor_screen_pos + new Vector2(0.0f, -5f), col);


        if ((Flags & InputFieldFlags.EnterReturnsTrue) == InputFieldFlags.EnterReturnsTrue)
        {
            Text = stb.Text;
            if (g.IsKeyPressed(Key.Return))
            {
                g.FocusID = 0;
                return true;
            }
            return false;
        }
        else
        {
            if (!is_multiline && g.IsKeyPressed(Key.Return))
                g.FocusID = 0;

            var oldText = Text;
            Text = stb.Text;
            return oldText != Text;
        }
    }

    private static void HandleKeyEvent(StbTextEditState stb, uint MaxLength, InputFieldFlags Flags)
    {
        var g = ActiveGUI;
        var KeyCode = g.KeyCode;
        if (KeyCode == Key.Unknown)
        {
            return;
        }

        if (!g.IsKeyPressed(KeyCode))
        {
            return;
        }

        StbTextEdit.ControlKeys? stb_key = null;
        var Ctrl = g.IsKeyDown(Key.LeftControl);
        var Shift = g.IsKeyDown(Key.LeftShift);
        var Alt = g.IsKeyDown(Key.LeftAlt);
        bool NoSelection = (Flags & InputFieldFlags.NoSelection) == InputFieldFlags.NoSelection;
        bool IsEditable = !((Flags & InputFieldFlags.Readonly) == InputFieldFlags.Readonly);
        bool Multiline = (Flags & InputFieldFlags.Multiline) == InputFieldFlags.Multiline;

        switch (KeyCode)
        {
            case Key.Tab:
                if ((Flags & InputFieldFlags.AllowTab) == InputFieldFlags.AllowTab)
                {
                    OnTextInput(stb, "\t", MaxLength, Flags);
                }
                //else Focus Next Focusable Interactable
                break;
            case Key.A when Ctrl && !NoSelection:
                stb.SelectStart = 0;
                stb.SelectEnd = stb.Text.Length;
                break;
            case Key.Escape:
                stb.SelectStart = 0;
                stb.SelectEnd = 0;
                break;

            case Key.Insert when IsEditable:
                stb_key = StbTextEdit.ControlKeys.InsertMode;
                break;
            case Key.C when Ctrl && !NoSelection:
                int selectStart = Math.Min(stb.SelectStart, stb.SelectEnd);
                int selectEnd = Math.Max(stb.SelectStart, stb.SelectEnd);

                if (selectStart < selectEnd)
                {
                    Input.Clipboard = stb.Text.Substring(selectStart, selectEnd - selectStart);
                }

                break;
            case Key.X when Ctrl && !NoSelection:
                selectStart = Math.Min(stb.SelectStart, stb.SelectEnd);
                selectEnd = Math.Max(stb.SelectStart, stb.SelectEnd);

                if (selectStart < selectEnd)
                {
                    Input.Clipboard = stb.Text.Substring(selectStart, selectEnd - selectStart);
                    if (IsEditable)
                        StbTextEdit.Cut(stb);
                }

                break;
            case Key.V when Ctrl && IsEditable:
                OnTextInput(stb, Input.Clipboard, MaxLength, Flags);
                break;
            case Key.Z when Ctrl && IsEditable:
                stb_key = StbTextEdit.ControlKeys.Undo;
                break;
            case Key.Y when Ctrl && IsEditable:
                stb_key = StbTextEdit.ControlKeys.Redo;
                break;
            case Key.Left:
                if (Ctrl && Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.WordLeft;
                }
                else if (Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.Left;
                }
                else if (Ctrl)
                    stb_key = StbTextEdit.ControlKeys.WordLeft;
                else
                    stb_key = StbTextEdit.ControlKeys.Left;
                stb.CursorFollow = true;
                break;
            case Key.Right:
                if (Ctrl && Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.WordRight;
                }
                else if (Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.Right;
                }
                else if (Ctrl)
                    stb_key = StbTextEdit.ControlKeys.WordRight;
                else
                    stb_key = StbTextEdit.ControlKeys.Right;
                break;
            case Key.Up:
                stb_key = StbTextEdit.ControlKeys.Up;
                if (Shift && !NoSelection) stb_key |= StbTextEdit.ControlKeys.Shift;
                break;
            case Key.Down:
                stb_key = StbTextEdit.ControlKeys.Down;
                if (Shift && !NoSelection) stb_key |= StbTextEdit.ControlKeys.Shift;
                break;
            case Key.Backspace when IsEditable:
                stb_key = StbTextEdit.ControlKeys.BackSpace;
                if (Shift && !NoSelection) stb_key |= StbTextEdit.ControlKeys.Shift;
                break;
            case Key.Delete when IsEditable:
                stb_key = StbTextEdit.ControlKeys.Delete;
                if (Shift && !NoSelection) stb_key |= StbTextEdit.ControlKeys.Shift;
                break;
            case Key.Home:
                if (Ctrl && Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.TextStart;
                }
                else if (Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.LineStart;
                }
                else if (Ctrl)
                    stb_key = StbTextEdit.ControlKeys.TextStart;
                else
                    stb_key = StbTextEdit.ControlKeys.LineStart;
                break;
            case Key.End:
                if (Ctrl && Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.TextEnd;
                }
                else if (Shift)
                {
                    if (!NoSelection)
                        stb_key = StbTextEdit.ControlKeys.Shift | StbTextEdit.ControlKeys.LineEnd;
                }
                else if (Ctrl)
                    stb_key = StbTextEdit.ControlKeys.TextEnd;
                else
                    stb_key = StbTextEdit.ControlKeys.LineEnd;
                break;
            case Key.KeypadEnter when IsEditable && Multiline:
            case Key.Return when IsEditable && Multiline:
                OnTextInput(stb, "\n", MaxLength, Flags);
                break;
        }

        if (stb_key != null)
        {
            stb.CursorFollow = true;
            StbTextEdit.Key(stb, stb_key.Value);
        }

        if (Input.InputString.Count > 0)
        {
            for (int i = 0; i < Input.InputString.Count; i++)
                OnTextInput(stb, Input.InputString[i].ToString(), MaxLength, Flags);
        }
    }

    protected static bool OnTextInput(StbTextEditState stb, string c, uint MaxLength, InputFieldFlags Flags)
    {
        bool IsEditable = !((Flags & InputFieldFlags.Readonly) == InputFieldFlags.Readonly);
        if (c == null || !IsEditable)
            return false;

        if (stb.SelectStart != stb.SelectEnd)
        {
            StbTextEdit.DeleteSelection(stb);
        }

        int count;

        if (MaxLength >= 0)
        {
            var remains = MaxLength - stb.Length;
            if (remains <= 0)
                return false;

            count = (int)Math.Min(remains, c.Length);
        }
        else
        {
            count = c.Length;
        }

        bool NumbersOnly = (Flags & InputFieldFlags.NumbersOnly) == InputFieldFlags.NumbersOnly;
        for (int i = 0; i < count; i++)
        {
            if ((NumbersOnly && !char.IsNumber(c[i])) || c[i] == '\r')
                if (c[i] != '.' && c[i] != '-')
                    continue;

            StbTextEdit.InputChar(stb, c[i]);
            stb.CursorFollow = true;
        }

        return true;
    }

    private static void HandleMouseEvent(StbTextEditState stb)
    {
        var g = ActiveGUI;
        var Pos = g.PointerPos - g.CurrentNode.LayoutData.InnerRect.Position;
        Pos.x -= 5; // Account for padding in text rendering
        Pos.x += stb.ScrollX;
        Pos.y += g.CurrentNode.VScroll;
        if (g.IsPointerClick(MouseButton.Left))
        {
            StbTextEdit.Click(stb, (float)Pos.x, (float)Pos.y);
            stb.cursorAnim = 0f;
        }
        if (g.IsPointerDown(MouseButton.Left) && g.IsPointerMoving)
        {
            StbTextEdit.Drag(stb, (float)Pos.x, (float)Pos.y);
            stb.cursorAnim = 0f;
            stb.CursorFollow = true;
        }
    }


}
