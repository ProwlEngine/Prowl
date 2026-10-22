// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

public sealed partial class CodeEditor
{
    // Code lenses by the position of the declared name, kept sorted by line and moved through edits like markers.
    private readonly List<(TextPosition Position, string Text)> _lenses = new();
    private readonly List<(Rect Rect, TextPosition Position)> _lensHits = new();
    private int _lensRequestedVersion = -1;
    private int _lensFrom, _lensTo;
    private CancellationTokenSource? _lensCts;

    // Lines that get a lens row drawn above them, with each one's logical row. Display rows are the logical rows
    // with these injected, which is what drawing, scrolling and hit testing use. Caret motion stays on logical rows,
    // so up and down step over lens rows.
    private int[] _lensLines = Array.Empty<int>();
    private int[] _lensLineRows = Array.Empty<int>();

    /// <summary>Rows drawn: visible lines plus the injected code lens rows.</summary>
    public int DisplayRowCount => VisibleLineCount + _lensLines.Length;

    /// <summary>The display row a line is drawn on, below its lens row if it has one.</summary>
    public int LineToDisplayRow(int line)
    {
        int row = LineToRow(line);
        // Lens rows on lines up to and including this one's header row sit above it.
        int lo = 0, hi = _lensLineRows.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_lensLineRows[mid] <= row) lo = mid + 1;
            else hi = mid;
        }
        return row + lo;
    }

    public int DisplayRowToLine(int displayRow) => DisplayRowToLine(displayRow, out _);

    /// <summary>The line at a display row. On a lens row that is the line the lens belongs to.</summary>
    public int DisplayRowToLine(int displayRow, out bool isLensRow)
    {
        displayRow = Math.Clamp(displayRow, 0, Math.Max(0, DisplayRowCount - 1));
        // Lens i sits at display row _lensLineRows[i] + i, its line right below.
        int lo = 0, hi = _lensLineRows.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_lensLineRows[mid] + mid <= displayRow) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        isLensRow = found >= 0 && _lensLineRows[found] + found == displayRow;
        if (isLensRow) return _lensLines[found];
        return RowToLine(displayRow - (found + 1));
    }

    private void RebuildDisplayRows()
    {
        var lines = new List<int>();
        var rows = new List<int>();
        if (Options.ShowCodeLens)
        {
            int last = -1;
            foreach (var (position, _) in _lenses)
            {
                int line = position.Line;
                if (line == last || line >= Document.LineCount || IsLineHidden(line)) continue;
                last = line;
                lines.Add(line);
                rows.Add(LineToRow(line));
            }
        }
        _lensLines = lines.ToArray();
        _lensLineRows = rows.ToArray();
    }

    private bool TryGetLens(int line, out (TextPosition Position, string Text) lens)
    {
        int lo = 0, hi = _lenses.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int l = _lenses[mid].Position.Line;
            if (l == line) { lens = _lenses[mid]; return true; }
            if (l < line) lo = mid + 1;
            else hi = mid - 1;
        }
        lens = default;
        return false;
    }

    private void TickCodeLens(int firstLine, int lastLine)
    {
        if (Session == null || !Options.ShowCodeLens) return;
        if (_time - _lastEditTime < Options.AnalysisDelay * 2) return;
        if (_lensRequestedVersion == Document.Version && firstLine >= _lensFrom && lastLine <= _lensTo) return;

        int margin = Math.Max(30, lastLine - firstLine);
        int from = Math.Max(0, firstLine - margin);
        int to = Math.Min(Document.LineCount - 1, lastLine + margin);
        _lensRequestedVersion = Document.Version;
        _lensFrom = from;
        _lensTo = to;

        int start = Document.GetLineOffset(from);
        int end = Document.GetLineOffset(to) + Document.GetLineLength(to);
        _lensCts?.Cancel();
        _lensCts = new CancellationTokenSource();
        Continue(Session.GetCodeLensesAsync(start, end - start, _lensCts.Token), Document.Version, lenses => ApplyLenses(lenses, from, to));
    }

    private void ApplyLenses(IReadOnlyList<CodeLens> lenses, int from, int to)
    {
        // Lens rows appearing above the view would push the text down, so the top line is held in place.
        double lh = LineHeightPx;
        int topLine = DisplayRowToLine((int)(_scrollY / lh), out bool topIsLens);
        double topOffset = _scrollY - LineToDisplayRow(topLine) * lh;

        _lenses.RemoveAll(l => l.Position.Line >= from && l.Position.Line <= to);
        foreach (var lens in lenses) _lenses.Add((Document.OffsetToPosition(lens.Offset), lens.Text));
        _lenses.Sort((a, b) => a.Position.CompareTo(b.Position));
        RebuildDisplayRows();

        double anchored = Math.Max(0, LineToDisplayRow(topLine) * lh + (topIsLens ? -lh : topOffset));
        _scrollY = anchored;
        _scrollYTarget = anchored;
    }

    private void ShiftLenses(TextChange change)
    {
        if (_lenses.Count == 0) return;
        for (int i = _lenses.Count - 1; i >= 0; i--)
        {
            var (position, text) = _lenses[i];
            // A declaration name inside the replaced text is gone until the next refresh.
            if (position > change.Range.Start && position < change.Range.End)
            {
                _lenses.RemoveAt(i);
                continue;
            }
            _lenses[i] = (change.Map(position, false), text);
        }
        RebuildDisplayRows();
    }

    private void ClearLenses()
    {
        _lenses.Clear();
        _lensRequestedVersion = -1;
        RebuildDisplayRows();
    }

    private void DrawLensRow(Canvas canvas, CodeEditorColors pal, FontFile font, int line, float rowY)
    {
        if (!TryGetLens(line, out var lens)) return;
        var v = _view;
        float size = MathF.Max(8, FontSize * 0.8f);
        float x = ColX(VisualColumn(line, FirstNonBlank(Document[line])));
        float w = lens.Text.Length * _charW * (size / FontSize);
        var rect = new Rect(x, rowY, x + w, rowY + v.LineH);
        bool hovered = rect.Contains(_pointer);
        float glyphH = _glyphH * (size / FontSize);
        float ty = rowY + (v.LineH - glyphH) * 0.6f;
        DrawPlain(canvas, font, lens.Text, x, ty, hovered ? pal.PopupText : pal.LineNumber, size);
        if (hovered) canvas.RectFilled(x, ty + glyphH, w, 1, pal.PopupText);
        _lensHits.Add((rect, lens.Position));
    }

    /// <summary>The declaration a code lens under the point belongs to, or null.</summary>
    private TextPosition? CodeLensAt(Float2 point)
    {
        foreach (var (rect, position) in _lensHits)
            if (rect.Contains(point)) return position;
        return null;
    }
}
