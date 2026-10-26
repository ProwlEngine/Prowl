// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

namespace Prowl.OrigamiUI.Graphite;

public enum ShaderSymbolKind : byte
{
    Function, Struct, Field, Global, Uniform, Input, Output, Buffer, Block, Macro, Local, Parameter, Property, Pass, Variant,
}

/// <summary>A declaration found in a shader or one of its includes.</summary>
public sealed class ShaderSymbol
{
    public string Name = "";
    public ShaderSymbolKind Kind;

    /// <summary>Declared type without array brackets, such as vec3 or a struct's name. The return type of a function.</summary>
    public string Type = "";

    /// <summary>The declaration as shown in hover and completion, such as "vec3 Normal(vec2 uv)".</summary>
    public string Detail = "";
    public string Doc = "";

    /// <summary>Offset and length of the name in its file.</summary>
    public int Offset, Length;

    /// <summary>Where the name can be used, as offsets in its own file.</summary>
    public int ScopeStart, ScopeEnd;

    /// <summary>The struct or block a field belongs to.</summary>
    public string? Container;

    /// <summary>Parameters of a function, as type and name.</summary>
    public List<(string Type, string Name)>? Parameters;

    /// <summary>The index the symbol was declared in.</summary>
    public ShaderIndex Owner = null!;

    public bool IsLocal => Kind is ShaderSymbolKind.Local or ShaderSymbolKind.Parameter;
}

internal enum TokKind : byte { Ident, Number, String, Punct }

internal readonly struct Tok
{
    public readonly int Offset, Length;
    public readonly TokKind Kind;
    public readonly string Text;

    public Tok(int offset, int length, TokKind kind, string text)
    {
        Offset = offset;
        Length = length;
        Kind = kind;
        Text = text;
    }

    public int End => Offset + Length;
}

internal sealed record IncludeRef(string Name, int Offset, int Length, int ScopeStart, int ScopeEnd);

internal sealed record ShaderSection(string Name, int HeaderOffset, int BodyStart, int BodyEnd);

internal sealed record ShaderProgram(int Start, int End);

/// <summary>A Surface block: its code runs inside the passes of a model shader, such as Graphite/Standard/Lit.shader.</summary>
internal sealed record SurfaceBlock(string Model, ShaderProgram Program);

internal sealed record Directive(string Name, string Args, int Offset, int ArgsOffset, int End);

/// <summary>
/// A tolerant scan of one shader file: its GLSL tokens, sections and declarations with their scopes, its includes and
/// its wrapper properties and passes. It never fails, so it works on code that is half typed.
/// </summary>
public sealed class ShaderIndex
{
    public string Text { get; }

    /// <summary>Path or library name of the file, null for the document being edited.</summary>
    public string? Path { get; }

    public bool IsShaderFile { get; }

    public List<ShaderSymbol> Symbols { get; } = new();

    internal List<Tok> GlslTokens { get; } = new();
    internal List<Tok> WrapperTokens { get; } = new();
    internal List<IncludeRef> Includes { get; } = new();
    internal List<ShaderSection> Sections { get; } = new();
    internal List<ShaderProgram> Programs { get; } = new();
    internal List<Directive> Directives { get; } = new();
    internal List<SurfaceBlock> Surfaces { get; } = new();

    private Dictionary<int, int> _match = new();

    public ShaderIndex(string text, string? path, bool isShaderFile)
    {
        Text = text;
        Path = path;
        IsShaderFile = isShaderFile;
        Scan();
        MatchBrackets();
        FindSurfaces();
        FindSections();
        foreach (var program in Programs) ParseProgram(program);
        ParseWrapper();
        ParseDirectives();
    }

    #region Scanning

    private void Scan()
    {
        string s = Text;
        int n = s.Length;
        bool glsl = !IsShaderFile;
        int programStart = glsl ? 0 : -1;
        int i = 0;
        bool lineStart = true;

        while (i < n)
        {
            char c = s[i];
            if (c == '\n') { lineStart = true; i++; continue; }
            if (c == ' ' || c == '\t' || c == '\r') { i++; continue; }

            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                while (i < n && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? n : close + 2;
                continue;
            }

            if (glsl && lineStart && c == '#')
            {
                int end = s.IndexOf('\n', i);
                if (end < 0) end = n;
                // A backslash at the end of a line continues the directive.
                while (end > 0 && end < n && s[end - 1] == '\\' || (end > 1 && end < n && s[end - 1] == '\r' && s[end - 2] == '\\'))
                {
                    int next = s.IndexOf('\n', end + 1);
                    end = next < 0 ? n : next;
                }
                AddDirective(i, end);
                i = end;
                continue;
            }
            lineStart = false;

            if (c == '"')
            {
                int start = i++;
                while (i < n && s[i] != '"' && s[i] != '\n') i += s[i] == '\\' ? 2 : 1;
                i = Math.Min(n, i + 1);
                Add(glsl, new Tok(start, i - start, TokKind.String, s.Substring(start, i - start)));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '.')) i++;
                Add(glsl, new Tok(start, i - start, TokKind.Number, s.Substring(start, i - start)));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                string word = s.Substring(start, i - start);
                if (IsShaderFile && !glsl && word == "GLSLPROGRAM")
                {
                    glsl = true;
                    programStart = i;
                    WrapperTokens.Add(new Tok(start, i - start, TokKind.Ident, word));
                    continue;
                }
                if (IsShaderFile && glsl && word == "ENDGLSL")
                {
                    glsl = false;
                    Programs.Add(new ShaderProgram(programStart, start));
                    WrapperTokens.Add(new Tok(start, i - start, TokKind.Ident, word));
                    continue;
                }
                Add(glsl, new Tok(start, i - start, TokKind.Ident, word));
                continue;
            }

            Add(glsl, new Tok(i, 1, TokKind.Punct, c.ToString()));
            i++;
        }

        if (glsl) Programs.Add(new ShaderProgram(programStart, n));
    }

    private void Add(bool glsl, Tok tok) => (glsl ? GlslTokens : WrapperTokens).Add(tok);

    private void AddDirective(int start, int end)
    {
        int i = start + 1;
        while (i < end && (Text[i] == ' ' || Text[i] == '\t')) i++;
        int nameStart = i;
        while (i < end && char.IsLetter(Text[i])) i++;
        string name = Text.Substring(nameStart, i - nameStart);
        while (i < end && (Text[i] == ' ' || Text[i] == '\t')) i++;
        int argsEnd = end;
        int comment = Text.IndexOf("//", i, Math.Max(0, end - i), StringComparison.Ordinal);
        if (comment >= 0) argsEnd = comment;
        Directives.Add(new Directive(name, Text.Substring(i, argsEnd - i).TrimEnd(), start, i, end));
    }

    private void MatchBrackets()
    {
        var stack = new Stack<int>();
        for (int i = 0; i < GlslTokens.Count; i++)
        {
            var t = GlslTokens[i];
            if (t.Kind != TokKind.Punct) continue;
            char c = t.Text[0];
            if (c is '{' or '(' or '[') stack.Push(i);
            else if (c is '}' or ')' or ']')
            {
                // Unbalanced code still pairs the closer with the nearest opener of its own kind.
                char open = c == '}' ? '{' : c == ')' ? '(' : '[';
                var skipped = new List<int>();
                while (stack.Count > 0 && GlslTokens[stack.Peek()].Text[0] != open) skipped.Add(stack.Pop());
                if (stack.Count > 0)
                {
                    int o = stack.Pop();
                    _match[o] = i;
                    _match[i] = o;
                }
                else for (int k = skipped.Count - 1; k >= 0; k--) stack.Push(skipped[k]);
            }
        }
    }

    /// <summary>Index of the bracket closing the one at <paramref name="i"/>, or the last token when it never closes.</summary>
    private int Close(int i) => _match.TryGetValue(i, out int m) ? m : GlslTokens.Count - 1;

    private void FindSections()
    {
        if (!IsShaderFile) return;
        foreach (var program in Programs)
        {
            int depth = 0;
            for (int i = 0; i < GlslTokens.Count; i++)
            {
                var t = GlslTokens[i];
                if (t.Offset < program.Start || t.Offset >= program.End) continue;
                if (t.Kind == TokKind.Punct)
                {
                    if (t.Text == "{") depth++;
                    else if (t.Text == "}") depth--;
                    continue;
                }
                if (depth != 0 || t.Kind != TokKind.Ident || !ShaderSyntax.Sections.ContainsKey(t.Text)) continue;
                if (i + 1 >= GlslTokens.Count || GlslTokens[i + 1].Text != "{") continue;

                int close = Close(i + 1);
                int bodyEnd = close < GlslTokens.Count && GlslTokens[close].Text == "}" ? GlslTokens[close].Offset : program.End;
                Sections.Add(new ShaderSection(t.Text, t.Offset, GlslTokens[i + 1].End, bodyEnd));
            }
        }
    }

    #endregion

    #region Scopes

    /// <summary>The section an offset is in, or null outside every section.</summary>
    internal ShaderSection? SectionAt(int offset)
    {
        foreach (var s in Sections)
            if (offset >= s.BodyStart && offset <= s.BodyEnd) return s;
        return null;
    }

    /// <summary>The Surface block an offset is in, or null.</summary>
    internal SurfaceBlock? SurfaceAt(int offset)
    {
        foreach (var s in Surfaces)
            if (offset >= s.Program.Start && offset <= s.Program.End) return s;
        return null;
    }

    // Surface "Model" followed by its block: the next program is the block's code.
    private void FindSurfaces()
    {
        for (int i = 0; i + 1 < WrapperTokens.Count; i++)
        {
            var t = WrapperTokens[i];
            var next = WrapperTokens[i + 1];
            if (t.Kind != TokKind.Ident || !string.Equals(t.Text, "Surface", StringComparison.OrdinalIgnoreCase) || next.Kind != TokKind.String) continue;
            var program = Programs.Find(p => p.Start > next.End);
            if (program != null) Surfaces.Add(new SurfaceBlock(next.Text.Trim('"'), program));
        }
    }

    internal ShaderProgram? ProgramAt(int offset)
    {
        foreach (var p in Programs)
            if (offset >= p.Start && offset <= p.End) return p;
        return null;
    }

    /// <summary>Where a top level declaration at the offset is visible: its section, or the whole program for Shared and unsectioned code.</summary>
    private (int Start, int End) GlobalScopeAt(int offset)
    {
        var section = SectionAt(offset);
        if (section != null && section.Name != "Shared") return (section.BodyStart, section.BodyEnd);
        var program = ProgramAt(offset);
        return program != null ? (program.Start, program.End) : (0, Text.Length);
    }

    /// <summary>The stages code at the offset runs in.</summary>
    internal GlslStages StagesAt(int offset)
    {
        if (!IsShaderFile) return GlslStages.All;
        if (SurfaceAt(offset) != null) return GlslStages.Fragment;
        return SectionAt(offset)?.Name switch
        {
            "Vertex" => GlslStages.Vertex,
            "Fragment" => GlslStages.Fragment,
            "Compute" => GlslStages.Compute,
            _ => GlslStages.All,
        };
    }

    #endregion

    #region Declarations

    private static readonly HashSet<string> s_storage = new(StringComparer.Ordinal) { "uniform", "in", "out", "buffer", "shared", "inout", "attribute", "varying" };

    private bool IsTypeName(string word) => GlslBuiltins.Types.Contains(word) || word == "void" || _structs.Contains(word);

    private readonly HashSet<string> _structs = new(StringComparer.Ordinal);

    private Tok T(int i) => i >= 0 && i < GlslTokens.Count ? GlslTokens[i] : default;

    private bool Is(int i, string text) => i >= 0 && i < GlslTokens.Count && GlslTokens[i].Text == text;

    private void ParseProgram(ShaderProgram program)
    {
        int i = 0;
        while (i < GlslTokens.Count && GlslTokens[i].Offset < program.Start) i++;

        while (i < GlslTokens.Count && GlslTokens[i].Offset < program.End)
        {
            var t = GlslTokens[i];
            // Section headers open a new top level.
            if (IsShaderFile && t.Kind == TokKind.Ident && ShaderSyntax.Sections.ContainsKey(t.Text) && Is(i + 1, "{"))
            {
                i += 2;
                continue;
            }
            if (t.Text == "}") { i++; continue; }
            i = ParseTopLevel(i);
        }
    }

    // Parses one top level statement and returns the token after it.
    private int ParseTopLevel(int i)
    {
        int start = i;
        string? storage = null;
        var qualifiers = new StringBuilder();

        while (i < GlslTokens.Count)
        {
            var t = GlslTokens[i];
            if (t.Text == "layout" && Is(i + 1, "("))
            {
                int close = Close(i + 1);
                qualifiers.Append(Text, t.Offset, T(close).End - t.Offset).Append(' ');
                i = close + 1;
                continue;
            }
            if (t.Kind == TokKind.Ident && GlslBuiltins.Keywords.Contains(t.Text) && t.Text != "struct" && t.Text != "void" && t.Text != "true" && t.Text != "false")
            {
                if (s_storage.Contains(t.Text)) storage = t.Text;
                qualifiers.Append(t.Text).Append(' ');
                i++;
                continue;
            }
            break;
        }

        if (i >= GlslTokens.Count) return i;
        var head = GlslTokens[i];

        if (head.Text == "struct") return ParseStruct(i, start);

        // Interface block: qualifiers then Name { members } [instance] ;
        if (storage != null && head.Kind == TokKind.Ident && Is(i + 1, "{"))
            return ParseBlock(i, storage, qualifiers.ToString());

        if (head.Kind == TokKind.Ident && T(i + 1).Kind == TokKind.Ident)
        {
            int typeIndex = i;
            int nameIndex = i + 1;
            if (Is(nameIndex + 1, "("))
                return ParseFunction(typeIndex, nameIndex, start);
            return ParseVariables(typeIndex, storage, qualifiers.ToString(), start, local: false, 0, 0);
        }

        // An array type such as vec3[4] name.
        if (head.Kind == TokKind.Ident && Is(i + 1, "["))
        {
            int close = Close(i + 1);
            if (T(close + 1).Kind == TokKind.Ident)
                return ParseVariables(i, storage, qualifiers.ToString(), start, local: false, 0, 0, typeEnd: close);
        }

        return SkipStatement(i);
    }

    private int SkipStatement(int i)
    {
        while (i < GlslTokens.Count)
        {
            var t = GlslTokens[i];
            if (t.Text == ";") return i + 1;
            if (t.Text == "{" ) return Close(i) + 1;
            if (t.Text == "}") return i;
            if (t.Text is "(" or "[") { i = Close(i) + 1; continue; }
            i++;
        }
        return i;
    }

    private string Doc(int offset)
    {
        // Line comments directly above a declaration document it.
        int lineStart = Text.LastIndexOf('\n', Math.Max(0, offset - 1));
        var lines = new List<string>();
        int cursor = lineStart;
        while (cursor > 0)
        {
            int prevStart = Text.LastIndexOf('\n', cursor - 1);
            string line = Text.Substring(prevStart + 1, cursor - prevStart - 1).Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal)) break;
            lines.Insert(0, line.TrimStart('/').Trim());
            cursor = prevStart;
            if (prevStart < 0) break;
        }
        return string.Join(" ", lines);
    }

    private ShaderSymbol Add(ShaderSymbolKind kind, Tok name, string type, string detail, int scopeStart, int scopeEnd, string? container = null)
    {
        var symbol = new ShaderSymbol
        {
            Name = name.Text, Kind = kind, Type = type, Detail = detail, Offset = name.Offset, Length = name.Length,
            ScopeStart = scopeStart, ScopeEnd = scopeEnd, Container = container, Owner = this,
        };
        Symbols.Add(symbol);
        return symbol;
    }

    private int ParseStruct(int i, int start)
    {
        var name = T(i + 1);
        if (name.Kind != TokKind.Ident) return SkipStatement(i + 1);
        var (s0, s1) = GlobalScopeAt(name.Offset);
        _structs.Add(name.Text);
        var symbol = Add(ShaderSymbolKind.Struct, name, name.Text, "struct " + name.Text, s0, s1);
        symbol.Doc = Doc(T(start).Offset);

        if (!Is(i + 2, "{")) return SkipStatement(i + 2);
        int close = Close(i + 2);
        ParseMembers(i + 3, close, name.Text, ShaderSymbolKind.Field, s0, s1);

        int j = close + 1;
        // struct S { ... } instance;
        if (T(j).Kind == TokKind.Ident) return ParseDeclarators(j, name.Text, ShaderSymbolKind.Global, "", s0, s1);
        return Is(j, ";") ? j + 1 : j;
    }

    private void ParseMembers(int from, int to, string container, ShaderSymbolKind kind, int s0, int s1)
    {
        int i = from;
        while (i < to)
        {
            while (i < to && (GlslBuiltins.Keywords.Contains(T(i).Text) || T(i).Text == "layout"))
                i = T(i).Text == "layout" && Is(i + 1, "(") ? Close(i + 1) + 1 : i + 1;
            if (i >= to) break;
            if (T(i).Kind == TokKind.Ident && T(i + 1).Kind == TokKind.Ident)
            {
                string type = T(i).Text;
                int j = i + 1;
                while (j < to)
                {
                    var name = T(j);
                    if (name.Kind != TokKind.Ident) break;
                    string array = "";
                    int k = j + 1;
                    if (Is(k, "[")) { int c = Close(k); array = Text.Substring(T(k).Offset, T(c).End - T(k).Offset); k = c + 1; }
                    var f = Add(kind, name, type, $"{type} {name.Text}{array}", s0, s1, container);
                    f.Doc = Doc(T(i).Offset);
                    if (Is(k, ",")) { j = k + 1; continue; }
                    j = k;
                    break;
                }
                i = j;
            }
            while (i < to && !Is(i, ";")) i++;
            i++;
        }
    }

    private int ParseBlock(int i, string storage, string qualifiers)
    {
        var name = T(i);
        int close = Close(i + 1);
        var (s0, s1) = GlobalScopeAt(name.Offset);
        int j = close + 1;
        var instance = T(j).Kind == TokKind.Ident ? T(j) : default;

        var block = Add(ShaderSymbolKind.Block, name, name.Text, $"{qualifiers.Trim()} {name.Text}".Trim(), s0, s1);
        block.Doc = Doc(name.Offset);
        _structs.Add(name.Text);

        // Without an instance name the members are globals, otherwise they are reached through the instance.
        var memberKind = instance.Length > 0 ? ShaderSymbolKind.Field : KindFor(storage);
        ParseMembers(i + 2, close, name.Text, memberKind, s0, s1);
        if (instance.Length == 0)
            foreach (var s in Symbols)
                if (s.Container == name.Text && s.Owner == this && s.Kind == memberKind) s.Container = null;

        if (instance.Length > 0)
        {
            Add(KindFor(storage), instance, name.Text, $"{storage} {name.Text} {instance.Text}", s0, s1);
            j++;
            if (Is(j, "[")) j = Close(j) + 1;
        }
        return Is(j, ";") ? j + 1 : j;
    }

    private static ShaderSymbolKind KindFor(string? storage) => storage switch
    {
        "uniform" => ShaderSymbolKind.Uniform,
        "in" or "attribute" or "varying" => ShaderSymbolKind.Input,
        "out" => ShaderSymbolKind.Output,
        "buffer" => ShaderSymbolKind.Buffer,
        _ => ShaderSymbolKind.Global,
    };

    private int ParseVariables(int typeIndex, string? storage, string qualifiers, int start, bool local, int scopeStart, int scopeEnd, int typeEnd = -1)
    {
        string type = T(typeIndex).Text;
        int nameIndex = (typeEnd >= 0 ? typeEnd : typeIndex) + 1;
        if (!local) (scopeStart, scopeEnd) = GlobalScopeAt(T(typeIndex).Offset);
        var kind = local ? ShaderSymbolKind.Local : KindFor(storage);
        string prefix = qualifiers.Trim();
        return ParseDeclarators(nameIndex, type, kind, prefix, scopeStart, scopeEnd, T(start).Offset, local);
    }

    private int ParseDeclarators(int i, string type, ShaderSymbolKind kind, string prefix, int s0, int s1, int docOffset = -1, bool local = false)
    {
        while (i < GlslTokens.Count)
        {
            var name = T(i);
            if (name.Kind != TokKind.Ident) break;
            int k = i + 1;
            string array = "";
            if (Is(k, "[")) { int c = Close(k); array = Text.Substring(T(k).Offset, T(c).End - T(k).Offset); k = c + 1; }
            var symbol = Add(kind, name, type, $"{(prefix.Length > 0 ? prefix + " " : "")}{type} {name.Text}{array}", local ? name.Offset : s0, s1);
            if (docOffset >= 0) symbol.Doc = Doc(docOffset);

            // Skip an initializer up to the next declarator or the end of the statement.
            while (k < GlslTokens.Count && !Is(k, ",") && !Is(k, ";") && !Is(k, "}"))
                k = T(k).Text is "(" or "[" or "{" ? Close(k) + 1 : k + 1;
            if (Is(k, ",")) { i = k + 1; continue; }
            return Is(k, ";") ? k + 1 : k;
        }
        return SkipStatement(i);
    }

    private int ParseFunction(int typeIndex, int nameIndex, int start)
    {
        var name = T(nameIndex);
        int open = nameIndex + 1;
        int close = Close(open);
        var (s0, s1) = GlobalScopeAt(name.Offset);
        string returnType = T(typeIndex).Text;

        var parameters = new List<(string Type, string Name)>();
        var paramTokens = new List<(Tok Name, string Type)>();
        int i = open + 1;
        while (i < close)
        {
            int segEnd = i;
            while (segEnd < close && !Is(segEnd, ",")) segEnd = T(segEnd).Text is "(" or "[" ? Close(segEnd) + 1 : segEnd + 1;
            var idents = new List<int>();
            for (int k = i; k < segEnd; k++)
                if (T(k).Kind == TokKind.Ident && !GlslBuiltins.Keywords.Contains(T(k).Text)) idents.Add(k);
            if (idents.Count >= 1)
            {
                string type = T(idents[0]).Text;
                if (idents.Count >= 2)
                {
                    var pname = T(idents[1]);
                    parameters.Add((type, pname.Text));
                    paramTokens.Add((pname, type));
                }
                else if (type != "void") parameters.Add((type, ""));
            }
            i = segEnd + 1;
        }

        string detail = $"{returnType} {name.Text}({string.Join(", ", parameters.ConvertAll(p => p.Name.Length > 0 ? p.Type + " " + p.Name : p.Type))})";
        int after = close + 1;
        bool isDefinition = Is(after, "{");

        // A prototype and its definition are one function, the definition wins.
        var existing = Symbols.Find(s => s.Kind == ShaderSymbolKind.Function && s.Name == name.Text && s.Detail == detail && s.Owner == this);
        if (existing == null || isDefinition)
        {
            if (existing != null) Symbols.Remove(existing);
            var fn = Add(ShaderSymbolKind.Function, name, returnType, detail, s0, s1);
            fn.Parameters = parameters;
            fn.Doc = Doc(T(start).Offset);
        }

        if (!isDefinition) return Is(after, ";") ? after + 1 : after;

        int bodyClose = Close(after);
        int bodyEnd = T(bodyClose).End;
        foreach (var (pname, type) in paramTokens)
            Add(ShaderSymbolKind.Parameter, pname, type, $"{type} {pname.Text}", T(after).Offset, bodyEnd);
        ParseBody(after + 1, bodyClose);
        return bodyClose + 1;
    }

    // Finds local declarations, each visible from its name to the end of its enclosing block.
    private void ParseBody(int from, int to)
    {
        var blockEnds = new Stack<int>();
        blockEnds.Push(T(to).End);
        int i = from;
        bool statementStart = true;
        while (i < to)
        {
            var t = T(i);
            if (t.Text == "{") { blockEnds.Push(T(Close(i)).End); i++; statementStart = true; continue; }
            if (t.Text == "}") { if (blockEnds.Count > 1) blockEnds.Pop(); i++; statementStart = true; continue; }
            if (t.Text is ";") { i++; statementStart = true; continue; }

            // for (int i = 0; ...) declares into the loop.
            if (t.Text == "for" && Is(i + 1, "("))
            {
                int close = Close(i + 1);
                int loopEnd = Is(close + 1, "{") ? T(Close(close + 1)).End : blockEnds.Peek();
                int k = i + 2;
                while (k < close && GlslBuiltins.Keywords.Contains(T(k).Text)) k++;
                if (T(k).Kind == TokKind.Ident && T(k + 1).Kind == TokKind.Ident && (IsTypeName(T(k).Text) || char.IsUpper(T(k).Text[0])))
                    ParseDeclarators(k + 1, T(k).Text, ShaderSymbolKind.Local, "", 0, loopEnd, -1, local: true);
                i = close + 1;
                statementStart = true;
                continue;
            }

            if (statementStart)
            {
                int k = i;
                while (k < to && GlslBuiltins.Keywords.Contains(T(k).Text) && T(k).Text is not ("return" or "true" or "false")) k++;
                if (T(k).Kind == TokKind.Ident && !GlslBuiltins.ControlKeywords.Contains(T(k).Text))
                {
                    int nameAt = k + 1;
                    if (Is(nameAt, "[") ) nameAt = Close(nameAt) + 1;
                    if (T(nameAt).Kind == TokKind.Ident && !Is(nameAt + 1, "("))
                    {
                        i = ParseDeclarators(nameAt, T(k).Text, ShaderSymbolKind.Local, "", 0, blockEnds.Peek(), -1, local: true);
                        statementStart = true;
                        continue;
                    }
                }
            }
            statementStart = false;
            i = t.Text is "(" or "[" ? Close(i) + 1 : i + 1;
        }
    }

    #endregion

    #region Directives and wrapper

    private void ParseDirectives()
    {
        foreach (var d in Directives)
        {
            var (s0, s1) = GlobalScopeAt(d.Offset);
            switch (d.Name)
            {
                case "include":
                {
                    int q = Text.IndexOfAny(new[] { '"', '<' }, d.ArgsOffset, d.End - d.ArgsOffset);
                    if (q < 0) break;
                    char closeChar = Text[q] == '"' ? '"' : '>';
                    int qe = Text.IndexOf(closeChar, q + 1, d.End - q - 1);
                    if (qe < 0) qe = d.End;
                    Includes.Add(new IncludeRef(Text.Substring(q + 1, qe - q - 1), q + 1, qe - q - 1, s0, s1));
                    break;
                }
                case "define":
                {
                    int i = d.ArgsOffset;
                    int nameStart = i;
                    while (i < d.End && (char.IsLetterOrDigit(Text[i]) || Text[i] == '_')) i++;
                    if (i == nameStart) break;
                    string name = Text.Substring(nameStart, i - nameStart);
                    string rest = Text.Substring(i, Math.Max(0, d.Offset + (d.ArgsOffset - d.Offset) + d.Args.Length - i)).Trim();
                    var macro = Add(ShaderSymbolKind.Macro, new Tok(nameStart, name.Length, TokKind.Ident, name), "", $"#define {name}{(rest.Length > 0 && rest[0] == '(' ? "" : " ")}{rest}", d.Offset, s1);
                    macro.Doc = Doc(d.Offset);
                    if (i < d.End && Text[i] == '(')
                    {
                        int pc = Text.IndexOf(')', i);
                        if (pc > i)
                        {
                            macro.Parameters = new List<(string, string)>();
                            foreach (var p in Text.Substring(i + 1, pc - i - 1).Split(','))
                                if (p.Trim().Length > 0) macro.Parameters.Add(("", p.Trim()));
                        }
                    }
                    break;
                }
                case "pragma":
                {
                    var parts = d.Args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) break;
                    var program = ProgramAt(d.Offset);
                    int p0 = program?.Start ?? 0, p1 = program?.End ?? Text.Length;
                    if (parts[0] == "variant")
                    {
                        int at = parts[1] == "global" ? 2 : 1;
                        if (parts.Length <= at) break;
                        string axis = parts[at];
                        int axisOffset = Text.IndexOf(axis, d.ArgsOffset, StringComparison.Ordinal);
                        bool global = at == 2;
                        string scope = global ? "global variant" : "material variant";
                        if (parts.Length == at + 1)
                            AddVariant(axis, axis, axisOffset, $"{scope} toggle {axis}", p0, p1);
                        for (int v = at + 1; v < parts.Length; v++)
                            AddVariant($"{axis}_{parts[v]}", axis, axisOffset, $"{scope} {axis} = {parts[v]}", p0, p1);
                    }
                    else if (parts[0] == "global" && parts.Length >= 3)
                    {
                        string type = parts[1];
                        int search = d.ArgsOffset;
                        for (int v = 2; v < parts.Length; v++)
                        {
                            int at = Text.IndexOf(parts[v], search, StringComparison.Ordinal);
                            search = at + parts[v].Length;
                            var g = Add(ShaderSymbolKind.Global, new Tok(at, parts[v].Length, TokKind.Ident, parts[v]), type, $"global {type} {parts[v]}", p0, p1);
                            g.Doc = "Set globally by the renderer, not per material.";
                        }
                    }
                    break;
                }
            }
        }
    }

    private void AddVariant(string name, string axis, int offset, string detail, int s0, int s1)
    {
        var symbol = Add(ShaderSymbolKind.Variant, new Tok(offset, axis.Length, TokKind.Ident, name), "", detail, s0, s1);
        symbol.Doc = "Defined while the variant is on.";
    }

    // Properties, pass names and texture helper macros from the ShaderDef wrapper.
    private void ParseWrapper()
    {
        if (!IsShaderFile) return;
        var w = WrapperTokens;
        int depth = 0;
        int propertiesDepth = -1;
        for (int i = 0; i < w.Count; i++)
        {
            var t = w[i];
            if (t.Text == "{") { depth++; continue; }
            if (t.Text == "}") { depth--; if (depth < propertiesDepth) propertiesDepth = -1; continue; }

            if (t.Kind == TokKind.Ident && string.Equals(t.Text, "Properties", StringComparison.OrdinalIgnoreCase) && i + 1 < w.Count && w[i + 1].Text == "{")
            {
                propertiesDepth = depth + 1;
                continue;
            }

            // _Name ( "Display" , Type )
            if (propertiesDepth >= 0 && depth == propertiesDepth && t.Kind == TokKind.Ident && i + 5 < w.Count && w[i + 1].Text == "("
                && w[i + 2].Kind == TokKind.String && w[i + 3].Text == "," && w[i + 4].Kind == TokKind.Ident)
            {
                string display = w[i + 2].Text.Trim('"');
                string type = w[i + 4].Text;
                var p = Add(ShaderSymbolKind.Property, t, type, $"{t.Text}(\"{display}\", {type})", 0, Text.Length);
                p.Doc = $"Material property \"{display}\". Declare uniform {GlslTypeFor(type)} {t.Text}; to use it.";
                if (type.StartsWith("Texture", StringComparison.Ordinal))
                {
                    AddHelper(t, "_Texture", "texture", $"The texture of {t.Text} without its sampler.");
                    AddHelper(t, "_Sampler", "sampler", $"The sampler of {t.Text}.");
                    AddHelper(t, "_ST", "vec4", $"Scale in xy and offset in zw of {t.Text}.");
                }
                continue;
            }

            if (t.Kind == TokKind.Ident && string.Equals(t.Text, "Name", StringComparison.OrdinalIgnoreCase) && i + 1 < w.Count && w[i + 1].Kind == TokKind.String)
            {
                var name = w[i + 1];
                Add(ShaderSymbolKind.Pass, new Tok(name.Offset + 1, Math.Max(0, name.Length - 2), TokKind.String, name.Text.Trim('"')), "", $"Pass \"{name.Text.Trim('"')}\"", 0, Text.Length);
            }
        }
    }

    private void AddHelper(Tok property, string suffix, string type, string doc)
    {
        var s = Add(ShaderSymbolKind.Macro, property, type, $"{type} {property.Text}{suffix}", 0, Text.Length);
        s.Name = property.Text + suffix;
        s.Doc = doc;
    }

    /// <summary>The GLSL uniform type a property is declared with.</summary>
    internal static string GlslTypeFor(string propertyType) => propertyType switch
    {
        "Float" or "Integer" => "float",
        "Color" or "Vector" => "vec4",
        "Vector2" => "vec2",
        "Vector3" => "vec3",
        "Matrix" => "mat4",
        "Texture2D" => "sampler2D",
        "Texture2DArray" => "sampler2DArray",
        "Texture3D" => "sampler3D",
        "TextureCubemap" => "samplerCube",
        "TextureCubemapArray" => "samplerCubeArray",
        _ => "float",
    };

    #endregion

    #region Queries

    /// <summary>The GLSL token at or just before an offset, or -1.</summary>
    internal int TokenAt(int offset)
    {
        int lo = 0, hi = GlslTokens.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (GlslTokens[mid].Offset <= offset) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    internal bool InGlsl(int offset) => ProgramAt(offset) != null;

    internal Directive? DirectiveAt(int offset)
    {
        foreach (var d in Directives)
            if (offset >= d.Offset && offset <= d.End) return d;
        return null;
    }

    internal Tok Token(int i) => T(i);

    internal int CloseOf(int i) => Close(i);

    #endregion
}
