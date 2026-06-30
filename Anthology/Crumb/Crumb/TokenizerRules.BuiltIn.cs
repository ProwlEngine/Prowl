using System;

namespace Prowl.Crumb;

public sealed partial class TokenizerRules<TKind>
{
    private static bool DefaultIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    /// <summary>
    /// Adds an identifier rule. <paramref name="start"/> recognises the first
    /// character; <paramref name="part"/> recognises continuation characters. The
    /// continuation predicate is also used for keyword word-boundary checks.
    /// </summary>
    public TokenizerRules<TKind> Identifier(TKind kind, CharPredicate? start = null, CharPredicate? part = null)
    {
        ThrowIfCompiled();
        start ??= DefaultIdentifierStart;
        part ??= DefaultIdentifierPart;
        _identifierPart = part;

        return Rule((ref Tokenizer<TKind> t, out Token<TKind> token) =>
        {
            var src = t.Source;
            int pos = t.Position;
            if (pos >= src.Length || !start(src[pos]))
            {
                token = default;
                return false;
            }

            int begin = pos, line = t.Line, col = t.Column;
            pos++;
            while (pos < src.Length && part(src[pos]))
                pos++;

            t.Advance(pos - begin);
            token = t.CreateToken(kind, begin, line, col);
            return true;
        });
    }

    /// <summary>
    /// Adds a number rule supporting integers, decimals, scientific notation and
    /// hexadecimal literals (0x...).
    /// </summary>
    public TokenizerRules<TKind> Number(TKind kind)
    {
        ThrowIfCompiled();

        return Rule((ref t, out token) =>
        {
            var src = t.Source;
            int pos = t.Position;
            int len = src.Length;

            bool startsWithDigit = pos < len && IsDigit(src[pos]);
            bool startsWithDot = pos + 1 < len && src[pos] == '.' && IsDigit(src[pos + 1]);
            if (!startsWithDigit && !startsWithDot)
            {
                token = default;
                return false;
            }

            int begin = pos, line = t.Line, col = t.Column;

            // Hexadecimal: 0x / 0X followed by hex digits.
            if (startsWithDigit && src[pos] == '0' && pos + 1 < len && (src[pos + 1] is 'x' or 'X'))
            {
                pos += 2;
                while (pos < len && IsHexDigit(src[pos]))
                    pos++;
            }
            else
            {
                while (pos < len && IsDigit(src[pos]))
                    pos++;

                if (pos < len && src[pos] == '.' && pos + 1 < len && IsDigit(src[pos + 1]))
                {
                    pos++;
                    while (pos < len && IsDigit(src[pos]))
                        pos++;
                }
                else if (pos < len && src[pos] == '.' && !startsWithDot)
                {
                    // Trailing dot, e.g. "1." — consume the dot.
                    pos++;
                }

                // Scientific notation: e[+/-]digits
                if (pos < len && (src[pos] is 'e' or 'E'))
                {
                    int save = pos;
                    pos++;
                    if (pos < len && (src[pos] is '+' or '-'))
                        pos++;
                    if (pos < len && IsDigit(src[pos]))
                    {
                        while (pos < len && IsDigit(src[pos]))
                            pos++;
                    }
                    else
                    {
                        pos = save; // not a valid exponent, back off
                    }
                }
            }

            t.Advance(pos - begin);
            token = t.CreateToken(kind, begin, line, col);
            return true;
        });
    }

    /// <summary>
    /// Adds a string rule delimited by <paramref name="delimiter"/>. Characters
    /// following <paramref name="escape"/> are taken literally. An unterminated string
    /// consumes to end of input.
    /// </summary>
    public TokenizerRules<TKind> String(char delimiter, TKind kind, char escape = '\\')
    {
        ThrowIfCompiled();

        return Rule((ref Tokenizer<TKind> t, out Token<TKind> token) =>
        {
            var src = t.Source;
            int pos = t.Position;
            int len = src.Length;
            if (pos >= len || src[pos] != delimiter)
            {
                token = default;
                return false;
            }

            int begin = pos, line = t.Line, col = t.Column;
            pos++;
            while (pos < len)
            {
                char c = src[pos];
                if (c == escape && pos + 1 < len)
                {
                    pos += 2;
                    continue;
                }
                pos++;
                if (c == delimiter)
                    break;
            }

            t.Advance(pos - begin);
            token = t.CreateToken(kind, begin, line, col);
            return true;
        });
    }

    /// <summary>
    /// Registers a comment that is skipped (not emitted as a token). Everything from
    /// <paramref name="open"/> up to and including <paramref name="close"/> is ignored.
    /// </summary>
    public TokenizerRules<TKind> Comment(string open, string close)
    {
        ThrowIfCompiled();
        Require(open);
        Require(close);
        _skipComments.Add((open, close));
        return this;
    }

    /// <summary>
    /// Registers a comment that is emitted as a token of the given kind. The token
    /// spans the delimiters and the content between them.
    /// </summary>
    public TokenizerRules<TKind> Comment(string open, string close, TKind kind)
    {
        ThrowIfCompiled();
        Require(open);
        Require(close);

        return Rule((ref Tokenizer<TKind> t, out Token<TKind> token) =>
        {
            var src = t.Source;
            int pos = t.Position;
            if (!StartsWith(src, pos, open))
            {
                token = default;
                return false;
            }

            int begin = pos, line = t.Line, col = t.Column;
            int idx = IndexOf(src, pos + open.Length, close);
            int end = idx < 0 ? src.Length : idx + close.Length;

            t.Advance(end - begin);
            token = t.CreateToken(kind, begin, line, col);
            return true;
        });
    }

    /// <summary>
    /// Registers a single-line comment running from <paramref name="prefix"/> to the
    /// end of the line. The comment is skipped and not emitted as a token.
    /// </summary>
    public TokenizerRules<TKind> LineComment(string prefix)
    {
        ThrowIfCompiled();
        Require(prefix);
        _skipComments.Add((prefix, "\n"));
        return this;
    }

    /// <summary>
    /// Registers a single-line comment running from <paramref name="prefix"/> to the
    /// end of the line, emitted as a token of the given kind. The terminating newline
    /// is not part of the token.
    /// </summary>
    public TokenizerRules<TKind> LineComment(string prefix, TKind kind)
    {
        ThrowIfCompiled();
        Require(prefix);

        return Rule((ref Tokenizer<TKind> t, out Token<TKind> token) =>
        {
            var src = t.Source;
            int pos = t.Position;
            if (!StartsWith(src, pos, prefix))
            {
                token = default;
                return false;
            }

            int begin = pos, line = t.Line, col = t.Column;
            int idx = IndexOf(src, pos + prefix.Length, "\n");
            int end = idx < 0 ? src.Length : idx;

            t.Advance(end - begin);
            token = t.CreateToken(kind, begin, line, col);
            return true;
        });
    }

    private static bool IsDigit(char c) => (uint)(c - '0') <= 9;

    private static bool IsHexDigit(char c) =>
        IsDigit(c) || (uint)((c | 0x20) - 'a') <= 5;

    internal static bool StartsWith(ReadOnlySpan<char> src, int at, string text)
    {
        if (at + text.Length > src.Length)
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (src[at + i] != text[i])
                return false;
        }
        return true;
    }

    internal static int IndexOf(ReadOnlySpan<char> src, int from, string text)
    {
        int last = src.Length - text.Length;
        for (int i = from; i <= last; i++)
        {
            if (StartsWith(src, i, text))
                return i;
        }
        return -1;
    }
}
