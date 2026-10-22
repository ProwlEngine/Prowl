// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using SDColor = System.Drawing.Color;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    private const int PopupLayer = Layer.Topmost + 20;

    #region Popup text

    // A popup line as coloured runs. Popups use the editor's monospace font so sizing is just counting chars.
    private sealed class PopupLine
    {
        public readonly List<(string Text, Color32 Color, bool Underline)> Runs = new();
        public int Length;

        public PopupLine Add(string text, Color32 color, bool underline = false)
        {
            if (text.Length == 0) return this;
            Runs.Add((text, color, underline));
            Length += text.Length;
            return this;
        }
    }

    private float PopupFontSize => MathF.Max(8, FontSize * 0.92f);
    private float PopupCharW => _charW * (PopupFontSize / FontSize);
    private float PopupLineH => MathF.Round(PopupFontSize * 1.45f);

    private static void Wrap(string text, int width, Color32 color, List<PopupLine> into)
    {
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            string p = paragraph.TrimEnd();
            if (p.Length == 0) { into.Add(new PopupLine()); continue; }
            int start = 0;
            while (start < p.Length)
            {
                int len = Math.Min(width, p.Length - start);
                if (start + len < p.Length)
                {
                    int space = p.LastIndexOf(' ', start + len, len);
                    if (space > start) len = space - start;
                }
                into.Add(new PopupLine().Add(p.Substring(start, len).Trim(), color));
                start += len;
                while (start < p.Length && p[start] == ' ') start++;
            }
        }
    }

    private void WrapTagged(IReadOnlyList<CodeTaggedText> parts, int width, CodeEditorColors pal, List<PopupLine> into)
    {
        var line = new PopupLine();
        foreach (var part in parts)
        {
            string[] pieces = part.Text.Replace("\r", "").Split('\n');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) { into.Add(line); line = new PopupLine(); }
                string piece = pieces[i];
                while (line.Length + piece.Length > width && line.Length > 0 && piece.Trim().Length > 0)
                {
                    into.Add(line);
                    line = new PopupLine().Add("    ", pal.PopupText);
                    piece = piece.TrimStart();
                    if (piece.Length > width) break;
                }
                line.Add(piece, KindColor(part.Kind, pal));
            }
        }
        if (line.Length > 0) into.Add(line);
    }

    private static Color32 KindColor(CodeTokenKind kind, CodeEditorColors pal)
        => kind == CodeTokenKind.Plain ? pal.PopupText : pal[kind];

    private void DrawPopupLines(Canvas canvas, FontFile font, List<PopupLine> lines, float x, float y, float lineH)
    {
        float cw = PopupCharW, size = PopupFontSize;
        float glyphH = _glyphH * (size / FontSize);
        for (int i = 0; i < lines.Count; i++)
        {
            float cx = x;
            float ty = y + i * lineH + (lineH - glyphH) * 0.5f;
            foreach (var (text, color, underline) in lines[i].Runs)
            {
                DrawPlain(canvas, font, text, cx, ty, color, size);
                if (underline) canvas.RectFilled(cx, ty + glyphH, text.Length * cw, 1, color);
                cx += text.Length * cw;
            }
        }
    }

    private static int MaxLength(List<PopupLine> lines)
    {
        int max = 0;
        foreach (var l in lines) max = Math.Max(max, l.Length);
        return max;
    }

    private ElementBuilder PopupBox(Paper paper, string id, float x, float y, float w, float h)
    {
        var pal = Palette();
        var v = _view;
        return paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x - v.X, y - v.Y)
            .Width(w).Height(h)
            .BackgroundColor(pal.Popup)
            .BorderColor(pal.PopupBorder).BorderWidth(1)
            .Rounded(6)
            .DropShadow(0, 8, 24, -4, SDColor.FromArgb(150, 0, 0, 0))
            .Layer(PopupLayer)
            .ClampToScreen()
            .IsNotFocusable()
            .StopEventPropagation();
    }

    #endregion

    #region Lifecycle

    private void BuildPopups(Paper paper, string id)
    {
        if (!_view.Valid) return;
        var font = EditorFont;
        if (font == null) return;

        if (_completion != null) BuildCompletion(paper, id, font);
        if (_signature != null && _completion == null) BuildSignature(paper, id, font);
        if (_hoverLines != null && _completion == null && _list == null) BuildHover(paper, id, font);
        if (_list != null) BuildList(paper, id, font);
        if (_findBarOpen) BuildFindBar(paper, id, font);
        if (_gotoOpen) BuildGoToLine(paper, id, font);
        if (_renameOpen) BuildRename(paper, id, font);
    }

    private void ClosePopups()
    {
        CloseCompletion();
        _signature = null;
        _sigCts?.Cancel();
        CloseHover();
        _list = null;
        _renameOpen = false;
        _gotoOpen = false;
    }

    private void CancelPopupRequests()
    {
        _completionCts?.Cancel();
        _sigCts?.Cancel();
        _hoverCts?.Cancel();
        _highlightCts?.Cancel();
        _semanticCts?.Cancel();
        _lensCts?.Cancel();
    }

    private bool IsPopupFocused(Paper paper)
        => (_findBarOpen || _gotoOpen || _renameOpen) && paper.FocusedElementId != 0 && _inputHandles.Contains(paper.FocusedElementId);

    private readonly HashSet<int> _inputHandles = new();

    private void OnEditForPopups(TextChange change)
    {
        _highlights.Clear();
        _highlightKey = -1;
        CloseHover();
        if (!_typingNow) _editedSinceRequest = true;

        bool singleLine = change.Range.IsSingleLine && change.NewEnd.Line == change.Range.Start.Line;
        if (_completion != null)
        {
            if (!singleLine || change.Range.Start.Line != _completion.Start.Line || change.Range.Start < _completion.Start) CloseCompletion();
            else _completion.Start = change.Map(_completion.Start, false);
        }

        if (!singleLine)
        {
            CloseSignature();
            _renameOpen = false;
        }
        else if (_renameOpen && change.Range.Start.Line == _renameAt.Line) _renameOpen = false;
    }

    private void OnCaretMovedForPopups()
    {
        if (_completion != null && !_completionUpdating)
        {
            var c = Caret;
            if (c.Line != _completion.Start.Line || c.Column < _completion.Start.Column) CloseCompletion();
            else RefilterCompletion();
        }
        if (_signature != null && !_completionUpdating)
        {
            int offset = Document.PositionToOffset(Caret);
            if (offset <= _signature.ArgumentListStart) CloseSignature();
            else RequestSignatureHelp(false);
        }
        _highlightKey = -1;
    }

    private bool HandlePopupKey(PaperKey key, bool ctrl, bool shift, bool alt)
    {
        if (_list != null)
        {
            switch (key)
            {
                case PaperKey.Escape: _list = null; return true;
                case PaperKey.Up: MoveListSelection(-1); return true;
                case PaperKey.Down: MoveListSelection(1); return true;
                case PaperKey.PageUp: MoveListSelection(-(ListRows - 1)); return true;
                case PaperKey.PageDown: MoveListSelection(ListRows - 1); return true;
                case PaperKey.Enter:
                case PaperKey.KeypadEnter:
                case PaperKey.Tab:
                    InvokeListSelection();
                    return true;
            }
            _list = null;
        }

        if (_completion != null && _completion.Filtered.Count > 0)
        {
            var c = _completion;
            int page = Math.Max(1, CompletionRows - 1);
            switch (key)
            {
                case PaperKey.Escape: CloseCompletion(); return true;
                case PaperKey.Up: c.Selected = c.Selected <= 0 ? c.Filtered.Count - 1 : c.Selected - 1; c.UserMoved = true; return true;
                case PaperKey.Down: c.Selected = c.Selected >= c.Filtered.Count - 1 ? 0 : c.Selected + 1; c.UserMoved = true; return true;
                case PaperKey.PageUp: c.Selected = Math.Max(0, c.Selected - page); c.UserMoved = true; return true;
                case PaperKey.PageDown: c.Selected = Math.Min(c.Filtered.Count - 1, c.Selected + page); c.UserMoved = true; return true;
                case PaperKey.Enter:
                case PaperKey.KeypadEnter:
                    if (ctrl || shift) break;
                    CommitCompletion('\n');
                    return true;
                case PaperKey.Tab:
                    CommitCompletion('\t');
                    return true;
                case PaperKey.Left:
                case PaperKey.Right:
                case PaperKey.Home:
                case PaperKey.End:
                    CloseCompletion();
                    return false;
            }
        }

        if (_signature != null)
        {
            switch (key)
            {
                case PaperKey.Escape: CloseSignature(); return true;
                case PaperKey.Up when _signature.Signatures.Count > 1 && !ctrl && !alt && !shift:
                    _sigIndex = (_sigIndex + _signature.Signatures.Count - 1) % _signature.Signatures.Count;
                    return true;
                case PaperKey.Down when _signature.Signatures.Count > 1 && !ctrl && !alt && !shift:
                    _sigIndex = (_sigIndex + 1) % _signature.Signatures.Count;
                    return true;
            }
        }

        if (key == PaperKey.Escape && _hoverLines != null)
        {
            CloseHover();
            return true;
        }
        return false;
    }

    private void AfterTypeForPopups(char c)
    {
        if (Session == null && !Options.CompletionOnTyping) return;

        if (_language.IsSignatureHelpTrigger(c)) RequestSignatureHelp(false, c);
        else if (c == ')' && _signature != null) RequestSignatureHelp(false);

        if (_completion != null)
        {
            if (_language.IsWordChar(c)) { RefilterCompletion(); return; }
            CloseCompletion();
        }

        if (_language.IsCompletionTrigger(c))
            RequestCompletion(new CodeCompletionTrigger(CodeCompletionTriggerKind.Character, c));
        else if (Options.CompletionOnTyping && _language.IsWordChar(c) && !char.IsDigit(c))
        {
            var w = WordAt(Caret);
            if (w.End.Column - w.Start.Column == 1 && IsCodeAt(Caret.Line, Math.Max(0, Caret.Column - 1)))
                RequestCompletion(new CodeCompletionTrigger(CodeCompletionTriggerKind.Character, c));
        }
    }

    private void AfterDeleteForPopups()
    {
        if (_completion != null)
        {
            if (Caret.Line != _completion.Start.Line || Caret.Column < _completion.Start.Column) CloseCompletion();
            else RefilterCompletion();
        }
        if (_signature != null) RequestSignatureHelp(false);
    }

    #endregion

    #region Completion

    private sealed class CompletionState
    {
        public CodeCompletionList List = null!;
        public TextPosition Start;
        public readonly List<(CodeCompletionItem Item, int Score, ulong Mask)> Filtered = new();
        public int Selected;
        public int Scroll;
        public bool UserMoved;
        public bool Explicit;
        public CodeQuickInfo? Description;
        public CodeCompletionItem? DescriptionFor;
        public double SelectedSince;
        public int LastSelected = -1;
    }

    private CompletionState? _completion;
    private bool _typingNow, _editedSinceRequest;
    private CancellationTokenSource? _completionCts;
    private bool _completionUpdating;
    private int CompletionRows => 10;

    public bool IsCompletionOpen => _completion != null;

    public void RequestCompletion(CodeCompletionTrigger trigger)
    {
        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        int offset = Document.PositionToOffset(Caret);
        int version = Document.Version;
        bool isExplicit = trigger.Kind == CodeCompletionTriggerKind.Invoke;

        if (Session == null)
        {
            ShowCompletion(WordCompletions(), isExplicit);
            return;
        }

        int line = Caret.Line;
        Continue(Session.GetCompletionsAsync(offset, trigger, _completionCts.Token), version, list =>
        {
            if (list == null || list.Items.Count == 0) { if (isExplicit) CloseCompletion(); return; }

            // Filter text typed while the list was computed is fine. Any other edit makes its offsets stale.
            if (version != Document.Version && (Caret.Line != line || Document.PositionToOffset(Caret) < offset || _editedSinceRequest)) return;
            ShowCompletion(list, isExplicit);
        }, requireVersion: false);
        _editedSinceRequest = false;
    }

    // Lexical fallback: every other word in the nearby text.
    private CodeCompletionList WordCompletions()
    {
        var word = WordAt(Caret);
        var start = word.IsEmpty ? Caret : word.Start;
        string current = Document.GetText(new TextRange(start, Caret));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<CodeCompletionItem>();
        int from = Math.Max(0, Caret.Line - 2000), to = Math.Min(Document.LineCount - 1, Caret.Line + 2000);
        for (int line = from; line <= to && items.Count < 5000; line++)
        {
            string s = Document[line];
            int i = 0;
            while (i < s.Length)
            {
                if (!_language.IsWordChar(s[i]) || char.IsDigit(s[i])) { i++; continue; }
                int j = i;
                while (j < s.Length && _language.IsWordChar(s[j])) j++;
                if (j - i > 1)
                {
                    string w = s.Substring(i, j - i);
                    if (w != current && seen.Add(w)) items.Add(new CodeCompletionItem { Label = w, Kind = CodeSymbolKind.Text });
                }
                i = j;
            }
        }
        return new CodeCompletionList { Items = items, SpanStart = Document.PositionToOffset(start) };
    }

    private void ShowCompletion(CodeCompletionList list, bool isExplicit)
    {
        var start = Document.OffsetToPosition(list.SpanStart);
        if (start.Line != Caret.Line || start.Column > Caret.Column) return;
        _completion = new CompletionState { List = list, Start = start, Explicit = isExplicit };
        RefilterCompletion();
        if (_completion != null && isExplicit && _completion.Filtered.Count == 1 && FilterText().Length > 0)
            CommitCompletion('\0');
    }

    private string FilterText()
    {
        var c = _completion!;
        if (Caret.Line != c.Start.Line) return "";
        int end = Math.Min(Caret.Column, Document.GetLineLength(c.Start.Line));
        return end > c.Start.Column ? Document[c.Start.Line].Substring(c.Start.Column, end - c.Start.Column) : "";
    }

    private void RefilterCompletion()
    {
        var c = _completion;
        if (c == null) return;
        string filter = FilterText();
        var prevItem = c.Filtered.Count > 0 && c.Selected < c.Filtered.Count ? c.Filtered[c.Selected].Item : null;

        c.Filtered.Clear();
        foreach (var item in c.List.Items)
        {
            int score = FuzzyScore(filter, item.Filter, out ulong mask);
            if (score < 0) continue;
            c.Filtered.Add((item, score, mask));
        }

        if (c.Filtered.Count == 0 || (filter.Length > 0 && c.Filtered.Count == 1 && c.Filtered[0].Item.Filter == filter && !c.Explicit && Session == null))
        {
            CloseCompletion();
            return;
        }

        c.Filtered.Sort((a, b) =>
        {
            int s = b.Score.CompareTo(a.Score);
            if (s != 0) return s;
            return string.Compare(a.Item.Sort, b.Item.Sort, StringComparison.OrdinalIgnoreCase);
        });

        c.Selected = 0;
        if (filter.Length == 0)
        {
            for (int i = 0; i < c.Filtered.Count; i++)
                if (c.Filtered[i].Item.Preselect) { c.Selected = i; break; }
        }
        else if (c.UserMoved && prevItem != null)
        {
            for (int i = 0; i < c.Filtered.Count; i++)
                if (ReferenceEquals(c.Filtered[i].Item, prevItem)) { c.Selected = i; break; }
        }
        c.Scroll = Math.Clamp(c.Scroll, 0, Math.Max(0, c.Filtered.Count - CompletionRows));
    }

    /// <summary>Scores how well <paramref name="pattern"/> matches <paramref name="word"/>, or -1 for no match. Prefix matches win, then camel hump and substring matches.</summary>
    internal static int FuzzyScore(string pattern, string word, out ulong mask)
    {
        mask = 0;
        if (pattern.Length == 0) return 0;
        if (pattern.Length > word.Length) return -1;

        if (word.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < pattern.Length && i < 64; i++) mask |= 1UL << i;
            int score = 10000 - word.Length;
            if (word.StartsWith(pattern, StringComparison.Ordinal)) score += 2000;
            if (word.Length == pattern.Length) score += 1000;
            return score;
        }

        // Subsequence that must start on a word boundary, preferring boundaries for every char.
        int wi = 0, gaps = 0, boundaries = 0;
        for (int pi = 0; pi < pattern.Length; pi++)
        {
            char pc = char.ToLowerInvariant(pattern[pi]);
            int found = -1;
            for (int k = wi; k < word.Length; k++)
                if (char.ToLowerInvariant(word[k]) == pc && IsBoundary(word, k)) { found = k; break; }
            if (found < 0 && pi > 0)
                for (int k = wi; k < word.Length; k++)
                    if (char.ToLowerInvariant(word[k]) == pc) { found = k; break; }
            if (found < 0) return -1;
            if (pi == 0 && !IsBoundary(word, found)) return -1;
            if (IsBoundary(word, found)) boundaries++;
            gaps += found - wi;
            if (found < 64) mask |= 1UL << found;
            wi = found + 1;
        }
        return 5000 + boundaries * 50 - gaps * 5 - word.Length;
    }

    private static bool IsBoundary(string w, int i)
    {
        if (i == 0) return true;
        char prev = w[i - 1], c = w[i];
        if (prev == '_' || prev == '.' || prev == '<') return true;
        if (char.IsUpper(c) && !char.IsUpper(prev)) return true;
        return char.IsDigit(c) && !char.IsDigit(prev);
    }

    private bool CompletionCommitsOn(char c)
    {
        var comp = _completion;
        if (comp == null || comp.Filtered.Count == 0 || _language.IsWordChar(c)) return false;
        if (comp.List.SuggestionMode && !comp.UserMoved) return false;
        if (FilterText().Length == 0 && !comp.UserMoved && !comp.Explicit) return false;
        var item = comp.Filtered[comp.Selected].Item;
        string commit = item.CommitCharacters ?? (Session != null ? ".,;()[]{}<>=" : "");
        return commit.IndexOf(c) >= 0;
    }

    private void CommitCompletion(char commitChar)
    {
        var comp = _completion;
        if (comp == null || comp.Filtered.Count == 0) { CloseCompletion(); return; }
        var item = comp.Filtered[comp.Selected].Item;
        _completion = null;

        int caretOffset = Document.PositionToOffset(Caret);
        CodeCompletionCommit? commit = null;
        try { commit = Session?.ResolveCompletion(comp.List, item, caretOffset, commitChar); }
        catch (Exception e) { Console.WriteLine($"CodeEditor completion commit failed: {e.Message}"); }

        _completionUpdating = true;
        try
        {
            if (commit != null && commit.Edits.Count > 0)
            {
                ApplyEdits(commit.Edits, CodeEditKind.Other);
                if (commit.CaretOffset != null) SetCaret(Document.OffsetToPosition(commit.CaretOffset.Value));
            }
            else
            {
                var end = Caret;
                var wordEnd = WordAt(end).End;
                if (wordEnd > end && wordEnd.Line == end.Line) end = wordEnd;
                Edit(CodeEditKind.Other, () =>
                {
                    var c = Document.Replace(new TextRange(comp.Start, end), item.InsertText ?? item.Label);
                    _selections.Clear();
                    _selections.Add(new CodeSelection(c.NewEnd));
                    _primary = 0;
                });
            }
        }
        finally { _completionUpdating = false; }
        AfterCaretMove(true);
    }

    private void CloseCompletion()
    {
        _completion = null;
        _completionCts?.Cancel();
    }

    private void BuildCompletion(Paper paper, string id, FontFile font)
    {
        var c = _completion!;
        var pal = Palette();
        if (c.Filtered.Count == 0) return;

        float cw = PopupCharW;
        float rowH = MathF.Round(PopupLineH * 1.08f);
        int rows = Math.Min(CompletionRows, c.Filtered.Count);
        c.Selected = Math.Clamp(c.Selected, 0, c.Filtered.Count - 1);
        if (c.Selected < c.Scroll) c.Scroll = c.Selected;
        if (c.Selected >= c.Scroll + rows) c.Scroll = c.Selected - rows + 1;

        int labelChars = 16, detailChars = 0;
        int scan = Math.Min(c.Filtered.Count, 200);
        for (int i = 0; i < scan; i++)
        {
            labelChars = Math.Max(labelChars, c.Filtered[i].Item.Label.Length);
            detailChars = Math.Max(detailChars, c.Filtered[i].Item.Detail?.Length ?? 0);
        }
        float iconW = rowH;
        float w = Math.Clamp(iconW + 10 + (Math.Min(labelChars, 48) + Math.Min(detailChars, 32) + 3) * cw, 220, 560);
        float h = rows * rowH + 4;

        var anchor = PointFromPosition(c.Start);
        float x = anchor.X - iconW - 6;
        float y = anchor.Y + _view.LineH + 2;
        if (y + h > _view.Y + _view.TextH && anchor.Y - h - 2 > _view.Y) y = anchor.Y - h - 2;

        ElementHandle popup = default;
        var box = PopupBox(paper, id + "_completion", x, y, w, h)
            .OnScroll(e =>
            {
                c.Scroll = Math.Clamp(c.Scroll - (int)MathF.Round(e.Delta * 3), 0, Math.Max(0, c.Filtered.Count - rows));
                e.StopPropagation();
            })
            .OnPress(e =>
            {
                int row = (int)((e.LocalPosition.Y - popup.Data.LayoutRect.Min.Y - 2) / rowH);
                int index = c.Scroll + row;
                if (index < 0 || index >= c.Filtered.Count) return;
                if (c.Selected == index && _completion == c) { CommitCompletion('\0'); return; }
                c.Selected = index;
                c.UserMoved = true;
            });

        using (box.Enter())
        {
            popup = paper.CurrentParent;
            paper.Draw((canvas, r) =>
            {
                float glyphH = _glyphH * (PopupFontSize / FontSize);
                for (int i = 0; i < rows; i++)
                {
                    int index = c.Scroll + i;
                    if (index >= c.Filtered.Count) break;
                    var (item, _, mask) = c.Filtered[index];
                    float ry = r.Min.Y + 2 + i * rowH;
                    if (index == c.Selected) canvas.RoundedRectFilled(r.Min.X + 2, ry, r.Size.X - 4, rowH, 4, pal.PopupSelection);

                    DrawKindIcon(canvas, font, item.Kind, r.Min.X + 6, ry + 3, rowH - 6, pal);

                    float tx = r.Min.X + iconW + 10;
                    float ty = ry + (rowH - glyphH) * 0.5f;
                    string label = item.Label;
                    var maskFor = FilterMaskFor(item, label, mask);
                    DrawMatchedLabel(canvas, font, label, tx, ty, maskFor, pal);

                    if (!string.IsNullOrEmpty(item.Detail))
                    {
                        string d = item.Detail!.Length > 40 ? item.Detail.Substring(0, 39) + "..." : item.Detail;
                        float dx = r.Max.X - 10 - d.Length * cw;
                        if (dx > tx + (label.Length + 2) * cw) DrawPlain(canvas, font, d, dx, ty, pal.PopupTextDim, PopupFontSize);
                    }
                }

                if (c.Filtered.Count > rows)
                {
                    float trackH = r.Size.Y - 4;
                    float th = MathF.Max(16, trackH * rows / c.Filtered.Count);
                    float t = c.Scroll / (float)Math.Max(1, c.Filtered.Count - rows);
                    canvas.RectFilled(r.Max.X - 5, r.Min.Y + 2 + t * (trackH - th), 3, th, pal.Scrollbar);
                }
            });
        }

        BuildCompletionDescription(paper, id, font, c, x + w + 4, y);
    }

    // The mask is over FilterText. When the label differs, the match is shown on the label by redoing it there.
    private static ulong FilterMaskFor(CodeCompletionItem item, string label, ulong mask)
    {
        if (item.FilterText == null || item.FilterText == label) return mask;
        return 0;
    }

    private void DrawMatchedLabel(Canvas canvas, FontFile font, string label, float x, float y, ulong mask, CodeEditorColors pal)
    {
        float cw = PopupCharW;
        if (mask == 0)
        {
            DrawPlain(canvas, font, label, x, y, pal.PopupText, PopupFontSize);
            return;
        }
        int i = 0;
        while (i < label.Length)
        {
            bool hit = i < 64 && (mask & (1UL << i)) != 0;
            int j = i;
            while (j < label.Length && (j < 64 && (mask & (1UL << j)) != 0) == hit) j++;
            DrawPlain(canvas, font, label.Substring(i, j - i), x + i * cw, y, hit ? pal.PopupMatch : pal.PopupText, PopupFontSize);
            i = j;
        }
    }

    private void DrawKindIcon(Canvas canvas, FontFile font, CodeSymbolKind kind, float x, float y, float size, CodeEditorColors pal)
    {
        var (letter, tokenKind) = kind switch
        {
            CodeSymbolKind.Keyword => ("K", CodeTokenKind.Keyword),
            CodeSymbolKind.Snippet => ("S", CodeTokenKind.Comment),
            CodeSymbolKind.Namespace => ("N", CodeTokenKind.PreprocessorText),
            CodeSymbolKind.Class => ("C", CodeTokenKind.Class),
            CodeSymbolKind.Record => ("R", CodeTokenKind.Record),
            CodeSymbolKind.Struct => ("S", CodeTokenKind.Struct),
            CodeSymbolKind.Interface => ("I", CodeTokenKind.Interface),
            CodeSymbolKind.Enum => ("E", CodeTokenKind.Enum),
            CodeSymbolKind.EnumMember => ("e", CodeTokenKind.EnumMember),
            CodeSymbolKind.Delegate => ("D", CodeTokenKind.Delegate),
            CodeSymbolKind.Method => ("M", CodeTokenKind.Method),
            CodeSymbolKind.ExtensionMethod => ("X", CodeTokenKind.ExtensionMethod),
            CodeSymbolKind.Constructor => ("M", CodeTokenKind.Method),
            CodeSymbolKind.Property => ("P", CodeTokenKind.Property),
            CodeSymbolKind.Field => ("F", CodeTokenKind.Field),
            CodeSymbolKind.Event => ("V", CodeTokenKind.Event),
            CodeSymbolKind.Local => ("L", CodeTokenKind.Local),
            CodeSymbolKind.Parameter => ("p", CodeTokenKind.Parameter),
            CodeSymbolKind.Constant => ("c", CodeTokenKind.Constant),
            CodeSymbolKind.TypeParameter => ("T", CodeTokenKind.TypeParameter),
            CodeSymbolKind.Operator => ("O", CodeTokenKind.Operator),
            CodeSymbolKind.Label => ("L", CodeTokenKind.Label),
            _ => ("a", CodeTokenKind.Plain),
        };
        var col = pal[tokenKind];
        canvas.RoundedRectFilled(x, y, size, size, 3, new Color32(col.R, col.G, col.B, 46));
        float fs = PopupFontSize * 0.9f;
        float gw = _charW * (fs / FontSize);
        float gh = _glyphH * (fs / FontSize);
        DrawPlain(canvas, font, letter, x + (size - gw) * 0.5f, y + (size - gh) * 0.5f, col, fs);
    }

    private CancellationTokenSource? _descCts;

    private void BuildCompletionDescription(Paper paper, string id, FontFile font, CompletionState c, float x, float y)
    {
        var item = c.Filtered[c.Selected].Item;
        if (c.LastSelected != c.Selected)
        {
            c.LastSelected = c.Selected;
            c.SelectedSince = _time;
        }

        if (!ReferenceEquals(c.DescriptionFor, item) && Session != null && _time - c.SelectedSince > 0.08)
        {
            c.DescriptionFor = item;
            c.Description = null;
            _descCts?.Cancel();
            _descCts = new CancellationTokenSource();
            Continue(Session.GetCompletionDescriptionAsync(item, _descCts.Token), Document.Version, info =>
            {
                if (_completion == c && ReferenceEquals(c.DescriptionFor, item)) c.Description = info;
            }, requireVersion: false);
        }

        var info = ReferenceEquals(c.DescriptionFor, item) ? c.Description : null;
        if (info == null || (info.Signature.Count == 0 && info.Documentation.Count == 0)) return;

        var pal = Palette();
        var lines = new List<PopupLine>();
        const int width = 56;
        WrapTagged(info.Signature, width, pal, lines);
        if (info.Documentation.Count > 0) lines.Add(new PopupLine());
        foreach (var doc in info.Documentation) Wrap(doc, width, pal.PopupTextDim, lines);
        if (lines.Count > 24) lines.RemoveRange(24, lines.Count - 24);

        float lh = PopupLineH;
        float w = MaxLength(lines) * PopupCharW + 20;
        float h = lines.Count * lh + 12;
        if (x + w > _view.X + _view.W + 200) x -= w + 8;

        using (PopupBox(paper, id + "_compdesc", x, y, w, h).IsNotInteractable().Enter())
            paper.Draw((canvas, r) => DrawPopupLines(canvas, font, lines, r.Min.X + 10, r.Min.Y + 6, lh));
    }

    #endregion

    #region Signature help

    private CodeSignatureHelp? _signature;
    private int _sigIndex;
    private CancellationTokenSource? _sigCts;

    public void RequestSignatureHelp(bool explicitly, char typed = '\0')
    {
        if (Session == null) return;
        _sigCts?.Cancel();
        _sigCts = new CancellationTokenSource();
        int offset = Document.PositionToOffset(Caret);
        Continue(Session.GetSignatureHelpAsync(offset, _sigCts.Token), Document.Version, help =>
        {
            if (help == null || help.Signatures.Count == 0) { CloseSignature(); return; }
            bool same = _signature != null && _signature.ArgumentListStart == help.ArgumentListStart && _signature.Signatures.Count == help.Signatures.Count;
            _signature = help;
            if (!same || explicitly) _sigIndex = Math.Clamp(help.ActiveSignature, 0, help.Signatures.Count - 1);
        }, requireVersion: false);
    }

    private void CloseSignature()
    {
        _signature = null;
        _sigCts?.Cancel();
    }

    private void BuildSignature(Paper paper, string id, FontFile font)
    {
        var help = _signature!;
        var pal = Palette();
        int index = Math.Clamp(_sigIndex, 0, help.Signatures.Count - 1);
        var sig = help.Signatures[index];
        const int width = 90;

        var line = new PopupLine();
        if (help.Signatures.Count > 1) line.Add($"{index + 1}/{help.Signatures.Count}  ", pal.PopupTextDim);
        var active = help.ActiveParameter >= 0 && help.ActiveParameter < sig.Parameters.Count ? sig.Parameters[help.ActiveParameter] : null;

        int pos = 0;
        foreach (var part in sig.Parts)
        {
            string t = part.Text;
            if (active != null && active.LabelLength > 0 && pos < active.LabelStart + active.LabelLength && pos + t.Length > active.LabelStart)
            {
                int a = Math.Max(0, active.LabelStart - pos), b = Math.Min(t.Length, active.LabelStart + active.LabelLength - pos);
                line.Add(t.Substring(0, a), KindColor(part.Kind, pal));
                line.Add(t.Substring(a, b - a), pal.PopupMatch, true);
                line.Add(t.Substring(b), KindColor(part.Kind, pal));
            }
            else line.Add(t, KindColor(part.Kind, pal));
            pos += t.Length;
        }

        var lines = new List<PopupLine> { line };

        if (active != null && !string.IsNullOrEmpty(active.Documentation))
        {
            lines.Add(new PopupLine());
            var docs = new List<PopupLine>();
            Wrap(active.Documentation!, width, pal.PopupTextDim, docs);
            if (docs.Count > 0) docs[0].Runs.Insert(0, (active.Name + ": ", pal.PopupText, false));
            lines.AddRange(docs);
        }
        if (!string.IsNullOrEmpty(sig.Documentation))
        {
            lines.Add(new PopupLine());
            Wrap(sig.Documentation!, width, pal.PopupTextDim, lines);
        }
        if (lines.Count > 16) lines.RemoveRange(16, lines.Count - 16);

        float lh = PopupLineH;
        float w = Math.Min(MaxLength(lines), 120) * PopupCharW + 20;
        float h = lines.Count * lh + 10;
        var anchor = PointFromPosition(Document.OffsetToPosition(Math.Min(help.ArgumentListStart, Document.Length)));
        float y = anchor.Y - h - 2;
        if (y < _view.Y) y = anchor.Y + _view.LineH + 2;

        using (PopupBox(paper, id + "_signature", anchor.X - 12, y, w, h).IsNotInteractable().Enter())
            paper.Draw((canvas, r) => DrawPopupLines(canvas, font, lines, r.Min.X + 10, r.Min.Y + 5, lh));
    }

    #endregion

    #region Hover

    private TextPosition _hoverPos = new(-1, -1);
    private double _hoverSince;
    private bool _hoverRequested;
    private List<PopupLine>? _hoverLines;
    private Float2 _hoverAnchor;
    private TextRange _hoverRange;
    private CancellationTokenSource? _hoverCts;
    private ElementHandle _hoverHandle;

    private void CloseHover()
    {
        _hoverLines = null;
        _hoverRequested = false;
        _hoverCts?.Cancel();
        _hoverSince = _time;
    }

    private void TickHover(Paper paper)
    {
        if (Options.HoverDelay <= 0) return;

        bool overPopup = _hoverLines != null && _hoverHandle.IsValid && paper.IsElementHovered(_hoverHandle.Data.ID);
        if (overPopup) return;

        if (!_pointerInside || !InTextArea(_pointer) || _drag != DragKind.None || _completion != null || _list != null)
        {
            if (_hoverLines != null && !overPopup) CloseHover();
            _hoverPos = new TextPosition(-1, -1);
            return;
        }

        var pos = PositionFromPoint(_pointer, false);
        var word = WordAt(pos);
        bool beyondText = _pointer.X > ColX(VisualColumn(pos.Line, Document.GetLineLength(pos.Line))) + _view.CharW;
        if (beyondText) word = new TextRange(new TextPosition(-1, -1), new TextPosition(-1, -1));

        var key = word.IsEmpty ? pos : word.Start;
        if (key != _hoverPos)
        {
            if (_hoverLines != null && !(_hoverRange.Contains(pos) && !beyondText)) CloseHover();
            _hoverPos = key;
            _hoverSince = _time;
            _hoverRequested = false;
            return;
        }

        if (_hoverRequested || beyondText || _time - _hoverSince < Options.HoverDelay) return;
        _hoverRequested = true;

        var diags = new List<CodeEditorDiagnostic>();
        foreach (var d in _diagnostics)
        {
            var r = d.Range;
            bool hit = r.IsEmpty ? r.Start.Line == pos.Line && Math.Abs(r.Start.Column - pos.Column) <= 1 : r.Start <= pos && pos <= r.End;
            if (hit) diags.Add(d);
        }

        _hoverRange = word.IsEmpty ? new TextRange(pos, pos) : word;
        _hoverAnchor = PointFromPosition(_hoverRange.Start);
        var pal = Palette();

        List<PopupLine> DiagLines()
        {
            var lines = new List<PopupLine>();
            foreach (var d in diags)
            {
                var color = d.Severity == CodeDiagnosticSeverity.Error ? pal.Error : d.Severity == CodeDiagnosticSeverity.Warning ? pal.Warning : pal.Info;
                var wrapped = new List<PopupLine>();
                Wrap(d.Message, 80, pal.PopupText, wrapped);
                if (wrapped.Count == 0) continue;
                wrapped[0].Runs.Insert(0, (d.Id.Length > 0 ? d.Id + "  " : "", color, false));
                wrapped[0].Length += d.Id.Length > 0 ? d.Id.Length + 2 : 0;
                lines.AddRange(wrapped);
            }
            return lines;
        }

        if (diags.Count > 0) _hoverLines = DiagLines();

        if (Session == null || word.IsEmpty) return;
        _hoverCts?.Cancel();
        _hoverCts = new CancellationTokenSource();
        var hoverKey = _hoverPos;
        Continue(Session.GetQuickInfoAsync(Document.PositionToOffset(word.Start), _hoverCts.Token), Document.Version, info =>
        {
            if (_hoverPos != hoverKey || info == null || (info.Signature.Count == 0 && info.Documentation.Count == 0)) return;
            var lines = DiagLines();
            if (lines.Count > 0) lines.Add(new PopupLine());
            WrapTagged(info.Signature, 80, pal, lines);
            if (info.Documentation.Count > 0) lines.Add(new PopupLine());
            foreach (var doc in info.Documentation) Wrap(doc, 80, pal.PopupTextDim, lines);
            if (lines.Count > 30) lines.RemoveRange(30, lines.Count - 30);
            _hoverLines = lines;
        });
    }

    private void BuildHover(Paper paper, string id, FontFile font)
    {
        var lines = _hoverLines!;
        if (lines.Count == 0) return;
        float lh = PopupLineH;
        float w = MaxLength(lines) * PopupCharW + 20;
        float h = lines.Count * lh + 12;
        float y = _hoverAnchor.Y + _view.LineH + 2;
        if (y + h > _view.Y + _view.H && _hoverAnchor.Y - h - 2 > _view.Y) y = _hoverAnchor.Y - h - 2;

        using (PopupBox(paper, id + "_hover", _hoverAnchor.X - 8, y, w, h).Enter())
        {
            _hoverHandle = paper.CurrentParent;
            paper.Draw((canvas, r) => DrawPopupLines(canvas, font, lines, r.Min.X + 10, r.Min.Y + 6, lh));
        }
    }

    #endregion

    #region Occurrence highlights

    private readonly List<TextRange> _highlights = new();
    private int _highlightKey = -1;
    private double _highlightAt;
    private CancellationTokenSource? _highlightCts;

    private void TickOccurrences()
    {
        if (!Options.HighlightOccurrences) { _highlights.Clear(); return; }
        int key = HashCode.Combine(Caret, Document.Version, Selection.IsEmpty);
        if (key == _highlightKey) return;
        if (_highlightAt == 0 || _time - _caretMoveTime < 0.25) { _highlightAt = _time; if (_time - _caretMoveTime < 0.25) return; }
        _highlightKey = key;
        _highlights.Clear();
        if (!Selection.IsEmpty && !Selection.Range.IsSingleLine) return;

        var word = Selection.IsEmpty ? WordAt(Caret) : Selection.Range;
        if (word.IsEmpty || !IsCodeAt(word.Start.Line, word.Start.Column)) return;

        if (Session != null && Selection.IsEmpty)
        {
            _highlightCts?.Cancel();
            _highlightCts = new CancellationTokenSource();
            int version = Document.Version;
            Continue(Session.GetHighlightsAsync(Document.PositionToOffset(Caret), _highlightCts.Token), version, spans =>
            {
                _highlights.Clear();
                foreach (var s in spans)
                    _highlights.Add(new TextRange(Document.OffsetToPosition(s.Start), Document.OffsetToPosition(s.Start + s.Length)));
                if (_highlights.Count <= 1) _highlights.Clear();
            });
            return;
        }

        // Without a compiler, whole word matches near the caret.
        string needle = Document.GetText(word);
        if (needle.Trim().Length == 0) return;
        int from = Math.Max(0, Caret.Line - 300), to = Math.Min(Document.LineCount - 1, Caret.Line + 300);
        for (int line = from; line <= to; line++)
        {
            string s = Document[line];
            int i = 0;
            while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                bool startOk = i == 0 || !_language.IsWordChar(s[i - 1]);
                bool endOk = i + needle.Length >= s.Length || !_language.IsWordChar(s[i + needle.Length]);
                if (startOk && endOk) _highlights.Add(new TextRange(line, i, line, i + needle.Length));
                i += needle.Length;
            }
        }
        if (_highlights.Count <= 1) _highlights.Clear();
    }

    #endregion

    #region Navigation and refactoring

    public void GoToDefinition() => GoToLocations("Definitions", s => s.GetDefinitionAsync);

    /// <summary>Ctrl+F12: the types implementing or deriving from the symbol, or the members overriding it.</summary>
    public void GoToImplementation() => GoToLocations("Implementations", s => s.GetImplementationsAsync);

    public void GoToBase() => GoToLocations("Bases", s => s.GetBaseDefinitionsAsync);

    public void GoToTypeDefinition() => GoToLocations("Type definitions", s => s.GetTypeDefinitionAsync);

    // Jumps straight to a single result, otherwise lists them.
    private void GoToLocations(string title, Func<CodeLanguageSession, Func<int, CancellationToken, Task<IReadOnlyList<CodeLocation>>>> query)
    {
        if (Session == null) return;
        int offset = Document.PositionToOffset(Caret);
        Continue(query(Session)(offset, CancellationToken.None), Document.Version, locations =>
        {
            if (locations.Count == 0) { OpenList($"No {title.ToLowerInvariant()} found", new List<ListItem>()); return; }
            if (locations.Count == 1) { Navigate(locations[0]); return; }
            ShowLocations(title, locations, lineText: false);
        });
    }

    public void FindReferences()
    {
        if (Session == null) return;
        int offset = Document.PositionToOffset(Caret);
        var word = WordAt(Caret);
        string name = word.IsEmpty ? "symbol" : Document.GetText(word);
        Continue(Session.FindReferencesAsync(offset, CancellationToken.None), Document.Version, locations =>
        {
            ReferencesFound?.Invoke(locations);
            ShowLocations(locations.Count == 1 ? $"1 reference to {name}" : $"{locations.Count} references to {name}", locations, lineText: true);
        });
    }

    private void Navigate(CodeLocation location)
    {
        if (location.IsCurrentDocument)
        {
            var start = Document.OffsetToPosition(location.Start);
            var end = Document.OffsetToPosition(location.Start + location.Length);
            Select(start, end);
            CenterOnCaret(onlyIfOffscreen: true);
            return;
        }
        NavigateRequested?.Invoke(location);
    }

    // Results grouped under a header per file, this file first. With lineText each entry previews its source
    // line with the match highlighted, otherwise it shows the location's display name.
    private void ShowLocations(string title, IReadOnlyList<CodeLocation> locations, bool lineText)
    {
        string thisFile = System.IO.Path.GetFileName(FilePath ?? "this file");
        var groups = locations
            .GroupBy(l => l.IsCurrentDocument ? "" : l.Path ?? "?")
            .OrderBy(g => g.Key.Length == 0 ? 0 : 1).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var items = new List<ListItem>();
        foreach (var group in groups)
        {
            var first = group.First();
            string file = first.IsCurrentDocument ? thisFile : first.Path != null ? System.IO.Path.GetFileName(first.Path) : "assemblies";
            int count = group.Count();
            items.Add(new ListItem(file, count == 1 ? "1" : count.ToString(), null, Header: true));

            foreach (var loc in group.OrderBy(l => l.IsCurrentDocument ? Document.OffsetToPosition(l.Start).Line : l.Line))
            {
                var target = loc;
                int line = loc.IsCurrentDocument ? Document.OffsetToPosition(loc.Start).Line : loc.Line;
                int column = loc.IsCurrentDocument ? Document.OffsetToPosition(loc.Start).Column : loc.Column;
                bool hasSource = loc.IsCurrentDocument || loc.Path != null;

                string detail = loc.Display;
                int matchStart = -1, matchLength = 0;
                if (lineText && hasSource)
                {
                    string source = loc.IsCurrentDocument && line < Document.LineCount ? Document[line] : loc.Display;
                    int indent = FirstNonBlank(source);
                    detail = source.Substring(indent).TrimEnd();
                    if (column >= indent && column - indent + loc.Length <= detail.Length)
                    {
                        matchStart = column - indent;
                        matchLength = loc.Length;
                    }
                }
                string label = hasSource ? (line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                items.Add(new ListItem(label, detail, () => Navigate(target), MatchStart: matchStart, MatchLength: matchLength));
            }
        }
        OpenList(title, items);
    }

    public void FormatDocument()
    {
        if (Session == null || IsReadOnly) return;
        bool whole = Selection.IsEmpty;
        int start = whole ? 0 : Document.PositionToOffset(Selection.Start);
        int length = whole ? -1 : Document.PositionToOffset(Selection.End) - start;
        Continue(Session.FormatAsync(start, length, CancellationToken.None), Document.Version, edits => ApplyEdits(edits));
    }

    private void FormatOnType(char c)
    {
        if (!Options.FormatOnType || Session == null || IsReadOnly || _selections.Count != 1 || !_language.IsFormatOnTypeTrigger(c)) return;
        int offset = Document.PositionToOffset(Caret);
        Continue(Session.FormatOnTypeAsync(offset, c, CancellationToken.None), Document.Version, edits => ApplyEdits(edits));
    }

    public void OpenCodeActions()
    {
        if (Session == null) return;
        int start = Document.PositionToOffset(Selection.Start);
        int length = Document.PositionToOffset(Selection.End) - start;
        Continue(Session.GetCodeActionsAsync(start, length, CancellationToken.None), Document.Version, actions =>
        {
            if (actions.Count == 0) { OpenList("No code actions", new List<ListItem>()); return; }
            var items = new List<ListItem>();
            foreach (var a in actions)
            {
                var action = a;
                items.Add(new ListItem(a.Title, a.IsQuickFix ? "fix" : "", () => ApplyCodeAction(action)));
            }
            OpenList("Code actions", items);
        });
    }

    private void ApplyCodeAction(CodeAction action)
    {
        if (action.Resolve == null)
        {
            ApplyEdits(action.Edits);
            return;
        }
        var task = action.Resolve(CancellationToken.None);
        task.ContinueWith(t => Post(() =>
        {
            if (t.IsFaulted)
            {
                OpenList($"'{action.Title}' failed: {t.Exception?.GetBaseException().Message}", new List<ListItem>());
                return;
            }
            var result = t.IsCompletedSuccessfully ? t.Result : null;
            if (result == null || (result.Edits.Count == 0 && result.OtherDocuments.Count == 0))
            {
                OpenList($"'{action.Title}' made no text changes", new List<ListItem>());
                return;
            }
            ApplyEdits(result.Edits);
            if (result.OtherDocuments.Count > 0) ExternalEditsRequested?.Invoke(result.OtherDocuments);
        }), TaskScheduler.Default);
    }

    private void OpenContextMenu(Float2 p)
    {
        var paper = _paper!;
        bool hasSession = Session != null;
        bool ro = IsReadOnly;
        ContextMenu.Show(p.X, p.Y, menu =>
        {
            if (hasSession)
            {
                menu.Item("Go to Definition", GoToDefinition);
                menu.Item("Go to Implementation", GoToImplementation);
                menu.Item("Go to Base", GoToBase);
                menu.Item("Go to Type Definition", GoToTypeDefinition);
                menu.Item("Find All References", FindReferences);
                menu.Item("Rename Symbol", StartRename, !ro);
                menu.Item("Quick Actions", OpenCodeActions);
                menu.Separator();
            }
            menu.Item("Cut", () => Cut(paper), !ro);
            menu.Item("Copy", () => Copy(paper));
            menu.Item("Paste", () => Paste(paper), !ro);
            menu.Separator();
            menu.Item("Toggle Comment", ToggleLineComment, !ro);
            if (hasSession) menu.Item("Format Document", FormatDocument, !ro);
            menu.Item("Find", () => OpenFind(false));
            menu.Item("Go to Line", OpenGoToLine);
            menu.Separator();
            menu.Item("Fold All", FoldAll);
            menu.Item("Unfold All", UnfoldAll);
        });
    }

    #endregion

    #region List popup

    private sealed record ListItem(string Label, string Detail, Action? Invoke, bool Header = false, int MatchStart = -1, int MatchLength = 0);

    private sealed class ListState
    {
        public string Title = "";
        public List<ListItem> Items = new();
        public int Selected;
        public int Scroll;
        public Float2 Anchor;
    }

    private ListState? _list;
    private const int ListRows = 16;

    private void OpenList(string title, List<ListItem> items)
    {
        var list = new ListState { Title = title, Items = items, Anchor = PointFromPosition(Caret) };
        list.Selected = NextSelectable(list, -1, 1);
        _list = list;
    }

    // The nearest entry that is not a file header, stepping from <paramref name="from"/> in <paramref name="step"/>.
    private static int NextSelectable(ListState list, int from, int step)
    {
        for (int i = from + step; i >= 0 && i < list.Items.Count; i += step)
            if (!list.Items[i].Header) return i;
        return from >= 0 && from < list.Items.Count && !list.Items[from].Header ? from : -1;
    }

    private void MoveListSelection(int delta)
    {
        var list = _list!;
        int target = list.Selected;
        int step = Math.Sign(delta);
        for (int n = 0; n < Math.Abs(delta); n++)
        {
            int next = NextSelectable(list, target, step);
            if (next == target || next < 0) break;
            target = next;
        }
        list.Selected = target;
    }

    private void InvokeListSelection()
    {
        var list = _list;
        _list = null;
        if (list != null && list.Selected >= 0 && list.Selected < list.Items.Count) list.Items[list.Selected].Invoke?.Invoke();
    }

    private void BuildList(Paper paper, string id, FontFile font)
    {
        var list = _list!;
        var pal = Palette();
        float cw = PopupCharW;
        float rowH = MathF.Round(PopupLineH * 1.1f);
        int rows = Math.Min(ListRows, list.Items.Count);

        int labelChars = 0, chars = list.Title.Length;
        foreach (var it in list.Items) if (!it.Header) labelChars = Math.Max(labelChars, it.Label.Length);
        foreach (var it in list.Items)
            chars = Math.Max(chars, Math.Min(110, (it.Header ? it.Label.Length + it.Detail.Length + 2 : labelChars + it.Detail.Length + 2)));
        float w = Math.Max(260, chars * cw + 40);
        float h = (rows + 1) * rowH + 6;

        if (list.Selected >= 0)
        {
            if (list.Selected < list.Scroll) list.Scroll = list.Selected > 0 && list.Items[list.Selected - 1].Header ? list.Selected - 1 : list.Selected;
            if (list.Selected >= list.Scroll + rows) list.Scroll = list.Selected - rows + 1;
        }
        list.Scroll = Math.Clamp(list.Scroll, 0, Math.Max(0, list.Items.Count - rows));

        ElementHandle popup = default;
        var box = PopupBox(paper, id + "_list", list.Anchor.X, list.Anchor.Y + _view.LineH + 2, w, h)
            .OnScroll(e =>
            {
                list.Scroll = Math.Clamp(list.Scroll - (int)MathF.Round(e.Delta * 3), 0, Math.Max(0, list.Items.Count - rows));
                e.StopPropagation();
            })
            .OnPress(e =>
            {
                int row = (int)((e.LocalPosition.Y - popup.Data.LayoutRect.Min.Y - 3) / rowH) - 1;
                int index = list.Scroll + row;
                if (index < 0 || index >= list.Items.Count || list.Items[index].Header) return;
                list.Selected = index;
                InvokeListSelection();
            });

        using (box.Enter())
        {
            popup = paper.CurrentParent;
            paper.Draw((canvas, r) =>
            {
                float glyphH = _glyphH * (PopupFontSize / FontSize);
                float x0 = r.Min.X + 10;
                DrawPlain(canvas, font, list.Title, x0, r.Min.Y + 3 + (rowH - glyphH) * 0.5f, pal.PopupTextDim, PopupFontSize);
                int maxDetail = Math.Max(8, (int)((r.Size.X - 30) / cw) - labelChars - 2);

                for (int i = 0; i < rows; i++)
                {
                    int index = list.Scroll + i;
                    if (index >= list.Items.Count) break;
                    var item = list.Items[index];
                    float ry = r.Min.Y + 3 + (i + 1) * rowH;
                    float ty = ry + (rowH - glyphH) * 0.5f;

                    if (item.Header)
                    {
                        canvas.RectFilled(r.Min.X + 3, ry, r.Size.X - 6, rowH, new Color32(255, 255, 255, 8));
                        DrawPlain(canvas, font, item.Label, x0, ty, pal.PopupText, PopupFontSize);
                        DrawPlain(canvas, font, item.Detail, x0 + (item.Label.Length + 2) * cw, ty, pal.PopupTextDim, PopupFontSize);
                        continue;
                    }

                    if (index == list.Selected) canvas.RoundedRectFilled(r.Min.X + 3, ry, r.Size.X - 6, rowH, 4, pal.PopupSelection);
                    float lx = x0 + cw * 2;
                    DrawPlain(canvas, font, item.Label.PadLeft(labelChars), lx, ty, pal.PopupTextDim, PopupFontSize);

                    float dx = lx + (labelChars + 2) * cw;
                    string detail = item.Detail.Length > maxDetail ? item.Detail.Substring(0, maxDetail - 3) + "..." : item.Detail;
                    if (item.MatchStart >= 0 && item.MatchStart + item.MatchLength <= detail.Length)
                    {
                        canvas.RoundedRectFilled(dx + item.MatchStart * cw - 1, ry + 2, item.MatchLength * cw + 2, rowH - 4, 2, pal.FindMatch);
                        DrawPlain(canvas, font, detail, dx, ty, pal.PopupText, PopupFontSize);
                    }
                    else DrawPlain(canvas, font, detail, dx, ty, pal.PopupText, PopupFontSize);
                }

                if (list.Items.Count > rows)
                {
                    float trackH = r.Size.Y - rowH - 6;
                    float th = MathF.Max(16, trackH * rows / list.Items.Count);
                    float t = list.Scroll / (float)Math.Max(1, list.Items.Count - rows);
                    canvas.RectFilled(r.Max.X - 5, r.Min.Y + 3 + rowH + t * (trackH - th), 3, th, pal.Scrollbar);
                }
            });
        }
    }

    #endregion

    #region Inline inputs

    private bool _findBarOpen, _replaceOpen;
    private int _focusFindFrames;
    private TextPosition _findStart;
    private bool _gotoOpen;
    private string _gotoText = "";
    private int _focusGotoFrames;
    private bool _renameOpen;
    private string _renameText = "";
    private TextPosition _renameAt;
    private int _focusRenameFrames;
    private bool _renameBusy;

    public bool IsFindOpen => _findBarOpen;

    public void OpenFind(bool replace)
    {
        _findBarOpen = true;
        _replaceOpen = replace;
        var sel = Selection;
        if (!sel.IsEmpty && sel.Range.IsSingleLine) FindQuery = Document.GetText(sel.Range);
        else if (sel.IsEmpty && FindQuery.Length == 0)
        {
            var w = WordAt(Caret);
            if (!w.IsEmpty) FindQuery = Document.GetText(w);
        }
        _findStart = Selection.Start;
        _focusFindFrames = 2;
    }

    public void CloseFind()
    {
        _findBarOpen = false;
        Focus();
    }

    public void OpenGoToLine()
    {
        _gotoOpen = true;
        _gotoText = "";
        _focusGotoFrames = 2;
    }

    public void StartRename()
    {
        if (Session == null || IsReadOnly) return;
        var w = WordAt(Caret);
        if (w.IsEmpty || !IsCodeAt(w.Start.Line, w.Start.Column)) return;
        _renameOpen = true;
        _renameBusy = false;
        _renameAt = w.Start;
        _renameText = Document.GetText(w);
        _focusRenameFrames = 2;
    }

    // A one line input styled for popups. Focus lands on it over two frames so select all applies after focus.
    private ElementHandle InputField(Paper paper, string id, string value, Action<string> onChange, ref int focusFrames, float width, string placeholder, FontFile font)
    {
        var pal = Palette();
        var settings = ElementBuilder.TextInputSettings.Default;
        settings.Font = font;
        settings.TextColor = pal.PopupText;
        settings.Placeholder = placeholder;
        settings.PlaceholderColor = pal.PopupTextDim;
        if (focusFrames == 1)
        {
            settings.ForceValue = value;
            settings.ForceSelectAll = true;
        }

        ElementHandle handle;
        using (paper.Box(id)
            .Width(width).Height(PopupLineH + 6)
            .Padding(6, 6, 0, 0)
            .BackgroundColor(SDColor.FromArgb(60, 0, 0, 0))
            .BorderColor(pal.PopupBorder).BorderWidth(1)
            .Focused.BorderColor((SDColor)pal.Caret).End()
            .Rounded(4)
            .FontSize(PopupFontSize)
            .Alignment(PaperUI.TextAlignment.MiddleLeft)
            .TextField(value, settings, onChange)
            .Enter())
        {
            handle = paper.CurrentParent;
            if (focusFrames == 2) paper.SetFocus(handle);
        }
        if (focusFrames > 0) focusFrames--;
        _inputHandles.Add(handle.Data.ID);
        return handle;
    }

    private void ToolButton(Paper paper, string id, string label, bool active, Action onClick, FontFile font, string? tooltip = null)
    {
        var pal = Palette();
        float h = PopupLineH + 6;
        paper.Box(id)
            .Width(Math.Max(h, label.Length * PopupCharW + 12)).Height(h)
            .Rounded(4)
            .BackgroundColor(active ? (SDColor)pal.PopupSelection : SDColor.FromArgb(0, 0, 0, 0))
            .Hovered.BackgroundColor(active ? (SDColor)pal.PopupSelection : SDColor.FromArgb(30, 255, 255, 255)).End()
            .Text(label, font).FontSize(PopupFontSize).TextColor((SDColor)(active ? pal.PopupText : pal.PopupTextDim))
            .Alignment(PaperUI.TextAlignment.MiddleCenter)
            .IsNotFocusable()
            .OnClick(_ => onClick());
    }

    private void BuildFindBar(Paper paper, string id, FontFile font)
    {
        var pal = Palette();
        float rowH = PopupLineH + 6;
        float fieldW = Math.Max(160, Math.Min(320, _view.TextW * 0.4f));
        float w = fieldW + 8 + rowH * 3 + 90 + rowH * 3 + 24;
        float h = rowH * (_replaceOpen ? 2 : 1) + 12 + (_replaceOpen ? 4 : 0);
        float x = _view.MinimapX - w - 12;
        float y = _view.Y + 4;

        using (PopupBox(paper, id + "_find", x, y, w, h).Padding(6).Gap(4).Enter())
        {
            using (paper.Row(id + "_find_row").Height(rowH).Gap(2).Enter())
            {
                var q = InputField(paper, id + "_find_q", FindQuery, v =>
                {
                    FindQuery = v;
                    if (v.Length > 0)
                    {
                        var start = _findStart;
                        SetCaretQuiet(start);
                        FindNextMatch();
                    }
                }, ref _focusFindFrames, fieldW, "Find", font);

                ToolButton(paper, id + "_f_case", "Aa", FindCaseSensitive, () => FindCaseSensitive = !FindCaseSensitive, font);
                ToolButton(paper, id + "_f_word", "ab", FindWholeWord, () => FindWholeWord = !FindWholeWord, font);
                ToolButton(paper, id + "_f_regex", ".*", FindRegex, () => FindRegex = !FindRegex, font);

                int count = FindMatchCount;
                int current = count > 0 ? CurrentMatchIndex() : 0;
                string status = FindQuery.Length == 0 ? "" : FindQueryInvalid ? "bad regex" : count == 0 ? "No results" : current > 0 ? $"{current} of {count}" : $"{count} found";
                paper.Box(id + "_f_count").Width(90).Height(rowH)
                    .Text(status, font).FontSize(PopupFontSize * 0.9f)
                    .TextColor((SDColor)(count == 0 && FindQuery.Length > 0 ? pal.Error : pal.PopupTextDim))
                    .Alignment(PaperUI.TextAlignment.MiddleCenter).IsNotInteractable();

                ToolButton(paper, id + "_f_prev", "<", false, () => FindNextMatch(false), font);
                ToolButton(paper, id + "_f_next", ">", false, () => FindNextMatch(true), font);
                ToolButton(paper, id + "_f_close", "x", false, CloseFind, font);

                if (paper.IsElementFocused(q.Data.ID))
                {
                    if (paper.IsKeyPressed(PaperKey.Enter) || paper.IsKeyPressed(PaperKey.F3))
                        FindNextMatch(!Shift(paper));
                    else if (paper.IsKeyPressed(PaperKey.Escape)) CloseFind();
                    else if (Ctrl(paper) && paper.IsKeyPressed(PaperKey.H)) _replaceOpen = true;
                }
            }

            if (_replaceOpen)
            {
                using (paper.Row(id + "_repl_row").Height(rowH).Gap(2).Enter())
                {
                    int none = 0;
                    var r = InputField(paper, id + "_find_r", ReplaceText, v => ReplaceText = v, ref none, fieldW, "Replace", font);
                    ToolButton(paper, id + "_r_one", "Replace", false, ReplaceNext, font);
                    ToolButton(paper, id + "_r_all", "All", false, () => ReplaceAll(), font);

                    if (paper.IsElementFocused(r.Data.ID))
                    {
                        if (paper.IsKeyPressed(PaperKey.Enter)) { if (Ctrl(paper)) ReplaceAll(); else ReplaceNext(); }
                        else if (paper.IsKeyPressed(PaperKey.Escape)) CloseFind();
                    }
                }
            }
        }
    }

    // Moves the caret without the side effects of a user move, for incremental search.
    private void SetCaretQuiet(TextPosition p)
    {
        _selections.Clear();
        _selections.Add(new CodeSelection(Document.Clamp(p)));
        _primary = 0;
    }

    private void BuildGoToLine(Paper paper, string id, FontFile font)
    {
        float rowH = PopupLineH + 6;
        float w = 300;
        float x = _view.TextX + (_view.TextW - w) * 0.5f;
        using (PopupBox(paper, id + "_goto", x, _view.Y + 4, w, rowH + 12).Padding(6).Enter())
        {
            var f = InputField(paper, id + "_goto_f", _gotoText, v => _gotoText = v, ref _focusGotoFrames, w - 12,
                $"Go to line (1 to {Document.LineCount}), line:col", font);

            if (paper.IsElementFocused(f.Data.ID))
            {
                if (paper.IsKeyPressed(PaperKey.Enter))
                {
                    string[] parts = _gotoText.Split(':', ',');
                    if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int line))
                    {
                        int col = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int c) ? c : 1;
                        GoToLine(line, col);
                    }
                    _gotoOpen = false;
                    Focus();
                }
                else if (paper.IsKeyPressed(PaperKey.Escape)) { _gotoOpen = false; Focus(); }
            }
            else if (_focusGotoFrames == 0 && !paper.IsElementFocused(_handle.Data.ID) && paper.IsPointerPressed(PaperMouseBtn.Left)) _gotoOpen = false;
        }
    }

    private void BuildRename(Paper paper, string id, FontFile font)
    {
        var anchor = PointFromPosition(_renameAt);
        float rowH = PopupLineH + 6;
        float w = Math.Max(200, (_renameText.Length + 8) * PopupCharW);
        using (PopupBox(paper, id + "_rename", anchor.X - 8, anchor.Y + _view.LineH + 2, w + 12, rowH + 12).Padding(6).Enter())
        {
            var f = InputField(paper, id + "_rename_f", _renameText, v => _renameText = v, ref _focusRenameFrames, w, "New name", font);
            if (paper.IsElementFocused(f.Data.ID))
            {
                if (paper.IsKeyPressed(PaperKey.Enter) && !_renameBusy) ApplyRename();
                else if (paper.IsKeyPressed(PaperKey.Escape)) { _renameOpen = false; Focus(); }
            }
        }
    }

    private void ApplyRename()
    {
        string name = _renameText.Trim();
        _renameOpen = false;
        Focus();
        if (Session == null || name.Length == 0) return;
        _renameBusy = true;
        Continue(Session.RenameAsync(Document.PositionToOffset(_renameAt), name, CancellationToken.None), Document.Version, result =>
        {
            _renameBusy = false;
            if (result == null) return;
            ApplyEdits(result.Edits);
            if (result.OtherDocuments.Count > 0) ExternalEditsRequested?.Invoke(result.OtherDocuments);
        });
    }

    #endregion
}
