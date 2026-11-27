// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.OrigamiUI;

/// <summary>A zero based line and column in a <see cref="CodeDocument"/>. Columns count UTF16 chars, not visual cells.</summary>
public readonly struct TextPosition : IEquatable<TextPosition>, IComparable<TextPosition>
{
    public readonly int Line;
    public readonly int Column;

    public TextPosition(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public static readonly TextPosition Zero = new(0, 0);

    public int CompareTo(TextPosition other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
    public bool Equals(TextPosition other) => Line == other.Line && Column == other.Column;
    public override bool Equals(object? obj) => obj is TextPosition p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(Line, Column);
    public override string ToString() => $"({Line + 1}:{Column + 1})";

    public static bool operator ==(TextPosition a, TextPosition b) => a.Equals(b);
    public static bool operator !=(TextPosition a, TextPosition b) => !a.Equals(b);
    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;

    public static TextPosition Min(TextPosition a, TextPosition b) => a <= b ? a : b;
    public static TextPosition Max(TextPosition a, TextPosition b) => a >= b ? a : b;
}

/// <summary>An ordered span between two positions. <see cref="Start"/> is always at or before <see cref="End"/>.</summary>
public readonly struct TextRange : IEquatable<TextRange>
{
    public readonly TextPosition Start;
    public readonly TextPosition End;

    public TextRange(TextPosition a, TextPosition b)
    {
        if (a <= b) { Start = a; End = b; }
        else { Start = b; End = a; }
    }

    public TextRange(int startLine, int startColumn, int endLine, int endColumn)
        : this(new TextPosition(startLine, startColumn), new TextPosition(endLine, endColumn)) { }

    public bool IsEmpty => Start == End;
    public bool IsSingleLine => Start.Line == End.Line;

    public bool Contains(TextPosition p) => p >= Start && p <= End;
    public bool Intersects(TextRange other) => Start < other.End && other.Start < End;

    public bool Equals(TextRange other) => Start == other.Start && End == other.End;
    public override bool Equals(object? obj) => obj is TextRange r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Start, End);
    public override string ToString() => $"{Start}-{End}";

    public static bool operator ==(TextRange a, TextRange b) => a.Equals(b);
    public static bool operator !=(TextRange a, TextRange b) => !a.Equals(b);
}

/// <summary>
/// One applied edit. <see cref="Range"/> is what was replaced, measured in the document before the edit.
/// <see cref="NewEnd"/> is where the inserted text ends after the edit.
/// </summary>
public sealed class TextChange
{
    public TextRange Range { get; }
    public string OldText { get; }
    public string NewText { get; }
    public TextPosition NewEnd { get; }

    /// <summary>Character offset of <see cref="TextRange.Start"/> before the edit, with lines joined by a single newline.</summary>
    public int StartOffset { get; }

    public TextChange(TextRange range, string oldText, string newText, TextPosition newEnd, int startOffset)
    {
        Range = range;
        OldText = oldText;
        NewText = newText;
        NewEnd = newEnd;
        StartOffset = startOffset;
    }

    /// <summary>How many lines the document grew by (negative when it shrank).</summary>
    public int LineDelta => (NewEnd.Line - Range.Start.Line) - (Range.End.Line - Range.Start.Line);

    /// <summary>Moves a position that was valid before this edit so it stays on the same text afterwards.</summary>
    public TextPosition Map(TextPosition p, bool stickToEnd = true)
    {
        if (p < Range.Start) return p;
        if (p == Range.Start)
            return Range.IsEmpty && stickToEnd ? NewEnd : p;
        if (p < Range.End) return stickToEnd ? NewEnd : Range.Start;

        if (p.Line == Range.End.Line)
            return new TextPosition(NewEnd.Line, NewEnd.Column + (p.Column - Range.End.Column));
        return new TextPosition(p.Line + LineDelta, p.Column);
    }
}
