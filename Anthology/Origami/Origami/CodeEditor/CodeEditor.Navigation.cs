// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    // Visual column each caret aims for while moving up and down, so short lines do not lose it.
    private readonly List<int> _goalColumns = new();
    private bool _keepGoalColumns;

    #region Columns

    /// <summary>Screen column of <paramref name="column"/> with tabs expanded.</summary>
    public int VisualColumn(int line, int column)
    {
        string s = Document[line];
        column = Math.Min(column, s.Length);
        int tab = Math.Max(1, Options.TabSize);
        int v = 0;
        for (int i = 0; i < column; i++)
            v = s[i] == '\t' ? v + tab - v % tab : v + 1;
        return v;
    }

    /// <summary>Column nearest to the screen column <paramref name="visual"/>.</summary>
    public int ColumnFromVisual(int line, int visual, bool round = false)
    {
        string s = Document[line];
        int tab = Math.Max(1, Options.TabSize);
        int v = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int next = s[i] == '\t' ? v + tab - v % tab : v + 1;
            if (next > visual)
                return round && visual - v > (next - v) / 2 ? i + 1 : i;
            v = next;
        }
        return s.Length;
    }

    internal static int FirstNonBlank(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return i;
    }

    internal string IndentOf(string line) => line.Substring(0, FirstNonBlank(line));

    internal string IndentUnit => Options.InsertSpaces ? new string(' ', Math.Max(1, Options.TabSize)) : "\t";

    #endregion

    #region Words

    private enum CharClass { Space, Word, Punct }

    private CharClass Classify(char c)
    {
        if (c == ' ' || c == '\t') return CharClass.Space;
        return _language.IsWordChar(c) ? CharClass.Word : CharClass.Punct;
    }

    internal TextPosition WordLeft(TextPosition p)
    {
        if (p.Column == 0) return p.Line > 0 ? new TextPosition(p.Line - 1, Document.GetLineLength(p.Line - 1)) : p;
        string s = Document[p.Line];
        int i = p.Column;
        while (i > 0 && Classify(s[i - 1]) == CharClass.Space) i--;
        if (i == 0) return new TextPosition(p.Line, 0);
        var cls = Classify(s[i - 1]);
        while (i > 0 && Classify(s[i - 1]) == cls) i--;
        return new TextPosition(p.Line, i);
    }

    internal TextPosition WordRight(TextPosition p)
    {
        string s = Document[p.Line];
        if (p.Column >= s.Length) return p.Line < Document.LineCount - 1 ? new TextPosition(p.Line + 1, 0) : p;
        int i = p.Column;
        var cls = Classify(s[i]);
        if (cls != CharClass.Space)
            while (i < s.Length && Classify(s[i]) == cls) i++;
        while (i < s.Length && Classify(s[i]) == CharClass.Space) i++;
        return new TextPosition(p.Line, i);
    }

    /// <summary>The word touching <paramref name="p"/>, or an empty range when there is none.</summary>
    public TextRange WordAt(TextPosition p)
    {
        string s = Document[p.Line];
        int c = Math.Min(p.Column, s.Length);
        int a = c, b = c;
        if ((c >= s.Length || !_language.IsWordChar(s[c])) && c > 0 && _language.IsWordChar(s[c - 1])) { a = b = c - 1; b++; }
        else if (c < s.Length && _language.IsWordChar(s[c])) b = c + 1;
        else return new TextRange(p, p);

        while (a > 0 && _language.IsWordChar(s[a - 1])) a--;
        while (b < s.Length && _language.IsWordChar(s[b])) b++;
        return new TextRange(p.Line, a, p.Line, b);
    }

    #endregion

    #region Motions

    private void Move(Func<CodeSelection, int, TextPosition> target, bool extend, bool vertical = false)
    {
        if (!vertical || !_keepGoalColumns)
        {
            _goalColumns.Clear();
            foreach (var s in _selections) _goalColumns.Add(VisualColumn(s.Caret.Line, s.Caret.Column));
        }

        for (int i = 0; i < _selections.Count; i++)
        {
            var s = _selections[i];
            var to = Document.Clamp(target(s, i));
            _selections[i] = extend ? new CodeSelection(s.Anchor, to) : new CodeSelection(to);
        }

        NormalizeSelections();
        AfterCaretMove(true);
        _keepGoalColumns = vertical;
    }

    public void MoveLeft(bool extend, bool word)
    {
        Move((s, _) =>
        {
            if (!extend && !s.IsEmpty && !word) return s.Start;
            var p = s.Caret;
            if (word) return WordLeft(p);
            if (p.Column > 0) return new TextPosition(p.Line, p.Column - 1);
            return PrevVisibleLineEnd(p.Line, p);
        }, extend);
    }

    public void MoveRight(bool extend, bool word)
    {
        Move((s, _) =>
        {
            if (!extend && !s.IsEmpty && !word) return s.End;
            var p = s.Caret;
            if (word) return WordRight(p);
            if (p.Column < Document.GetLineLength(p.Line)) return new TextPosition(p.Line, p.Column + 1);
            int next = NextVisibleLine(p.Line);
            return next != p.Line ? new TextPosition(next, 0) : p;
        }, extend);
    }

    private TextPosition PrevVisibleLineEnd(int line, TextPosition fallback)
    {
        if (line == 0) return fallback;
        int prev = RowToLine(LineToRow(line) - 1);
        if (prev >= line) return fallback;
        return new TextPosition(prev, Document.GetLineLength(prev));
    }

    private int NextVisibleLine(int line)
    {
        int row = LineToRow(line);
        if (row >= VisibleLineCount - 1) return line;
        return RowToLine(row + 1);
    }

    public void MoveVertical(int rows, bool extend)
    {
        Move((s, i) =>
        {
            var p = s.Caret;
            if (!extend && !s.IsEmpty && rows != 0) p = rows < 0 ? s.Start : s.End;
            int row = LineToRow(p.Line) + rows;
            if (row < 0) return TextPosition.Zero;
            if (row >= VisibleLineCount) return Document.EndPosition;
            int line = RowToLine(row);
            int goal = i < _goalColumns.Count ? _goalColumns[i] : VisualColumn(p.Line, p.Column);
            return new TextPosition(line, ColumnFromVisual(line, goal));
        }, extend, vertical: true);
    }

    /// <summary>Home toggles between the first non blank character and the start of the line.</summary>
    public void MoveHome(bool extend)
    {
        Move((s, _) =>
        {
            var p = s.Caret;
            int first = FirstNonBlank(Document[p.Line]);
            return new TextPosition(p.Line, p.Column == first ? 0 : first);
        }, extend);
    }

    public void MoveEnd(bool extend) => Move((s, _) => new TextPosition(s.Caret.Line, Document.GetLineLength(s.Caret.Line)), extend);

    public void MoveDocumentStart(bool extend) => Move((_, _) => TextPosition.Zero, extend);

    public void MoveDocumentEnd(bool extend) => Move((_, _) => Document.EndPosition, extend);

    public void MovePage(int direction, bool extend)
    {
        int rows = Math.Max(1, VisibleRowCapacity - 1) * Math.Sign(direction);
        ScrollBy(rows * LineHeightPx);
        MoveVertical(rows, extend);
    }

    /// <summary>Moves the caret to a 1 based line and centres it.</summary>
    public void GoToLine(int oneBasedLine, int oneBasedColumn = 1)
    {
        int line = Math.Clamp(oneBasedLine - 1, 0, Document.LineCount - 1);
        SetCaret(new TextPosition(line, Math.Max(0, oneBasedColumn - 1)));
        CenterOnCaret();
    }

    public void GoTo(TextPosition position, bool center = true)
    {
        SetCaret(position);
        if (center) CenterOnCaret();
    }

    public void SelectLine()
    {
        for (int i = 0; i < _selections.Count; i++)
        {
            var s = _selections[i];
            int a = s.Start.Line, b = s.End.Line;
            var end = b + 1 < Document.LineCount ? new TextPosition(b + 1, 0) : new TextPosition(b, Document.GetLineLength(b));
            _selections[i] = new CodeSelection(new TextPosition(a, 0), end);
        }
        NormalizeSelections();
        AfterCaretMove(true);
    }

    /// <summary>Selects the word at each caret, or adds a caret at the next match of the primary selection.</summary>
    public void AddNextOccurrence()
    {
        var primary = _selections[_primary];
        if (primary.IsEmpty)
        {
            for (int i = 0; i < _selections.Count; i++)
            {
                var w = WordAt(_selections[i].Caret);
                if (!w.IsEmpty) _selections[i] = new CodeSelection(w.Start, w.End);
            }
            NormalizeSelections();
            AfterCaretMove(true);
            return;
        }

        string needle = Document.GetText(primary.Range);
        if (needle.Length == 0 || needle.Contains('\n')) return;

        var last = OrderedSelections()[^1].End;
        var found = FindNext(needle, last, true, false, wrap: true);
        if (found == null) return;
        foreach (var s in _selections)
            if (s.Start == found.Value.Start) return;
        AddSelection(new CodeSelection(found.Value.Start, found.Value.End));
    }

    // Screen column that repeated Ctrl+Alt+Up/Down keeps adding carets at, so short lines do not drag it left.
    private int _addCaretGoal = -1;
    private int _addCaretKey;

    /// <summary>Adds a caret on the row past the outermost caret in <paramref name="direction"/>, in the original screen column.</summary>
    public void AddCaretVertical(int direction)
    {
        var edge = _selections[0];
        foreach (var s in _selections)
            if (direction > 0 ? s.Caret > edge.Caret : s.Caret < edge.Caret) edge = s;

        int key = HashCode.Combine(_selections.Count, edge.Caret);
        if (_addCaretGoal < 0 || key != _addCaretKey) _addCaretGoal = VisualColumn(edge.Caret.Line, edge.Caret.Column);

        int row = LineToRow(edge.Caret.Line) + direction;
        if (row < 0 || row >= VisibleLineCount) return;
        int line = RowToLine(row);
        var caret = new TextPosition(line, ColumnFromVisual(line, _addCaretGoal));
        _selections.Add(new CodeSelection(caret));
        _primary = _selections.Count - 1;
        NormalizeSelections();
        int goal = _addCaretGoal;
        AfterCaretMove(true);
        _addCaretGoal = goal;
        _addCaretKey = HashCode.Combine(_selections.Count, caret);
    }

    /// <summary>Finds the bracket matching the one beside <paramref name="p"/>, skipping brackets in strings and comments.</summary>
    public bool TryFindMatchingBracket(TextPosition p, out TextPosition open, out TextPosition close)
    {
        open = close = default;
        string s = Document[p.Line];

        for (int side = 0; side < 2; side++)
        {
            int col = side == 0 ? p.Column - 1 : p.Column;
            if (col < 0 || col >= s.Length) continue;
            char c = s[col];
            foreach (var (o, cl) in _language.Brackets)
            {
                if (c != o && c != cl) continue;
                if (!IsCodeAt(p.Line, col)) break;
                bool forward = c == o;
                var match = ScanBracket(new TextPosition(p.Line, col), o, cl, forward);
                if (match == null) return false;
                open = forward ? new TextPosition(p.Line, col) : match.Value;
                close = forward ? match.Value : new TextPosition(p.Line, col);
                return true;
            }
        }
        return false;
    }

    private const int BracketScanLineLimit = 3000;

    private TextPosition? ScanBracket(TextPosition from, char open, char close, bool forward)
    {
        int depth = 0;
        var tokens = new List<CodeToken>();
        int lines = 0;
        for (int line = from.Line; line >= 0 && line < Document.LineCount && lines < BracketScanLineLimit; line += forward ? 1 : -1, lines++)
        {
            string s = Document[line];
            LexLine(line, tokens);
            int start = line == from.Line ? from.Column : forward ? 0 : s.Length - 1;
            for (int col = start; forward ? col < s.Length : col >= 0; col += forward ? 1 : -1)
            {
                char c = s[col];
                if (c != open && c != close) continue;
                if (!IsPunctuation(tokens, col)) continue;
                if (c == (forward ? open : close)) depth++;
                else if (--depth == 0) return new TextPosition(line, col);
            }
        }
        return null;
    }

    internal void LexLine(int line, List<CodeToken> tokens)
    {
        tokens.Clear();
        var lexer = _language.Lexer;
        if (lexer == null) tokens.Add(new CodeToken(0, Document.GetLineLength(line), CodeTokenKind.Plain));
        else lexer.Tokenize(Document[line], GetLineState(line), tokens);
    }

    private static bool IsPunctuation(List<CodeToken> tokens, int col)
    {
        foreach (var t in tokens)
            if (col >= t.Start && col < t.End) return t.Kind == CodeTokenKind.Punctuation || t.Kind == CodeTokenKind.Plain;
        return true;
    }

    private readonly List<CodeToken> _probeTokens = new();

    /// <summary>True when the char at the position is code rather than a string or comment.</summary>
    internal bool IsCodeAt(int line, int col)
    {
        LexLine(line, _probeTokens);
        foreach (var t in _probeTokens)
        {
            if (col < t.Start || col >= t.End) continue;
            return t.Kind is not (CodeTokenKind.String or CodeTokenKind.StringEscape or CodeTokenKind.Char
                or CodeTokenKind.Comment or CodeTokenKind.DocComment or CodeTokenKind.DocCommentTag);
        }
        return true;
    }

    internal CodeTokenKind TokenKindBefore(TextPosition p)
    {
        if (p.Column == 0) return CodeTokenKind.Plain;
        LexLine(p.Line, _probeTokens);
        int col = p.Column - 1;
        foreach (var t in _probeTokens)
            if (col >= t.Start && col < t.End) return t.Kind;
        return CodeTokenKind.Plain;
    }

    public void GoToMatchingBracket()
    {
        if (!TryFindMatchingBracket(Caret, out var open, out var close)) return;
        bool atClose = Caret.Line == close.Line && (Caret.Column == close.Column || Caret.Column == close.Column + 1);
        SetCaret(atClose ? open : new TextPosition(close.Line, close.Column + 1));
    }

    #endregion
}
