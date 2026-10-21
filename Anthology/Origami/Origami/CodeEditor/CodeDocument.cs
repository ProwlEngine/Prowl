// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace Prowl.OrigamiUI;

/// <summary>
/// Line based text buffer behind a <see cref="CodeEditor"/>. Lines are stored without terminators
/// and joined by '\n' internally; the original line ending is remembered and restored by <see cref="GetText()"/>.
/// </summary>
/// <remarks>
/// An edit only replaces the strings of the lines it touches. Untouched lines keep the same string
/// instance, which caches across the editor key off, so editing one line of a huge file stays cheap.
/// </remarks>
public sealed class CodeDocument
{
    private readonly List<string> _lines = new() { string.Empty };

    // Prefix sums of line start offsets, valid for lines [0, _offsetsValid).
    private readonly List<int> _lineOffsets = new() { 0 };
    private int _offsetsValid = 1;

    private int _longestLine;
    private bool _longestDirty;

    /// <summary>Line ending written back out by <see cref="GetText()"/>. Detected from the text given to <see cref="SetText"/>.</summary>
    public string LineEnding { get; set; } = "\n";

    /// <summary>Bumped on every edit. Async language results tagged with an older version are stale.</summary>
    public int Version { get; private set; }

    public int LineCount => _lines.Count;

    /// <summary>Total characters with lines joined by one '\n'.</summary>
    public int Length => GetLineOffset(_lines.Count - 1) + _lines[^1].Length;

    /// <summary>Raised after every edit, including those from undo and redo.</summary>
    public event Action<TextChange>? Changed;

    /// <summary>Raised after <see cref="SetText"/> replaces everything.</summary>
    public event Action? Reset;

    public CodeDocument() { }

    public CodeDocument(string text) => SetText(text);

    public string this[int line] => _lines[line];

    public string GetLine(int line) => _lines[line];

    public int GetLineLength(int line) => _lines[line].Length;

    public TextPosition EndPosition => new(_lines.Count - 1, _lines[^1].Length);

    /// <summary>Longest line in chars. Cheap to read, recomputed lazily after lines shrink.</summary>
    public int LongestLineLength
    {
        get
        {
            if (_longestDirty)
            {
                int max = 0;
                foreach (var l in _lines) if (l.Length > max) max = l.Length;
                _longestLine = max;
                _longestDirty = false;
            }
            return _longestLine;
        }
    }

    public void SetText(string text)
    {
        text ??= string.Empty;
        LineEnding = DetectLineEnding(text);
        _lines.Clear();
        SplitLines(text, _lines);
        _offsetsValid = 1;
        _longestDirty = true;
        Version++;
        Reset?.Invoke();
    }

    public string GetText() => GetText(LineEnding);

    public string GetText(string lineEnding)
    {
        var sb = new StringBuilder(Length + _lines.Count * (lineEnding.Length - 1));
        for (int i = 0; i < _lines.Count; i++)
        {
            if (i > 0) sb.Append(lineEnding);
            sb.Append(_lines[i]);
        }
        return sb.ToString();
    }

    public string GetText(TextRange range)
    {
        var s = Clamp(range.Start);
        var e = Clamp(range.End);
        if (s.Line == e.Line) return _lines[s.Line].Substring(s.Column, e.Column - s.Column);

        var sb = new StringBuilder();
        sb.Append(_lines[s.Line], s.Column, _lines[s.Line].Length - s.Column);
        for (int i = s.Line + 1; i < e.Line; i++)
            sb.Append('\n').Append(_lines[i]);
        sb.Append('\n').Append(_lines[e.Line], 0, e.Column);
        return sb.ToString();
    }

    /// <summary>A copy of the line strings, safe to hand to a background thread.</summary>
    public string[] Snapshot() => _lines.ToArray();

    public TextPosition Clamp(TextPosition p)
    {
        if (p.Line < 0) return TextPosition.Zero;
        if (p.Line >= _lines.Count) return EndPosition;
        return new TextPosition(p.Line, Math.Clamp(p.Column, 0, _lines[p.Line].Length));
    }

    public int GetLineOffset(int line)
    {
        line = Math.Clamp(line, 0, _lines.Count - 1);
        if (line >= _offsetsValid)
        {
            while (_lineOffsets.Count < _lines.Count) _lineOffsets.Add(0);
            for (int i = _offsetsValid; i <= line; i++)
                _lineOffsets[i] = _lineOffsets[i - 1] + _lines[i - 1].Length + 1;
            _offsetsValid = line + 1;
        }
        return _lineOffsets[line];
    }

    public int PositionToOffset(TextPosition p)
    {
        p = Clamp(p);
        return GetLineOffset(p.Line) + p.Column;
    }

    public TextPosition OffsetToPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        int lo = 0, hi = _lines.Count - 1;
        GetLineOffset(hi);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_lineOffsets[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return new TextPosition(lo, offset - _lineOffsets[lo]);
    }

    /// <summary>Replaces <paramref name="range"/> with <paramref name="text"/> and returns the applied change.</summary>
    public TextChange Replace(TextRange range, string text)
    {
        text ??= string.Empty;
        if (text.IndexOf('\r') >= 0) text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var start = Clamp(range.Start);
        var end = Clamp(range.End);
        range = new TextRange(start, end);

        int startOffset = PositionToOffset(start);
        string oldText = range.IsEmpty ? string.Empty : GetText(range);

        string head = _lines[start.Line].Substring(0, start.Column);
        string tail = _lines[end.Line].Substring(end.Column);
        bool shrinks = false;
        for (int i = start.Line; i <= end.Line; i++)
            if (_lines[i].Length >= _longestLine) { shrinks = true; break; }

        TextPosition newEnd;
        int nl = text.IndexOf('\n');
        if (nl < 0)
        {
            string merged = string.Concat(head, text, tail);
            if (end.Line > start.Line) _lines.RemoveRange(start.Line + 1, end.Line - start.Line);
            _lines[start.Line] = merged;
            newEnd = new TextPosition(start.Line, head.Length + text.Length);
            TrackLongest(merged.Length);
        }
        else
        {
            var inserted = new List<string>();
            SplitLines(text, inserted);
            inserted[0] = head + inserted[0];
            int lastIdx = inserted.Count - 1;
            int lastLen = inserted[lastIdx].Length;
            inserted[lastIdx] += tail;

            int removeCount = end.Line - start.Line + 1;
            int common = Math.Min(removeCount, inserted.Count);
            for (int i = 0; i < common; i++) _lines[start.Line + i] = inserted[i];
            if (removeCount > inserted.Count)
                _lines.RemoveRange(start.Line + common, removeCount - common);
            else if (inserted.Count > removeCount)
                _lines.InsertRange(start.Line + common, inserted.GetRange(common, inserted.Count - common));

            newEnd = new TextPosition(start.Line + lastIdx, lastLen);
            foreach (var l in inserted) TrackLongest(l.Length);
        }

        if (shrinks && !range.IsEmpty) _longestDirty = true;
        _offsetsValid = Math.Min(_offsetsValid, start.Line + 1);
        Version++;

        var change = new TextChange(range, oldText, text, newEnd, startOffset);
        Changed?.Invoke(change);
        return change;
    }

    public TextChange Insert(TextPosition at, string text) => Replace(new TextRange(at, at), text);

    public TextChange Delete(TextRange range) => Replace(range, string.Empty);

    private void TrackLongest(int length)
    {
        if (length > _longestLine) _longestLine = length;
    }

    private static void SplitLines(string text, List<string> into)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r') continue;
            into.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        into.Add(text.Substring(start));
    }

    private static string DetectLineEnding(string text)
    {
        int nl = text.IndexOf('\n');
        if (nl > 0 && text[nl - 1] == '\r') return "\r\n";
        if (nl < 0 && text.IndexOf('\r') >= 0) return "\r";
        return "\n";
    }
}
