// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.OrigamiUI.Graphite;

public sealed partial class ShaderSession
{
    #region Completion

    private static CodeCompletionItem Item(string label, CodeSymbolKind kind, string? detail = null, string? insert = null)
        => new() { Label = label, Kind = kind, Detail = detail, InsertText = insert };

    public override Task<CodeCompletionList?> GetCompletionsAsync(int offset, CodeCompletionTrigger trigger, CancellationToken ct) => Run<CodeCompletionList?>(() =>
    {
        var index = Index();
        string text = index.Text;
        offset = Math.Clamp(offset, 0, text.Length);
        if (InComment(text, offset)) return null;

        var (wordStart, _) = WordAt(text, offset);
        bool typedWord = trigger.Kind == CodeCompletionTriggerKind.Character && IsWordChar(trigger.Character);

        if (index.IsShaderFile && !index.InGlsl(offset))
            return List(WrapperCompletions(index, offset, wordStart), wordStart);

        var directive = index.DirectiveAt(offset);
        if (directive != null) return DirectiveCompletions(index, directive, offset, wordStart);

        // After a dot: the members of whatever is to its left.
        if (wordStart > 0 && text[wordStart - 1] == '.')
        {
            var visible = Visible(index, wordStart);
            var type = ReceiverType(index, visible, wordStart - 1);
            return type == null ? null : List(MemberCompletions(visible, type), wordStart);
        }
        if (trigger.Kind == CodeCompletionTriggerKind.Character && !typedWord) return null;
        if (InString(text, offset)) return null;

        if (LayoutParens(index, offset))
            return List(ShaderSyntax.LayoutQualifiers.Select(q => Item(q, CodeSymbolKind.Keyword)), wordStart);

        return List(GlslCompletions(index, wordStart), wordStart);
    }, ct);

    private static CodeCompletionList? List(IEnumerable<CodeCompletionItem> items, int spanStart)
    {
        var list = items.ToList();
        return list.Count == 0 ? null : new CodeCompletionList { Items = list, SpanStart = spanStart };
    }

    private static bool InComment(string text, int offset)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        int comment = text.IndexOf("//", lineStart, StringComparison.Ordinal);
        if (comment >= 0 && comment < offset) return true;
        int open = text.LastIndexOf("/*", Math.Max(0, offset - 1), StringComparison.Ordinal);
        return open >= 0 && text.IndexOf("*/", open, StringComparison.Ordinal) is int close && (close < 0 || close >= offset);
    }

    private static bool InString(string text, int offset)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        int quotes = 0;
        for (int i = lineStart; i < offset; i++) if (text[i] == '"') quotes++;
        return (quotes & 1) == 1;
    }

    private static bool LayoutParens(ShaderIndex index, int offset)
    {
        int i = index.TokenAt(offset - 1);
        int depth = 0;
        for (int j = i; j >= 0 && j > i - 40; j--)
        {
            var t = index.Token(j);
            if (t.Text == ")") depth++;
            else if (t.Text == "(")
            {
                if (depth == 0) return j > 0 && index.Token(j - 1).Text == "layout";
                depth--;
            }
            else if (t.Text is ";" or "{" or "}") return false;
        }
        return false;
    }

    private IEnumerable<CodeCompletionItem> GlslCompletions(ShaderIndex index, int offset)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stages = index.StagesAt(offset);

        foreach (var s in Visible(index, offset).OrderByDescending(s => s.IsLocal))
        {
            if (s.Kind == ShaderSymbolKind.Field && s.Container != null) continue;
            if (!seen.Add(s.Name)) continue;
            yield return Item(s.Name, SymbolKind(s.Kind), s.Kind == ShaderSymbolKind.Function ? s.Detail : s.Type.Length > 0 ? s.Type : s.Kind.ToString().ToLowerInvariant());
        }

        foreach (var define in ShaderSyntax.StageDefines.Keys)
            if (seen.Add(define)) yield return Item(define, CodeSymbolKind.Constant, "define");

        if (index.IsShaderFile && index.SectionAt(offset) == null)
            foreach (var section in ShaderSyntax.Sections.Keys)
                if (seen.Add(section)) yield return Item(section, CodeSymbolKind.Keyword, "section");

        foreach (var t in GlslBuiltins.Types)
            if (seen.Add(t)) yield return Item(t, CodeSymbolKind.Struct, "type");
        foreach (var k in GlslBuiltins.Keywords.Concat(GlslBuiltins.ControlKeywords))
            if (seen.Add(k)) yield return Item(k, CodeSymbolKind.Keyword);
        foreach (var f in GlslBuiltins.Functions)
            if ((f.Stages & stages) != 0 && seen.Add(f.Name)) yield return Item(f.Name, CodeSymbolKind.Method, $"{f.ReturnType} {f.Name}({f.Parameters})");
        foreach (var v in GlslBuiltins.Variables)
            if ((v.Stages & stages) != 0 && seen.Add(v.Name)) yield return Item(v.Name, CodeSymbolKind.Field, v.Type);
    }

    private static IEnumerable<CodeCompletionItem> MemberCompletions(List<ShaderSymbol> visible, string type)
    {
        int size = VectorSize(type);
        if (size > 0)
        {
            string scalar = ScalarOf(type);
            string[] sets = { "xyzw", "rgba", "stpq" };
            foreach (var set in sets)
            {
                for (int k = 0; k < size; k++) yield return Item(set[k].ToString(), CodeSymbolKind.Field, scalar);
                if (size >= 2) yield return Item(set.Substring(0, 2), CodeSymbolKind.Field, VectorOf(type, 2));
                if (size >= 3) yield return Item(set.Substring(0, 3), CodeSymbolKind.Field, VectorOf(type, 3));
                if (size >= 4) yield return Item(set, CodeSymbolKind.Field, VectorOf(type, 4));
            }
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in MembersOf(visible, type))
            if (seen.Add(f.Name)) yield return Item(f.Name, CodeSymbolKind.Field, f.Type);
    }

    private static string VectorOf(string vectorType, int n) => (vectorType[0] == 'v' ? "vec" : vectorType.Substring(0, 4)) + n;

    private CodeCompletionList? DirectiveCompletions(ShaderIndex index, Directive directive, int offset, int wordStart)
    {
        string text = index.Text;
        int nameEnd = text.IndexOf(directive.Name, directive.Offset, StringComparison.Ordinal) + directive.Name.Length;
        if (offset <= nameEnd)
            return List(ShaderSyntax.Directives.Keys.Select(d => Item(d, CodeSymbolKind.Keyword, "directive")), wordStart);

        if (directive.Name == "include")
        {
            int quote = text.IndexOfAny(new[] { '"', '<' }, directive.ArgsOffset);
            if (quote < 0 || quote >= offset) return null;
            int start = quote + 1;
            return List(_workspace.IncludeNames(_path).Select(n => Item(n, CodeSymbolKind.Text, "include")), start);
        }

        if (directive.Name == "pragma")
        {
            var before = text.Substring(directive.ArgsOffset, offset - directive.ArgsOffset);
            var words = before.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool firstWord = words.Length == 0 || (words.Length == 1 && !char.IsWhiteSpace(before[^1]));
            if (firstWord) return List(ShaderSyntax.Pragmas.Keys.Select(p => Item(p, CodeSymbolKind.Keyword, "pragma")), wordStart);
            if (words[0] == "global" && (words.Length == 1 || (words.Length == 2 && !char.IsWhiteSpace(before[^1]))))
                return List(GlslPragmaGlobalTypes.Select(t => Item(t, CodeSymbolKind.Struct, "type")), wordStart);
            if (words[0] == "variant" && (words.Length == 1 || (words.Length == 2 && !char.IsWhiteSpace(before[^1]))))
                return List(new[] { Item("global", CodeSymbolKind.Keyword, "set by the renderer") }, wordStart);
            return null;
        }

        if (directive.Name is "ifdef" or "ifndef" or "if" or "elif" or "undef" or "define")
        {
            var visible = Visible(index, directive.Offset);
            var macros = visible.Where(s => s.Kind is ShaderSymbolKind.Macro or ShaderSymbolKind.Variant).Select(s => s.Name)
                .Concat(ShaderSyntax.StageDefines.Keys).Distinct();
            return List(macros.Select(m => Item(m, CodeSymbolKind.Constant, "define")), wordStart);
        }
        return null;
    }

    private static readonly string[] GlslPragmaGlobalTypes = ["float", "int", "ivec2", "ivec3", "ivec4", "vec2", "vec3", "vec4", "mat4", "sampler2D", "sampler2DArray", "sampler3D", "samplerCube", "samplerCubeArray"];

    // The ShaderDef wrapper: what can be typed depends on the enclosing block and the command on the line.
    private IEnumerable<CodeCompletionItem> WrapperCompletions(ShaderIndex index, int offset, int wordStart)
    {
        string text = index.Text;
        var stack = new Stack<string>();
        string last = "";
        foreach (var t in index.WrapperTokens)
        {
            if (t.Offset >= wordStart) break;
            if (t.Text == "{") stack.Push(last);
            else if (t.Text == "}") { if (stack.Count > 0) stack.Pop(); }
            if (t.Kind == TokKind.Ident) last = t.Text;
        }
        string block = stack.Count > 0 ? stack.Peek() : "";

        int lineStart = text.LastIndexOf('\n', Math.Max(0, wordStart - 1)) + 1;
        string before = text.Substring(lineStart, wordStart - lineStart);
        string trimmed = before.TrimStart();
        var words = Regex.Matches(trimmed, @"[A-Za-z_][A-Za-z0-9_]*").Select(m => m.Value).ToList();
        bool firstOnLine = trimmed.Length == 0;

        // Inside [ ] after a property: its attributes.
        if (before.LastIndexOf('[') > before.LastIndexOf(']'))
        {
            foreach (var (name, doc) in ShaderSyntax.Attributes) yield return Item(name, CodeSymbolKind.Method, doc);
            yield break;
        }

        if (string.Equals(block, "Properties", StringComparison.OrdinalIgnoreCase))
        {
            // _Name("Display", |) takes the property type.
            if (before.Contains('(') && before.Contains(','))
                foreach (var (type, doc) in ShaderSyntax.PropertyTypes) yield return Item(type, CodeSymbolKind.Class, doc);
            yield break;
        }

        if (string.Equals(block, "Tags", StringComparison.OrdinalIgnoreCase))
        {
            var key = Regex.Match(before, "\"([A-Za-z]+)\"\\s*=\\s*\"[^\"]*$");
            if (key.Success && ShaderSyntax.TagValues.TryGetValue(key.Groups[1].Value, out var values))
                foreach (var v in values) yield return Item(v, CodeSymbolKind.EnumMember);
            else if (before.Count(c => c == '"') % 2 == 1)
                foreach (var (tag, doc) in ShaderSyntax.TagDocs) yield return Item(tag, CodeSymbolKind.Property, doc);
            yield break;
        }

        bool inStencil = string.Equals(block, "Stencil", StringComparison.OrdinalIgnoreCase);
        if (!firstOnLine && words.Count > 0)
        {
            var values = ShaderSyntax.ValuesFor(words[0], inStencil);
            if (values != null) foreach (var v in values) yield return Item(v, CodeSymbolKind.EnumMember, words[0]);
            else if (string.Equals(words[0], "Surface", StringComparison.OrdinalIgnoreCase))
                foreach (var m in new[] { "Lit", "Unlit" }) yield return Item(m, CodeSymbolKind.EnumMember, "surface model");
            yield break;
        }

        if (inStencil)
        {
            foreach (var (c, doc) in ShaderSyntax.StencilCommands) yield return Item(c, CodeSymbolKind.Keyword, doc);
            yield break;
        }

        switch (block.ToLowerInvariant())
        {
            case "":
                yield return Item("Shader", CodeSymbolKind.Keyword, ShaderSyntax.Blocks["Shader"]);
                break;
            case "shader":
                foreach (var b in new[] { "Properties", "Surface", "Pass", "Fallback" }) yield return Item(b, CodeSymbolKind.Keyword, ShaderSyntax.Blocks[b]);
                break;
            case "pass":
                foreach (var b in new[] { "Name", "Tags", "Stencil" }) yield return Item(b, CodeSymbolKind.Keyword, ShaderSyntax.Blocks[b]);
                yield return Item("GLSLPROGRAM", CodeSymbolKind.Keyword, "Opens the pass's GLSL, closed by ENDGLSL.");
                foreach (var (c, doc) in ShaderSyntax.Commands) yield return Item(c, CodeSymbolKind.Keyword, doc);
                break;
            case "surface":
                yield return Item("GLSLPROGRAM", CodeSymbolKind.Keyword, "Opens the surface function's GLSL, closed by ENDGLSL.");
                break;
        }
    }

    #endregion

    #region Hover

    private static IReadOnlyList<CodeTaggedText> Tagged(string detail, string name, CodeTokenKind nameKind)
    {
        int at = name.Length > 0 ? detail.IndexOf(name, StringComparison.Ordinal) : -1;
        if (at < 0) return new[] { new CodeTaggedText(detail) };
        return new[]
        {
            new CodeTaggedText(detail.Substring(0, at), CodeTokenKind.Keyword),
            new CodeTaggedText(name, nameKind),
            new CodeTaggedText(detail.Substring(at + name.Length)),
        };
    }

    public override Task<CodeQuickInfo?> GetQuickInfoAsync(int offset, CancellationToken ct) => Run<CodeQuickInfo?>(() =>
    {
        var index = Index();
        string text = index.Text;
        var (a, b) = WordAt(text, offset);
        string word = text.Substring(a, b - a);

        foreach (var include in index.Includes)
            if (offset >= include.Offset && offset <= include.Offset + include.Length)
            {
                var source = _workspace.Resolve(include.Name, _path);
                return Info(a, b, new[] { new CodeTaggedText("#include " + include.Name) },
                    source == null ? "Not found." : source.Path == include.Name ? "Graphite library file." : source.Path);
            }

        if (word.Length == 0) return null;

        var directive = index.DirectiveAt(offset);
        if (directive != null)
        {
            if (ShaderSyntax.Directives.TryGetValue(word, out var doc) && a <= directive.ArgsOffset) return Info(a, b, new[] { new CodeTaggedText("#" + word) }, doc);
            if (directive.Name == "pragma" && ShaderSyntax.Pragmas.TryGetValue(word, out doc)) return Info(a, b, new[] { new CodeTaggedText("#pragma " + word) }, doc);
        }

        if (index.IsShaderFile && !index.InGlsl(a))
        {
            string? doc = null;
            if (ShaderSyntax.Blocks.TryGetValue(word, out var d)) doc = d;
            else if (ShaderSyntax.Commands.TryGetValue(word, out d) || ShaderSyntax.StencilCommands.TryGetValue(word, out d)) doc = d;
            else if (ShaderSyntax.PropertyTypes.TryGetValue(word, out d)) doc = d;
            else if (ShaderSyntax.Attributes.TryGetValue(word, out d)) doc = d;
            else if (ShaderSyntax.TagDocs.TryGetValue(word, out d)) doc = d;
            if (doc != null) return Info(a, b, new[] { new CodeTaggedText(word, CodeTokenKind.Keyword) }, doc);
        }

        var symbol = SymbolAt(index, offset, out _, out _);
        if (symbol != null)
        {
            var docs = new List<string>();
            if (symbol.Doc.Length > 0) docs.Add(symbol.Doc);
            if (symbol.Owner.Path != null) docs.Add("From " + symbol.Owner.Path);
            var property = symbol.Kind == ShaderSymbolKind.Uniform ? index.Symbols.FirstOrDefault(s => s.Kind == ShaderSymbolKind.Property && s.Name == symbol.Name) : null;
            if (property != null) docs.Add("Material property " + property.Detail);
            return new CodeQuickInfo
            {
                SpanStart = a, SpanLength = b - a,
                Signature = Tagged(symbol.Detail, symbol.Name, symbol.Kind == ShaderSymbolKind.Function ? CodeTokenKind.Method : CodeTokenKind.Field),
                Documentation = docs,
            };
        }

        if (index.IsShaderFile && ShaderSyntax.Sections.TryGetValue(word, out var section)) return Info(a, b, new[] { new CodeTaggedText(word, CodeTokenKind.Keyword) }, section);
        if (ShaderSyntax.StageDefines.TryGetValue(word, out var stageDoc)) return Info(a, b, new[] { new CodeTaggedText(word, CodeTokenKind.Constant) }, stageDoc);

        var overloads = GlslBuiltins.Functions.Where(f => f.Name == word).ToList();
        if (overloads.Count > 0)
        {
            var first = overloads[0];
            var docs = new List<string> { first.Doc };
            if (overloads.Count > 1) docs.Add(string.Join("\n", overloads.Skip(1).Take(8).Select(f => $"{f.ReturnType} {f.Name}({f.Parameters})"))
                + (overloads.Count > 9 ? $"\n(+{overloads.Count - 9} more)" : ""));
            return new CodeQuickInfo { SpanStart = a, SpanLength = b - a, Signature = Tagged($"{first.ReturnType} {first.Name}({first.Parameters})", word, CodeTokenKind.Method), Documentation = docs };
        }

        var variable = GlslBuiltins.Variables.FirstOrDefault(v => v.Name == word);
        if (variable != null) return Info(a, b, Tagged($"{(variable.Output ? "out" : "in")} {variable.Type} {variable.Name}", word, CodeTokenKind.Field), variable.Doc);
        if (GlslBuiltins.TypeDocs.TryGetValue(word, out var typeDoc)) return Info(a, b, new[] { new CodeTaggedText(word, CodeTokenKind.Struct) }, typeDoc);
        return null;
    }, ct);

    private static CodeQuickInfo Info(int a, int b, IReadOnlyList<CodeTaggedText> signature, string doc)
        => new() { SpanStart = a, SpanLength = b - a, Signature = signature, Documentation = doc.Length > 0 ? new[] { doc } : Array.Empty<string>() };

    #endregion

    #region Signature help

    public override Task<CodeSignatureHelp?> GetSignatureHelpAsync(int offset, CancellationToken ct) => Run<CodeSignatureHelp?>(() =>
    {
        var index = Index();
        string text = index.Text;

        // Walk back to the unclosed '(' the caret is inside, counting the commas at its level.
        int depth = 0, commas = 0, open = -1;
        for (int i = Math.Min(offset, text.Length) - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c is ')' or ']') depth++;
            else if (c is '(' or '[')
            {
                if (depth == 0) { if (c == '(') open = i; break; }
                depth--;
            }
            else if (c == ',' && depth == 0) commas++;
            else if (c is ';' or '{' or '}' && depth == 0) break;
        }
        if (open < 0) return null;

        int nameEnd = open;
        while (nameEnd > 0 && text[nameEnd - 1] == ' ') nameEnd--;
        var (a, _) = WordAt(text, nameEnd);
        string name = text.Substring(a, nameEnd - a);
        if (name.Length == 0) return null;

        var signatures = new List<CodeSignature>();
        foreach (var s in Visible(index, a).Where(s => s.Name == name && s.Parameters != null))
            signatures.Add(Signature(s.Type, name, s.Parameters!.Select(p => p.Name.Length > 0 && p.Type.Length > 0 ? $"{p.Type} {p.Name}" : p.Type + p.Name).ToList(), s.Doc));
        var stages = index.StagesAt(a);
        foreach (var f in GlslBuiltins.Functions.Where(f => f.Name == name && (f.Stages & stages) != 0))
            signatures.Add(Signature(f.ReturnType, name, f.Parameters.Length == 0 ? new List<string>() : f.Parameters.Split(", ").ToList(), f.Doc));
        if (signatures.Count == 0) return null;

        int best = signatures.FindIndex(s => s.Parameters.Count > commas);
        return new CodeSignatureHelp
        {
            Signatures = signatures,
            ActiveSignature = Math.Max(0, best),
            ActiveParameter = commas,
            ArgumentListStart = open,
        };
    }, ct);

    private static CodeSignature Signature(string returnType, string name, List<string> parameters, string doc)
    {
        var parts = new List<CodeTaggedText> { new($"{returnType} ", CodeTokenKind.Struct), new(name, CodeTokenKind.Method), new("(") };
        var paramInfos = new List<CodeSignatureParameter>();
        int position = returnType.Length + 1 + name.Length + 1;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0) { parts.Add(new CodeTaggedText(", ")); position += 2; }
            string p = parameters[i];
            parts.Add(new CodeTaggedText(p, CodeTokenKind.Parameter));
            paramInfos.Add(new CodeSignatureParameter { Name = p.Split(' ').Last(), LabelStart = position, LabelLength = p.Length });
            position += p.Length;
        }
        parts.Add(new CodeTaggedText(")"));
        return new CodeSignature { Parts = parts, Parameters = paramInfos, Documentation = doc.Length > 0 ? doc : null };
    }

    #endregion

    #region Formatting

    public override Task<IReadOnlyList<CodeTextEdit>> FormatAsync(int start, int length, CancellationToken ct) => Run<IReadOnlyList<CodeTextEdit>>(() =>
    {
        string text = Index().Text;
        bool shader = _kind == ShaderFileKind.Shader;
        if (length < 0) return ShaderFormatter.Format(text, shader);
        return ShaderFormatter.Format(text, shader, LineOf(text, start), LineOf(text, start + length));
    }, ct);

    public override Task<IReadOnlyList<CodeTextEdit>> FormatOnTypeAsync(int offset, char typed, CancellationToken ct) => Run<IReadOnlyList<CodeTextEdit>>(()
        => ShaderFormatter.FormatOnType(Index().Text, _kind == ShaderFileKind.Shader, offset, typed), ct);

    private static int LineOf(string text, int offset)
    {
        int line = 0;
        for (int i = 0, end = Math.Min(offset, text.Length); i < end; i++) if (text[i] == '\n') line++;
        return line;
    }

    #endregion

    #region Diagnostics

    private static readonly Regex s_compilerError = new(@"^(?<file>.*?)(:(?<line>\d+))?: (?<severity>error|warning): (?<text>.*)$", RegexOptions.Multiline);

    public override Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(CancellationToken ct) => Run<IReadOnlyList<CodeDiagnostic>>(() =>
    {
        var index = Index();
        var result = new List<CodeDiagnostic>();
        string text = index.Text;

        foreach (var include in index.Includes)
            if (_workspace.Resolve(include.Name, _path) == null)
                result.Add(new CodeDiagnostic { Start = include.Offset, Length = include.Length, Severity = CodeDiagnosticSeverity.Error, Id = "include", Message = $"Cannot find include '{include.Name}'." });

        if (_kind == ShaderFileKind.Shader) CompileShader(text, result, ct);
        else CheckGlsl(text, result);
        return result;
    }, ct);

    private void CompileShader(string text, List<CodeDiagnostic> result, CancellationToken ct)
    {
        ShaderDefinition definition;
        try { definition = ShaderParser.Parse(text); }
        catch (ParseException e)
        {
            int at = OffsetOf(text, e.Line, e.Column);
            var (a, b) = WordAt(text, at);
            string message = Regex.Replace(e.Message, @" at line \d+, column \d+\.?$", "");
            result.Add(new CodeDiagnostic { Start = a, Length = Math.Max(1, b - a), Severity = CodeDiagnosticSeverity.Error, Id = "shader", Message = message });
            return;
        }
        catch (Exception e)
        {
            result.Add(new CodeDiagnostic { Start = 0, Length = 0, Severity = CodeDiagnosticSeverity.Error, Id = "shader", Message = e.Message });
            return;
        }

        IEnumerable<ShaderPass> passes = definition.Passes ?? Array.Empty<ShaderPass>();
        if (definition.Surface != null)
        {
            if (_workspace.ExpandSurface == null) return;
            try { passes = _workspace.ExpandSurface(definition); }
            catch (Exception e)
            {
                result.Add(new CodeDiagnostic { Start = OffsetOf(text, definition.Surface.Line, 1), Length = 7, Severity = CodeDiagnosticSeverity.Error, Id = "surface", Message = e.Message });
                return;
            }
        }

        foreach (var pass in passes)
        {
            ct.ThrowIfCancellationRequested();
            string file = pass.Name.Length > 0 ? $"{(pass.SourceName.Length > 0 ? pass.SourceName : "shader")}/{pass.Name}" : (pass.SourceName.Length > 0 ? pass.SourceName : "shader");
            int fallback = OffsetOf(text, Math.Max(1, pass.SourceLine), 1);
            try
            {
                var program = _workspace.Compiler.CompileProgram(pass, ReadOnlySpan<Keyword>.Empty);
                AddCompilerMessages(text, program.Warnings, file, fallback, CodeDiagnosticSeverity.Warning, result);
            }
            catch (ShaderCompileException e) when (e.Line > 0)
            {
                string where = e.File ?? file;
                result.Add(Place(text, where == file || where.Length == 0 ? null : where, e.Line, e.Column, e.Length, CodeDiagnosticSeverity.Error, FirstMessage(e.Message), fallback));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (!AddCompilerMessages(text, e.Message, file, fallback, CodeDiagnosticSeverity.Error, result))
                    result.Add(new CodeDiagnostic { Start = fallback, Length = 0, Severity = CodeDiagnosticSeverity.Error, Id = "glsl", Message = e.Message });
            }
        }
    }

    private static string FirstMessage(string message)
    {
        var m = s_compilerError.Match(message);
        return m.Success ? m.Groups["text"].Value.Trim() : message;
    }

    // Maps "file:line: error: text" lines from the compiler onto the document, for messages that carry no column.
    private bool AddCompilerMessages(string text, string messages, string file, int fallback, CodeDiagnosticSeverity severity, List<CodeDiagnostic> result)
    {
        bool any = false;
        foreach (Match m in s_compilerError.Matches(messages))
        {
            any = true;
            string where = m.Groups["file"].Value;
            int line = m.Groups["line"].Success ? int.Parse(m.Groups["line"].Value) : 0;
            var sev = m.Groups["severity"].Value == "warning" ? CodeDiagnosticSeverity.Warning : severity;
            result.Add(Place(text, where == file || where.Length == 0 ? null : where, line, 0, 0, sev, m.Groups["text"].Value.Trim(), fallback));
        }
        return any;
    }

    /// <summary>
    /// A diagnostic for a compiler location. With a column it covers the reported token, else the line. An include's
    /// message lands on its #include line, and a location without a line on the fallback.
    /// </summary>
    private CodeDiagnostic Place(string text, string? includeFile, int line, int column, int length, CodeDiagnosticSeverity severity, string message, int fallback)
    {
        if (includeFile != null)
        {
            var include = Index().Includes.FirstOrDefault(i => includeFile.EndsWith(i.Name, StringComparison.Ordinal) || i.Name.EndsWith(includeFile, StringComparison.Ordinal));
            string where = line > 0 ? $"{includeFile}({line}{(column > 0 ? "," + column : "")})" : includeFile;
            return new CodeDiagnostic { Start = include?.Offset ?? fallback, Length = include?.Length ?? 0, Severity = severity, Id = "glsl", Message = $"{where}: {message}" };
        }
        if (line <= 0) return new CodeDiagnostic { Start = fallback, Length = 0, Severity = severity, Id = "glsl", Message = message };

        int lineStart = OffsetOf(text, line, 1);
        int lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        if (column > 0 && lineStart + column - 1 < lineEnd)
        {
            int start = lineStart + column - 1;
            int end = length > 0 ? Math.Min(lineEnd, start + length) : WordAt(text, start + 1).End;
            return new CodeDiagnostic { Start = start, Length = Math.Max(1, end - start), Severity = severity, Id = "glsl", Message = message };
        }

        int first = lineStart;
        while (first < lineEnd && char.IsWhiteSpace(text[first])) first++;
        int last = lineEnd;
        while (last > first && char.IsWhiteSpace(text[last - 1])) last--;
        return new CodeDiagnostic { Start = first, Length = Math.Max(0, last - first), Severity = severity, Id = "glsl", Message = message };
    }

    // A .glsl or .compute file has no passes to compile, so it is parsed as GLSL to catch syntax errors.
    private void CheckGlsl(string text, List<CodeDiagnostic> result)
    {
        string source = Regex.IsMatch(text, @"^\s*#version", RegexOptions.Multiline) ? text : "#version 460\n#line 1\n" + text;
        try
        {
            Prowl.Prism.Glsl.Parser.Parse(source, _workspace.Defines, (name, isSystem, requesting) =>
            {
                var resolved = _workspace.Resolve(name, requesting == "" ? _path : requesting);
                return resolved == null ? null : new Prowl.Prism.Glsl.IncludeResult(resolved.Path, resolved.Text);
            });
        }
        catch (Prowl.Prism.Glsl.GlslParseException e)
        {
            string? include = string.IsNullOrEmpty(e.File) ? null : e.File;
            result.Add(Place(text, include, e.Line, e.Column, e.Length, CodeDiagnosticSeverity.Error, e.Text, 0));
        }
        catch (Exception) { }
    }

    private static int OffsetOf(string text, int line, int column)
    {
        int offset = 0;
        for (int l = 1; l < line && offset >= 0; l++)
        {
            offset = text.IndexOf('\n', offset);
            if (offset < 0) return text.Length;
            offset++;
        }
        return Math.Min(text.Length, offset + Math.Max(0, column - 1));
    }

    #endregion
}
