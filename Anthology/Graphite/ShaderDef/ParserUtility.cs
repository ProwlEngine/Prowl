using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Vector;

using Prowl.Crumb;


namespace Prowl.Graphite.ShaderDef;


public static class ParserUtility
{
    // Materializes a token's source text.
    public static string Text(ref Tokenizer<ShaderToken> t, Token<ShaderToken> token)
        => t.Slice(token).ToString();


    // A human-readable name for a token kind, preferring the literal symbol where one exists.
    static string Describe(ShaderToken kind) => kind switch
    {
        ShaderToken.OpenBrace => "{",
        ShaderToken.CloseBrace => "}",
        ShaderToken.OpenParen => "(",
        ShaderToken.CloseParen => ")",
        ShaderToken.Equals => "=",
        ShaderToken.Comma => ",",
        ShaderToken.Minus => "-",
        ShaderToken.String => "string",
        ShaderToken.Number => "number",
        ShaderToken.Identifier => "identifier",
        ShaderToken.SlangProgram => "SLANGPROGRAM block",
        _ => kind.ToString()
    };


    // Describes the token actually encountered, used as the "found" half of a diagnostic.
    public static string Found(ref Tokenizer<ShaderToken> t, Token<ShaderToken> token)
        => token.Kind == ShaderToken.EndOfFile ? "end of input" : Text(ref t, token);


    // Consumes the next token, requiring it to match kind. Throws a located ParseException
    // (rather than the tokenizer's generic exception) so all parse errors share one shape.
    // An optional description overrides the default name in the message.
    public static Token<ShaderToken> Expect(ref Tokenizer<ShaderToken> t, ShaderToken kind, string? description = null)
    {
        Token<ShaderToken> token = t.Peek();

        if (token.Kind != kind)
            throw Exceptions.Expected(description ?? Describe(kind), Found(ref t, token), token);

        return t.Next();
    }


    // True if the token is an identifier matching the keyword, ignoring case.
    public static bool IsKeyword(ref Tokenizer<ShaderToken> t, Token<ShaderToken> token, string expected)
        => token.Kind == ShaderToken.Identifier
            && t.Slice(token).Equals(expected, StringComparison.OrdinalIgnoreCase);


    // True if the next token is the given keyword, without consuming it.
    public static bool PeekKeyword(ref Tokenizer<ShaderToken> t, string expected)
        => IsKeyword(ref t, t.Peek(), expected);


    // Consumes the next token, requiring it to be the given keyword.
    public static void ExpectKeyword(ref Tokenizer<ShaderToken> t, string expected)
    {
        Token<ShaderToken> token = t.Peek();

        if (!IsKeyword(ref t, token, expected))
            throw Exceptions.Expected(expected, Found(ref t, token), token);

        t.Next();
    }


    // Parses contents of a quoted string "", stripping the surrounding quotes.
    public static string QuotedString(ref Tokenizer<ShaderToken> t)
    {
        Token<ShaderToken> token = Expect(ref t, ShaderToken.String);
        return t.Slice(token).ToString().Trim('"');
    }


    // Parses an integer, honoring an optional leading '-'.
    public static int Integer(ref Tokenizer<ShaderToken> t)
    {
        bool negative = t.TryConsume(ShaderToken.Minus);
        Token<ShaderToken> token = Expect(ref t, ShaderToken.Number);

        if (!int.TryParse(t.Slice(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw Exceptions.InvalidNumber("integer", Text(ref t, token), token);

        return negative ? -value : value;
    }


    // Parses a float, honoring an optional leading '-'.
    public static float Float(ref Tokenizer<ShaderToken> t)
    {
        bool negative = t.TryConsume(ShaderToken.Minus);
        Token<ShaderToken> token = Expect(ref t, ShaderToken.Number);

        if (!float.TryParse(t.Slice(token), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            throw Exceptions.InvalidNumber("number", Text(ref t, token), token);

        return negative ? -value : value;
    }


    // Parses an identifier and maps it to an enum value by name.
    public static T Keywords<T>(ref Tokenizer<ShaderToken> t) where T : struct, Enum
    {
        Token<ShaderToken> token = Expect(ref t, ShaderToken.Identifier);
        string value = t.Slice(token).ToString();

        if (Enum.TryParse(value, out T result))
            return result;

        throw Exceptions.ExpectedAny(Enum.GetNames<T>(), value, token);
    }


    // Parses an identifier and maps it through a lookup table.
    public static T Keywords<T>(ref Tokenizer<ShaderToken> t, Dictionary<string, T> values)
    {
        Token<ShaderToken> token = Expect(ref t, ShaderToken.Identifier);
        string value = t.Slice(token).ToString();

        if (values.TryGetValue(value, out T? result))
            return result;

        throw Exceptions.ExpectedAny(values.Keys, value, token);
    }


    // Parses a 4-dimensional vector: ( x, y, z, w )
    public static Float4 Vector(ref Tokenizer<ShaderToken> t)
    {
        Expect(ref t, ShaderToken.OpenParen);
        float x = Float(ref t);
        Expect(ref t, ShaderToken.Comma);
        float y = Float(ref t);
        Expect(ref t, ShaderToken.Comma);
        float z = Float(ref t);
        Expect(ref t, ShaderToken.Comma);
        float w = Float(ref t);
        Expect(ref t, ShaderToken.CloseParen);

        return new Float4(x, y, z, w);
    }


    // Parses a 4x4 matrix as 4 vectors delimited by an open-close parenthesis.
    public static Float4x4 Matrix(ref Tokenizer<ShaderToken> t)
    {
        Expect(ref t, ShaderToken.OpenParen);
        Float4 x = Vector(ref t);
        Float4 y = Vector(ref t);
        Float4 z = Vector(ref t);
        Float4 w = Vector(ref t);
        Expect(ref t, ShaderToken.CloseParen);

        return new Float4x4(x, y, z, w);
    }


    // Parses a SLANGPROGRAM ... ENDSLANG block, returning the embedded Slang source verbatim.
    public static string SlangProgram(ref Tokenizer<ShaderToken> t)
    {
        Token<ShaderToken> peeked = t.Peek();
        if (peeked.Kind != ShaderToken.SlangProgram)
            throw Exceptions.MissingSlangProgram(peeked);

        Token<ShaderToken> token = t.Next();

        // Crumb's block rule consumes to end-of-source when the terminator is absent, so a missing
        // ENDSLANG leaves no terminator immediately following the captured content.
        if (!t.Source.Slice(token.End).StartsWith("ENDSLANG"))
            throw Exceptions.UnterminatedSlangProgram(token);

        return t.Slice(token).ToString().Trim();
    }


    // Parses a texture definition as "" {}
    public static string Texture(ref Tokenizer<ShaderToken> t)
    {
        string name = QuotedString(ref t);
        Expect(ref t, ShaderToken.OpenBrace);
        Expect(ref t, ShaderToken.CloseBrace);
        return name;
    }
}
