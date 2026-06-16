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
            throw Exceptions.Expected(expected, Text(ref t, token), token);

        t.Next();
    }


    // Parses contents of a quoted string "", stripping the surrounding quotes.
    public static string QuotedString(ref Tokenizer<ShaderToken> t)
    {
        Token<ShaderToken> token = t.Expect(ShaderToken.String);
        return t.Slice(token).ToString().Trim('"');
    }


    // Parses an integer, honoring an optional leading '-'.
    public static int Integer(ref Tokenizer<ShaderToken> t)
    {
        bool negative = t.TryConsume(ShaderToken.Minus);
        Token<ShaderToken> token = t.Expect(ShaderToken.Number);
        int value = int.Parse(t.Slice(token), provider: CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }


    // Parses a float, honoring an optional leading '-'.
    public static float Float(ref Tokenizer<ShaderToken> t)
    {
        bool negative = t.TryConsume(ShaderToken.Minus);
        Token<ShaderToken> token = t.Expect(ShaderToken.Number);
        float value = float.Parse(t.Slice(token), provider: CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }


    // Parses an identifier and maps it to an enum value by name.
    public static T Keywords<T>(ref Tokenizer<ShaderToken> t) where T : struct, Enum
    {
        Token<ShaderToken> token = t.Expect(ShaderToken.Identifier);
        string value = t.Slice(token).ToString();

        if (Enum.TryParse(value, out T result))
            return result;

        throw Exceptions.ExpectedAny(Enum.GetNames<T>(), value, token);
    }


    // Parses an identifier and maps it through a lookup table.
    public static T Keywords<T>(ref Tokenizer<ShaderToken> t, Dictionary<string, T> values)
    {
        Token<ShaderToken> token = t.Expect(ShaderToken.Identifier);
        string value = t.Slice(token).ToString();

        if (values.TryGetValue(value, out T? result))
            return result;

        throw Exceptions.ExpectedAny(values.Keys, value, token);
    }


    // Parses a 4-dimensional vector: ( x, y, z, w )
    public static Float4 Vector(ref Tokenizer<ShaderToken> t)
    {
        t.Expect(ShaderToken.OpenParen);
        float x = Float(ref t);
        t.Expect(ShaderToken.Comma);
        float y = Float(ref t);
        t.Expect(ShaderToken.Comma);
        float z = Float(ref t);
        t.Expect(ShaderToken.Comma);
        float w = Float(ref t);
        t.Expect(ShaderToken.CloseParen);

        return new Float4(x, y, z, w);
    }


    // Parses a 4x4 matrix as 4 vectors delimited by an open-close parenthesis.
    public static Float4x4 Matrix(ref Tokenizer<ShaderToken> t)
    {
        t.Expect(ShaderToken.OpenParen);
        Float4 x = Vector(ref t);
        Float4 y = Vector(ref t);
        Float4 z = Vector(ref t);
        Float4 w = Vector(ref t);
        t.Expect(ShaderToken.CloseParen);

        return new Float4x4(x, y, z, w);
    }


    // Parses a texture definition as "" {}
    public static string Texture(ref Tokenizer<ShaderToken> t)
    {
        string name = QuotedString(ref t);
        t.Expect(ShaderToken.OpenBrace);
        t.Expect(ShaderToken.CloseBrace);
        return name;
    }
}
