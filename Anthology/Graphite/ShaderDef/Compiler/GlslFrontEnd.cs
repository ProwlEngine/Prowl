using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;


namespace Prowl.Graphite.ShaderDef.Compiler;


internal enum GlslStage { Vertex, Fragment, Compute }


internal enum TokenKind : byte { Identifier, Number, Punct, Directive }


internal readonly record struct GlslToken(TokenKind Kind, int Start, int Length)
{
    public int End => Start + Length;
}


internal enum DeclKind { Other, Function, Struct, Variable, Block, QualifierOnly }


internal sealed class GlslMember
{
    public required string TypeName;
    public required string TypeText;
    public required string Name;
    public required string ArrayText;
    public required int[] ArrayDims;
    public string? File;
    public int Line;
}


internal sealed class GlslDeclaration
{
    public DeclKind Kind;
    public int First;
    public int Last;
    public List<int> Tokens = [];
    public List<(int Open, int Close)> Layouts = [];
    public List<string> LayoutItems = [];
    public Dictionary<string, string?> LayoutKeys = new(StringComparer.Ordinal);
    public List<int> QualifierTokens = [];
    public HashSet<string> Qualifiers = new(StringComparer.Ordinal);
    public string? TypeName;
    public string TypeText = "";
    public List<GlslMember> Declarators = [];
    public bool HasInitializer;
    public string? BlockName;
    public List<GlslMember> Members = [];
    public bool Edited;

    public bool Has(string qualifier) => Qualifiers.Contains(qualifier);
}


/// <summary>
/// One preprocessed stage: its tokens and top level declarations, rewritten against a program layout.
/// </summary>
internal sealed class GlslStageSource
{
    private static readonly HashSet<string> s_qualifiers = new(StringComparer.Ordinal)
    {
        "uniform", "in", "out", "inout", "buffer", "shared", "const", "attribute", "varying",
        "flat", "smooth", "noperspective", "centroid", "sample", "patch",
        "highp", "mediump", "lowp",
        "coherent", "volatile", "restrict", "readonly", "writeonly",
        "invariant", "precise",
    };

    private static readonly HashSet<string> s_opaqueTypes = BuildOpaqueTypes();

    public readonly GlslStage Stage;
    public readonly string Text;
    public readonly List<GlslToken> Tokens = [];
    public readonly List<GlslDeclaration> Declarations = [];
    public readonly List<int> Directives = [];
    public readonly Dictionary<string, (GlslDeclaration Declaration, List<GlslMember> Members)> Structs = new(StringComparer.Ordinal);


    public GlslStageSource(GlslStage stage, string text)
    {
        Stage = stage;
        Text = text;
        Lex();
        Split();
    }


    public string Str(int token) => Text.Substring(Tokens[token].Start, Tokens[token].Length);

    private bool Is(int token, string value) => Tokens[token].Length == value.Length && string.CompareOrdinal(Text, Tokens[token].Start, value, 0, value.Length) == 0;

    private bool IsPunct(int token, char c) => Tokens[token].Kind == TokenKind.Punct && Text[Tokens[token].Start] == c;

    public static bool IsOpaque(string type) => s_opaqueTypes.Contains(type);


    private void Lex()
    {
        string s = Text;
        int i = 0;
        bool lineStart = true;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n')
            {
                lineStart = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n')
                    i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? s.Length : close + 2;
                continue;
            }

            int start = i;
            if (c == '#' && lineStart)
            {
                while (i < s.Length && s[i] != '\n')
                    i++;
                int end = i;
                while (end > start && char.IsWhiteSpace(s[end - 1]))
                    end--;
                Directives.Add(Tokens.Count);
                Tokens.Add(new GlslToken(TokenKind.Directive, start, end - start));
                continue;
            }

            lineStart = false;
            if (char.IsLetter(c) || c == '_')
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;
                Tokens.Add(new GlslToken(TokenKind.Identifier, start, i - start));
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                bool hex = c == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X');
                i++;
                while (i < s.Length)
                {
                    char d = s[i];
                    if (char.IsLetterOrDigit(d) || d == '.' || d == '_')
                        i++;
                    else if (!hex && (d == '+' || d == '-') && (s[i - 1] == 'e' || s[i - 1] == 'E'))
                        i++;
                    else
                        break;
                }
                Tokens.Add(new GlslToken(TokenKind.Number, start, i - start));
            }
            else
            {
                i++;
                Tokens.Add(new GlslToken(TokenKind.Punct, start, 1));
            }
        }
    }


    private void Split()
    {
        int n = Tokens.Count;
        int i = 0;
        while (i < n)
        {
            if (Tokens[i].Kind == TokenKind.Directive)
            {
                i++;
                continue;
            }

            int start = i;
            int depth = 0;
            int prev = -1;
            int j = i;
            GlslDeclaration? declaration = null;
            for (; j < n; j++)
            {
                if (Tokens[j].Kind == TokenKind.Directive)
                    continue;

                if (IsPunct(j, '(') || IsPunct(j, '['))
                    depth++;
                else if (IsPunct(j, ')') || IsPunct(j, ']'))
                    depth--;
                else if (depth == 0 && IsPunct(j, '{'))
                {
                    int close = MatchBrace(j);
                    if (prev >= 0 && IsPunct(prev, ')'))
                    {
                        declaration = new GlslDeclaration { Kind = DeclKind.Function, First = start, Last = close };
                        j = close;
                        break;
                    }

                    j = close;
                }
                else if (depth == 0 && IsPunct(j, ';'))
                {
                    declaration = new GlslDeclaration { First = start, Last = j };
                    break;
                }

                prev = j;
            }

            declaration ??= new GlslDeclaration { First = start, Last = n - 1 };
            for (int k = declaration.First; k <= declaration.Last; k++)
            {
                if (Tokens[k].Kind != TokenKind.Directive)
                    declaration.Tokens.Add(k);
            }

            if (declaration.Kind != DeclKind.Function)
                Classify(declaration);

            Declarations.Add(declaration);
            i = declaration.Last + 1;
        }
    }


    private int MatchBrace(int open)
    {
        int depth = 0;
        for (int k = open; k < Tokens.Count; k++)
        {
            if (Tokens[k].Kind != TokenKind.Punct)
                continue;
            if (IsPunct(k, '{'))
                depth++;
            else if (IsPunct(k, '}') && --depth == 0)
                return k;
        }

        throw Error(Tokens[open].Start, "unbalanced '{'");
    }


    private static int Match(List<int> t, int open, Func<int, char, bool> isPunct, char openChar, char closeChar)
    {
        int depth = 0;
        for (int k = open; k < t.Count; k++)
        {
            if (isPunct(t[k], openChar))
                depth++;
            else if (isPunct(t[k], closeChar) && --depth == 0)
                return k;
        }

        return -1;
    }


    private int MatchIn(List<int> t, int open, char openChar, char closeChar)
    {
        int close = Match(t, open, IsPunct, openChar, closeChar);
        if (close < 0)
            throw Error(Tokens[t[open]].Start, $"unbalanced '{openChar}'");
        return close;
    }


    private void Classify(GlslDeclaration d)
    {
        List<int> t = d.Tokens;
        int p = 0;
        while (p < t.Count)
        {
            if (Is(t[p], "layout") && p + 1 < t.Count && IsPunct(t[p + 1], '('))
            {
                int close = MatchIn(t, p + 1, '(', ')');
                d.Layouts.Add((t[p + 1], t[close]));
                ParseLayoutItems(d, t, p + 2, close);
                p = close + 1;
                continue;
            }

            if (Tokens[t[p]].Kind == TokenKind.Identifier && s_qualifiers.Contains(Str(t[p])))
            {
                d.QualifierTokens.Add(t[p]);
                d.Qualifiers.Add(Str(t[p]));
                p++;
                continue;
            }

            break;
        }

        if (p >= t.Count || IsPunct(t[p], ';'))
        {
            d.Kind = DeclKind.QualifierOnly;
            return;
        }

        if (Tokens[t[p]].Kind != TokenKind.Identifier)
            return;

        if (Is(t[p], "struct"))
        {
            if (p + 2 < t.Count && Tokens[t[p + 1]].Kind == TokenKind.Identifier && IsPunct(t[p + 2], '{'))
            {
                d.Kind = DeclKind.Struct;
                d.TypeName = Str(t[p + 1]);
                int close = MatchIn(t, p + 2, '{', '}');
                List<GlslMember> members = ParseMembers(t, p + 3, close);
                Structs.TryAdd(d.TypeName, (d, members));
                d.Members = members;
            }
            return;
        }

        if (p + 1 < t.Count && IsPunct(t[p + 1], '{'))
        {
            d.Kind = DeclKind.Block;
            d.BlockName = Str(t[p]);
            int close = MatchIn(t, p + 1, '{', '}');
            d.Members = ParseMembers(t, p + 2, close);
            return;
        }

        int typeStart = p;
        string typeName = Str(t[p++]);
        List<int> typeDims = [];
        while (p < t.Count && IsPunct(t[p], '['))
            p = ParseArray(t, p, typeDims);

        string typeText = Span(t[typeStart], t[p - 1]);
        while (p < t.Count)
        {
            if (Tokens[t[p]].Kind != TokenKind.Identifier)
                return;

            int nameToken = t[p++];
            if (p < t.Count && IsPunct(t[p], '('))
                return;

            int arrayStart = p;
            List<int> dims = [.. typeDims];
            while (p < t.Count && IsPunct(t[p], '['))
                p = ParseArray(t, p, dims);

            string arrayText = arrayStart < p ? Span(t[arrayStart], t[p - 1]) : "";
            d.Declarators.Add(new GlslMember { TypeName = typeName, TypeText = typeText, Name = Str(nameToken), ArrayText = arrayText, ArrayDims = [.. dims] });

            if (p < t.Count && IsPunct(t[p], '='))
            {
                d.HasInitializer = true;
                int depth = 0;
                while (p < t.Count)
                {
                    if (IsPunct(t[p], '(') || IsPunct(t[p], '[') || IsPunct(t[p], '{'))
                        depth++;
                    else if (IsPunct(t[p], ')') || IsPunct(t[p], ']') || IsPunct(t[p], '}'))
                        depth--;
                    else if (depth == 0 && (IsPunct(t[p], ',') || IsPunct(t[p], ';')))
                        break;
                    p++;
                }
            }

            if (p < t.Count && IsPunct(t[p], ','))
            {
                p++;
                continue;
            }

            if (p < t.Count && IsPunct(t[p], ';'))
            {
                d.Kind = DeclKind.Variable;
                d.TypeName = typeName;
                d.TypeText = typeText;
            }
            return;
        }
    }


    private void ParseLayoutItems(GlslDeclaration d, List<int> t, int from, int close)
    {
        int itemStart = from;
        int depth = 0;
        for (int k = from; k <= close; k++)
        {
            bool end = k == close;
            if (!end && (IsPunct(t[k], '(')))
                depth++;
            else if (!end && IsPunct(t[k], ')'))
                depth--;

            if (end || (depth == 0 && IsPunct(t[k], ',')))
            {
                if (k > itemStart)
                {
                    string key = Str(t[itemStart]);
                    string? value = null;
                    if (itemStart + 1 < k && IsPunct(t[itemStart + 1], '='))
                        value = Span(t[itemStart + 2], t[k - 1]);

                    d.LayoutItems.Add(Span(t[itemStart], t[k - 1]));
                    d.LayoutKeys[key] = value;
                }
                itemStart = k + 1;
            }
        }
    }


    private int ParseArray(List<int> t, int open, List<int> dims)
    {
        int close = MatchIn(t, open, '[', ']');
        if (close == open + 1)
            dims.Add(0);
        else if (close == open + 2 && Tokens[t[open + 1]].Kind == TokenKind.Number && TryParseInt(Str(t[open + 1]), out int value))
            dims.Add(value);
        else
            dims.Add(-1);
        return close + 1;
    }


    private List<GlslMember> ParseMembers(List<int> t, int from, int close)
    {
        List<GlslMember> members = [];
        int p = from;
        while (p < close)
        {
            while (p < close)
            {
                if (Is(t[p], "layout") && IsPunct(t[p + 1], '('))
                {
                    p = MatchIn(t, p + 1, '(', ')') + 1;
                    continue;
                }

                if (Tokens[t[p]].Kind == TokenKind.Identifier && (s_qualifiers.Contains(Str(t[p])) || Is(t[p], "row_major") || Is(t[p], "column_major")))
                {
                    p++;
                    continue;
                }

                break;
            }

            if (p >= close)
                break;

            if (Tokens[t[p]].Kind != TokenKind.Identifier)
                throw Error(Tokens[t[p]].Start, "unexpected token in a struct or block");

            int typeStart = p;
            string typeName = Str(t[p++]);
            List<int> typeDims = [];
            while (p < close && IsPunct(t[p], '['))
                p = ParseArray(t, p, typeDims);
            string typeText = Span(t[typeStart], t[p - 1]);

            while (p < close)
            {
                string name = Str(t[p++]);
                int arrayStart = p;
                List<int> dims = [.. typeDims];
                while (p < close && IsPunct(t[p], '['))
                    p = ParseArray(t, p, dims);

                string arrayText = arrayStart < p ? Span(t[arrayStart], t[p - 1]) : "";
                members.Add(new GlslMember { TypeName = typeName, TypeText = typeText, Name = name, ArrayText = arrayText, ArrayDims = [.. dims] });

                if (IsPunct(t[p], ','))
                {
                    p++;
                    continue;
                }

                if (IsPunct(t[p], ';'))
                {
                    p++;
                    break;
                }

                throw Error(Tokens[t[p]].Start, "expected ';' after a member");
            }
        }

        return members;
    }


    private string Span(int first, int last) => Text.Substring(Tokens[first].Start, Tokens[last].End - Tokens[first].Start);


    private static bool TryParseInt(string text, out int value)
    {
        string trimmed = text.TrimEnd('u', 'U');
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(trimmed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }


    public static bool TryLayoutInt(GlslDeclaration d, string key, out int value)
    {
        value = 0;
        return d.LayoutKeys.TryGetValue(key, out string? text) && text != null && TryParseInt(text, out value);
    }


    /// <summary>
    /// Maps a text offset back to the file and line named by the preceding #line directive.
    /// </summary>
    public (string File, int Line) Locate(int offset)
    {
        string file = "";
        int line = 1;
        int from = 0;
        foreach (int directive in Directives)
        {
            GlslToken token = Tokens[directive];
            if (token.Start > offset)
                break;

            string text = Text.Substring(token.Start, token.Length);
            string[] parts = text.TrimStart('#').Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == "line" && int.TryParse(parts[1], out int number))
            {
                line = number;
                from = token.End + 1;
                if (parts.Length == 3)
                    file = parts[2].Trim('"');
            }
        }

        for (int k = from; k < offset && k < Text.Length; k++)
        {
            if (Text[k] == '\n')
                line++;
        }

        return (file, line);
    }


    public ShaderCompileException Error(int offset, string message)
    {
        (string file, int line) = Locate(offset);
        return new ShaderCompileException($"{file}:{line}: error: {message}");
    }


    /// <summary>
    /// Applies the program layout: the merged uniform block, bindings, locations and Vulkan builtins.
    /// </summary>
    public string Rewrite(GlslProgramLayout layout)
    {
        List<(int At, int Remove, string Insert)> edits = [];
        GlslDeclaration? firstUniform = null;

        foreach (GlslDeclaration d in Declarations)
        {
            switch (layout.Classify(d, Stage))
            {
                case GlslUsage.LooseUniform:
                    d.Edited = true;
                    if (firstUniform == null)
                    {
                        firstUniform = d;
                        CheckUniformTypes(layout, d);
                        int start = Tokens[d.First].Start;
                        int end = Tokens[d.Last].End;
                        (string file, int line) = Locate(end);
                        edits.Add((start, end - start, layout.UniformBlockText(file, line)));
                    }
                    else
                    {
                        edits.Add(Replace(d, ""));
                    }
                    break;

                case GlslUsage.Resource:
                    d.Edited = true;
                    if (d.Kind == DeclKind.Block)
                    {
                        GlslResource block = layout.ResourceFor(d.BlockName!);
                        edits.Add(AddLayout(d, layout.LayoutItemsFor(d, block)));
                    }
                    else
                    {
                        AddVariableLayouts(d, edits, member => layout.LayoutItemsFor(d, layout.ResourceFor(member.Name)));
                    }
                    break;

                case GlslUsage.Interface:
                    d.Edited = true;
                    if (d.Kind == DeclKind.Block)
                        edits.Add(AddLayout(d, layout.LocationItemsFor(d, Stage, d.BlockName!)));
                    else
                        AddVariableLayouts(d, edits, member => layout.LocationItemsFor(d, Stage, member.Name));
                    break;
            }
        }

        foreach (GlslDeclaration d in Declarations)
        {
            if (d.Edited)
                continue;

            foreach (int k in d.Tokens)
            {
                if (Tokens[k].Kind != TokenKind.Identifier)
                    continue;
                if (Is(k, "gl_VertexID"))
                    edits.Add((Tokens[k].Start, Tokens[k].Length, "gl_VertexIndex"));
                else if (Is(k, "gl_InstanceID"))
                    edits.Add((Tokens[k].Start, Tokens[k].Length, "(gl_InstanceIndex - gl_BaseInstance)"));
            }
        }

        foreach (int directive in Directives)
        {
            if (GlslPragmas.IsGraphitePragma(Str(directive)))
                edits.Add((Tokens[directive].Start, Tokens[directive].Length, ""));
        }

        edits.Sort((a, b) => b.At.CompareTo(a.At));
        StringBuilder sb = new(Text);
        foreach ((int at, int remove, string insert) in edits)
        {
            sb.Remove(at, remove);
            sb.Insert(at, insert);
        }

        return sb.ToString();
    }


    /// <summary>Declares each global the stage reads as a loose uniform, ahead of its first declaration.</summary>
    public string DeclareGlobals(IReadOnlyList<ShaderGlobal> globals)
    {
        HashSet<string> read = new(StringComparer.Ordinal);
        for (int k = 0; k < Tokens.Count; k++)
        {
            if (Tokens[k].Kind == TokenKind.Identifier)
                read.Add(Str(k));
        }

        StringBuilder block = new();
        foreach (ShaderGlobal global in globals)
        {
            if (read.Contains(global.Name))
                block.Append("uniform ").Append(global.Type).Append(' ').Append(global.Name).Append(";\n");
        }
        if (block.Length == 0 || Declarations.Count == 0)
            return Text;

        int at = Tokens[Declarations[0].First].Start;
        (string file, int line) = Locate(at);
        block.Append("#line ").Append(line).Append(" \"").Append(file).Append("\"\n");
        return Text.Insert(at, block.ToString());
    }

    /// <summary>
    /// Puts the material ABI in place of the stage's loose uniforms, before the program is laid out. Properties and the
    /// pass's globals become macros over the ABI's reads, other loose uniforms an error where they are used, and a stage
    /// reading a property or global starts with the ABI's declarations it does not make itself.
    /// </summary>
    public string ApplyMaterial(MaterialAbi abi, IReadOnlyList<ShaderGlobal> globals, out bool readsProperties)
    {
        readsProperties = false;
        HashSet<string> globalNames = new(StringComparer.Ordinal);
        foreach (ShaderGlobal global in globals)
            globalNames.Add(global.Name);

        List<(int At, int Remove, string Insert)> edits = [];
        StringBuilder macros = new();
        foreach (GlslDeclaration d in Declarations)
        {
            if (d.Kind != DeclKind.Variable || !d.Has("uniform") || d.LayoutItems.Count > 0)
                continue;

            foreach (GlslMember member in d.Declarators)
            {
                if (abi.Properties.TryGetValue(member.Name, out (string Type, string Read, string With) property))
                {
                    if (member.TypeName != property.Type || member.ArrayDims.Length > 0)
                        throw Error(Tokens[d.First].Start, $"'{member.Name}' is a {property.Type} in Properties but a {member.TypeText}{member.ArrayText} here");
                    macros.Append("#define ").Append(member.Name).Append(' ').Append(property.Read).Append('\n').Append(property.With);
                    readsProperties = true;
                }
                else if (!globalNames.Contains(member.Name))
                {
                    macros.Append("#define ").Append(member.Name).Append(' ').Append(member.Name).Append("_is_neither_a_property_nor_a_global\n");
                }
            }
            edits.Add(Replace(d, ""));
        }

        bool readsGlobals = false;
        for (int k = 0; k < Tokens.Count && !readsGlobals && globalNames.Count > 0; k++)
            readsGlobals = Tokens[k].Kind == TokenKind.Identifier && globalNames.Contains(Str(k));

        if (Declarations.Count > 0 && (readsProperties || readsGlobals || macros.Length > 0))
        {
            HashSet<string> declared = new(Structs.Keys, StringComparer.Ordinal);
            foreach (GlslDeclaration d in Declarations)
            {
                if (d.BlockName != null)
                    declared.Add(d.BlockName);
                foreach (GlslMember member in d.Declarators)
                    declared.Add(member.Name);
            }

            StringBuilder block = new();
            if (readsProperties || readsGlobals)
            {
                foreach ((string name, string text) in abi.Declarations)
                {
                    if (!declared.Contains(name))
                        block.Append(text).Append('\n');
                }
                if (readsProperties && Stage == GlslStage.Fragment && !declared.Contains(abi.Slot.Name))
                    block.Append(abi.Slot.FragmentDeclaration).Append('\n');
            }
            if (readsGlobals)
            {
                for (int i = 0; i < globals.Count; i++)
                {
                    string constant = "graphite_Global_" + globals[i].Name;
                    block.Append("layout(constant_id = ").Append(i).Append(") const uint ").Append(constant).Append(" = 0u;\n");
                    block.Append("#define ").Append(globals[i].Name).Append(" Graphite_Global_").Append(globals[i].Type).Append('(').Append(constant).Append(")\n");
                }
            }
            block.Append(macros);

            int at = Tokens[Declarations[0].First].Start;
            (string file, int line) = Locate(at);
            edits.Add((at, 0, block.Append("#line ").Append(line).Append(" \"").Append(file).Append("\"\n").ToString()));
        }

        edits.Sort((a, b) => b.At.CompareTo(a.At));
        StringBuilder sb = new(Text);
        foreach ((int at, int remove, string insert) in edits)
        {
            sb.Remove(at, remove);
            sb.Insert(at, insert);
        }
        return sb.ToString();
    }


    private void CheckUniformTypes(GlslProgramLayout layout, GlslDeclaration at)
    {
        foreach (GlslMember uniform in layout.Uniforms)
        {
            if (!layout.IsStruct(uniform.TypeName))
                continue;

            if (!Structs.TryGetValue(uniform.TypeName, out var def) || def.Declaration.First > at.First)
                throw Error(Tokens[at.First].Start, $"struct '{uniform.TypeName}' of uniform '{uniform.Name}' must be declared before the first uniform of every stage, put it in the Shared section");
        }
    }


    private (int, int, string) Replace(GlslDeclaration d, string insert)
    {
        int start = Tokens[d.First].Start;
        int end = Tokens[d.Last].End;
        int newlines = 0;
        for (int k = start; k < end; k++)
        {
            if (Text[k] == '\n')
                newlines++;
        }

        return (start, end - start, insert + new string('\n', newlines));
    }


    private (int, int, string) AddLayout(GlslDeclaration d, List<string> items)
    {
        if (items.Count == 0)
            return (Tokens[d.First].Start, 0, "");

        if (d.Layouts.Count > 0)
            return (Tokens[d.Layouts[0].Close].Start, 0, ", " + string.Join(", ", items));

        return (Tokens[d.First].Start, 0, $"layout({string.Join(", ", items)}) ");
    }


    private void AddVariableLayouts(GlslDeclaration d, List<(int, int, string)> edits, Func<GlslMember, List<string>> itemsFor)
    {
        if (d.Declarators.Count == 1)
        {
            edits.Add(AddLayout(d, itemsFor(d.Declarators[0])));
            return;
        }

        for (int k = d.First; k <= d.Last; k++)
        {
            if (Tokens[k].Kind == TokenKind.Directive)
                throw Error(Tokens[d.First].Start, "a declaration of several variables can not span an include or #line");
        }

        string qualifiers = string.Join(" ", d.QualifierTokens.Select(Str));
        StringBuilder sb = new();
        foreach (GlslMember member in d.Declarators)
        {
            List<string> items = [.. d.LayoutItems, .. itemsFor(member)];
            if (items.Count > 0)
                sb.Append("layout(").Append(string.Join(", ", items)).Append(") ");
            sb.Append(qualifiers).Append(' ').Append(member.TypeText).Append(' ').Append(member.Name).Append(member.ArrayText).Append("; ");
        }

        edits.Add(Replace(d, sb.ToString()));
    }


    private static HashSet<string> BuildOpaqueTypes()
    {
        string[] dims = ["1D", "2D", "3D", "Cube", "2DRect", "1DArray", "2DArray", "CubeArray", "Buffer", "2DMS", "2DMSArray"];
        HashSet<string> set = new(StringComparer.Ordinal)
        {
            "sampler", "samplerShadow", "accelerationStructureEXT", "atomic_uint",
            "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow", "sampler2DRectShadow",
            "sampler1DArrayShadow", "sampler2DArrayShadow", "samplerCubeArrayShadow",
        };

        foreach (string prefix in new[] { "", "i", "u" })
        {
            foreach (string dim in dims)
            {
                set.Add($"{prefix}sampler{dim}");
                set.Add($"{prefix}texture{dim}");
                set.Add($"{prefix}image{dim}");
            }
            set.Add($"{prefix}subpassInput");
            set.Add($"{prefix}subpassInputMS");
        }

        return set;
    }
}


internal enum GlslUsage { None, LooseUniform, Resource, Interface, PushConstant }


internal enum ResourceClass { Opaque, UniformBlock, StorageBlock }


internal sealed class GlslResource
{
    public required string Name;
    public required string SourceName;
    public required ResourceClass Class;
    public uint Set;
    public int Binding = -1;
    public bool ExplicitBinding;
    public bool ExplicitSet;
    public List<GlslMember> Members = [];
}


/// <summary>
/// Bindings, locations and the merged uniform block of one program, shared by all of its stages.
/// </summary>
internal sealed class GlslProgramLayout
{
    public const string GlobalBlockName = "$Global";
    public const string GlobalBlockType = "_GraphiteGlobal";

    public readonly List<GlslMember> Uniforms = [];
    public readonly List<GlslResource> Resources = [];
    public readonly Dictionary<(uint Set, int Binding), GlslResource> ByBinding = [];
    public readonly Dictionary<int, string> VertexInputs = [];
    public readonly Dictionary<(GlslStage Stage, string Name), int> Locations = [];

    private readonly Dictionary<string, GlslResource> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GlslMember>> _structs = new(StringComparer.Ordinal);
    private GlslResource? _global;


    public GlslProgramLayout(IReadOnlyList<GlslStageSource> stages, uint? defaultSet = null)
    {
        foreach (GlslStageSource stage in stages)
        {
            foreach ((string name, var def) in stage.Structs)
                _structs.TryAdd(name, def.Members);
        }

        foreach (GlslStageSource stage in stages)
            CollectResources(stage);

        // A program with a default set, from "#pragma resource_set N", puts every resource without an explicit set there,
        // the loose uniform block too, so it can bind Graphite's own tables in the low sets
        if (defaultSet is uint set)
        {
            foreach (GlslResource resource in Resources)
            {
                if (!resource.ExplicitSet)
                    resource.Set = set;
            }
        }

        AssignBindings();

        foreach (GlslStageSource stage in stages)
            CollectInterface(stage, stages);
    }


    public bool IsStruct(string type) => _structs.ContainsKey(type);

    public bool TryGetStruct(string type, out List<GlslMember> members) => _structs.TryGetValue(type, out members!);

    public GlslResource ResourceFor(string sourceName) => _byName[sourceName];


    public GlslUsage Classify(GlslDeclaration d, GlslStage stage)
    {
        if (d.Kind == DeclKind.Variable && d.Has("uniform"))
            return GlslStageSource.IsOpaque(d.TypeName!) ? GlslUsage.Resource : GlslUsage.LooseUniform;

        if (d.Kind == DeclKind.Block && d.Has("uniform") && d.LayoutKeys.ContainsKey("push_constant"))
            return GlslUsage.PushConstant;

        if (d.Kind == DeclKind.Block && (d.Has("uniform") || d.Has("buffer")))
            return GlslUsage.Resource;

        bool input = d.Has("in") || d.Has("attribute");
        bool output = d.Has("out");
        if ((input || output) && (d.Kind == DeclKind.Variable || d.Kind == DeclKind.Block) && stage != GlslStage.Compute)
        {
            string name = d.Kind == DeclKind.Block ? d.BlockName! : d.Declarators[0].Name;
            if (!name.StartsWith("gl_", StringComparison.Ordinal))
                return GlslUsage.Interface;
        }

        return GlslUsage.None;
    }


    private void CollectResources(GlslStageSource stage)
    {
        foreach (GlslDeclaration d in stage.Declarations)
        {
            switch (Classify(d, stage.Stage))
            {
                case GlslUsage.LooseUniform:
                    if (d.HasInitializer)
                        throw stage.Error(stage.Tokens[d.First].Start, "uniforms can not have initializers, declare the default in Properties");

                    if (d.LayoutItems.Count > 0)
                        throw stage.Error(stage.Tokens[d.First].Start, "a loose uniform can not have a layout, it is packed into the uniform block");

                    _global ??= AddResource(stage, d, GlobalBlockName, GlobalBlockName, ResourceClass.UniformBlock);
                    foreach (GlslMember member in d.Declarators)
                        AddUniform(stage, d, member);
                    break;

                case GlslUsage.Resource when d.Kind == DeclKind.Block:
                    GlslResource block = AddResource(stage, d, d.BlockName!, d.BlockName!, d.Has("buffer") ? ResourceClass.StorageBlock : ResourceClass.UniformBlock);
                    if (block.Members.Count == 0)
                        block.Members = d.Members;
                    break;

                case GlslUsage.Resource:
                    foreach (GlslMember member in d.Declarators)
                        AddResource(stage, d, member.Name, member.Name, ResourceClass.Opaque);
                    break;
            }
        }
    }


    private void AddUniform(GlslStageSource stage, GlslDeclaration d, GlslMember member)
    {
        GlslMember? existing = Uniforms.Find(u => u.Name == member.Name);
        if (existing == null)
        {
            (member.File, member.Line) = stage.Locate(stage.Tokens[d.First].Start);
            Uniforms.Add(member);
            return;
        }

        if (Normalize(existing.TypeText + existing.ArrayText) != Normalize(member.TypeText + member.ArrayText))
            throw stage.Error(stage.Tokens[d.First].Start, $"uniform '{member.Name}' is declared as '{existing.TypeText}{existing.ArrayText}' in another stage");
    }


    private GlslResource AddResource(GlslStageSource stage, GlslDeclaration d, string name, string sourceName, ResourceClass kind)
    {
        bool hasBinding = GlslStageSource.TryLayoutInt(d, "binding", out int binding);
        bool hasSet = GlslStageSource.TryLayoutInt(d, "set", out int set);
        if (d.LayoutKeys.ContainsKey("binding") && !hasBinding || d.LayoutKeys.ContainsKey("set") && !hasSet)
            throw stage.Error(stage.Tokens[d.First].Start, $"the set and binding of '{sourceName}' must be integer literals");

        if (d.Declarators.Count > 1 && hasBinding)
            throw stage.Error(stage.Tokens[d.First].Start, "an explicit binding can not be shared by several variables");

        if (!_byName.TryGetValue(sourceName, out GlslResource? resource))
        {
            resource = new GlslResource { Name = name, SourceName = sourceName, Class = kind };
            _byName[sourceName] = resource;
            Resources.Add(resource);
        }
        else if (resource.Class != kind)
        {
            throw stage.Error(stage.Tokens[d.First].Start, $"'{sourceName}' is declared as a different kind of resource in another stage");
        }

        if (hasSet)
        {
            if (resource.ExplicitSet && resource.Set != (uint)set)
                throw stage.Error(stage.Tokens[d.First].Start, $"'{sourceName}' is in a different set in another stage");
            resource.Set = (uint)set;
            resource.ExplicitSet = true;
        }

        if (hasBinding)
        {
            if (resource.ExplicitBinding && resource.Binding != binding)
                throw stage.Error(stage.Tokens[d.First].Start, $"'{sourceName}' has a different binding in another stage");
            resource.Binding = binding;
            resource.ExplicitBinding = true;
        }

        return resource;
    }


    private void AssignBindings()
    {
        foreach (GlslResource resource in Resources)
        {
            if (!resource.ExplicitBinding)
                continue;

            if (!ByBinding.TryAdd((resource.Set, resource.Binding), resource))
                throw new ShaderCompileException($"error: '{resource.SourceName}' and '{ByBinding[(resource.Set, resource.Binding)].SourceName}' both use set {resource.Set} binding {resource.Binding}");
        }

        foreach (GlslResource resource in Resources)
        {
            if (resource.ExplicitBinding)
                continue;

            int binding = 0;
            while (ByBinding.ContainsKey((resource.Set, binding)))
                binding++;

            resource.Binding = binding;
            ByBinding[(resource.Set, binding)] = resource;
        }

        if (_global != null)
            _global.Members = Uniforms;
    }


    private void CollectInterface(GlslStageSource stage, IReadOnlyList<GlslStageSource> stages)
    {
        List<(GlslDeclaration Declaration, string Name, int Count)> inputs = [];
        List<(GlslDeclaration Declaration, string Name, int Count)> outputs = [];
        foreach (GlslDeclaration d in stage.Declarations)
        {
            if (Classify(d, stage.Stage) != GlslUsage.Interface)
                continue;

            var list = d.Has("out") ? outputs : inputs;
            if (d.Kind == DeclKind.Block)
            {
                int count = 0;
                foreach (GlslMember member in d.Members)
                    count += LocationCount(stage, d, member);
                list.Add((d, d.BlockName!, count));
            }
            else
            {
                foreach (GlslMember member in d.Declarators)
                    list.Add((d, member.Name, LocationCount(stage, d, member)));
            }
        }

        switch (stage.Stage)
        {
            case GlslStage.Vertex:
                Allocate(stage, inputs, GlslStage.Vertex, isInput: true, shared: null);
                Allocate(stage, outputs, GlslStage.Vertex, isInput: false, shared: VaryingsOf(stages));
                foreach (var input in inputs)
                    VertexInputs[Locations[(GlslStage.Vertex, "in " + input.Name)]] = input.Name;
                break;

            case GlslStage.Fragment:
                Allocate(stage, inputs, GlslStage.Fragment, isInput: true, shared: VaryingsOf(stages));
                Allocate(stage, outputs, GlslStage.Fragment, isInput: false, shared: null);
                break;
        }
    }


    private Dictionary<string, int>? _varyings;


    // Vertex outputs and fragment inputs share locations by name. Explicit locations on either side win,
    // the rest are allocated in vertex declaration order, then fragment only inputs.
    private Dictionary<string, int> VaryingsOf(IReadOnlyList<GlslStageSource> stages)
    {
        if (_varyings != null)
            return _varyings;

        List<(GlslStageSource Stage, GlslDeclaration Declaration, string Name, int Count)> all = [];
        foreach (GlslStageSource stage in stages)
        {
            bool isVarying(GlslDeclaration d) => stage.Stage == GlslStage.Vertex ? d.Has("out") : stage.Stage == GlslStage.Fragment && (d.Has("in") || d.Has("attribute"));
            foreach (GlslDeclaration d in stage.Declarations)
            {
                if (Classify(d, stage.Stage) != GlslUsage.Interface || !isVarying(d))
                    continue;

                if (d.Kind == DeclKind.Block)
                    all.Add((stage, d, d.BlockName!, d.Members.Sum(m => LocationCount(stage, d, m))));
                else
                    foreach (GlslMember member in d.Declarators)
                        all.Add((stage, d, member.Name, LocationCount(stage, d, member)));
            }
        }

        Dictionary<string, int> map = new(StringComparer.Ordinal);
        List<(int From, int Count)> used = [];
        foreach (var v in all)
        {
            if (!GlslStageSource.TryLayoutInt(v.Declaration, "location", out int location) || DeclaratorCount(v.Declaration) > 1)
                continue;

            if (map.TryGetValue(v.Name, out int existing) && existing != location)
                throw v.Stage.Error(v.Stage.Tokens[v.Declaration.First].Start, $"varying '{v.Name}' has a different location in another stage");

            map[v.Name] = location;
            used.Add((location, v.Count));
        }

        foreach (var v in all)
        {
            if (map.ContainsKey(v.Name))
                continue;

            int location = Free(used, v.Count);
            map[v.Name] = location;
            used.Add((location, v.Count));
        }

        _varyings = map;
        return map;
    }


    private static int DeclaratorCount(GlslDeclaration d) => d.Kind == DeclKind.Block ? 1 : d.Declarators.Count;


    private void Allocate(GlslStageSource stage, List<(GlslDeclaration Declaration, string Name, int Count)> items, GlslStage key, bool isInput, Dictionary<string, int>? shared)
    {
        string prefix = isInput ? "in " : "out ";
        if (shared != null)
        {
            foreach (var item in items)
                Locations[(key, prefix + item.Name)] = shared[item.Name];
            return;
        }

        List<(int From, int Count)> used = [];
        foreach (var item in items)
        {
            if (DeclaratorCount(item.Declaration) == 1 && GlslStageSource.TryLayoutInt(item.Declaration, "location", out int location))
            {
                Locations[(key, prefix + item.Name)] = location;
                used.Add((location, item.Count));
            }
        }

        foreach (var item in items)
        {
            if (Locations.ContainsKey((key, prefix + item.Name)))
                continue;

            int location = Free(used, item.Count);
            Locations[(key, prefix + item.Name)] = location;
            used.Add((location, item.Count));
        }
    }


    private static int Free(List<(int From, int Count)> used, int count)
    {
        int location = 0;
        while (true)
        {
            bool clash = false;
            foreach ((int from, int n) in used)
            {
                if (location < from + n && from < location + count)
                {
                    location = from + n;
                    clash = true;
                    break;
                }
            }

            if (!clash)
                return location;
        }
    }


    private int LocationCount(GlslStageSource stage, GlslDeclaration d, GlslMember member)
    {
        int count = TypeLocations(stage, d, member.TypeName);
        foreach (int dim in member.ArrayDims)
        {
            if (dim <= 0)
                throw stage.Error(stage.Tokens[d.First].Start, $"the array size of '{member.Name}' must be an integer literal");
            count *= dim;
        }

        return count;
    }


    private int TypeLocations(GlslStageSource stage, GlslDeclaration d, string type)
    {
        if (_structs.TryGetValue(type, out List<GlslMember>? members))
            return members.Sum(m => LocationCount(stage, d, m));

        bool isDouble = type.StartsWith('d') && type != "double" || type == "double";
        if (type.Contains("mat", StringComparison.Ordinal))
        {
            string dims = type[(type.IndexOf("mat", StringComparison.Ordinal) + 3)..];
            int columns = dims[0] - '0';
            int rows = dims.Length >= 3 ? dims[2] - '0' : columns;
            return columns * (isDouble && rows > 2 ? 2 : 1);
        }

        if (isDouble && (type.EndsWith('3') || type.EndsWith('4')))
            return 2;

        return 1;
    }


    public List<string> LayoutItemsFor(GlslDeclaration d, GlslResource resource)
    {
        List<string> items = [];
        if (!d.LayoutKeys.ContainsKey("set"))
            items.Add($"set = {resource.Set}");
        if (!d.LayoutKeys.ContainsKey("binding"))
            items.Add($"binding = {resource.Binding}");
        return items;
    }


    public List<string> LocationItemsFor(GlslDeclaration d, GlslStage stage, string name)
    {
        if (DeclaratorCount(d) == 1 && d.LayoutKeys.ContainsKey("location"))
            return [];

        string prefix = d.Has("out") ? "out " : "in ";
        return [$"location = {Locations[(stage, prefix + name)]}"];
    }


    /// <summary>
    /// The merged uniform block. Each member carries a #line back to its own declaration, and the text after the block
    /// continues at <paramref name="line"/> of <paramref name="file"/>, where the replaced declaration ended.
    /// </summary>
    public string UniformBlockText(string file, int line)
    {
        StringBuilder sb = new();
        sb.Append($"layout(set = {_global!.Set}, binding = {_global.Binding}, std140) uniform {GlobalBlockType} {{\n");
        foreach (GlslMember uniform in Uniforms)
        {
            sb.Append("#line ").Append(uniform.Line).Append(" \"").Append(uniform.File).Append("\"\n");
            sb.Append(uniform.TypeText).Append(' ').Append(uniform.Name).Append(uniform.ArrayText).Append(";\n");
        }
        sb.Append("};\n#line ").Append(line).Append(" \"").Append(file).Append("\"\n");
        return sb.ToString();
    }


    private static string Normalize(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
}
