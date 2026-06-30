using System;

namespace Prowl.Crumb;

/// <summary>
/// Thrown by <see cref="Tokenizer{TKind}.Expect"/> when the next token does not
/// match the expected kind. Carries enough information to produce a useful message
/// without pulling in a heavyweight diagnostics system.
/// </summary>
public sealed class UnexpectedTokenException : Exception
{
    public string Expected { get; }
    public string Actual { get; }
    public int Line { get; }
    public int Column { get; }

    public UnexpectedTokenException(string expected, string actual, int line, int column)
        : base($"Expected {expected} but found {actual} at line {line}, column {column}.")
    {
        Expected = expected;
        Actual = actual;
        Line = line;
        Column = column;
    }
}
