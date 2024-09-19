﻿// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prowl.Runtime.Utils;

public class Tokenizer
{
    public ReadOnlyMemory<char> TokenMemory { get; set; }
    public ReadOnlyMemory<char> Input { get; }
    public ReadOnlySpan<char> Token => TokenMemory.Span;
    public int TokenPosition { get; set; }
    public int InputPosition { get; private set; }
    public int CurrentLine { get; set; } = 1;
    public int CurrentColumn { get; set; }

    public Func<char, Tokenizer, bool> IsWhitespace;
    public Func<char, Tokenizer, bool> IsQuote;

    public Tokenizer(ReadOnlyMemory<char> input, Func<char, Tokenizer, bool>? isWhitespace = null, Func<char, Tokenizer, bool>? isQuote = null)
    {
        Input = input;
        IsWhitespace = isWhitespace ?? DefaultWhitespaceHandler;
        IsQuote = isQuote ?? DefaultQuoteHandler;
    }

    public virtual void IncrementInputPosition(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (InputPosition < Input.Length && Input.Span[InputPosition] == '\n')
            {
                CurrentLine++;
                CurrentColumn = 0;
            }

            InputPosition++;
            CurrentColumn++;
        }
    }

    public virtual bool MoveNext()
    {
        while (InputPosition < Input.Length && IsWhitespace.Invoke(Input.Span[InputPosition], this))
            IncrementInputPosition();

        if (InputPosition >= Input.Length)
        {
            TokenPosition = Input.Length;
            TokenMemory = ReadOnlyMemory<char>.Empty;
            return false;
        }

        TokenPosition = InputPosition;
        var firstChar = Input.Span[InputPosition];

        // TODO: Should make this an option to allow for quoted strings or not and to specify the quote characters
        if (IsQuote.Invoke(firstChar, this))
            return HandleQuotedString(firstChar);

        return HandleToken();
    }

    private bool HandleQuotedString(char quoteChar)
    {
        IncrementInputPosition();

        while (InputPosition < Input.Length)
        {
            if (Input.Span[InputPosition] == quoteChar)
            {
                IncrementInputPosition();
                TokenMemory = Input.Slice(TokenPosition, InputPosition - TokenPosition);
                return true;
            }

            if (Input.Span[InputPosition] == '\\')
            {
                if (InputPosition + 1 >= Input.Length)
                    throw new InvalidDataException($"Reached end of input while reading an escape sequence in a quoted string starting at position {TokenPosition}.");

                IncrementInputPosition();
            }

            IncrementInputPosition();
        }

        throw new InvalidDataException($"Reached end of input while reading a quoted string starting at position {TokenPosition}.");
    }

    private bool HandleToken()
    {
        IncrementInputPosition();
        while (InputPosition < Input.Length
               && !IsWhitespace.Invoke(Input.Span[InputPosition], this)
               && !IsQuote.Invoke(Input.Span[InputPosition], this))
            IncrementInputPosition();

        TokenMemory = Input.Slice(TokenPosition, InputPosition - TokenPosition);
        return true;
    }

    public string ParseQuotedStringValue()
    {
        var token = Token;

        var s = new char[token.Length];
        var len = 0;

        var quote = Token[0];
        if (token[^1] != quote)
            throw new InvalidDataException($"Missing ending quote from string \"{token}\" at position {TokenPosition}");

        var original = token;
        token = token[1..^1];

        while (!token.IsEmpty)
        {
            if (token[0] == quote)
                throw new InvalidDataException($"Unescaped quote character in string \"{original}\" at position {TokenPosition}");

            if (token[0] == '\\')
            {
                if (token.Length < 2)
                    throw new EndOfStreamException();

                switch (token[1])
                {
                    case '\\':
                        s[len++] = '\\';
                        break;

                    case 't':
                        s[len++] = '\t';
                        break;

                    case 'n':
                        s[len++] = '\n';
                        break;

                    case 'r':
                        s[len++] = '\r';
                        break;

                    default:
                        if (token[1] == quote)
                            s[len++] = quote;
                        else
                            throw new InvalidDataException($"Invalid escape sequence in string \"{original}\" at position {TokenPosition}");
                        break;
                }

                token = token[2..];
                continue;
            }

            s[len++] = token[0];
            token = token[1..];
        }

        var result = new string(s[..len]);
        return result;
    }

    static bool DefaultWhitespaceHandler(char c, Tokenizer t)
    {
        return char.IsWhiteSpace(c);
    }

    static bool DefaultQuoteHandler(char c, Tokenizer t)
    {
        return c is '"' or '\'';
    }
}

public class Tokenizer<TTokenType> : Tokenizer
{
    public TTokenType TokenType { get; private set; }

    private readonly Dictionary<char, Func<Tokenizer, TTokenType>> _symbolHandlers;
    private readonly Func<char, bool> _isSymbol;
    private readonly TTokenType _defaultTokenType;
    private readonly TTokenType _noneTokenType;

    public Tokenizer(ReadOnlyMemory<char> input,
        Dictionary<char, Func<Tokenizer, TTokenType>> symbolHandlers,
        Func<char, bool> isSymbol,
        TTokenType defaultTokenType,
        TTokenType noneTokenType,
        Func<char, Tokenizer, bool>? isWhitespace = null,
        Func<char, Tokenizer, bool>? isQuote = null) :
        base(input, isWhitespace, isQuote)
    {
        _symbolHandlers = symbolHandlers;
        _isSymbol = isSymbol;
        _defaultTokenType = defaultTokenType;
        _noneTokenType = noneTokenType;
    }

    public Tokenizer(ReadOnlyMemory<char> input,
        Dictionary<char, Func<TTokenType>> symbolHandlers,
        Func<char, bool> isSymbol,
        TTokenType defaultTokenType,
        TTokenType noneTokenType,
        Func<char, Tokenizer, bool>? isWhitespace = null,
        Func<char, Tokenizer, bool>? isQuote = null) :
        base(input, isWhitespace, isQuote)
    {
        _symbolHandlers = new(symbolHandlers.Select((x) => new KeyValuePair<char, Func<Tokenizer, TTokenType>>(x.Key, (y) => x.Value())));
        _isSymbol = isSymbol;
        _defaultTokenType = defaultTokenType;
        _noneTokenType = noneTokenType;
    }

    public override bool MoveNext()
    {
        while (InputPosition < Input.Length && IsWhitespace.Invoke(Input.Span[InputPosition], this))
            IncrementInputPosition();

        if (InputPosition >= Input.Length)
        {
            TokenPosition = Input.Length;
            TokenType = _noneTokenType;
            TokenMemory = ReadOnlyMemory<char>.Empty;
            return false;
        }

        TokenPosition = InputPosition;
        char firstChar = Input.Span[InputPosition];

        if (_symbolHandlers.TryGetValue(firstChar, out Func<Tokenizer, TTokenType>? handler))
        {
            TokenType = handler(this);
            return true;
        }

        // TODO: Should make this an option to allow for quoted strings or not and to specify the quote characters
        if (firstChar is '"' or '\'')
        {
            return HandleQuotedString(firstChar);
        }

        return HandleDefaultToken();
    }

    private bool HandleQuotedString(char quoteChar)
    {
        TokenType = _defaultTokenType;
        IncrementInputPosition();

        while (InputPosition < Input.Length)
        {
            if (Input.Span[InputPosition] == quoteChar)
            {
                IncrementInputPosition();
                TokenMemory = Input.Slice(TokenPosition, InputPosition - TokenPosition);
                return true;
            }

            if (Input.Span[InputPosition] == '\\')
            {
                if (InputPosition + 1 >= Input.Length)
                    throw new InvalidDataException($"Reached end of input while reading an escape sequence in a quoted string starting at position {TokenPosition}.");

                IncrementInputPosition();
            }

            IncrementInputPosition();
        }

        throw new InvalidDataException($"Reached end of input while reading a quoted string starting at position {TokenPosition}.");
    }

    private bool HandleDefaultToken()
    {
        TokenType = _defaultTokenType;
        IncrementInputPosition();

        while (InputPosition < Input.Length
               && !_isSymbol(Input.Span[InputPosition])
               && !IsWhitespace.Invoke(Input.Span[InputPosition], this)
               && !IsQuote.Invoke(Input.Span[InputPosition], this))
            IncrementInputPosition();

        TokenMemory = Input.Slice(TokenPosition, InputPosition - TokenPosition);
        return true;
    }
}
