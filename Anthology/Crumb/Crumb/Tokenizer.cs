using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Prowl.Crumb;

/// <summary>
/// A zero-allocation lexer that operates directly over a <see cref="ReadOnlySpan{Char}"/>.
/// Behaviour is driven entirely by a compiled <see cref="TokenizerRules{TKind}"/> object;
/// the engine itself knows nothing about identifiers, numbers, comments or strings.
/// </summary>
public ref struct Tokenizer<TKind> where TKind : unmanaged, Enum
{
    private readonly ReadOnlySpan<char> _source;
    private readonly TokenizerRules<TKind> _rules;

    private int _pos;
    private int _line;
    private int _column;

    // One-token lookahead cache for Peek().
    private Token<TKind> _peeked;
    private bool _hasPeeked;
    private int _peekEndPos;
    private int _peekEndLine;
    private int _peekEndColumn;

    public Tokenizer(ReadOnlySpan<char> source, TokenizerRules<TKind> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Compile();
        _source = source;
        _rules = rules;
        _pos = 0;
        _line = 1;
        _column = 1;
        _hasPeeked = false;
        _peeked = default;
        _peekEndPos = 0;
        _peekEndLine = 0;
        _peekEndColumn = 0;
    }

    /// <summary>True once the cursor has reached the end of the source.</summary>
    public readonly bool IsAtEnd => _pos >= _source.Length;

    // --- Low-level cursor surface used by dynamic rules ---------------------

    /// <summary>The full source span.</summary>
    public readonly ReadOnlySpan<char> Source => _source;

    /// <summary>Current character offset.</summary>
    public readonly int Position => _pos;

    /// <summary>Current one-based line.</summary>
    public readonly int Line => _line;

    /// <summary>Current one-based column.</summary>
    public readonly int Column => _column;

    /// <summary>The unconsumed remainder of the source.</summary>
    public readonly ReadOnlySpan<char> Remaining() => _source.Slice(_pos);

    /// <summary>Returns the source text a token references. Never allocates.</summary>
    public readonly ReadOnlySpan<char> Slice(Token<TKind> token) => _source.Slice(token.Start, token.Length);

    /// <summary>
    /// Advances the cursor by <paramref name="count"/> characters, updating line and
    /// column tracking. Intended for use inside dynamic rules.
    /// </summary>
    public void Advance(int count)
    {
        int end = _pos + count;
        if (end > _source.Length)
            end = _source.Length;

        while (_pos < end)
        {
            if (_source[_pos] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _pos++;
        }
    }

    /// <summary>
    /// Builds a token spanning from <paramref name="start"/> to the current position.
    /// Intended for use inside dynamic rules after consuming with <see cref="Advance"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Token<TKind> CreateToken(TKind kind, int start, int line, int column) =>
        new(kind, start, _pos - start, line, column);

    // --- Public token stream ------------------------------------------------

    /// <summary>Produces the next token, advancing the cursor.</summary>
    public Token<TKind> Next()
    {
        if (_hasPeeked)
        {
            _hasPeeked = false;
            _pos = _peekEndPos;
            _line = _peekEndLine;
            _column = _peekEndColumn;
            return _peeked;
        }

        return ScanNext();
    }

    /// <summary>Returns the next token without advancing the cursor.</summary>
    public Token<TKind> Peek()
    {
        if (_hasPeeked)
            return _peeked;

        int savePos = _pos, saveLine = _line, saveColumn = _column;
        Token<TKind> token = ScanNext();

        _peeked = token;
        _peekEndPos = _pos;
        _peekEndLine = _line;
        _peekEndColumn = _column;
        _hasPeeked = true;

        _pos = savePos;
        _line = saveLine;
        _column = saveColumn;
        return token;
    }

    /// <summary>Consumes the next token if it matches <paramref name="kind"/>.</summary>
    public bool TryConsume(TKind kind) => TryConsume(kind, out _);

    /// <summary>Consumes and returns the next token if it matches <paramref name="kind"/>.</summary>
    public bool TryConsume(TKind kind, out Token<TKind> token)
    {
        Token<TKind> next = Peek();
        if (EqualityComparer<TKind>.Default.Equals(next.Kind, kind))
        {
            token = Next();
            return true;
        }

        token = default;
        return false;
    }

    /// <summary>Consumes the next token, throwing if it does not match <paramref name="kind"/>.</summary>
    public Token<TKind> Expect(TKind kind)
    {
        Token<TKind> next = Peek();
        if (!EqualityComparer<TKind>.Default.Equals(next.Kind, kind))
            throw new UnexpectedTokenException(kind.ToString(), next.Kind.ToString(), next.Line, next.Column);

        return Next();
    }

    /// <summary>Captures the current position for later backtracking.</summary>
    public readonly TokenizerMark Mark() => new(_pos, _line, _column);

    /// <summary>Restores a previously captured position, discarding any lookahead.</summary>
    public void Reset(TokenizerMark mark)
    {
        _pos = mark.Position;
        _line = mark.Line;
        _column = mark.Column;
        _hasPeeked = false;
    }

    // --- Core scan ----------------------------------------------------------

    private Token<TKind> ScanNext()
    {
        SkipTrivia();

        if (_pos >= _source.Length)
            return new Token<TKind>(_rules.EndOfFileKindValue, _pos, 0, _line, _column);

        if (TryFixed(out Token<TKind> fixedToken))
            return fixedToken;

        var rules = _rules.Rules;
        for (int i = 0; i < rules.Length; i++)
        {
            if (rules[i](ref this, out Token<TKind> ruleToken))
                return ruleToken;
        }

        // Nothing matched: emit a single-character error token so callers can recover.
        int start = _pos, line = _line, col = _column;
        Advance(1);
        return new Token<TKind>(_rules.ErrorKindValue, start, _pos - start, line, col);
    }

    private void SkipTrivia()
    {
        var ws = _rules.WhitespacePredicate;
        var comments = _rules.SkipComments;

        bool progressed = true;
        while (progressed)
        {
            progressed = false;

            if (ws is not null)
            {
                while (_pos < _source.Length && ws(_source[_pos]))
                {
                    Advance(1);
                    progressed = true;
                }
            }

            for (int i = 0; i < comments.Length; i++)
            {
                var (open, close) = comments[i];
                if (!TokenizerRules<TKind>.StartsWith(_source, _pos, open))
                    continue;

                int idx = TokenizerRules<TKind>.IndexOf(_source, _pos + open.Length, close);
                int end = idx < 0 ? _source.Length : idx + close.Length;
                Advance(end - _pos);
                progressed = true;
            }
        }
    }

    private bool TryFixed(out Token<TKind> token)
    {
        if (!_rules.Dispatch.TryGetValue(_source[_pos], out FixedEntry<TKind>[]? entries))
        {
            token = default;
            return false;
        }

        var identifierPart = _rules.IdentifierPart;

        // Entries are sorted longest-first, so the first match is the greediest.
        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly FixedEntry<TKind> entry = ref entries[i];
            if (!TokenizerRules<TKind>.StartsWith(_source, _pos, entry.Text))
                continue;

            int matchEnd = _pos + entry.Text.Length;

            if (entry.Type == FixedKind.Keyword)
            {
                // Require a word boundary so "Shader" does not match inside "Shaders".
                if (matchEnd < _source.Length && identifierPart(_source[matchEnd]))
                    continue;
            }

            int start = _pos, line = _line, col = _column;

            if (entry.Type == FixedKind.Block)
            {
                Advance(entry.Text.Length); // consume opener
                int contentStart = _pos;
                int contentLine = _line, contentCol = _column;

                int closeIdx = TokenizerRules<TKind>.IndexOf(_source, _pos, entry.BlockEnd!);
                int contentEnd = closeIdx < 0 ? _source.Length : closeIdx;

                Advance(contentEnd - _pos); // consume content
                int contentLength = _pos - contentStart;

                if (closeIdx >= 0)
                    Advance(entry.BlockEnd!.Length); // consume terminator

                token = new Token<TKind>(entry.Kind, contentStart, contentLength, contentLine, contentCol);
                return true;
            }

            Advance(entry.Text.Length);
            token = new Token<TKind>(entry.Kind, start, _pos - start, line, col);
            return true;
        }

        token = default;
        return false;
    }
}
