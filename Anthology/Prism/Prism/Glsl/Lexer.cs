using System.Text;

namespace Prowl.Prism.Glsl;

/// <summary>
/// Turns GLSL source into a flat token list. Comments and whitespace are skipped; '#'
/// is emitted as a <see cref="TokenKind.Hash"/> token for the preprocessor to interpret.
/// </summary>
public sealed class Lexer
{
    // Multi-character operators, longest first so greedy matching is correct.
    static readonly string[] Operators =
    {
        "<<=", ">>=",
        "==", "!=", "<=", ">=", "&&", "||", "^^", "++", "--",
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>",
        "+", "-", "*", "/", "%", "=", "<", ">", "!", "&", "|", "^", "~",
    };

    readonly string _src;
    int _pos, _line = 1, _lineStart;

    // Token lexemes (keywords, types, identifiers, numeric literals, operators) repeat heavily, so they are
    // interned by an ordinal span hash: a unique string is allocated once per distinct lexeme and reused.
    // The table is thread-static and PERSISTS across compiles on this thread, so re-lexing the same or
    // similar shaders allocates no token strings at all (the vocabulary is finite). (net8.0 lacks
    // Dictionary.GetAlternateLookup, so the bucket is hand-rolled.)
    [ThreadStatic] static Dictionary<int, object>? _internTls;

    Lexer(string source) => _src = source;

    string Intern(int start, int end)
    {
        var intern = _internTls ??= [];
        var span = _src.AsSpan(start, end - start);
        int h = string.GetHashCode(span);
        if (intern.TryGetValue(h, out object? bucket))
        {
            if (bucket is string s) { if (span.SequenceEqual(s)) return s; var ns = span.ToString(); intern[h] = new List<string> { s, ns }; return ns; }
            var list = (List<string>)bucket;
            foreach (var t in list) if (span.SequenceEqual(t)) return t;
            var added = span.ToString();
            list.Add(added);
            return added;
        }
        var fresh = span.ToString();
        intern[h] = fresh;
        return fresh;
    }

    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        Tokenize(source, tokens);
        return tokens;
    }

    /// <summary>Lexes into a caller-provided (poolable) list, cleared first, so no token list is allocated.</summary>
    public static void Tokenize(string source, List<Token> output)
    {
        output.Clear();
        var lexer = new Lexer(source);
        while (true)
        {
            var token = lexer.Next();
            output.Add(token);
            if (token.Kind == TokenKind.End) return;
        }
    }

    Token Next()
    {
        SkipTrivia();
        if (_pos >= _src.Length) return new Token(TokenKind.End, "", _line, _pos - _lineStart + 1);

        char c = _src[_pos];
        int col = _pos - _lineStart + 1;

        // glslang reads source through C strings: a NUL byte terminates the input and everything
        // after it is ignored (a shader whose 'main' sits past a NUL fails entry-point lookup).
        // Match that instead of erroring on the character or parsing past it.
        if (c == '\0') return new Token(TokenKind.End, "", _line, col);

        if (char.IsAsciiLetter(c) || c == '_') return ReadWord(col);
        if (char.IsAsciiDigit(c) || (c == '.' && Peek(1) is >= '0' and <= '9')) return ReadNumber(col);
        if (c == '"') return ReadString(col);

        switch (c)
        {
            case '(': return Punct(TokenKind.LParen, col);
            case ')': return Punct(TokenKind.RParen, col);
            case '[': return Punct(TokenKind.LBracket, col);
            case ']': return Punct(TokenKind.RBracket, col);
            case '{': return Punct(TokenKind.LBrace, col);
            case '}': return Punct(TokenKind.RBrace, col);
            case ';': return Punct(TokenKind.Semicolon, col);
            case ',': return Punct(TokenKind.Comma, col);
            case '.': return Punct(TokenKind.Dot, col);
            case ':': return Punct(TokenKind.Colon, col);
            case '?': return Punct(TokenKind.Question, col);
            case '#': return Punct(TokenKind.Hash, col);
        }

        foreach (var op in Operators)
            if (Matches(op))
            {
                _pos += op.Length;
                return new Token(TokenKind.Operator, op, _line, col);
            }

        throw new GlslParseException($"unexpected character '{c}'", _line);
    }

    Token ReadWord(int col)
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsAsciiLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) _pos++;
        string text = Intern(start, _pos);
        // glslang rejects identifiers (and macro names - they ride the same token stream) longer than
        // its 1024-char token limit with "name too long"; match that instead of accepting them and
        // producing identifiers downstream consumers may choke on.
        if (text.Length > 1024) throw new GlslParseException("name too long", _line);
        var kind = text is "true" or "false" ? TokenKind.BoolConst : TokenKind.Identifier;
        return new Token(kind, text, _line, col);
    }

    Token ReadString(int col)
    {
        _pos++;
        var sb = new StringBuilder();
        while (_pos < _src.Length && _src[_pos] != '"')
        {
            char ch = _src[_pos++];
            if (ch == '\\' && _pos < _src.Length)
            {
                char e = _src[_pos++];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', '"' => '"', _ => e });
            }
            else sb.Append(ch);
        }
        if (_pos >= _src.Length) throw new GlslParseException("unterminated string", _line);
        _pos++;
        return new Token(TokenKind.StringConst, sb.ToString(), _line, col);
    }

    Token ReadNumber(int col)
    {
        int start = _pos;

        if (_src[_pos] == '0' && (Peek(1) is 'x' or 'X'))
        {
            _pos += 2;
            while (_pos < _src.Length && Uri.IsHexDigit(_src[_pos])) _pos++;
            return NumberSuffix(start, col, isFloat: false);
        }

        bool isFloat = false;
        while (_pos < _src.Length && char.IsAsciiDigit(_src[_pos])) _pos++;
        if (_pos < _src.Length && _src[_pos] == '.')
        {
            isFloat = true;
            _pos++;
            while (_pos < _src.Length && char.IsAsciiDigit(_src[_pos])) _pos++;
        }
        if (_pos < _src.Length && (_src[_pos] is 'e' or 'E'))
        {
            isFloat = true;
            _pos++;
            if (_pos < _src.Length && (_src[_pos] is '+' or '-')) _pos++;
            int expDigits = _pos;
            while (_pos < _src.Length && char.IsAsciiDigit(_src[_pos])) _pos++;
            // An exponent marker must be followed by at least one digit; otherwise the literal is malformed
            // and would only fail later, deep in the numeric parse.
            if (_pos == expDigits) throw new GlslParseException($"exponent has no digits in '{_src[start.._pos]}'", _line);
        }
        return NumberSuffix(start, col, isFloat);
    }

    Token NumberSuffix(int start, int col, bool isFloat)
    {
        // glslang rejects numeric literals whose token exceeds its 1024-char limit ("numeric literal
        // too long") before any value is interpreted - a 100,000-digit literal must never reach the
        // value parsers (which would fold it to +/-Inf where glslang rejects).
        if (_pos - start > 1024) throw new GlslParseException("numeric literal too long", _line);
        TokenKind kind;
        // 'lf' marks a double literal and 'hf' a float16 literal; both must be checked before the plain 'f'.
        if (_pos + 1 < _src.Length && (_src[_pos] is 'l' or 'L') && (_src[_pos + 1] is 'f' or 'F'))
        {
            _pos += 2;
            kind = TokenKind.DoubleConst;
        }
        else if (_pos + 1 < _src.Length && (_src[_pos] is 'h' or 'H') && (_src[_pos + 1] is 'f' or 'F'))
        {
            _pos += 2;
            kind = TokenKind.HalfConst;
        }
        else if (_pos < _src.Length && (_src[_pos] is 'f' or 'F'))
        {
            _pos++;
            kind = TokenKind.FloatConst;
        }
        else if (!isFloat && IntegerSuffix() is TokenKind suffixed)
        {
            kind = suffixed;
        }
        else
        {
            kind = isFloat ? TokenKind.FloatConst : TokenKind.IntConst;
        }
        return new Token(kind, Intern(start, _pos), _line, col);
    }

    // An integer literal's suffix: 'u' is unsigned, 'l' is 64-bit, and the two may combine in either
    // order and case ('ul' / 'lu'); 's' is 16-bit ('s' / 'us', GL_EXT_shader_explicit_arithmetic_types).
    // Returns null when there is no suffix.
    TokenKind? IntegerSuffix()
    {
        if (_pos < _src.Length && _src[_pos] is 's' or 'S') { _pos++; return TokenKind.Int16Const; }
        if (_pos + 1 < _src.Length && _src[_pos] is 'u' or 'U' && _src[_pos + 1] is 's' or 'S') { _pos += 2; return TokenKind.Uint16Const; }
        bool unsigned = false, wide = false;
        int p = _pos;
        while (p < _src.Length)
        {
            if (_src[p] is 'u' or 'U' && !unsigned) unsigned = true;
            else if (_src[p] is 'l' or 'L' && !wide) wide = true;
            else break;
            p++;
        }
        if (p == _pos) return null;
        _pos = p;
        return (unsigned, wide) switch
        {
            (true, true) => TokenKind.Uint64Const,
            (false, true) => TokenKind.Int64Const,
            _ => TokenKind.UintConst,
        };
    }

    void SkipTrivia()
    {
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == '\n') NewLine();
            else if (char.IsWhiteSpace(c)) _pos++;
            else if (c == '/' && Peek(1) == '/') { while (_pos < _src.Length && _src[_pos] != '\n') _pos++; }
            else if (c == '/' && Peek(1) == '*') SkipBlockComment();
            else return;
        }
    }

    void SkipBlockComment()
    {
        _pos += 2;
        while (_pos < _src.Length && !(_src[_pos] == '*' && Peek(1) == '/'))
        {
            if (_src[_pos] == '\n') NewLine(); else _pos++;
        }
        _pos += 2;
    }

    void NewLine() { _pos++; _line++; _lineStart = _pos; }
    char Peek(int ahead) => _pos + ahead < _src.Length ? _src[_pos + ahead] : '\0';
    bool Matches(string s) => _pos + s.Length <= _src.Length && _src.AsSpan(_pos, s.Length).SequenceEqual(s);
    Token Punct(TokenKind kind, int col) { char c = _src[_pos++]; return new Token(kind, PunctText(c), _line, col); }

    // Constant (runtime-interned) single-char strings, so a punctuation token allocates no text string.
    static string PunctText(char c) => c switch
    {
        '(' => "(", ')' => ")", '[' => "[", ']' => "]", '{' => "{", '}' => "}",
        ';' => ";", ',' => ",", '.' => ".", ':' => ":", '?' => "?", '#' => "#",
        _ => c.ToString(),
    };
}
