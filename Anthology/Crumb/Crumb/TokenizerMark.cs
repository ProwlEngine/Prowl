namespace Prowl.Crumb;

/// <summary>
/// An immutable snapshot of a <see cref="Tokenizer{TKind}"/>'s position, used for
/// backtracking. Obtain one with <see cref="Tokenizer{TKind}.Mark"/> and restore it
/// with <see cref="Tokenizer{TKind}.Reset"/>.
/// </summary>
public readonly struct TokenizerMark
{
    public readonly int Position;
    public readonly int Line;
    public readonly int Column;

    internal TokenizerMark(int position, int line, int column)
    {
        Position = position;
        Line = line;
        Column = column;
    }
}
