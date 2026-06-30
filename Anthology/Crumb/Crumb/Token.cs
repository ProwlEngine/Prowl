using System;

namespace Prowl.Crumb;

/// <summary>
/// A lexical token. Tokens never own text; they only reference a slice of the
/// original source via <see cref="Start"/> and <see cref="Length"/>.
/// </summary>
public readonly struct Token<TKind> where TKind : unmanaged, Enum
{
    /// <summary>The classification of this token.</summary>
    public readonly TKind Kind;

    /// <summary>Character offset of the first character of the token.</summary>
    public readonly int Start;

    /// <summary>Number of characters the token spans.</summary>
    public readonly int Length;

    /// <summary>One-based line number of the first character.</summary>
    public readonly int Line;

    /// <summary>One-based column number of the first character.</summary>
    public readonly int Column;

    /// <summary>Character offset just past the last character of the token.</summary>
    public int End => Start + Length;

    public Token(TKind kind, int start, int length, int line, int column)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }

    public override string ToString() => $"{Kind} @ {Line}:{Column} [{Start}..{End})";
}
