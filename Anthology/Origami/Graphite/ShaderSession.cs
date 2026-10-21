// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.OrigamiUI.Graphite;

/// <summary>Language features for one open shader, worked out from a tolerant index of the file and its includes.</summary>
public sealed partial class ShaderSession : CodeLanguageSession
{
    private readonly ShaderWorkspace _workspace;
    private readonly ShaderFileKind _kind;
    private readonly string? _path;
    private readonly object _lock = new();
    private string _text;
    private ShaderIndex? _index;

    public ShaderSession(ShaderWorkspace workspace, ShaderFileKind kind, string? path, string text)
    {
        _workspace = workspace;
        _kind = kind;
        _path = path;
        _text = text;
    }

    public override void OnTextReset(string text)
    {
        lock (_lock) { _text = text; _index = null; }
    }

    public override void OnTextChanged(TextChange change)
    {
        lock (_lock)
        {
            _text = _text.Remove(change.StartOffset, Math.Min(change.OldText.Length, _text.Length - change.StartOffset)).Insert(change.StartOffset, change.NewText);
            _index = null;
        }
    }

    /// <summary>The index of the current text, built once per edit and shared by every request.</summary>
    internal ShaderIndex Index()
    {
        lock (_lock)
        {
            return _index ??= new ShaderIndex(_text, null, _kind == ShaderFileKind.Shader);
        }
    }

    private static Task<T> Run<T>(Func<T> work, CancellationToken ct) => Task.Run(work, ct);

    #region Symbol resolution

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The word touching an offset in any text.</summary>
    internal static (int Start, int End) WordAt(string text, int offset)
    {
        int a = Math.Clamp(offset, 0, text.Length), b = a;
        while (a > 0 && IsWordChar(text[a - 1])) a--;
        while (b < text.Length && IsWordChar(text[b])) b++;
        return (a, b);
    }

    /// <summary>Every symbol usable at an offset: the file's own whose scope covers it, then those of the includes in scope.</summary>
    internal List<ShaderSymbol> Visible(ShaderIndex index, int offset)
    {
        var result = new List<ShaderSymbol>();
        foreach (var s in index.Symbols)
        {
            if (s.Kind == ShaderSymbolKind.Pass) continue;
            if (offset < s.ScopeStart || offset > s.ScopeEnd) continue;
            result.Add(s);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var include in index.Includes)
        {
            if (offset < include.ScopeStart || offset > include.ScopeEnd) continue;
            AddInclude(include.Name, index.Path ?? _path, result, seen);
        }
        return result;
    }

    private void AddInclude(string name, string? fromPath, List<ShaderSymbol> into, HashSet<string> seen)
    {
        var child = _workspace.IncludeIndex(name, fromPath);
        if (child?.Path == null || !seen.Add(child.Path)) return;
        foreach (var s in child.Symbols)
            if (!s.IsLocal && s.Kind != ShaderSymbolKind.Pass) into.Add(s);
        foreach (var include in child.Includes) AddInclude(include.Name, child.Path, into, seen);
    }

    /// <summary>The symbol a name refers to at an offset. The narrowest scope wins, so locals shadow globals.</summary>
    internal static ShaderSymbol? Lookup(List<ShaderSymbol> visible, string name)
    {
        ShaderSymbol? best = null;
        foreach (var s in visible)
        {
            if (s.Name != name || s.Kind is ShaderSymbolKind.Field && s.Container != null) continue;
            if (best == null || Narrower(s, best)) best = s;
        }
        return best;
    }

    private static bool Narrower(ShaderSymbol a, ShaderSymbol b)
    {
        if (a.IsLocal != b.IsLocal) return a.IsLocal;
        if (a.Owner.Path == null && b.Owner.Path != null) return true;
        if (a.Owner.Path != null && b.Owner.Path == null) return false;
        return a.ScopeEnd - a.ScopeStart < b.ScopeEnd - b.ScopeStart;
    }

    internal static IEnumerable<ShaderSymbol> MembersOf(List<ShaderSymbol> visible, string type)
    {
        foreach (var s in visible)
            if (s.Kind == ShaderSymbolKind.Field && s.Container == type) yield return s;
    }

    private static int VectorSize(string type)
    {
        if (type.Length >= 4 && char.IsDigit(type[^1]) && (type.StartsWith("vec") || type.StartsWith("ivec") || type.StartsWith("uvec")
            || type.StartsWith("bvec") || type.StartsWith("dvec")) && !type.Contains('x'))
            return type[^1] - '0';
        return 0;
    }

    private static string ScalarOf(string vectorType) => vectorType[0] switch
    {
        'i' => "int", 'u' => "uint", 'b' => "bool", 'd' => "double", _ => "float",
    };

    /// <summary>The type of the expression ending just before a '.', such as light.color in light.color.rgb.</summary>
    internal string? ReceiverType(ShaderIndex index, List<ShaderSymbol> visible, int dotOffset)
    {
        int i = index.TokenAt(dotOffset);
        if (i < 0 || index.Token(i).Text != "." || index.Token(i).Offset != dotOffset) return null;

        var chain = new List<(string Name, bool Call)>();
        int j = i - 1;
        while (j >= 0)
        {
            var t = index.Token(j);
            if (t.Text == "]") { j = index.CloseOf(j) - 1; continue; }
            if (t.Text == ")")
            {
                int open = index.CloseOf(j);
                var callee = index.Token(open - 1);
                if (callee.Kind != TokKind.Ident) return null;
                chain.Add((callee.Text, true));
                j = open - 2;
            }
            else if (t.Kind == TokKind.Ident)
            {
                chain.Add((t.Text, false));
                j--;
            }
            else return null;

            if (j >= 0 && index.Token(j).Text == ".") { j--; continue; }
            break;
        }
        if (chain.Count == 0) return null;
        chain.Reverse();

        string? type = TypeOfName(visible, chain[0].Name, chain[0].Call);
        for (int k = 1; k < chain.Count && type != null; k++)
            type = MemberType(visible, type, chain[k].Name);
        return type;
    }

    private static string? TypeOfName(List<ShaderSymbol> visible, string name, bool call)
    {
        if (call)
        {
            if (GlslBuiltins.Types.Contains(name)) return name;
            var fn = visible.FirstOrDefault(s => s.Kind == ShaderSymbolKind.Function && s.Name == name);
            if (fn != null) return fn.Type;
            if (visible.Any(s => s.Kind == ShaderSymbolKind.Struct && s.Name == name)) return name;
            return GlslBuiltins.Functions.FirstOrDefault(f => f.Name == name)?.ReturnType;
        }
        var symbol = Lookup(visible, name);
        if (symbol != null) return symbol.Type.Length > 0 ? symbol.Type : null;
        return GlslBuiltins.Variables.FirstOrDefault(v => v.Name == name)?.Type;
    }

    private static string? MemberType(List<ShaderSymbol> visible, string type, string member)
    {
        int size = VectorSize(type);
        if (size > 0)
        {
            if (member.Length == 1) return ScalarOf(type);
            return member.Length <= 4 ? (type[0] == 'v' ? "vec" : type.Substring(0, 4)) + member.Length : null;
        }
        return MembersOf(visible, type).FirstOrDefault(f => f.Name == member)?.Type;
    }

    /// <summary>What the identifier at an offset refers to: a user symbol, a member, or null for built ins and unknown names.</summary>
    internal ShaderSymbol? SymbolAt(ShaderIndex index, int offset, out string word, out bool isMember)
    {
        var (a, b) = WordAt(index.Text, offset);
        string found = index.Text.Substring(a, b - a);
        word = found;
        isMember = false;
        if (found.Length == 0) return null;

        var visible = Visible(index, a);
        if (a > 0 && index.Text[a - 1] == '.')
        {
            isMember = true;
            var receiver = ReceiverType(index, visible, a - 1);
            if (receiver == null) return null;
            return MembersOf(visible, receiver).FirstOrDefault(f => f.Name == found);
        }

        // In the wrapper only property names are symbols.
        if (index.IsShaderFile && !index.InGlsl(a))
            return index.Symbols.FirstOrDefault(s => s.Kind == ShaderSymbolKind.Property && s.Name == found);

        return Lookup(visible, word);
    }

    #endregion

    #region Navigation

    private CodeLocation Location(ShaderSymbol symbol)
    {
        var owner = symbol.Owner;
        var (line, column) = LineColumn(owner.Text, symbol.Offset);
        return new CodeLocation
        {
            IsCurrentDocument = owner.Path == null,
            Path = owner.Path,
            Start = symbol.Offset,
            Length = symbol.Length,
            Line = line,
            Column = column,
            Display = symbol.Detail,
            Kind = SymbolKind(symbol.Kind),
        };
    }

    internal static (int Line, int Column) LineColumn(string text, int offset)
    {
        int line = 0, lineStart = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        return (line, offset - lineStart);
    }

    private static string LineText(string text, int offset)
    {
        int a = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        int b = text.IndexOf('\n', offset);
        if (b < 0) b = text.Length;
        return text.Substring(a, b - a).TrimEnd('\r');
    }

    internal static CodeSymbolKind SymbolKind(ShaderSymbolKind kind) => kind switch
    {
        ShaderSymbolKind.Function => CodeSymbolKind.Method,
        ShaderSymbolKind.Struct or ShaderSymbolKind.Block => CodeSymbolKind.Struct,
        ShaderSymbolKind.Field => CodeSymbolKind.Field,
        ShaderSymbolKind.Local => CodeSymbolKind.Local,
        ShaderSymbolKind.Parameter => CodeSymbolKind.Parameter,
        ShaderSymbolKind.Macro or ShaderSymbolKind.Variant => CodeSymbolKind.Constant,
        ShaderSymbolKind.Property => CodeSymbolKind.Property,
        ShaderSymbolKind.Pass => CodeSymbolKind.Text,
        _ => CodeSymbolKind.Field,
    };

    public override Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(int offset, CancellationToken ct) => Run<IReadOnlyList<CodeLocation>>(() =>
    {
        var index = Index();

        // On an include, go to the included file.
        foreach (var include in index.Includes)
            if (offset >= include.Offset && offset <= include.Offset + include.Length)
            {
                var source = _workspace.Resolve(include.Name, _path);
                return source == null ? Array.Empty<CodeLocation>() : new[] { new CodeLocation { Path = source.Path, Display = source.Path } };
            }

        var symbol = SymbolAt(index, offset, out string word, out _);
        if (symbol != null) return new[] { Location(symbol) };

        var builtin = GlslBuiltins.Functions.Where(f => f.Name == word).ToList();
        if (builtin.Count > 0) return builtin.Select(f => new CodeLocation { Display = $"{f.ReturnType} {f.Name}({f.Parameters})", Kind = CodeSymbolKind.Method }).ToList();
        var variable = GlslBuiltins.Variables.FirstOrDefault(v => v.Name == word);
        if (variable != null) return new[] { new CodeLocation { Display = $"{variable.Type} {variable.Name}", Kind = CodeSymbolKind.Field } };
        return Array.Empty<CodeLocation>();
    }, ct);

    /// <summary>Every place a symbol is written: its declaration and uses, in its own file and the files that can see it.</summary>
    internal List<(ShaderIndex Index, int Offset, int Length)> Occurrences(ShaderIndex index, ShaderSymbol symbol)
    {
        var result = new List<(ShaderIndex, int, int)>();
        string name = symbol.Name;
        var files = new List<ShaderIndex> { index };
        if (symbol.Owner != index) files.Add(symbol.Owner);
        else if (!symbol.IsLocal)
            foreach (var include in _workspace.WithIncludes(index).Skip(1))
                if (include.Symbols.All(s => s.Name != name)) files.Add(include);

        foreach (var file in files.Distinct())
        {
            foreach (var t in file.GlslTokens)
            {
                if (t.Kind != TokKind.Ident || t.Text != name) continue;
                bool afterDot = t.Offset > 0 && file.Text[t.Offset - 1] == '.';
                if (symbol.Kind == ShaderSymbolKind.Field && symbol.Container != null) { if (!afterDot) continue; }
                else
                {
                    if (afterDot) continue;
                    bool property = symbol.Kind == ShaderSymbolKind.Property;
                    if (!property && file == symbol.Owner && (t.Offset < symbol.ScopeStart || t.Offset > symbol.ScopeEnd)) continue;
                    // Another declaration of the same name nearer to this use shadows the symbol.
                    if (file == index && symbol.Owner == index)
                    {
                        var resolved = Lookup(Visible(index, t.Offset), name);
                        if (resolved != null && resolved != symbol && !(property && resolved.Kind == ShaderSymbolKind.Uniform)) continue;
                    }
                }
                result.Add((file, t.Offset, t.Length));
            }

            // A property and the uniform declared for it are one name across the wrapper and the GLSL.
            if (file == index && (symbol.Kind == ShaderSymbolKind.Property || IsPropertyUniform(index, symbol)))
                foreach (var t in file.WrapperTokens)
                    if (t.Kind == TokKind.Ident && t.Text == name) result.Add((file, t.Offset, t.Length));
        }

        if (!result.Any(r => r.Item1 == symbol.Owner && r.Item2 == symbol.Offset) && symbol.Kind is not ShaderSymbolKind.Variant)
            result.Add((symbol.Owner, symbol.Offset, symbol.Length));
        result.Sort((x, y) => x.Item1 == y.Item1 ? x.Item2.CompareTo(y.Item2) : x.Item1 == index ? -1 : 1);
        return result.Distinct().ToList();
    }

    private static bool IsPropertyUniform(ShaderIndex index, ShaderSymbol symbol)
        => symbol.Kind == ShaderSymbolKind.Uniform && index.Symbols.Any(s => s.Kind == ShaderSymbolKind.Property && s.Name == symbol.Name);

    public override Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(int offset, CancellationToken ct) => Run<IReadOnlyList<CodeLocation>>(() =>
    {
        var index = Index();
        var symbol = SymbolAt(index, offset, out _, out _);
        if (symbol == null) return Array.Empty<CodeLocation>();

        var result = new List<CodeLocation>();
        foreach (var (file, start, length) in Occurrences(index, symbol))
        {
            var (line, column) = LineColumn(file.Text, start);
            result.Add(new CodeLocation
            {
                IsCurrentDocument = file == index,
                Path = file.Path,
                Start = start,
                Length = length,
                Line = line,
                Column = column,
                Display = LineText(file.Text, start),
                Kind = SymbolKind(symbol.Kind),
            });
        }
        return result;
    }, ct);

    public override Task<IReadOnlyList<CodeClassifiedSpan>> GetHighlightsAsync(int offset, CancellationToken ct) => Run<IReadOnlyList<CodeClassifiedSpan>>(() =>
    {
        var index = Index();
        var symbol = SymbolAt(index, offset, out _, out _);
        if (symbol == null) return Array.Empty<CodeClassifiedSpan>();
        return Occurrences(index, symbol).Where(o => o.Index == index)
            .Select(o => new CodeClassifiedSpan(o.Offset, o.Length, CodeTokenKind.Identifier)).ToList();
    }, ct);

    public override Task<CodeRenameResult?> RenameAsync(int offset, string newName, CancellationToken ct) => Run<CodeRenameResult?>(() =>
    {
        if (newName.Length == 0 || !(char.IsLetter(newName[0]) || newName[0] == '_') || !newName.All(IsWordChar)) return null;
        if (GlslBuiltins.Keywords.Contains(newName) || GlslBuiltins.Types.Contains(newName)) return null;

        var index = Index();
        var symbol = SymbolAt(index, offset, out _, out _);
        if (symbol == null) return null;

        var edits = new List<CodeTextEdit>();
        var others = new Dictionary<string, List<CodeTextEdit>>();
        foreach (var (file, start, length) in Occurrences(index, symbol))
        {
            var edit = new CodeTextEdit(start, length, newName);
            if (file == index) edits.Add(edit);
            // Library files such as Graphite/Core.glsl are read only, only files on disk are renamed in.
            else if (file.Path != null && System.IO.Path.IsPathRooted(file.Path))
                (others.TryGetValue(file.Path, out var list) ? list : others[file.Path] = new List<CodeTextEdit>()).Add(edit);
        }
        return new CodeRenameResult { Edits = edits, OtherDocuments = others.ToDictionary(p => p.Key, p => (IReadOnlyList<CodeTextEdit>)p.Value) };
    }, ct);

    public override Task<IReadOnlyList<CodeClassifiedSpan>> GetClassificationsAsync(int start, int length, CancellationToken ct) => Run<IReadOnlyList<CodeClassifiedSpan>>(() =>
    {
        var index = Index();
        var result = new List<CodeClassifiedSpan>();
        int end = start + length;
        foreach (var t in index.GlslTokens)
        {
            if (t.End < start) continue;
            if (t.Offset > end) break;
            if (t.Kind != TokKind.Ident || GlslBuiltins.Keywords.Contains(t.Text) || GlslBuiltins.Types.Contains(t.Text)) continue;
            if (t.Offset > 0 && index.Text[t.Offset - 1] == '.') continue;

            var symbol = Lookup(Visible(index, t.Offset), t.Text);
            if (symbol == null) continue;
            var kind = symbol.Kind switch
            {
                ShaderSymbolKind.Function => CodeTokenKind.Method,
                ShaderSymbolKind.Struct or ShaderSymbolKind.Block => CodeTokenKind.Struct,
                ShaderSymbolKind.Macro or ShaderSymbolKind.Variant => CodeTokenKind.Constant,
                ShaderSymbolKind.Local => CodeTokenKind.Local,
                ShaderSymbolKind.Parameter => CodeTokenKind.Parameter,
                ShaderSymbolKind.Uniform => CodeTokenKind.Property,
                _ => CodeTokenKind.Field,
            };
            result.Add(new CodeClassifiedSpan(t.Offset, t.Length, kind));
        }
        return result;
    }, ct);

    #endregion
}
