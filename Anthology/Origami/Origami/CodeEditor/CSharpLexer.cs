// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.OrigamiUI;

/// <summary>
/// Hand written C# lexer for instant highlighting with no compiler behind it. Handles every literal form
/// (verbatim, interpolated, raw and multi line), doc comments, preprocessor lines and contextual keywords.
/// </summary>
/// <remarks>
/// State layout: low byte is the mode, bits 8 to 15 the raw string quote count, bits 16 to 23 the
/// interpolation dollar count. Mode 0 is plain code.
/// </remarks>
public sealed class CSharpLexer : ICodeLexer
{
    private const int ModeCode = 0;
    private const int ModeBlockComment = 1;
    private const int ModeVerbatim = 2;
    private const int ModeVerbatimInterpolated = 3;
    private const int ModeRaw = 4;

    public static readonly CSharpLexer Instance = new();

    private static readonly HashSet<string> s_keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "byte", "char", "checked", "class", "const", "decimal", "delegate",
        "double", "enum", "event", "explicit", "extern", "false", "fixed", "float", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "this", "true", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile",
        "var", "dynamic", "async", "get", "set", "init", "add", "remove", "partial", "record", "nameof",
        "global", "required", "scoped", "file", "notnull", "unmanaged", "managed", "nint", "nuint", "with",
        "and", "or", "not", "where", "alias", "args", "value",
        "from", "select", "group", "into", "orderby", "join", "let", "on", "equals", "by", "ascending", "descending",
    };

    private static readonly HashSet<string> s_contextual = new(StringComparer.Ordinal)
    {
        "var", "dynamic", "async", "get", "set", "init", "add", "remove", "partial", "record", "nameof",
        "global", "required", "scoped", "file", "notnull", "unmanaged", "managed", "nint", "nuint", "with",
        "and", "or", "not", "where", "alias", "args", "value",
        "from", "select", "group", "into", "orderby", "join", "let", "on", "equals", "by", "ascending", "descending",
    };

    private static readonly HashSet<string> s_control = new(StringComparer.Ordinal)
    {
        "if", "else", "switch", "case", "default", "for", "foreach", "while", "do", "break", "continue",
        "return", "goto", "throw", "try", "catch", "finally", "yield", "await", "when",
    };

    private static readonly HashSet<string> s_typeDeclarators = new(StringComparer.Ordinal)
    {
        "class", "struct", "interface", "enum", "record", "new", "delegate",
    };

    public int Tokenize(string line, int state, List<CodeToken>? tokens)
    {
        var lx = new Lexer(line, tokens);
        int mode = state & 0xFF;
        int pos = 0;

        switch (mode)
        {
            case ModeBlockComment:
                if (!lx.BlockCommentBody(ref pos)) return ModeBlockComment;
                break;
            case ModeVerbatim:
            case ModeVerbatimInterpolated:
                if (!lx.VerbatimBody(ref pos, mode == ModeVerbatimInterpolated)) return state;
                break;
            case ModeRaw:
                if (!lx.RawBody(ref pos, (state >> 8) & 0xFF, (state >> 16) & 0xFF)) return state;
                break;
        }

        return lx.Code(ref pos, false);
    }

    private ref struct Lexer
    {
        private readonly string _s;
        private readonly List<CodeToken>? _out;
        private string? _prevWord;
        private int _lastStart, _lastLen;
        private CodeTokenKind _lastKind;
        private bool _hasLast;

        public Lexer(string s, List<CodeToken>? output)
        {
            _s = s;
            _out = output;
            _prevWord = null;
            _lastStart = _lastLen = 0;
            _lastKind = CodeTokenKind.Plain;
            _hasLast = false;
        }

        private void Emit(int start, int length, CodeTokenKind kind)
        {
            if (_out == null || length <= 0) return;
            if (_hasLast && _lastKind == kind && _lastStart + _lastLen == start && kind != CodeTokenKind.Identifier)
            {
                _lastLen += length;
                _out[^1] = new CodeToken(_lastStart, _lastLen, kind);
                return;
            }
            _out.Add(new CodeToken(start, length, kind));
            _lastStart = start;
            _lastLen = length;
            _lastKind = kind;
            _hasLast = true;
        }

        private char At(int i) => i < _s.Length ? _s[i] : '\0';

        // Lexes code until the line ends, or until an unmatched '}' when inside an interpolation hole.
        // Returns the state the next line starts in, or -1 when a hole closed.
        public int Code(ref int pos, bool inHole)
        {
            int depth = 0;
            bool lineStart = true;

            while (pos < _s.Length)
            {
                char c = _s[pos];

                if (c == ' ' || c == '\t')
                {
                    int ws = pos;
                    while (pos < _s.Length && (_s[pos] == ' ' || _s[pos] == '\t')) pos++;
                    Emit(ws, pos - ws, CodeTokenKind.Plain);
                    continue;
                }

                if (lineStart && c == '#' && !inHole)
                {
                    Preprocessor(pos);
                    return ModeCode;
                }
                lineStart = false;

                if (c == '/' && At(pos + 1) == '/')
                {
                    if (At(pos + 2) == '/' && At(pos + 3) != '/') DocComment(pos);
                    else Emit(pos, _s.Length - pos, CodeTokenKind.Comment);
                    return ModeCode;
                }

                if (c == '/' && At(pos + 1) == '*')
                {
                    int start = pos;
                    pos += 2;
                    Emit(start, 2, CodeTokenKind.Comment);
                    if (!BlockCommentBody(ref pos)) return ModeBlockComment;
                    continue;
                }

                if (c == '"' || ((c == '$' || c == '@') && IsStringPrefix(pos)))
                {
                    int r = StringLiteral(ref pos);
                    if (r != ModeCode) return r;
                    _prevWord = null;
                    continue;
                }

                if (c == '\'')
                {
                    CharLiteral(ref pos);
                    _prevWord = null;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && char.IsDigit(At(pos + 1))))
                {
                    Number(ref pos);
                    _prevWord = null;
                    continue;
                }

                if (char.IsLetter(c) || c == '_' || (c == '@' && (char.IsLetter(At(pos + 1)) || At(pos + 1) == '_')))
                {
                    Identifier(ref pos);
                    continue;
                }

                if (inHole)
                {
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        if (depth == 0) return -1;
                        depth--;
                    }
                    else if (c == ':' && depth == 0 && At(pos + 1) != ':')
                    {
                        // Format specifier, up to the closing brace.
                        int fs = pos;
                        while (pos < _s.Length && _s[pos] != '}') pos++;
                        Emit(fs, pos - fs, CodeTokenKind.String);
                        if (pos < _s.Length) return -1;
                        continue;
                    }
                }

                Emit(pos, 1, IsOperator(c) ? CodeTokenKind.Operator : CodeTokenKind.Punctuation);
                if (c != '.' && c != '?') _prevWord = null;
                pos++;
            }

            return ModeCode;
        }

        private static bool IsOperator(char c) => "+-*/%=<>!&|^~?:".IndexOf(c) >= 0;

        private bool IsStringPrefix(int pos)
        {
            int i = pos;
            while (i < _s.Length && (_s[i] == '$' || _s[i] == '@')) i++;
            return i < _s.Length && _s[i] == '"' && i - pos <= 8;
        }

        public bool BlockCommentBody(ref int pos)
        {
            int start = pos;
            int end = _s.IndexOf("*/", pos, StringComparison.Ordinal);
            if (end < 0)
            {
                Emit(start, _s.Length - start, CodeTokenKind.Comment);
                pos = _s.Length;
                return false;
            }
            pos = end + 2;
            Emit(start, pos - start, CodeTokenKind.Comment);
            return true;
        }

        private void Preprocessor(int pos)
        {
            int start = pos;
            pos++;
            while (pos < _s.Length && _s[pos] == ' ') pos++;
            while (pos < _s.Length && char.IsLetter(_s[pos])) pos++;
            Emit(start, pos - start, CodeTokenKind.Preprocessor);

            int comment = _s.IndexOf("//", pos, StringComparison.Ordinal);
            int textEnd = comment >= 0 ? comment : _s.Length;
            Emit(pos, textEnd - pos, CodeTokenKind.PreprocessorText);
            if (comment >= 0) Emit(comment, _s.Length - comment, CodeTokenKind.Comment);
        }

        private void DocComment(int pos)
        {
            Emit(pos, 3, CodeTokenKind.DocComment);
            pos += 3;
            while (pos < _s.Length)
            {
                int lt = _s.IndexOf('<', pos);
                if (lt < 0)
                {
                    Emit(pos, _s.Length - pos, CodeTokenKind.DocComment);
                    return;
                }
                Emit(pos, lt - pos, CodeTokenKind.DocComment);
                int gt = _s.IndexOf('>', lt);
                int end = gt < 0 ? _s.Length : gt + 1;
                Emit(lt, end - lt, CodeTokenKind.DocCommentTag);
                pos = end;
            }
        }

        private void CharLiteral(ref int pos)
        {
            int start = pos++;
            while (pos < _s.Length)
            {
                char c = _s[pos];
                if (c == '\\') { pos = Math.Min(_s.Length, pos + 2); continue; }
                pos++;
                if (c == '\'') break;
            }
            Emit(start, pos - start, CodeTokenKind.Char);
        }

        private void Number(ref int pos)
        {
            int start = pos;
            if (_s[pos] == '0' && (At(pos + 1) == 'x' || At(pos + 1) == 'X' || At(pos + 1) == 'b' || At(pos + 1) == 'B'))
            {
                pos += 2;
                while (pos < _s.Length && (Uri.IsHexDigit(_s[pos]) || _s[pos] == '_')) pos++;
            }
            else
            {
                while (pos < _s.Length && (char.IsDigit(_s[pos]) || _s[pos] == '_')) pos++;
                if (At(pos) == '.' && char.IsDigit(At(pos + 1)))
                {
                    pos++;
                    while (pos < _s.Length && (char.IsDigit(_s[pos]) || _s[pos] == '_')) pos++;
                }
                if (At(pos) == 'e' || At(pos) == 'E')
                {
                    int save = pos++;
                    if (At(pos) == '+' || At(pos) == '-') pos++;
                    if (char.IsDigit(At(pos))) while (pos < _s.Length && char.IsDigit(_s[pos])) pos++;
                    else pos = save;
                }
            }
            while (pos < _s.Length && "uUlLfFdDmM".IndexOf(_s[pos]) >= 0) pos++;
            Emit(start, pos - start, CodeTokenKind.Number);
        }

        private void Identifier(ref int pos)
        {
            int start = pos;
            bool verbatim = _s[pos] == '@';
            if (verbatim) pos++;
            while (pos < _s.Length && (char.IsLetterOrDigit(_s[pos]) || _s[pos] == '_')) pos++;

            // Words never change the line state, so a state only pass skips classifying them.
            if (_out == null) return;

            string word = _s.Substring(start, pos - start);
            CodeTokenKind kind;
            if (!verbatim && s_control.Contains(word)) kind = CodeTokenKind.ControlKeyword;
            else if (!verbatim && s_keywords.Contains(word) && !(s_contextual.Contains(word) && !LooksLikeContextualKeyword(word, pos)))
                kind = CodeTokenKind.Keyword;
            else if (_prevWord != null && s_typeDeclarators.Contains(_prevWord))
                kind = CodeTokenKind.Class;
            else
            {
                int p = pos;
                while (p < _s.Length && _s[p] == ' ') p++;
                kind = At(p) == '(' ? CodeTokenKind.Method : CodeTokenKind.Identifier;
            }

            Emit(start, pos - start, kind);
            _prevWord = kind == CodeTokenKind.Keyword || kind == CodeTokenKind.ControlKeyword ? word : null;
        }

        // Contextual keywords double as ordinary names, so only colour them where they read as keywords.
        private bool LooksLikeContextualKeyword(string word, int end)
        {
            int p = end;
            while (p < _s.Length && _s[p] == ' ') p++;
            char next = At(p);
            switch (word)
            {
                case "var":
                case "dynamic":
                    return char.IsLetter(next) || next == '_' || next == '(' || next == '@' || next == '[';
                case "get":
                case "set":
                case "init":
                case "add":
                case "remove":
                    return next == ';' || next == '{' || next == '=' || next == '\0';
                case "nameof":
                    return next == '(';
                case "value":
                case "args":
                    return false;
                case "async":
                case "partial":
                case "required":
                case "scoped":
                case "file":
                case "record":
                case "global":
                case "unmanaged":
                case "notnull":
                case "managed":
                    return char.IsLetter(next) || next == ':' || next == ',' || next == '>' || next == '{' || next == '(' ;
                case "nint":
                case "nuint":
                    return true;
                case "where":
                    return char.IsLetter(next);
                case "with":
                    return next == '{';
                case "and":
                case "or":
                case "not":
                    return next != '=' && next != ';' && next != ')' && next != ',' && next != '.';
                default:
                    return char.IsLetter(next) || next == '_';
            }
        }

        private int StringLiteral(ref int pos)
        {
            int start = pos;
            int dollars = 0;
            bool verbatim = false;
            while (pos < _s.Length && (_s[pos] == '$' || _s[pos] == '@'))
            {
                if (_s[pos] == '$') dollars++;
                else verbatim = true;
                pos++;
            }

            int quotes = 0;
            while (pos + quotes < _s.Length && _s[pos + quotes] == '"') quotes++;

            if (quotes >= 3)
            {
                pos += quotes;
                Emit(start, pos - start, CodeTokenKind.String);
                return RawBody(ref pos, quotes, dollars) ? ModeCode : ModeRaw | (quotes << 8) | (dollars << 16);
            }

            pos++;
            Emit(start, pos - start, CodeTokenKind.String);

            if (verbatim)
                return VerbatimBody(ref pos, dollars > 0) ? ModeCode : (dollars > 0 ? ModeVerbatimInterpolated : ModeVerbatim);

            RegularBody(ref pos, dollars > 0);
            return ModeCode;
        }

        private void RegularBody(ref int pos, bool interpolated)
        {
            int run = pos;
            while (pos < _s.Length)
            {
                char c = _s[pos];
                if (c == '\\')
                {
                    Emit(run, pos - run, CodeTokenKind.String);
                    int e = pos + 2;
                    if (At(pos + 1) == 'u') e = pos + 6;
                    else if (At(pos + 1) == 'U') e = pos + 10;
                    else if (At(pos + 1) == 'x') { e = pos + 2; while (e < _s.Length && e < pos + 6 && Uri.IsHexDigit(_s[e])) e++; }
                    e = Math.Min(e, _s.Length);
                    Emit(pos, e - pos, CodeTokenKind.StringEscape);
                    pos = run = e;
                    continue;
                }
                if (c == '"')
                {
                    pos++;
                    Emit(run, pos - run, CodeTokenKind.String);
                    return;
                }
                if (interpolated && c == '{')
                {
                    if (At(pos + 1) == '{') { pos += 2; continue; }
                    Emit(run, pos - run, CodeTokenKind.String);
                    Hole(ref pos, 1);
                    run = pos;
                    continue;
                }
                pos++;
            }
            Emit(run, pos - run, CodeTokenKind.String);
        }

        public bool VerbatimBody(ref int pos, bool interpolated)
        {
            int run = pos;
            while (pos < _s.Length)
            {
                char c = _s[pos];
                if (c == '"')
                {
                    if (At(pos + 1) == '"')
                    {
                        Emit(run, pos - run, CodeTokenKind.String);
                        Emit(pos, 2, CodeTokenKind.StringEscape);
                        pos += 2;
                        run = pos;
                        continue;
                    }
                    pos++;
                    Emit(run, pos - run, CodeTokenKind.String);
                    return true;
                }
                if (interpolated && c == '{')
                {
                    if (At(pos + 1) == '{') { pos += 2; continue; }
                    Emit(run, pos - run, CodeTokenKind.String);
                    Hole(ref pos, 1);
                    run = pos;
                    continue;
                }
                pos++;
            }
            Emit(run, pos - run, CodeTokenKind.String);
            return false;
        }

        public bool RawBody(ref int pos, int quotes, int dollars)
        {
            int run = pos;
            while (pos < _s.Length)
            {
                char c = _s[pos];
                if (c == '"')
                {
                    int n = 0;
                    while (pos + n < _s.Length && _s[pos + n] == '"') n++;
                    pos += n;
                    if (n >= quotes)
                    {
                        Emit(run, pos - run, CodeTokenKind.String);
                        return true;
                    }
                    continue;
                }
                if (dollars > 0 && c == '{')
                {
                    int n = 0;
                    while (pos + n < _s.Length && _s[pos + n] == '{') n++;
                    if (n >= dollars)
                    {
                        // Extra braces beyond the dollar count are literal text.
                        pos += n - dollars;
                        Emit(run, pos - run, CodeTokenKind.String);
                        Hole(ref pos, dollars);
                        run = pos;
                        continue;
                    }
                    pos += n;
                    continue;
                }
                pos++;
            }
            Emit(run, pos - run, CodeTokenKind.String);
            return false;
        }

        private void Hole(ref int pos, int braces)
        {
            Emit(pos, braces, CodeTokenKind.Punctuation);
            pos += braces;
            int r = Code(ref pos, true);
            if (r == -1 && pos < _s.Length)
            {
                int n = Math.Min(braces, _s.Length - pos);
                Emit(pos, n, CodeTokenKind.Punctuation);
                pos += n;
            }
        }
    }
}

/// <summary>Plain C# without a compiler: lexical highlighting, brace folding and C# indentation rules.</summary>
public class CSharpLanguage : CodeLanguage
{
    public override string Name => "C#";
    public override ICodeLexer? Lexer => CSharpLexer.Instance;
    public override string? LineComment => "//";
    public override string? BlockCommentStart => "/*";
    public override string? BlockCommentEnd => "*/";

    public override bool IsCompletionTrigger(char c) => c == '.';
    public override bool IsSignatureHelpTrigger(char c) => c == '(' || c == ',';

    public override bool IndentsNextLine(string lineBeforeCaret)
    {
        string t = lineBeforeCaret.TrimEnd();
        if (t.Length == 0) return false;
        char last = t[^1];
        if (last == '{' || last == '(' || last == '[') return true;

        // Braceless control statements, such as "if (x)" on its own line.
        string s = t.TrimStart();
        if (last == ')' && (s.StartsWith("if ") || s.StartsWith("if(") || s.StartsWith("for") || s.StartsWith("while")
            || s.StartsWith("else if") || s.StartsWith("using (") || s.StartsWith("lock")))
            return true;
        return s == "else" || s == "do";
    }
}
