// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    private enum DragKind { None, Text, Lines, Box, VScroll, HScroll, Minimap, Move }

    // Dragging a selection to move or copy it.
    private TextRange _moveRange;
    private TextPosition _movePress, _moveDrop;
    private bool _moveMoved;

    private Paper? _paper;
    private ElementHandle _handle;
    private float _frameDelta;
    private bool _wantFocus;

    private DragKind _drag;
    private CodeSelection _dragOrigin;
    private int _clickCount;
    private double _lastClickTime = -10;
    private Float2 _lastClickPos;
    private float _dragGrab;

    private bool _pointerInGutter, _vbarHover, _hbarHover, _minimapHover;
    private int _hoverFoldLine = -1, _hoverGlyphLine = -1;
    private Float2 _pointer;
    private bool _pointerInside;
    private bool _chordK;

    private TextRange? _linkRange;

    private Action<ClickEvent>? _onPress;
    private Action<DragEvent>? _onDragging;
    private Action<ClickEvent>? _onRelease;
    private Action<ScrollEvent>? _onScroll;
    private Action<KeyEvent>? _onKey;
    private Action<TextInputEvent>? _onText;
    private Action<FocusEvent>? _onFocus;
    private Action<ClickEvent>? _onRightClick;
    private Action<Prowl.Quill.Canvas, Rect>? _onDraw;

    /// <summary>Gives the editor keyboard focus the next time it is drawn.</summary>
    public void Focus() => _wantFocus = true;

    /// <summary>Builds the editor element for this frame. Called by <see cref="CodeEditorBuilder.Show"/>.</summary>
    internal void Build(Paper paper, string id, UnitValue width, UnitValue height)
    {
        _paper = paper;
        _frameDelta = paper.DeltaTime;
        _time += paper.DeltaTime;
        if (_fontSize < 0) _fontSize = Options.FontSize;

        PumpUiQueue();
        TickAnalysis();

        _onPress ??= HandlePress;
        _onDragging ??= HandleDragging;
        _onRelease ??= HandleRelease;
        _onScroll ??= HandleScroll;
        _onKey ??= HandleKey;
        _onText ??= HandleText;
        _onFocus ??= HandleFocus;
        _onRightClick ??= HandleRightClick;
        _onDraw ??= DrawEditor;

        var element = paper.Box(id)
            .Width(width).Height(height)
            .Clip()
            .Cursor(CurrentCursor(paper))
            .OnPress(_onPress)
            .OnDragging(_onDragging)
            .OnRelease(_onRelease)
            .OnScroll(_onScroll)
            .OnKeyPressed(_onKey)
            .OnTextInput(_onText)
            .OnFocusChange(_onFocus)
            .OnRightClick(_onRightClick);

        using (element.Enter())
        {
            _handle = paper.CurrentParent;
            if (_wantFocus)
            {
                _wantFocus = false;
                paper.SetFocus(_handle);
            }

            bool focused = paper.IsElementFocused(_handle.Data.ID);
            IsFocused = focused || IsPopupFocused(paper);
            if (focused)
            {
                paper.SkipKeyboardNavigation = true;
                paper.CaptureKeyboard();
            }

            TrackPointer(paper);
            TickDragAutoScroll(paper);
            TickHover(paper);
            TickOccurrences();

            paper.Draw(_onDraw);
            BuildPopups(paper, id);
        }
    }

    private PaperCursor CurrentCursor(Paper paper)
    {
        if (!_view.Valid) return PaperCursor.Text;
        if (_drag == DragKind.Text || _drag == DragKind.Box) return PaperCursor.Text;
        if (_drag != DragKind.None) return PaperCursor.Default;
        var p = _pointer;
        if (_linkRange != null || CodeLensAt(_pointer) != null) return PaperCursor.Pointer;
        if (p.X < _view.X + _view.GutterW || p.X >= _view.MinimapX || p.Y >= _view.StatusY) return PaperCursor.Default;
        return PaperCursor.Text;
    }

    private void TrackPointer(Paper paper)
    {
        var v = _view;
        _pointerInside = v.Valid && paper.IsElementHovered(_handle.Data.ID) && _pointer.X >= v.X && _pointer.Y >= v.Y && _pointer.X < v.X + v.W && _pointer.Y < v.Y + v.H;

        _pointerInGutter = _pointerInside && _pointer.X < v.X + v.GutterW && _pointer.Y < v.StatusY;
        _vbarHover = _pointerInside && _pointer.X >= v.VBarX && _pointer.Y < v.StatusY;
        _minimapHover = _pointerInside && v.MinimapW > 0 && _pointer.X >= v.MinimapX && _pointer.X < v.VBarX && _pointer.Y < v.StatusY;
        _hbarHover = _pointerInside && MaxScrollX() > 0 && _pointer.Y >= v.HBarY && _pointer.Y < v.StatusY && _pointer.X >= v.TextX && _pointer.X < v.MinimapX;

        _hoverFoldLine = -1;
        _hoverGlyphLine = -1;
        if (_pointerInGutter)
        {
            int line = LineAtY(_pointer.Y);
            if (line >= 0)
            {
                if (_pointer.X >= v.FoldX && TryGetFoldAt(line, out _)) _hoverFoldLine = line;
                if (Options.ShowGlyphMargin && _pointer.X < v.GlyphX + v.GlyphW) _hoverGlyphLine = line;
            }
        }

        bool goToKey = GoToDefinitionKey(paper);
        _linkRange = null;
        if (goToKey && _pointerInside && InTextArea(_pointer) && Session != null && _drag == DragKind.None)
        {
            var pos = PositionFromPoint(_pointer, false);
            var word = WordAt(pos);
            if (!word.IsEmpty && IsCodeAt(word.Start.Line, word.Start.Column)) _linkRange = word;
        }
    }

    private int LineAtY(float y)
    {
        double yy = y - _view.TextY + _scrollY;
        int row = (int)Math.Floor(yy / _view.LineH);
        if (row < 0 || row >= DisplayRowCount) return -1;
        return DisplayRowToLine(row);
    }

    private bool InTextArea(Float2 p)
    {
        var v = _view;
        return p.X >= v.X + v.GutterW && p.X < v.MinimapX && p.Y >= v.TextY && p.Y < v.TextY + v.TextH && !(MaxScrollX() > 0 && p.Y >= v.HBarY);
    }

    private static bool Ctrl(Paper p) => p.IsKeyDown(PaperKey.LeftControl) || p.IsKeyDown(PaperKey.RightControl);
    private static bool Shift(Paper p) => p.IsKeyDown(PaperKey.LeftShift) || p.IsKeyDown(PaperKey.RightShift);
    private static bool Alt(Paper p) => p.IsKeyDown(PaperKey.LeftAlt) || p.IsKeyDown(PaperKey.RightAlt);
    private bool AddCaretKey(Paper p) => Options.MultiCaretModifier == MultiCaretModifier.Ctrl ? Ctrl(p) : Alt(p);
    private bool GoToDefinitionKey(Paper p) => Options.MultiCaretModifier == MultiCaretModifier.Ctrl ? Alt(p) : Ctrl(p);

    #region Mouse

    private void HandleFocus(FocusEvent e)
    {
        if (!e.IsFocused) _drag = DragKind.None;
        _caretMoveTime = _time;
    }

    private void HandlePress(ClickEvent e)
    {
        var paper = _paper!;
        var v = _view;
        if (!v.Valid || e.Button != PaperMouseBtn.Left) return;
        var p = e.LocalPosition;
        CloseHover();

        if (p.Y >= v.StatusY) return;

        if (p.X >= v.VBarX)
        {
            var (ty, th) = VerticalThumb();
            if (p.Y < ty || p.Y > ty + th) ScrollThumbTo(p.Y - th * 0.5f, th);
            (ty, _) = VerticalThumb();
            _dragGrab = p.Y - ty;
            _drag = DragKind.VScroll;
            return;
        }

        if (v.MinimapW > 0 && p.X >= v.MinimapX)
        {
            _drag = DragKind.Minimap;
            MinimapScrollTo(p.Y);
            return;
        }

        if (MaxScrollX() > 0 && p.Y >= v.HBarY && p.X >= v.TextX && p.X < v.MinimapX)
        {
            var (tx, tw) = HorizontalThumb();
            if (p.X < tx || p.X > tx + tw) HScrollThumbTo(p.X - tw * 0.5f, tw);
            (tx, _) = HorizontalThumb();
            _dragGrab = p.X - tx;
            _drag = DragKind.HScroll;
            return;
        }

        ClosePopups();
        if (CodeLensAt(p) is { } lens)
        {
            SetCaret(lens);
            FindReferences();
            return;
        }
        bool shift = Shift(paper), add = AddCaretKey(paper), goTo = GoToDefinitionKey(paper);

        if (p.X < v.X + v.GutterW)
        {
            int line = LineAtY(p.Y);
            if (line < 0) return;
            if (Options.ShowGlyphMargin && p.X < v.GlyphX + v.GlyphW)
            {
                GlyphMarginClicked?.Invoke(line);
                return;
            }
            if (Options.ShowFolding && p.X >= v.FoldX && TryGetFoldAt(line, out _))
            {
                ToggleFold(line);
                return;
            }
            var start = new TextPosition(line, 0);
            var anchor = shift ? Selection.Anchor : start;
            Select(anchor, LineEndOrNext(line));
            _dragOrigin = new CodeSelection(start, LineEndOrNext(line));
            _drag = DragKind.Lines;
            return;
        }

        double now = _time;
        if (now - _lastClickTime < 0.4 && Float2.Length(p - _lastClickPos) < 4) _clickCount++;
        else _clickCount = 1;
        _lastClickTime = now;
        _lastClickPos = p;

        var pos = PositionFromPoint(p);

        int clickedLine = pos.Line;
        if (TryGetCollapsed(clickedLine, out var fold) && pos.Column >= Document.GetLineLength(clickedLine))
        {
            float endX = ColX(VisualColumn(clickedLine, Document.GetLineLength(clickedLine))) + v.CharW;
            if (p.X >= endX)
            {
                Unfold(fold.StartLine);
                return;
            }
        }

        if (goTo && !add && Session != null && _clickCount == 1)
        {
            SetCaret(pos);
            GoToDefinition();
            return;
        }

        if (add && shift)
        {
            _dragOrigin = new CodeSelection(Caret);
            _drag = DragKind.Box;
            BoxSelect(_dragOrigin.Caret, pos);
            return;
        }

        if (_clickCount == 2)
        {
            var w = WordAt(pos);
            if (w.IsEmpty) w = new TextRange(pos, pos);
            ReplacePrimary(new CodeSelection(w.Start, w.End), add);
            _dragOrigin = new CodeSelection(w.Start, w.End);
            _drag = DragKind.Text;
            return;
        }

        if (_clickCount >= 3)
        {
            var sel = new CodeSelection(new TextPosition(pos.Line, 0), LineEndOrNext(pos.Line));
            ReplacePrimary(sel, add);
            _dragOrigin = sel;
            _drag = DragKind.Lines;
            return;
        }

        if (Options.DragAndDropText && !shift && !add && !Options.ReadOnly && SelectionContaining(pos) is { } moving)
        {
            _moveRange = moving;
            _movePress = _moveDrop = pos;
            _moveMoved = false;
            _drag = DragKind.Move;
            return;
        }

        if (shift) ReplacePrimary(new CodeSelection(Selection.Anchor, pos), false);
        else if (add) AddSelection(new CodeSelection(pos));
        else SetCaret(pos);

        _dragOrigin = new CodeSelection(shift ? Selection.Anchor : pos, pos);
        _drag = DragKind.Text;
    }

    private TextRange? SelectionContaining(TextPosition pos)
    {
        foreach (var s in _selections)
            if (!s.IsEmpty && s.Start <= pos && pos < s.End) return s.Range;
        return null;
    }

    // Moves the text to drop, or copies it there, as one undo step, and selects it in its new place.
    private void MoveText(TextRange range, TextPosition drop, bool copy)
    {
        string text = Document.GetText(range);
        int dropOffset = Document.PositionToOffset(drop);
        int start = Document.PositionToOffset(range.Start), end = Document.PositionToOffset(range.End);
        Edit(CodeEditKind.Other, () =>
        {
            if (!copy)
            {
                Document.Delete(range);
                if (dropOffset > end) dropOffset -= end - start;
            }
            var at = Document.OffsetToPosition(dropOffset);
            var change = Document.Insert(at, text);
            _selections.Clear();
            _selections.Add(new CodeSelection(at, change.NewEnd));
            _primary = 0;
        });
    }

    private TextPosition LineEndOrNext(int line)
        => line + 1 < Document.LineCount ? new TextPosition(line + 1, 0) : new TextPosition(line, Document.GetLineLength(line));

    private void ReplacePrimary(CodeSelection selection, bool add)
    {
        if (add) { AddSelection(selection); return; }
        Select(selection.Anchor, selection.Caret);
    }

    private void HandleDragging(DragEvent e) => UpdateDrag(e.LocalPosition);

    private void UpdateDrag(Float2 p)
    {
        var v = _view;
        switch (_drag)
        {
            case DragKind.VScroll:
                ScrollThumbTo(p.Y - _dragGrab, VerticalThumb().H);
                break;
            case DragKind.HScroll:
                HScrollThumbTo(p.X - _dragGrab, HorizontalThumb().W);
                break;
            case DragKind.Minimap:
                MinimapScrollTo(p.Y);
                break;
            case DragKind.Text:
            {
                var pos = PositionFromPoint(p);
                var o = _dragOrigin;
                CodeSelection sel;
                if (_clickCount == 2)
                {
                    var w = WordAt(pos);
                    sel = pos < o.Start ? new CodeSelection(o.End, w.IsEmpty ? pos : w.Start) : new CodeSelection(o.Start, w.IsEmpty ? pos : w.End);
                }
                else sel = new CodeSelection(o.Anchor, pos);
                SetPrimaryDuringDrag(sel);
                break;
            }
            case DragKind.Lines:
            {
                int line = LineAtY(Math.Clamp(p.Y, v.TextY, v.TextY + v.TextH - 1));
                if (line < 0) line = p.Y < v.TextY ? 0 : Document.LineCount - 1;
                var o = _dragOrigin;
                var sel = line < o.Start.Line ? new CodeSelection(o.End, new TextPosition(line, 0)) : new CodeSelection(o.Start, LineEndOrNext(line));
                SetPrimaryDuringDrag(sel);
                break;
            }
            case DragKind.Box:
                BoxSelect(_dragOrigin.Caret, PositionFromPoint(p));
                break;
            case DragKind.Move:
                _moveDrop = PositionFromPoint(p);
                _moveMoved |= _moveDrop != _movePress;
                break;
        }
    }

    private void SetPrimaryDuringDrag(CodeSelection sel)
    {
        _selections[_primary] = new CodeSelection(Document.Clamp(sel.Anchor), Document.Clamp(sel.Caret));
        _caretMoveTime = _time;
        _revealCaret = true;
    }

    // Alt+Shift drag: one selection per row, spanning the same screen columns.
    private void BoxSelect(TextPosition from, TextPosition to)
    {
        int c0 = VisualColumn(from.Line, from.Column);
        float x = (_pointer.X - _view.TextX + _scrollX) / _view.CharW;
        int c1 = Math.Max(0, (int)MathF.Round(x));
        int r0 = LineToRow(from.Line), r1 = LineToRow(to.Line);
        int step = r1 >= r0 ? 1 : -1;

        _selections.Clear();
        for (int row = r0; ; row += step)
        {
            int line = RowToLine(row);
            int a = ColumnFromVisual(line, c0), b = ColumnFromVisual(line, c1);
            _selections.Add(new CodeSelection(new TextPosition(line, a), new TextPosition(line, b)));
            if (row == r1) break;
        }
        _primary = _selections.Count - 1;
        _caretMoveTime = _time;
    }

    private void HandleRelease(ClickEvent e)
    {
        if (_drag == DragKind.Move)
        {
            // A drop inside the dragged text, or no drag at all, is a plain click.
            bool inside = _moveRange.Start <= _moveDrop && _moveDrop <= _moveRange.End;
            if (!_moveMoved || inside) SetCaret(_movePress);
            else MoveText(_moveRange, _moveDrop, Ctrl(_paper!));
            _drag = DragKind.None;
            return;
        }

        if (_drag == DragKind.Text || _drag == DragKind.Lines || _drag == DragKind.Box)
        {
            NormalizeSelections();
            AfterCaretMove(false);
        }
        _drag = DragKind.None;
    }

    private void TickDragAutoScroll(Paper paper)
    {
        if (_drag == DragKind.None) return;
        if (!paper.IsPointerDown(PaperMouseBtn.Left))
        {
            _drag = DragKind.None;
            return;
        }
        if (_drag != DragKind.Text && _drag != DragKind.Lines && _drag != DragKind.Box && _drag != DragKind.Move) return;

        var v = _view;
        float speed = 0;
        if (_pointer.Y < v.TextY) speed = _pointer.Y - v.TextY;
        else if (_pointer.Y > v.TextY + v.TextH) speed = _pointer.Y - (v.TextY + v.TextH);
        float hspeed = 0;
        if (_pointer.X < v.TextX) hspeed = _pointer.X - v.TextX;
        else if (_pointer.X > v.MinimapX) hspeed = _pointer.X - v.MinimapX;

        if (speed == 0 && hspeed == 0) return;
        _scrollYTarget = Math.Clamp(_scrollYTarget + speed * _frameDelta * 12, 0, MaxScrollY());
        _scrollY = _scrollYTarget;
        _scrollXTarget = Math.Clamp(_scrollXTarget + hspeed * _frameDelta * 12, 0, MaxScrollX());
        _scrollX = _scrollXTarget;
        UpdateDrag(_pointer);
    }

    private void ScrollThumbTo(float thumbTop, float thumbH)
    {
        var v = _view;
        float range = v.TextH - thumbH;
        if (range <= 0) return;
        double t = Math.Clamp((thumbTop - v.Y) / range, 0, 1);
        _scrollYTarget = _scrollY = t * MaxScrollY();
    }

    private void HScrollThumbTo(float thumbLeft, float thumbW)
    {
        var v = _view;
        float range = v.TextW - thumbW;
        if (range <= 0) return;
        float t = Math.Clamp((thumbLeft - v.TextX) / range, 0, 1);
        _scrollXTarget = _scrollX = t * MaxScrollX();
    }

    private void MinimapScrollTo(float y)
    {
        var v = _view;
        float rowH = 2f * MinimapScale;
        double row = MinimapTopRow() + (y - v.Y) / rowH;
        double target = row * v.LineH - v.TextH * 0.5;
        _scrollYTarget = Math.Clamp(target, 0, MaxScrollY());
        if (_drag == DragKind.Minimap) _scrollY = _scrollYTarget;
    }

    private void HandleScroll(ScrollEvent e)
    {
        var paper = _paper!;
        e.StopPropagation();
        CloseHover();
        if (Ctrl(paper))
        {
            Zoom(e.Delta > 0 ? 1 : -1);
            return;
        }
        float lh = LineHeightPx;
        float dx = e.DeltaX, dy = e.Delta;
        if (Shift(paper)) { dx += dy; dy = 0; }
        if (dx != 0) _scrollXTarget = Math.Clamp(_scrollXTarget - dx * _view.CharW * 8, 0, MaxScrollX());
        if (dy != 0) _scrollYTarget = Math.Clamp(_scrollYTarget - dy * lh * 3, 0, MaxScrollY());
    }

    private void HandleRightClick(ClickEvent e)
    {
        var p = e.LocalPosition;
        if (!InTextArea(p)) return;
        var pos = PositionFromPoint(p);
        bool inside = false;
        foreach (var s in _selections) if (!s.IsEmpty && s.Range.Contains(pos)) inside = true;
        if (!inside) SetCaret(pos);
        OpenContextMenu(p);
    }

    #endregion

    #region Keyboard

    private void HandleKey(KeyEvent e)
    {
        var paper = _paper!;
        if (!paper.IsElementFocused(_handle.Data.ID)) return;
        bool ctrl = Ctrl(paper), shift = Shift(paper), alt = Alt(paper);
        var key = e.Key;

        if (key is PaperKey.LeftControl or PaperKey.RightControl or PaperKey.LeftShift or PaperKey.RightShift
            or PaperKey.LeftAlt or PaperKey.RightAlt or PaperKey.LeftSuper or PaperKey.RightSuper)
            return;

        if (HandlePopupKey(key, ctrl, shift, alt)) return;

        if (_chordK)
        {
            _chordK = false;
            if (ctrl && HandleChord(key)) return;
        }

        bool ro = IsReadOnly;
        switch (key)
        {
            case PaperKey.Escape:
                if (_findBarOpen) { CloseFind(); return; }
                if (_selections.Count > 1) { CollapseToPrimary(); AfterCaretMove(true); return; }
                if (!Selection.IsEmpty) { SetCaret(Caret); return; }
                return;

            case PaperKey.Left:
                MoveLeft(shift, ctrl);
                return;
            case PaperKey.Right:
                MoveRight(shift, ctrl);
                return;
            case PaperKey.Up:
            case PaperKey.Down:
            {
                int dir = key == PaperKey.Up ? -1 : 1;
                if (ctrl && alt) AddCaretVertical(dir);
                else if (alt && shift && !ro) DuplicateLines(dir > 0);
                else if (alt && !ro) MoveLines(dir);
                else if (ctrl) ScrollBy(dir * LineHeightPx);
                else MoveVertical(dir, shift);
                return;
            }
            case PaperKey.Home:
                if (ctrl) MoveDocumentStart(shift); else MoveHome(shift);
                return;
            case PaperKey.End:
                if (ctrl) MoveDocumentEnd(shift); else MoveEnd(shift);
                return;
            case PaperKey.PageUp:
                MovePage(-1, shift);
                return;
            case PaperKey.PageDown:
                MovePage(1, shift);
                return;

            case PaperKey.Backspace:
                if (!ro) Backspace(ctrl);
                AfterDeleteForPopups();
                return;
            case PaperKey.Delete:
                if (shift && !ctrl) { Cut(paper); return; }
                if (!ro) DeleteForward(ctrl);
                return;
            case PaperKey.Enter:
            case PaperKey.KeypadEnter:
                if (ro) return;
                if (ctrl && shift) InsertLineAbove();
                else if (ctrl) InsertLineBelow();
                else NewLine();
                return;
            case PaperKey.Tab:
                if (ro) return;
                if (shift) Outdent(); else Indent();
                return;
            case PaperKey.Insert:
                if (ctrl) Copy(paper);
                else if (shift) Paste(paper);
                return;

            case PaperKey.F2:
                StartRename();
                return;
            case PaperKey.F3:
                if (FindQuery.Length == 0) OpenFind(false);
                else FindNextMatch(!shift);
                return;
            case PaperKey.F12:
                if (shift) FindReferences();
                else if (ctrl) GoToImplementation();
                else GoToDefinition();
                return;
        }

        if (alt && shift && key == PaperKey.F && !ro) { FormatDocument(); return; }

        if (!ctrl) return;

        switch (key)
        {
            case PaperKey.A:
                if (shift) ToggleBlockComment(); else SelectAll();
                return;
            case PaperKey.C: Copy(paper); return;
            case PaperKey.X: Cut(paper); return;
            case PaperKey.V: Paste(paper); return;
            case PaperKey.Z:
                if (shift) RedoEdit(); else UndoEdit();
                return;
            case PaperKey.Y: RedoEdit(); return;
            case PaperKey.D: AddNextOccurrence(); return;
            case PaperKey.L:
                if (shift) { if (FindQuery.Length > 0 && _findBarOpen) SelectAllMatches(); else SelectAllOccurrences(); }
                else SelectLine();
                return;
            case PaperKey.F: OpenFind(false); return;
            case PaperKey.H: OpenFind(true); return;
            case PaperKey.G: OpenGoToLine(); return;
            case PaperKey.S: SaveRequested?.Invoke(); return;
            case PaperKey.K:
                if (shift) DeleteLines();
                else _chordK = true;
                return;
            case PaperKey.Slash:
            case PaperKey.KeypadDivide:
                ToggleLineComment();
                return;
            case PaperKey.Space:
                if (shift) RequestSignatureHelp(true);
                else RequestCompletion(CodeCompletionTrigger.Invoke);
                return;
            case PaperKey.Period:
                OpenCodeActions();
                return;
            case PaperKey.LeftBracket:
                if (shift) FoldAtCaret(); else Outdent();
                return;
            case PaperKey.RightBracket:
                if (shift) UnfoldAtCaret(); else Indent();
                return;
            case PaperKey.Backslash:
                if (shift) GoToMatchingBracket();
                return;
            case PaperKey.Equals:
            case PaperKey.KeypadPlus:
                Zoom(1);
                return;
            case PaperKey.Minus:
            case PaperKey.KeypadMinus:
                Zoom(-1);
                return;
            case PaperKey.Num0:
            case PaperKey.Keypad0:
                ResetZoom();
                return;
            case PaperKey.U:
                TransformCase(!shift);
                return;
        }
    }

    // Ctrl+K followed by a second Ctrl chord.
    private bool HandleChord(PaperKey key)
    {
        switch (key)
        {
            case PaperKey.Num0: FoldAll(); return true;
            case PaperKey.J: UnfoldAll(); return true;
            case PaperKey.C: CommentLines(true); return true;
            case PaperKey.U: CommentLines(false); return true;
            case PaperKey.D: FormatDocument(); return true;
            case PaperKey.X: TrimTrailingWhitespace(); return true;
            case PaperKey.L: ToggleFold(FoldStartAround(Caret.Line)); return true;
        }
        return false;
    }

    private int FoldStartAround(int line)
    {
        int best = -1;
        foreach (var f in _folds)
        {
            if (f.StartLine > line) break;
            if (f.EndLine >= line) best = f.StartLine;
        }
        return best;
    }

    private void CommentLines(bool comment)
    {
        string? token = _language.LineComment;
        if (token == null) return;
        bool allCommented = true;
        foreach (var (a, b) in SelectedLineBlocks(false))
            for (int l = a; l <= b; l++)
            {
                string s = Document[l].TrimStart();
                if (s.Length > 0 && !s.StartsWith(token, StringComparison.Ordinal)) allCommented = false;
            }
        if (comment ? !allCommented : allCommented) ToggleLineComment();
    }

    private void SelectAllOccurrences()
    {
        var w = Selection.IsEmpty ? WordAt(Caret) : Selection.Range;
        if (w.IsEmpty || !w.IsSingleLine) return;
        string needle = Document.GetText(w);
        var found = new List<CodeSelection>();
        var start = TextPosition.Zero;
        bool whole = Selection.IsEmpty;
        while (found.Count < 10000)
        {
            var m = FindNext(needle, start, true, whole, wrap: false);
            if (m == null) break;
            found.Add(new CodeSelection(m.Value.Start, m.Value.End));
            start = m.Value.End;
        }
        if (found.Count == 0) return;
        _selections.Clear();
        _selections.AddRange(found);
        _primary = 0;
        for (int i = 0; i < found.Count; i++) if (found[i].Start == w.Start) _primary = i;
        AfterCaretMove(true);
    }

    private void HandleText(TextInputEvent e)
    {
        var paper = _paper!;
        if (!paper.IsElementFocused(_handle.Data.ID) || IsReadOnly) return;
        char c = e.Character;
        if (char.IsControl(c)) return;
        if (Ctrl(paper) && !Alt(paper)) return;

        if (_completion != null && CompletionCommitsOn(c)) CommitCompletion(c);

        TypeChar(c);
        AfterTypeForPopups(c);
        FormatOnType(c);
    }

    #endregion
}
