using System;
using System.Collections.Generic;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public sealed class ParseException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public ParseException(string message, int line, int column)
        : base($"{message} at line {line}, column {column}.")
    {
        Line = line;
        Column = column;
    }
}


public static class Exceptions
{
    public static ParseException Expected(string expected, string found, Token<ShaderToken> at) =>
        new($"Expected '{expected}' but found '{found}'", at.Line, at.Column);


    public static ParseException ExpectedAny(IEnumerable<string> expected, string found, Token<ShaderToken> at) =>
        new($"Expected any of '{string.Join(", ", expected)}' but got '{found}'", at.Line, at.Column);


    public static ParseException MissingSlangProgram(Token<ShaderToken> at) =>
        new("Each Pass must contain a SLANGPROGRAM block", at.Line, at.Column);


    public static ParseException UnterminatedSlangProgram(Token<ShaderToken> at) =>
        new("Unterminated SLANGPROGRAM block: missing closing 'ENDSLANG'", at.Line, at.Column);


    public static ParseException InvalidNumber(string type, string found, Token<ShaderToken> at) =>
        new($"'{found}' is not a valid {type}", at.Line, at.Column);
}
