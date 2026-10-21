// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    private string? _lineCopyText;

    public bool IsReadOnly => Options.ReadOnly;

    /// <summary>Groups everything <paramref name="body"/> does into one undo step.</summary>
    public void Edit(CodeEditKind kind, Action body)
    {
        if (IsReadOnly) return;
        Undo.Begin(SnapshotSelections(), kind, _time);
        try { body(); }
        finally
        {
            NormalizeSelections();
            Undo.End(SnapshotSelections());
        }
        _keepGoalColumns = false;
        _caretMoveTime = _time;
        _revealCaret = true;
    }

    // Runs an edit for every caret, last in the document first, so earlier carets are not shifted
    // before their own edit runs. The callback returns the caret's new selection, or null to keep the mapped one.
    private void ForEachSelection(Func<int, CodeSelection, CodeSelection?> edit)
    {
        var order = new List<int>(_selections.Count);
        for (int i = 0; i < _selections.Count; i++) order.Add(i);
        order.Sort((a, b) => _selections[b].Start.CompareTo(_selections[a].Start));
        foreach (int i in order)
        {
            var result = edit(i, _selections[i]);
            if (result != null) _selections[i] = result.Value;
        }
    }

    public void UndoEdit()
    {
        var step = Undo.PopUndo();
        if (step == null) return;
        _replaying = true;
        try
        {
            for (int i = step.Changes.Count - 1; i >= 0; i--)
            {
                var c = step.Changes[i];
                Document.Replace(new TextRange(c.Range.Start, c.NewEnd), c.OldText);
            }
        }
        finally { _replaying = false; }
        RestoreSelections(step.Before);
        AfterCaretMove(true);
    }

    public void RedoEdit()
    {
        var step = Undo.PopRedo();
        if (step == null) return;
        _replaying = true;
        try
        {
            foreach (var c in step.Changes) Document.Replace(c.Range, c.NewText);
        }
        finally { _replaying = false; }
        RestoreSelections(step.After);
        AfterCaretMove(true);
    }

    /// <summary>Applies edits given in offsets of the current text as one undo step.</summary>
    public void ApplyEdits(IReadOnlyList<CodeTextEdit> edits, CodeEditKind kind = CodeEditKind.Other)
    {
        if (edits.Count == 0) return;
        var sorted = new List<CodeTextEdit>(edits);
        sorted.Sort((a, b) => b.Start.CompareTo(a.Start));
        Edit(kind, () =>
        {
            foreach (var e in sorted)
            {
                var a = Document.OffsetToPosition(e.Start);
                var b = Document.OffsetToPosition(e.Start + e.Length);
                Document.Replace(new TextRange(a, b), e.NewText);
            }
        });
    }

    /// <summary>Inserts text at every caret, replacing selections, as if it were typed or pasted.</summary>
    public void InsertText(string text, CodeEditKind kind = CodeEditKind.Other)
    {
        Edit(kind, () => ForEachSelection((_, s) => new CodeSelection(Document.Replace(s.Range, text).NewEnd)));
    }

    #region Typing

    public void TypeChar(char c)
    {
        if (IsReadOnly) return;
        _typingNow = true;
        try { Edit(CodeEditKind.Typing, () => ForEachSelection((_, s) => TypeCharAt(s, c))); }
        finally { _typingNow = false; }
    }

    private CodeSelection? TypeCharAt(CodeSelection s, char c)
    {
        string line = Document[s.Caret.Line];
        char next = s.Caret.Column < line.Length ? line[s.Caret.Column] : '\0';
        char prev = s.Caret.Column > 0 ? line[s.Caret.Column - 1] : '\0';

        if (Options.AutoClosePairs)
        {
            foreach (var (open, close) in _language.AutoClosingPairs)
            {
                if (s.IsEmpty && c == close && next == close && (open != close || IsInsideOwnString(s.Caret)))
                    return new CodeSelection(new TextPosition(s.Caret.Line, s.Caret.Column + 1));

                if (c != open) continue;

                if (!s.IsEmpty && (open != close || s.Start.Line == s.End.Line))
                {
                    var start = s.Start;
                    var end = s.End;
                    Document.Insert(end, close.ToString());
                    Document.Insert(start, open.ToString());
                    var innerStart = new TextPosition(start.Line, start.Column + 1);
                    var innerEnd = end.Line == start.Line ? new TextPosition(end.Line, end.Column + 1) : end;
                    return s.IsReversed ? new CodeSelection(innerEnd, innerStart) : new CodeSelection(innerStart, innerEnd);
                }

                if (s.IsEmpty && CanAutoClose(s.Caret, open, close, prev, next))
                {
                    Document.Insert(s.Caret, string.Concat(open, close));
                    return new CodeSelection(new TextPosition(s.Caret.Line, s.Caret.Column + 1));
                }
                break;
            }
        }

        var change = Document.Replace(s.Range, c.ToString());
        var caret = change.NewEnd;

        if (Options.AutoIndent && _language.OutdentsOn(c))
            caret = ReindentClosingLine(caret);
        return new CodeSelection(caret);
    }

    private bool CanAutoClose(TextPosition at, char open, char close, char prev, char next)
    {
        bool nextOk = next == '\0' || char.IsWhiteSpace(next) || next == ')' || next == ']' || next == '}' || next == ';' || next == ',';
        if (!nextOk) return false;

        var kind = TokenKindBefore(at);
        bool inText = kind is CodeTokenKind.String or CodeTokenKind.Char or CodeTokenKind.Comment or CodeTokenKind.DocComment;
        if (open == close)
        {
            if (inText) return false;
            return !_language.IsWordChar(prev) || prev == '$' || prev == '@';
        }
        return !inText;
    }

    // A quote is overtyped only when it closes the literal the caret is in.
    private bool IsInsideOwnString(TextPosition p)
    {
        var kind = TokenKindBefore(p);
        return kind is CodeTokenKind.String or CodeTokenKind.Char or CodeTokenKind.StringEscape || p.Column > 0 && Document[p.Line][p.Column - 1] is '"' or '\'';
    }

    // After typing a closer as the first thing on a line, lines it up with the line holding its opener.
    private TextPosition ReindentClosingLine(TextPosition caret)
    {
        string line = Document[caret.Line];
        int first = FirstNonBlank(line);
        if (first != caret.Column - 1) return caret;
        if (!TryFindMatchingBracket(caret, out var open, out _)) return caret;
        if (open.Line == caret.Line) return caret;

        string indent = IndentOf(Document[open.Line]);
        if (indent == line.Substring(0, first)) return caret;
        Document.Replace(new TextRange(caret.Line, 0, caret.Line, first), indent);
        return new TextPosition(caret.Line, indent.Length + 1);
    }

    public void NewLine()
    {
        Edit(CodeEditKind.Other, () => ForEachSelection((_, s) =>
        {
            string line = Document[s.Start.Line];
            string before = line.Substring(0, s.Start.Column);
            string after = Document[s.End.Line].Substring(s.End.Column);
            string indent = Options.AutoIndent ? IndentOf(line) : "";
            if (indent.Length > before.Length) indent = indent.Substring(0, before.Length);

            bool deeper = Options.AutoIndent && _language.IndentsNextLine(before);
            string afterTrim = after.TrimStart();
            bool betweenPair = deeper && afterTrim.Length > 0 && IsCloserFor(before.TrimEnd(), afterTrim[0]);

            if (betweenPair)
            {
                string inner = indent + IndentUnit;
                var removeWs = new TextRange(s.Start, new TextPosition(s.End.Line, s.End.Column + (after.Length - afterTrim.Length)));
                var change = Document.Replace(removeWs, "\n" + inner + "\n" + indent);
                return new CodeSelection(new TextPosition(change.Range.Start.Line + 1, inner.Length));
            }

            string newIndent = deeper ? indent + IndentUnit : indent;
            int wsAfter = after.Length - afterTrim.Length;
            var range = new TextRange(s.Start, new TextPosition(s.End.Line, s.End.Column + wsAfter));
            var c = Document.Replace(range, "\n" + newIndent);

            // An empty line left behind keeps no trailing indentation.
            string left = Document[c.Range.Start.Line];
            if (left.Trim().Length == 0 && left.Length > 0)
                Document.Replace(new TextRange(c.Range.Start.Line, 0, c.Range.Start.Line, left.Length), "");
            return new CodeSelection(new TextPosition(c.Range.Start.Line + 1, newIndent.Length));
        }));
    }

    private bool IsCloserFor(string before, char closer)
    {
        if (before.Length == 0) return false;
        char open = before[^1];
        foreach (var (o, c) in _language.Brackets)
            if (o == open && c == closer) return true;
        return false;
    }

    public void InsertLineBelow()
    {
        Move((s, _) => new TextPosition(s.Caret.Line, Document.GetLineLength(s.Caret.Line)), false);
        NewLine();
    }

    public void InsertLineAbove()
    {
        Edit(CodeEditKind.Other, () => ForEachSelection((_, s) =>
        {
            int line = s.Caret.Line;
            string indent = IndentOf(Document[line]);
            Document.Insert(new TextPosition(line, 0), indent + "\n");
            return new CodeSelection(new TextPosition(line, indent.Length));
        }));
    }

    #endregion

    #region Deleting

    public void Backspace(bool word)
    {
        _typingNow = true;
        try { BackspaceCore(word); }
        finally { _typingNow = false; }
    }

    private void BackspaceCore(bool word)
    {
        Edit(CodeEditKind.Deleting, () => ForEachSelection((_, s) =>
        {
            if (!s.IsEmpty) return new CodeSelection(Document.Delete(s.Range).NewEnd);
            var p = s.Caret;
            if (p == TextPosition.Zero) return null;

            TextPosition from;
            if (word) from = WordLeft(p);
            else if (p.Column == 0) from = new TextPosition(p.Line - 1, Document.GetLineLength(p.Line - 1));
            else
            {
                string line = Document[p.Line];
                from = new TextPosition(p.Line, p.Column - 1);

                // In leading spaces, delete back to the previous indent stop.
                if (Options.InsertSpaces && line[p.Column - 1] == ' ' && FirstNonBlank(line) >= p.Column)
                {
                    int tab = Math.Max(1, Options.TabSize);
                    int target = (p.Column - 1) / tab * tab;
                    int c = p.Column;
                    while (c > target && line[c - 1] == ' ') c--;
                    from = new TextPosition(p.Line, c);
                }
                else if (Options.AutoClosePairs && p.Column < line.Length)
                {
                    foreach (var (o, cl) in _language.AutoClosingPairs)
                        if (line[p.Column - 1] == o && line[p.Column] == cl)
                            return new CodeSelection(Document.Delete(new TextRange(from, new TextPosition(p.Line, p.Column + 1))).NewEnd);
                }
            }
            return new CodeSelection(Document.Delete(new TextRange(from, p)).NewEnd);
        }));
    }

    public void DeleteForward(bool word)
    {
        Edit(CodeEditKind.Deleting, () => ForEachSelection((_, s) =>
        {
            if (!s.IsEmpty) return new CodeSelection(Document.Delete(s.Range).NewEnd);
            var p = s.Caret;
            TextPosition to;
            if (word) to = WordRight(p);
            else if (p.Column >= Document.GetLineLength(p.Line))
            {
                if (p.Line >= Document.LineCount - 1) return null;
                to = new TextPosition(p.Line + 1, 0);
            }
            else to = new TextPosition(p.Line, p.Column + 1);
            return new CodeSelection(Document.Delete(new TextRange(p, to)).NewEnd);
        }));
    }

    public void DeleteLines()
    {
        Edit(CodeEditKind.Other, () =>
        {
            foreach (var (a, b) in SelectedLineBlocks(descending: true))
            {
                TextRange range;
                if (b + 1 < Document.LineCount) range = new TextRange(a, 0, b + 1, 0);
                else if (a > 0) range = new TextRange(a - 1, Document.GetLineLength(a - 1), b, Document.GetLineLength(b));
                else range = new TextRange(a, 0, b, Document.GetLineLength(b));
                Document.Delete(range);
            }
            for (int i = 0; i < _selections.Count; i++) _selections[i] = new CodeSelection(_selections[i].Caret);
        });
    }

    // Line blocks touched by selections, merged where they overlap or touch.
    private List<(int First, int Last)> SelectedLineBlocks(bool descending)
    {
        var blocks = new List<(int, int)>();
        foreach (var s in OrderedSelections())
        {
            int a = s.Start.Line, b = s.End.Line;
            if (b > a && s.End.Column == 0) b--;
            if (blocks.Count > 0 && a <= blocks[^1].Item2 + 1)
            {
                blocks[^1] = (blocks[^1].Item1, Math.Max(blocks[^1].Item2, b));
                continue;
            }
            blocks.Add((a, b));
        }
        if (descending) blocks.Reverse();
        return blocks;
    }

    #endregion

    #region Indentation and comments

    /// <summary>Tab: indents selected lines, or inserts indentation at each caret.</summary>
    public void Indent()
    {
        bool anySelection = false;
        foreach (var s in _selections) if (!s.IsEmpty) anySelection = true;

        if (!anySelection)
        {
            Edit(CodeEditKind.Typing, () => ForEachSelection((_, s) =>
            {
                string text = IndentUnit;
                if (Options.InsertSpaces)
                {
                    int tab = Math.Max(1, Options.TabSize);
                    int v = VisualColumn(s.Start.Line, s.Start.Column);
                    text = new string(' ', tab - v % tab);
                }
                return new CodeSelection(Document.Replace(s.Range, text).NewEnd);
            }));
            return;
        }

        Edit(CodeEditKind.Other, () =>
        {
            foreach (var (a, b) in SelectedLineBlocks(true))
                for (int line = a; line <= b; line++)
                    if (Document.GetLineLength(line) > 0) Document.Insert(new TextPosition(line, 0), IndentUnit);
        });
    }

    public void Outdent()
    {
        Edit(CodeEditKind.Other, () =>
        {
            int tab = Math.Max(1, Options.TabSize);
            foreach (var (a, b) in SelectedLineBlocks(true))
                for (int line = a; line <= b; line++)
                {
                    string s = Document[line];
                    int n = 0;
                    if (s.Length > 0 && s[0] == '\t') n = 1;
                    else while (n < tab && n < s.Length && s[n] == ' ') n++;
                    if (n > 0) Document.Delete(new TextRange(line, 0, line, n));
                }
        });
    }

    public void ToggleLineComment()
    {
        string? token = _language.LineComment;
        if (token == null) { ToggleBlockComment(); return; }

        Edit(CodeEditKind.Other, () =>
        {
            foreach (var (a, b) in SelectedLineBlocks(true))
            {
                bool allCommented = true;
                int minIndent = int.MaxValue;
                for (int line = a; line <= b; line++)
                {
                    string s = Document[line];
                    if (s.Trim().Length == 0) continue;
                    int first = FirstNonBlank(s);
                    minIndent = Math.Min(minIndent, first);
                    if (string.CompareOrdinal(s, first, token, 0, token.Length) != 0) allCommented = false;
                }
                if (minIndent == int.MaxValue) continue;

                for (int line = b; line >= a; line--)
                {
                    string s = Document[line];
                    if (s.Trim().Length == 0) continue;
                    if (allCommented)
                    {
                        int first = FirstNonBlank(s);
                        int len = token.Length + (first + token.Length < s.Length && s[first + token.Length] == ' ' ? 1 : 0);
                        Document.Delete(new TextRange(line, first, line, first + len));
                    }
                    else Document.Insert(new TextPosition(line, minIndent), token + " ");
                }
            }
        });
    }

    public void ToggleBlockComment()
    {
        string? open = _language.BlockCommentStart, close = _language.BlockCommentEnd;
        if (open == null || close == null) return;

        Edit(CodeEditKind.Other, () => ForEachSelection((_, s) =>
        {
            var range = s.IsEmpty ? WordAt(s.Caret) : s.Range;
            string text = Document.GetText(range);
            if (text.StartsWith(open, StringComparison.Ordinal) && text.EndsWith(close, StringComparison.Ordinal) && text.Length >= open.Length + close.Length)
            {
                var c = Document.Replace(range, text.Substring(open.Length, text.Length - open.Length - close.Length));
                return new CodeSelection(c.Range.Start, c.NewEnd);
            }
            var r = Document.Replace(range, open + text + close);
            return new CodeSelection(new TextPosition(r.Range.Start.Line, r.Range.Start.Column + open.Length),
                new TextPosition(r.NewEnd.Line, r.NewEnd.Column - close.Length));
        }));
    }

    #endregion

    #region Lines

    /// <summary>Copies the selected lines below them. The carets follow the copy when <paramref name="down"/> is true.</summary>
    public void DuplicateLines(bool down = true)
    {
        var blocks = SelectedLineBlocks(true);
        Edit(CodeEditKind.Other, () =>
        {
            var saved = SnapshotSelections();
            foreach (var (a, b) in blocks)
            {
                string block = Document.GetText(new TextRange(a, 0, b, Document.GetLineLength(b)));
                Document.Insert(new TextPosition(b, Document.GetLineLength(b)), "\n" + block);
            }

            _selections.Clear();
            foreach (var s in saved)
            {
                int shift = 0;
                foreach (var (a, b) in blocks)
                {
                    int count = b - a + 1;
                    if (b < s.Start.Line) shift += count;
                    else if (down && s.Start.Line >= a && s.Start.Line <= b) shift += count;
                }
                _selections.Add(new CodeSelection(new TextPosition(s.Anchor.Line + shift, s.Anchor.Column),
                    new TextPosition(s.Caret.Line + shift, s.Caret.Column)));
            }
        });
    }

    /// <summary>Alt+Up and Alt+Down: swaps the selected lines with the line above or below.</summary>
    public void MoveLines(int direction)
    {
        var blocks = SelectedLineBlocks(direction > 0);
        foreach (var (a, b) in blocks)
        {
            if (direction < 0 && a == 0) return;
            if (direction > 0 && b >= Document.LineCount - 1) return;
        }

        Edit(CodeEditKind.Other, () =>
        {
            var saved = SnapshotSelections();
            foreach (var (a, b) in blocks)
            {
                if (direction < 0)
                {
                    string above = Document[a - 1];
                    Document.Delete(new TextRange(a - 1, 0, a, 0));
                    Document.Insert(new TextPosition(b - 1, Document.GetLineLength(b - 1)), "\n" + above);
                }
                else
                {
                    string below = Document[b + 1];
                    Document.Delete(new TextRange(b, Document.GetLineLength(b), b + 1, below.Length));
                    Document.Insert(new TextPosition(a, 0), below + "\n");
                }
            }

            _selections.Clear();
            foreach (var s in saved)
            {
                bool moved = false;
                foreach (var (a, b) in blocks)
                    if (s.Start.Line >= a && s.Start.Line <= b) moved = true;
                int d = moved ? direction : 0;
                _selections.Add(new CodeSelection(Document.Clamp(new TextPosition(s.Anchor.Line + d, s.Anchor.Column)),
                    Document.Clamp(new TextPosition(s.Caret.Line + d, s.Caret.Column))));
            }
        });
    }

    public void TransformCase(bool upper)
    {
        Edit(CodeEditKind.Other, () => ForEachSelection((_, s) =>
        {
            var range = s.IsEmpty ? WordAt(s.Caret) : s.Range;
            if (range.IsEmpty) return null;
            string text = Document.GetText(range);
            var c = Document.Replace(range, upper ? text.ToUpperInvariant() : text.ToLowerInvariant());
            return s.IsEmpty ? s : new CodeSelection(c.Range.Start, c.NewEnd);
        }));
    }

    public void TrimTrailingWhitespace()
    {
        Edit(CodeEditKind.Other, () =>
        {
            for (int line = Document.LineCount - 1; line >= 0; line--)
            {
                string s = Document[line];
                int end = s.Length;
                while (end > 0 && (s[end - 1] == ' ' || s[end - 1] == '\t')) end--;
                if (end < s.Length) Document.Delete(new TextRange(line, end, line, s.Length));
            }
        });
    }

    #endregion

    #region Clipboard

    public void Copy(Paper paper)
    {
        string text = GetSelectedText();
        _lineCopyText = null;
        if (text.Length == 0)
        {
            var sb = new StringBuilder();
            foreach (var (a, b) in SelectedLineBlocks(false))
                sb.Append(Document.GetText(new TextRange(a, 0, b, Document.GetLineLength(b)))).Append('\n');
            text = sb.ToString();
            _lineCopyText = text;
        }
        paper.SetClipboard(text.Replace("\n", Document.LineEnding));
    }

    public void Cut(Paper paper)
    {
        bool empty = true;
        foreach (var s in _selections) if (!s.IsEmpty) empty = false;
        Copy(paper);
        if (IsReadOnly) return;
        if (empty) DeleteLines();
        else InsertText("");
    }

    public void Paste(Paper paper)
    {
        string text = paper.GetClipboard();
        if (string.IsNullOrEmpty(text) || IsReadOnly) return;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Text copied as whole lines goes in above the caret's line.
        if (_lineCopyText != null && text == _lineCopyText)
        {
            Edit(CodeEditKind.Other, () => ForEachSelection((_, s) =>
            {
                Document.Insert(new TextPosition(s.Caret.Line, 0), text);
                return null;
            }));
            return;
        }

        string[] parts = text.Split('\n');
        if (_selections.Count > 1 && parts.Length == _selections.Count)
        {
            var order = new List<int>();
            for (int i = 0; i < _selections.Count; i++) order.Add(i);
            order.Sort((x, y) => _selections[x].Start.CompareTo(_selections[y].Start));
            Edit(CodeEditKind.Other, () =>
            {
                for (int i = order.Count - 1; i >= 0; i--)
                {
                    int idx = order[i];
                    var c = Document.Replace(_selections[idx].Range, parts[i]);
                    _selections[idx] = new CodeSelection(c.NewEnd);
                }
            });
            return;
        }

        InsertText(text);
    }

    #endregion
}
