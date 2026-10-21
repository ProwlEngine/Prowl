// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.OrigamiUI.Graphite;

/// <summary>
/// Line lexer for Graphite shaders. A .shader file is the ShaderDef wrapper until GLSLPROGRAM and GLSL until ENDGLSL.
/// A .glsl or .compute file is GLSL throughout.
/// </summary>
/// <remarks>State: 0 wrapper, 1 wrapper block comment, 2 GLSL, 3 GLSL block comment. A GLSL only lexer starts in 2.</remarks>
public sealed class ShaderLexer : ICodeLexer
{
    internal const int Wrapper = 0, WrapperComment = 1, Glsl = 2, GlslComment = 3;

    public static readonly ShaderLexer ShaderFile = new(false);
    public static readonly ShaderLexer GlslFile = new(true);

    private readonly bool _glslOnly;

    private ShaderLexer(bool glslOnly) => _glslOnly = glslOnly;

    /// <summary>The state a document starts in.</summary>
    internal int InitialState => _glslOnly ? Glsl : Wrapper;

    public int Tokenize(string line, int state, List<CodeToken>? tokens)
    {
        int mode = _glslOnly && state == 0 ? Glsl : state;
        int end = Run(line, mode, tokens);
        return _glslOnly && end == Glsl ? 0 : end;
    }

    private static void Emit(List<CodeToken>? tokens, int start, int length, CodeTokenKind kind)
    {
        if (tokens != null && length > 0) tokens.Add(new CodeToken(start, length, kind));
    }

    private static int Run(string s, int mode, List<CodeToken>? tokens)
    {
        int pos = 0;
        int n = s.Length;

        if (mode == WrapperComment || mode == GlslComment)
        {
            int close = s.IndexOf("*/", System.StringComparison.Ordinal);
            if (close < 0) { Emit(tokens, 0, n, CodeTokenKind.Comment); return mode; }
            Emit(tokens, 0, close + 2, CodeTokenKind.Comment);
            pos = close + 2;
            mode = mode == WrapperComment ? Wrapper : Glsl;
        }

        if (mode == Glsl)
        {
            int first = pos;
            while (first < n && (s[first] == ' ' || s[first] == '\t')) first++;
            if (first < n && s[first] == '#')
            {
                Preprocessor(s, first, tokens);
                return Glsl;
            }
        }

        char prevSignificant = '\0';
        while (pos < n)
        {
            char c = s[pos];
            if (c == ' ' || c == '\t') { pos++; continue; }

            if (c == '/' && pos + 1 < n && s[pos + 1] == '/')
            {
                Emit(tokens, pos, n - pos, CodeTokenKind.Comment);
                return mode;
            }
            if (c == '/' && pos + 1 < n && s[pos + 1] == '*')
            {
                int close = s.IndexOf("*/", pos + 2, System.StringComparison.Ordinal);
                if (close < 0)
                {
                    Emit(tokens, pos, n - pos, CodeTokenKind.Comment);
                    return mode == Glsl ? GlslComment : WrapperComment;
                }
                Emit(tokens, pos, close + 2 - pos, CodeTokenKind.Comment);
                pos = close + 2;
                continue;
            }
            if (c == '"')
            {
                int start = pos++;
                while (pos < n && s[pos] != '"') pos += s[pos] == '\\' ? 2 : 1;
                pos = System.Math.Min(n, pos + 1);
                Emit(tokens, start, pos - start, CodeTokenKind.String);
                prevSignificant = '"';
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && pos + 1 < n && char.IsDigit(s[pos + 1])))
            {
                int start = pos;
                if (c == '0' && pos + 1 < n && (s[pos + 1] == 'x' || s[pos + 1] == 'X'))
                {
                    pos += 2;
                    while (pos < n && System.Uri.IsHexDigit(s[pos])) pos++;
                }
                else
                {
                    while (pos < n && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
                    if (pos < n && (s[pos] == 'e' || s[pos] == 'E'))
                    {
                        pos++;
                        if (pos < n && (s[pos] == '+' || s[pos] == '-')) pos++;
                        while (pos < n && char.IsDigit(s[pos])) pos++;
                    }
                }
                while (pos < n && (s[pos] == 'f' || s[pos] == 'F' || s[pos] == 'u' || s[pos] == 'U' || s[pos] == 'l' || s[pos] == 'L')) pos++;
                Emit(tokens, start, pos - start, CodeTokenKind.Number);
                prevSignificant = '0';
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = pos;
                while (pos < n && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
                string word = s.Substring(start, pos - start);

                if (mode == Wrapper)
                {
                    if (word == "GLSLPROGRAM")
                    {
                        Emit(tokens, start, pos - start, CodeTokenKind.Keyword);
                        return Continue(s, pos, Glsl, tokens);
                    }
                    Emit(tokens, start, pos - start, WrapperKind(word, prevSignificant));
                }
                else
                {
                    if (word == "ENDGLSL")
                    {
                        Emit(tokens, start, pos - start, CodeTokenKind.Keyword);
                        return Continue(s, pos, Wrapper, tokens);
                    }
                    Emit(tokens, start, pos - start, GlslKind(s, pos, word));
                }
                prevSignificant = 'a';
                continue;
            }

            Emit(tokens, pos, 1, "+-*/%=<>!&|^~?:".IndexOf(c) >= 0 ? CodeTokenKind.Operator : CodeTokenKind.Punctuation);
            prevSignificant = c;
            pos++;
        }
        return mode;
    }

    // Lexes the rest of a line in a new mode, with token columns kept relative to the whole line.
    private static int Continue(string s, int from, int mode, List<CodeToken>? tokens)
    {
        if (tokens == null) return Run(s.Substring(from), mode, null);
        var rest = new List<CodeToken>();
        int end = Run(s.Substring(from), mode, rest);
        foreach (var t in rest) tokens.Add(new CodeToken(t.Start + from, t.Length, t.Kind));
        return end;
    }

    private static CodeTokenKind WrapperKind(string word, char prev)
    {
        if (prev == '[') return CodeTokenKind.Method;
        if (ShaderSyntax.Blocks.ContainsKey(word)) return CodeTokenKind.Keyword;
        if (ShaderSyntax.Commands.ContainsKey(word) || ShaderSyntax.StencilCommands.ContainsKey(word)) return CodeTokenKind.ControlKeyword;
        if (ShaderSyntax.PropertyTypes.ContainsKey(word)) return CodeTokenKind.Class;
        if (ShaderSyntax.IsValue(word)) return CodeTokenKind.EnumMember;
        if (word.StartsWith('_')) return CodeTokenKind.Field;
        return CodeTokenKind.Identifier;
    }

    private static CodeTokenKind GlslKind(string s, int end, string word)
    {
        if (GlslBuiltins.ControlKeywords.Contains(word)) return CodeTokenKind.ControlKeyword;
        if (GlslBuiltins.Keywords.Contains(word)) return CodeTokenKind.Keyword;
        if (GlslBuiltins.Types.Contains(word)) return CodeTokenKind.Struct;
        if (ShaderSyntax.Sections.ContainsKey(word) && NextChar(s, end) is '{' or '\0') return CodeTokenKind.Keyword;
        if (word.StartsWith("gl_", System.StringComparison.Ordinal)) return CodeTokenKind.Field;
        if (NextChar(s, end) == '(') return CodeTokenKind.Method;
        return CodeTokenKind.Identifier;
    }

    private static char NextChar(string s, int from)
    {
        while (from < s.Length && (s[from] == ' ' || s[from] == '\t')) from++;
        return from < s.Length ? s[from] : '\0';
    }

    private static void Preprocessor(string s, int pos, List<CodeToken>? tokens)
    {
        int n = s.Length;
        int start = pos++;
        while (pos < n && (s[pos] == ' ' || s[pos] == '\t')) pos++;
        int wordStart = pos;
        while (pos < n && char.IsLetter(s[pos])) pos++;
        string directive = s.Substring(wordStart, pos - wordStart);
        Emit(tokens, start, pos - start, CodeTokenKind.Preprocessor);

        int comment = s.IndexOf("//", pos, System.StringComparison.Ordinal);
        int textEnd = comment >= 0 ? comment : n;

        if (directive == "include")
        {
            int q = pos;
            while (q < textEnd && s[q] != '"' && s[q] != '<') q++;
            if (q < textEnd)
            {
                char close = s[q] == '"' ? '"' : '>';
                int qEnd = s.IndexOf(close, q + 1);
                qEnd = qEnd < 0 ? textEnd : System.Math.Min(textEnd, qEnd + 1);
                Emit(tokens, q, qEnd - q, CodeTokenKind.String);
                pos = qEnd;
            }
        }
        else if (directive == "pragma")
        {
            while (pos < textEnd && (s[pos] == ' ' || s[pos] == '\t')) pos++;
            int p0 = pos;
            while (pos < textEnd && (char.IsLetter(s[pos]) || s[pos] == '_')) pos++;
            if (pos > p0) Emit(tokens, p0, pos - p0, ShaderSyntax.Pragmas.ContainsKey(s.Substring(p0, pos - p0)) ? CodeTokenKind.Preprocessor : CodeTokenKind.PreprocessorText);
        }
        else if (directive == "define")
        {
            while (pos < textEnd && (s[pos] == ' ' || s[pos] == '\t')) pos++;
            int p0 = pos;
            while (pos < textEnd && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            Emit(tokens, p0, pos - p0, CodeTokenKind.Constant);
        }

        Emit(tokens, pos, textEnd - pos, CodeTokenKind.PreprocessorText);
        if (comment >= 0) Emit(tokens, comment, n - comment, CodeTokenKind.Comment);
    }
}
