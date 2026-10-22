// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.OrigamiUI.Graphite;

/// <summary>
/// Formats Graphite shaders by changing whitespace only: indentation from braces, spacing around operators and
/// punctuation and no trailing whitespace. Comments, strings and the text of preprocessor lines are kept as written.
/// </summary>
internal static class ShaderFormatter
{
    private const int TabWidth = 4;

    private static readonly string[] s_operators = { "<<=", ">>=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "==", "!=", "<=", ">=", "&&", "||", "^^", "<<", ">>", "++", "--" };
    private static readonly HashSet<string> s_spacedBeforeParen = new(StringComparer.Ordinal) { "if", "for", "while", "switch", "return", "else", "do" };
    private static readonly HashSet<string> s_valueKeywords = new(StringComparer.Ordinal) { "return", "case", "else", "do" };

    private enum Role : byte { Word, Punct, Binary, Prefix, Postfix, Comment, Preprocessor }

    private struct Item
    {
        public int Start, End;
        public string Text;
        public Role Role;
        public bool Glsl;
    }

    private sealed class Block
    {
        public int OpenLine, OpenWidth;
        public bool IsSwitch, InCase, FlatCases;
        public int Weight => IsSwitch && InCase && !FlatCases ? 2 : 1;
    }

    private sealed class Line
    {
        public int Start;
        public string Text = "";
        public readonly List<Item> Items = new();
        public string[] Gaps = Array.Empty<string>();
        public string Indent = "";
        public bool Untouched, Preprocessor;
    }

    /// <summary>Edits that format the lines from <paramref name="firstLine"/> to <paramref name="lastLine"/>, the whole text by default.</summary>
    public static List<CodeTextEdit> Format(string text, bool isShaderFile, int firstLine = 0, int lastLine = int.MaxValue)
    {
        var lines = Lex(text, isShaderFile);
        Layout(lines);
        return Edits(lines, firstLine, lastLine);
    }

    /// <summary>Edits after typing <paramref name="typed"/> before <paramref name="offset"/>: the line of a ';', the block of a '}'.</summary>
    public static List<CodeTextEdit> FormatOnType(string text, bool isShaderFile, int offset, char typed)
    {
        var lines = Lex(text, isShaderFile);
        var closes = Layout(lines);

        int at = offset - 1;
        int li = lines.FindLastIndex(l => l.Start <= at);
        if (li < 0) return new List<CodeTextEdit>();
        var line = lines[li];
        int column = at - line.Start;
        int index = line.Items.FindIndex(i => i.Start == column);
        if (index < 0 || line.Items[index].Role != Role.Punct || line.Items[index].Text != typed.ToString()) return new List<CodeTextEdit>();

        if (typed == ';') return line.Items[index].Glsl ? Edits(lines, li, li) : new List<CodeTextEdit>();
        return closes.TryGetValue(at, out int open) ? Edits(lines, open, li) : new List<CodeTextEdit>();
    }

    private static List<Line> Lex(string text, bool isShaderFile)
    {
        var lexer = isShaderFile ? ShaderLexer.ShaderFile : ShaderLexer.GlslFile;
        var lines = new List<Line>();
        var tokens = new List<CodeToken>();
        int state = 0, start = 0;
        while (true)
        {
            int end = text.IndexOf('\n', start);
            int stop = end < 0 ? text.Length : end;
            if (stop > start && text[stop - 1] == '\r') stop--;
            var line = new Line { Start = start, Text = text.Substring(start, stop - start) };
            line.Untouched = state == ShaderLexer.GlslComment || (isShaderFile && state == ShaderLexer.WrapperComment);
            bool glsl = !isShaderFile || state is ShaderLexer.Glsl or ShaderLexer.GlslComment;

            tokens.Clear();
            state = lexer.Tokenize(line.Text, state, tokens);
            AddItems(line, tokens, glsl);
            lines.Add(line);

            if (end < 0) break;
            start = end + 1;
        }
        return lines;
    }

    private static void AddItems(Line line, List<CodeToken> tokens, bool glsl)
    {
        string s = line.Text;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == CodeTokenKind.Operator)
            {
                // The lexer splits operators into single characters, adjacent ones are joined back into GLSL operators.
                int runEnd = t.Start + t.Length;
                while (i + 1 < tokens.Count && tokens[i + 1].Kind == CodeTokenKind.Operator && tokens[i + 1].Start == runEnd)
                    runEnd = tokens[++i].Start + tokens[i].Length;
                for (int p = t.Start; p < runEnd;)
                {
                    int length = 1;
                    foreach (var op in s_operators)
                        if (op.Length <= runEnd - p && string.CompareOrdinal(s, p, op, 0, op.Length) == 0) { length = op.Length; break; }
                    Add(line, p, p + length, CodeTokenKind.Operator, glsl);
                    p += length;
                }
                continue;
            }

            string text = s.Substring(t.Start, t.Length);
            Add(line, t.Start, t.Start + t.Length, t.Kind, glsl);
            if (!glsl && text == "GLSLPROGRAM") glsl = true;
            else if (glsl && text == "ENDGLSL")
            {
                var items = line.Items;
                var last = items[^1];
                last.Glsl = false;
                items[^1] = last;
                glsl = false;
            }
        }
    }

    private static void Add(Line line, int start, int end, CodeTokenKind kind, bool glsl)
    {
        string text = line.Text.Substring(start, end - start);
        Item? prev = null;
        for (int i = line.Items.Count - 1; i >= 0; i--)
            if (line.Items[i].Role != Role.Comment) { prev = line.Items[i]; break; }

        var role = kind switch
        {
            CodeTokenKind.Comment or CodeTokenKind.DocComment => Role.Comment,
            CodeTokenKind.Preprocessor or CodeTokenKind.PreprocessorText => Role.Preprocessor,
            CodeTokenKind.Operator => OperatorRole(text, prev, glsl),
            CodeTokenKind.Punctuation => Role.Punct,
            _ => Role.Word,
        };
        line.Items.Add(new Item { Start = start, End = end, Text = text, Role = role, Glsl = glsl });
    }

    private static Role OperatorRole(string op, Item? prev, bool glsl)
    {
        if (!glsl) return op is "-" or "+" ? Role.Prefix : Role.Binary;
        bool afterValue = prev is { } p && (p.Role is Role.Postfix || p.Text is ")" or "]" || (p.Role == Role.Word && !s_valueKeywords.Contains(p.Text)));
        if (op is "++" or "--") return afterValue ? Role.Postfix : Role.Prefix;
        if (op is "!" or "~") return Role.Prefix;
        if (op is "-" or "+") return afterValue ? Role.Binary : Role.Prefix;
        return Role.Binary;
    }

    // Indents every line and picks the spacing between its tokens. Returns the line each '}' closes, by its offset.
    private static Dictionary<int, int> Layout(List<Line> lines)
    {
        string unit = IndentUnit(lines);
        var stack = new List<Block>();
        var closes = new Dictionary<int, int>();
        int paren = 0, delta = 0;
        string? lastCode = null;
        bool lastCodeGlsl = false, lastCodeEndsValue = false, pendingSwitch = false;

        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            var items = line.Items;
            int oldWidth = Width(line.Text);
            line.Preprocessor = !line.Untouched && items.Count > 0 && items[0].Role == Role.Preprocessor;

            if (!line.Untouched && items.Count > 0)
            {
                var first = items[0];
                var top = stack.Count > 0 ? stack[^1] : null;
                bool caseLine = first.Glsl && first.Text is "case" or "default";

                if (line.Preprocessor) line.Indent = IndentOf(Math.Max(0, oldWidth + delta), unit);
                else
                {
                    bool continuation = first.Glsl && first.Role != Role.Comment && first.Text is not ("{" or "}")
                        && (paren > 0 || (lastCodeGlsl && lastCode is not (null or ";" or "{" or "}" or ":")));
                    int level = 0;
                    foreach (var b in stack) level += b.Weight;
                    // Case labels sit one level inside their switch, or level with its braces when the file writes them so.
                    if (caseLine && top is { IsSwitch: true, InCase: false }) top.FlatCases = top.OpenWidth > 0 && oldWidth <= top.OpenWidth;
                    if (first.Text == "}" && first.Role == Role.Punct && top != null) level -= top.Weight;
                    else if (caseLine && top is { IsSwitch: true }) level -= top.FlatCases ? 1 : top.InCase ? 1 : 0;

                    // A continued expression can start with its operator, binary since the previous line holds its left side.
                    if (continuation && first.Role == Role.Prefix && first.Text is "+" or "-" && lastCodeEndsValue)
                        items[0] = first with { Role = Role.Binary };

                    if (continuation) line.Indent = IndentOf(Math.Max(0, oldWidth + delta), unit);
                    else
                    {
                        line.Indent = Repeat(unit, level);
                        delta = Width(line.Indent) - oldWidth;
                    }

                    line.Gaps = new string[items.Count];
                    for (int i = 1; i < items.Count; i++)
                    {
                        string original = line.Text.Substring(items[i - 1].End, items[i].Start - items[i - 1].End);
                        line.Gaps[i] = Gap(items[i - 1], items[i], caseLine) ?? (original.Length == 0 && items[i].Role == Role.Comment ? " " : original);
                    }
                }
                if (caseLine && top is { IsSwitch: true }) top.InCase = true;
            }

            foreach (var item in items)
            {
                if (item.Role is Role.Comment or Role.Preprocessor) continue;
                if (item.Role == Role.Punct)
                {
                    switch (item.Text)
                    {
                        case "{":
                            stack.Add(new Block { OpenLine = li, OpenWidth = oldWidth, IsSwitch = pendingSwitch });
                            pendingSwitch = false;
                            break;
                        case "}":
                            if (stack.Count > 0)
                            {
                                closes[line.Start + item.Start] = stack[^1].OpenLine;
                                stack.RemoveAt(stack.Count - 1);
                            }
                            break;
                        case "(" or "[": paren++; break;
                        case ")" or "]": paren = Math.Max(0, paren - 1); break;
                        case ";": pendingSwitch = false; break;
                    }
                }
                else if (item.Role == Role.Word && item.Glsl && item.Text == "switch") pendingSwitch = true;
                if (!line.Untouched)
                {
                    lastCode = item.Text;
                    lastCodeGlsl = item.Glsl;
                    lastCodeEndsValue = item.Role is Role.Word or Role.Postfix || item.Text is ")" or "]";
                }
            }
        }
        return closes;
    }

    // The whitespace between two tokens on a line, or null to keep what is there.
    private static string? Gap(Item a, Item b, bool caseLine)
    {
        if (a.Role == Role.Comment || b.Role == Role.Comment) return null;
        if (a.Glsl != b.Glsl) return " ";
        string x = a.Text, y = b.Text;

        if (!a.Glsl)
        {
            if (y is ")" or "]" or ",") return "";
            if (x is "(" or "[") return "";
            if (a.Role == Role.Prefix) return "";
            if (y == "(") return a.Role == Role.Word ? "" : " ";
            if (x == "{" && y == "}") return null;
            return " ";
        }

        if (y is ")" or "]" or "," or ";") return "";
        if (x is "(" or "[") return "";
        if (x is "," or ";") return " ";
        if (x == "." || y == ".") return "";
        if (b.Role == Role.Postfix || a.Role == Role.Prefix) return "";
        if (y == ":" && caseLine) return "";
        if (a.Role == Role.Binary || b.Role == Role.Binary) return " ";
        if (y == "(") return a.Role == Role.Word ? (s_spacedBeforeParen.Contains(x) ? " " : "") : x is ")" or "]" ? "" : " ";
        if (y == "[") return a.Role == Role.Word || x is ")" or "]" ? "" : " ";
        if (x == "{" && y == "}") return null;
        return " ";
    }

    private static List<CodeTextEdit> Edits(List<Line> lines, int firstLine, int lastLine)
    {
        var edits = new List<CodeTextEdit>();
        for (int li = Math.Max(0, firstLine); li <= Math.Min(lastLine, lines.Count - 1); li++)
        {
            var line = lines[li];
            string s = line.Text;
            if (line.Untouched) continue;

            int end = s.Length;
            while (end > 0 && s[end - 1] is ' ' or '\t') end--;
            if (line.Items.Count == 0)
            {
                if (s.Length > 0) edits.Add(new CodeTextEdit(line.Start, s.Length, ""));
                continue;
            }

            var items = line.Items;
            Replace(edits, line, 0, items[0].Start, line.Indent);
            if (!line.Preprocessor)
                for (int i = 1; i < items.Count; i++) Replace(edits, line, items[i - 1].End, items[i].Start, line.Gaps[i]);
            if (end < s.Length) edits.Add(new CodeTextEdit(line.Start + end, s.Length - end, ""));
        }
        return edits;
    }

    private static void Replace(List<CodeTextEdit> edits, Line line, int from, int to, string with)
    {
        if (to - from == with.Length && string.CompareOrdinal(line.Text, from, with, 0, with.Length) == 0) return;
        edits.Add(new CodeTextEdit(line.Start + from, to - from, with));
    }

    private static string IndentUnit(List<Line> lines)
    {
        int tabs = 0, spaces = 0;
        foreach (var line in lines)
        {
            if (line.Text.Length == 0 || line.Untouched) continue;
            if (line.Text[0] == '\t') tabs++;
            else if (line.Text[0] == ' ') spaces++;
        }
        return tabs > spaces ? "\t" : new string(' ', TabWidth);
    }

    private static int Width(string text)
    {
        int width = 0;
        foreach (char c in text)
        {
            if (c == ' ') width++;
            else if (c == '\t') width += TabWidth - width % TabWidth;
            else break;
        }
        return width;
    }

    private static string IndentOf(int width, string unit)
        => unit == "\t" ? new string('\t', width / TabWidth) + new string(' ', width % TabWidth) : new string(' ', width);

    private static string Repeat(string unit, int level)
    {
        if (level <= 0) return "";
        var sb = new System.Text.StringBuilder(unit.Length * level);
        for (int i = 0; i < level; i++) sb.Append(unit);
        return sb.ToString();
    }
}
