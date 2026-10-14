using System.Globalization;
namespace Prowl.Prism.Glsl;

/// <summary>
/// The GLSL preprocessor. Runs before the parser: it handles <c>#define</c> (object and
/// function-like macros), <c>#undef</c>, the conditional family
/// (<c>#if/#ifdef/#ifndef/#elif/#else/#endif</c>) with <c>#if</c> constant-expression
/// evaluation and the <c>defined</c> operator, <c>##</c> token pasting, the predefined
/// macros <c>__LINE__ / __FILE__ / __VERSION__</c>, and records <c>#version</c>. Callers
/// may inject macros up front (this is how shader variants are compiled).
///
/// It consumes the raw token stream (including <see cref="TokenKind.Hash"/>) and produces
/// a clean token stream for the <see cref="Parser"/>.
/// </summary>
public sealed class Preprocessor
{
    sealed record Macro(bool IsFunction, List<string> Parameters, List<Token> Body);

    sealed class CondFrame
    {
        public bool ParentEmitting;
        public bool Emitting;
        public bool AnyTaken;
    }

    readonly Dictionary<string, Macro> _macros = new();
    readonly List<CondFrame> _cond = [];
    readonly HashSet<string> _hidden = [];   // macros currently expanding (recursion guard)
    readonly List<string> _extensions = [];   // #extension directives, in source order
    readonly List<string> _pragmas = [];       // #pragma names, in source order
    int _version = 100;   // glslang's default profile with no #version is ES 100 - which cannot compile
                      // to SPIR-V (see the version gate at the end of Process)
    bool _isEs = true;   // `#version N es` selects the OpenGL ES (ESSL) profile; the default matches the
                     // ES 100 default version - version-less input is ES until #version says otherwise
    [ThreadStatic] static List<Token>? _inputBuf, _outputBuf;   // pooled main token lists (see Process)
    int _lineOffset;   // added to a token's physical line by #line, so __LINE__ reports the adjusted number
    int _directiveLine;   // physical line of the directive currently being processed, for diagnostics

    /// <summary>
    /// Preprocesses a shader source into a token stream. The returned list is owned by the caller.
    /// </summary>
    public static (List<Token> Tokens, int Version, bool IsEs, List<string> Extensions, List<string> Pragmas) Process(string source, IReadOnlyDictionary<string, string>? defines = null)
    {
        var r = ProcessPooled(source, defines);
        // The pooled buffer is reused by the next call on this thread, so a public caller gets a copy.
        return (new List<Token>(r.Tokens), r.Version, r.IsEs, r.Extensions, r.Pragmas);
    }

    // The internal form returns the pooled token buffer directly; the parser consumes it before the next
    // compile on this thread can reuse it.
    internal static (List<Token> Tokens, int Version, bool IsEs, List<string> Extensions, List<string> Pragmas) ProcessPooled(string source, IReadOnlyDictionary<string, string>? defines = null)
    {
        var pp = new Preprocessor();
        if (defines is not null)
            foreach (var (name, value) in defines) pp.Define(name, value);

        // The main input/output token lists are the two biggest front-end allocations (~18K tokens each);
        // pool them per-thread. Both are fully consumed within one compile (input by Run, output by the
        // parser) before the next compile reuses them, so reuse is safe. Nested macro/paste tokenizations
        // use the allocating Tokenize overload and never touch these buffers.
        var input = _inputBuf ??= [];
        Lexer.Tokenize(Splice(source), input);
        var output = _outputBuf ??= [];
        output.Clear();
        pp.Run(input, output);
        if (pp._cond.Count > 0) throw new GlslParseException("unterminated #if / #ifdef");
        // glslang compiles ES only for SPIR-V from version 310 on ("#version: ES shaders for SPIR-V
        // require version 310 or higher"); since a version-less shader defaults to ES 100, this gate
        // is what rejects version-less input the same way glslang does.
        if (pp._isEs && pp._version < 310)
            throw new GlslParseException("#version: ES shaders for SPIR-V require version 310 or higher");
        // A reserved keyword cannot be used as an identifier (glslang's ReservedSet, checked after
        // preprocessing so disabled #if blocks and macro bodies are already resolved).
        foreach (var t in output)
            if (t.Kind == TokenKind.Identifier && ReservedWords.Contains(t.Text))
                throw new GlslParseException($"'{t.Text}' : reserved word cannot be used as an identifier", t.Line);
        output.Add(new Token(TokenKind.End, "", 0, 0));
        return (output, pp._version, pp._isEs, pp._extensions, pp._pragmas);
    }

    bool Emitting => _cond.Count == 0 || _cond[^1].Emitting;

    void Run(List<Token> input, List<Token> output)
    {
        int i = 0;
        while (input[i].Kind != TokenKind.End)
        {
            bool lineStart = i == 0 || input[i].Line != input[i - 1].Line;
            if (lineStart && input[i].Kind == TokenKind.Hash) i = Directive(input, i);
            else if (Emitting) { _sawCode = true; i = ExpandOne(input, i, output); }
            else i++;
        }
    }

    // ---- directives ----

    int Directive(List<Token> input, int i)
    {
        int line = _directiveLine = input[i].Line;
        i++;   // past '#'
        var dir = new List<Token>();
        while (input[i].Kind != TokenKind.End && input[i].Line == line) dir.Add(input[i++]);

        string name = dir.Count > 0 ? dir[0].Text : "";
        switch (name)
        {
            case "if": PushIf(() => EvalIf(dir.Skip(1))); break;
            case "ifdef": PushIf(() => _macros.ContainsKey(Name(dir, 1))); break;
            case "ifndef": PushIf(() => !_macros.ContainsKey(Name(dir, 1))); break;
            case "elif": Elif(() => EvalIf(dir.Skip(1))); break;
            case "else": Else(); break;
            case "endif": PopIf(); break;
            default:
                if (Emitting) Active(name, dir);
                break;
        }
        return i;
    }

    void Active(string name, List<Token> dir)
    {
        switch (name)
        {
            case "define": DefineDirective(dir); break;
            case "undef": _macros.Remove(Name(dir, 1)); break;
            case "version": VersionDirective(dir); break;
            case "error": throw new GlslParseException("#error: " + string.Join(' ', dir.Skip(1).Select(t => t.Text)), _directiveLine);
            case "extension": ExtensionDirective(dir); break;
            case "pragma": if (dir.Count > 1) _pragmas.Add(dir[1].Text); break;
            case "line": LineDirective(dir); break;
            case "": _sawCode = true; break;   // a bare '#' is inert content, but like glslang it still
                                               // counts as code, so a later '#version' is too late
            default: throw new GlslParseException($"unknown preprocessor directive '#{name}'", _directiveLine);
        }
    }

    // `#version N [profile]`. The core profile (explicit, or the default from 150 on) predefines
    // GL_core_profile, which shaders test with #ifdef.
    // `#line N` makes the following source line report line number N (so __LINE__ on it yields N).
    void LineDirective(List<Token> dir)
    {
        if (dir.Count < 2 || !int.TryParse(dir[1].Text, out int n))
            throw new GlslParseException("#line must be followed by a line number", dir[0].Line);
        _lineOffset = n - (dir[0].Line + 1);
    }

    // GLSL words reserved for future use that may not appear as identifiers (glslang Scan.cpp ReservedSet).
    // The ACTIVE keywords are NOT here: Prism's lexer emits every word (including `void`, `float`,
    // `return`...) as an Identifier token and the parser classifies them contextually, so this stream-level
    // check cannot tell keyword USE from keyword-as-identifier. The active-keyword rejection lives in the
    // parser's name-reading sites (see Parser.CheckDeclName), which know the context.
    static readonly HashSet<string> ReservedWords =
    [
        "common", "partition", "active", "asm", "class", "union", "enum", "typedef", "template", "this",
        "goto", "inline", "noinline", "public", "static", "extern", "external", "interface", "long", "short",
        "half", "fixed", "unsigned", "input", "output", "hvec2", "hvec3", "hvec4", "fvec2", "fvec3", "fvec4",
        "sampler3DRect", "filter", "sizeof", "cast", "namespace", "using",
    ];

    // The GLSL version numbers glslang recognises; anything else is "version not supported".
    static readonly HashSet<int> SupportedVersions =
        [100, 110, 120, 130, 140, 150, 300, 310, 320, 330, 400, 410, 420, 430, 440, 450, 460];

    bool _sawCode;

    void VersionDirective(List<Token> dir)
    {
        if (_sawCode) throw new GlslParseException("#version must occur first in the shader", dir[0].Line);
        if (dir.Count < 2 || !int.TryParse(dir[1].Text, out int v))
            throw new GlslParseException("#version must be followed by version number", dir[0].Line);
        if (!SupportedVersions.Contains(v))
            throw new GlslParseException($"version not supported: {v}", dir[0].Line);
        _version = v;
        // GLSL 100 only exists as ES (desktop GLSL never had a version 100); glslang classifies it as
        // ES even without the profile keyword.
        if (v == 100) _isEs = true;
        if (dir.Count > 2 && dir[2].Text is not ("core" or "compatibility" or "es"))
            throw new GlslParseException($"invalid profile '{dir[2].Text}' in #version directive", dir[0].Line);
        _isEs = dir.Count > 2 && dir[2].Text == "es";
        bool core = dir.Count > 2 ? dir[2].Text == "core" : _version >= 150;
        if (core) Define("GL_core_profile", "1");
    }

    // `#extension NAME : behavior` records the extension (it becomes an OpSourceExtension) and predefines
    // NAME so `#ifdef NAME` sees it; 'disable' does neither.
    void ExtensionDirective(List<Token> dir)
    {
        string ext = Name(dir, 1);
        // The form is `#extension <name> : <behavior>`; the colon and behavior are mandatory.
        if (dir.Count < 4 || dir[2].Kind != TokenKind.Colon)
            throw new GlslParseException("malformed #extension (expected '#extension <name> : <behavior>')", dir[0].Line);
        string behavior = dir[3].Text;
        if (behavior is not ("require" or "enable" or "warn" or "disable"))
            throw new GlslParseException($"invalid #extension behavior '{behavior}'", dir[0].Line);
        if (behavior == "disable") { _macros.Remove(ext); return; }
        if (ext != "all" && !_extensions.Contains(ext)) _extensions.Add(ext);
        if (ext != "all") Define(ext, "1");
    }

    void PushIf(Func<bool> condition)
    {
        bool parent = Emitting;
        bool taken = parent && condition();
        _cond.Add(new CondFrame { ParentEmitting = parent, Emitting = taken, AnyTaken = taken });
    }

    void Elif(Func<bool> condition)
    {
        if (_cond.Count == 0) throw new GlslParseException("#elif without #if", _directiveLine);
        var f = _cond[^1];
        bool take = f.ParentEmitting && !f.AnyTaken && condition();
        f.Emitting = take;
        f.AnyTaken |= take;
    }

    void Else()
    {
        if (_cond.Count == 0) throw new GlslParseException("#else without #if", _directiveLine);
        var f = _cond[^1];
        f.Emitting = f.ParentEmitting && !f.AnyTaken;
        f.AnyTaken = true;
    }

    void PopIf()
    {
        if (_cond.Count == 0) throw new GlslParseException("#endif without #if", _directiveLine);
        _cond.RemoveAt(_cond.Count - 1);
    }

    void DefineDirective(List<Token> dir)
    {
        string macro = Name(dir, 1);
        int idx = 2;
        var parameters = new List<string>();
        bool isFunction = dir.Count > 2 && dir[2].Kind == TokenKind.LParen && Adjacent(dir[1], dir[2]);
        if (isFunction)
        {
            idx = 3;
            Token At(int i) => i < dir.Count ? dir[i]
                : throw new GlslParseException($"unterminated parameter list in #define {Clip(macro)}", _directiveLine);
            if (At(idx).Kind != TokenKind.RParen)
                while (true)
                {
                    parameters.Add(At(idx++).Text);
                    if (At(idx).Kind == TokenKind.Comma) { idx++; continue; }
                    break;
                }
            if (At(idx).Kind != TokenKind.RParen)
                throw new GlslParseException($"expected ')' in #define {macro} parameter list", _directiveLine);
            idx++;   // past ')'
        }
        var replacement = new Macro(isFunction, parameters, dir.Skip(idx).ToList());
        // Redefining a macro is only legal when the new definition is identical to the old one.
        if (_macros.TryGetValue(macro, out var existing) && !SameDefinition(existing, replacement))
            throw new GlslParseException($"'{Clip(macro)}' : redefinition of a macro with a different definition", _directiveLine);
        _macros[macro] = replacement;
    }

    static bool SameDefinition(Macro a, Macro b) =>
        a.IsFunction == b.IsFunction
        && a.Parameters.SequenceEqual(b.Parameters)
        && a.Body.Count == b.Body.Count
        && a.Body.Zip(b.Body).All(p => p.First.Kind == p.Second.Kind && p.First.Text == p.Second.Text);

    void Define(string name, string? value)
    {
        var body = string.IsNullOrEmpty(value)
            ? [new Token(TokenKind.IntConst, "1", 0, 0)]
            : Lexer.Tokenize(value).Where(t => t.Kind != TokenKind.End).ToList();
        _macros[name] = new Macro(false, [], body);
    }

    // ---- macro expansion ----

    void ExpandTokens(IReadOnlyList<Token> input, List<Token> output)
    {
        int i = 0;
        while (i < input.Count) i = ExpandOne(input, i, output);
    }

    int ExpandOne(IReadOnlyList<Token> input, int i, List<Token> output)
    {
        var t = input[i];
        if (t.Kind == TokenKind.Identifier && !_hidden.Contains(t.Text))
        {
            switch (t.Text)
            {
                case "__LINE__": output.Add(Int(t.Line + _lineOffset)); return i + 1;
                case "__FILE__": output.Add(Int(0)); return i + 1;
                case "__VERSION__": output.Add(Int(_version)); return i + 1;
            }
            if (_macros.TryGetValue(t.Text, out var macro))
            {
                if (!macro.IsFunction)
                {
                    Rescan(t.Text, Substitute(macro, null), output);
                    return i + 1;
                }
                if (i + 1 < input.Count && input[i + 1].Kind == TokenKind.LParen)
                {
                    var (args, next) = CollectArgs(input, i + 1);
                    if (args.Count != macro.Parameters.Count)
                        throw new GlslParseException($"'{t.Text}' : macro expects {macro.Parameters.Count} argument(s), but got {args.Count}", t.Line);
                    // Arguments are macro-expanded BEFORE substitution, and before this macro is hidden, so an
                    // argument may legally name the macro being expanded (ID(ID(x))) or another macro whose
                    // expansion the body re-uses (TWICE(ADD(a,b))). Operands of ## keep their raw spelling.
                    Rescan(t.Text, Substitute(macro, ExpandArgs(macro, args)), output);
                    return next;
                }
            }
        }
        output.Add(t);
        return i + 1;
    }

    // The _hidden set stops a macro expanding itself, but nothing bounds how much a chain of distinct macros
    // can produce -- a doubling ladder (#define A1 A0 A0 ...) grows exponentially. Cap the total tokens one
    // preprocessor run may materialize so a pathological source fails fast instead of stalling the process.
    const int MaxExpansionTokens = 4_000_000;
    int _expansionBudget = MaxExpansionTokens;

    // The token budget bounds the VOLUME of an expansion, but expansion is recursive: a ladder of
    // distinct macros (#define M1 (M0+M0); #define M2 (M1+M1); ...) nests Rescan/ExpandTokens/ExpandOne
    // one level per link, and a long enough chain walks the C# stack off the end -- an uncatchable
    // StackOverflowException that kills the host process (glslang rejects such sources with a clean
    // "macro expansion too deep" instead). Cap the nesting depth at 256, mirroring the parser's
    // expression-nesting limit; no real shader expands macros anywhere near that deep.
    const int MaxExpansionDepth = 256;
    int _expansionDepth;

    void Rescan(string macro, List<Token> expanded, List<Token> output)
    {
        if (++_expansionDepth > MaxExpansionDepth)
            throw new GlslParseException($"macro expansion of '{macro}' is nested too deeply (limit {MaxExpansionDepth})", _directiveLine);
        try
        {
            if ((_expansionBudget -= expanded.Count) < 0)
                throw new GlslParseException($"macro expansion of '{macro}' is too large (limit {MaxExpansionTokens} tokens)", _directiveLine);
            _hidden.Add(macro);
            ExpandTokens(expanded, output);
            _hidden.Remove(macro);
        }
        finally { _expansionDepth--; }
    }

    // Pre-expands each argument. A parameter that is an operand of ## in the body is pasted with its raw
    // spelling, so those arguments are left unexpanded.
    List<List<Token>> ExpandArgs(Macro macro, List<List<Token>> args)
    {
        var result = new List<List<Token>>(args.Count);
        for (int p = 0; p < args.Count; p++)
        {
            if (p < macro.Parameters.Count && IsPasteOperand(macro, macro.Parameters[p])) { result.Add(args[p]); continue; }
            var expanded = new List<Token>();
            ExpandTokens(args[p], expanded);
            result.Add(expanded);
        }
        return result;
    }

    static bool IsPasteOperand(Macro macro, string parameter)
    {
        var body = macro.Body;
        for (int j = 0; j < body.Count; j++)
        {
            if (body[j].Kind != TokenKind.Operator || body[j].Text != "##") continue;
            if (j > 0 && body[j - 1].Text == parameter) return true;
            if (j + 1 < body.Count && body[j + 1].Text == parameter) return true;
        }
        return false;
    }

    List<Token> Substitute(Macro macro, List<List<Token>>? args)
    {
        var result = new List<Token>();
        foreach (var bt in macro.Body)
        {
            int p = macro.IsFunction ? macro.Parameters.IndexOf(bt.Text) : -1;
            if (p >= 0 && args is not null && p < args.Count) result.AddRange(args[p]);
            else result.Add(bt);
        }
        return Paste(result);
    }

    static List<Token> Paste(List<Token> body)
    {
        var result = new List<Token>();
        for (int j = 0; j < body.Count; j++)
        {
            if (body[j].Kind == TokenKind.Hash && j + 1 < body.Count && body[j + 1].Kind == TokenKind.Hash)
            {
                string left = result.Count > 0 ? result[^1].Text : "";
                string right = j + 2 < body.Count ? body[j + 2].Text : "";
                if (result.Count > 0) result.RemoveAt(result.Count - 1);
                result.AddRange(Lexer.Tokenize(left + right).Where(t => t.Kind != TokenKind.End));
                j += 2;
            }
            else result.Add(body[j]);
        }
        return result;
    }

    static (List<List<Token>> Args, int Next) CollectArgs(IReadOnlyList<Token> input, int i)
    {
        i++;   // past '('
        var args = new List<List<Token>>();
        var current = new List<Token>();
        int depth = 0;
        while (i < input.Count && input[i].Kind != TokenKind.End)
        {
            var t = input[i++];
            if (t.Kind == TokenKind.RParen && depth == 0) break;
            if (t.Kind == TokenKind.Comma && depth == 0) { args.Add(current); current = []; continue; }
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) depth--;
            current.Add(t);
        }
        if (current.Count > 0 || args.Count > 0) args.Add(current);
        return (args, i);
    }

    // ---- #if evaluation ----

    bool EvalIf(IEnumerable<Token> tokens)
    {
        var withoutDefined = ReplaceDefined(tokens.ToList());
        var expanded = new List<Token>();
        ExpandTokens(withoutDefined, expanded);

        var final = new List<Token>();
        foreach (var t in expanded)
            final.Add(t.Kind == TokenKind.Identifier ? Int(0) : t);   // undefined identifier -> 0
        final.Add(new Token(TokenKind.End, "", 0, 0));

        return Eval(Parser.ParseConstExpr(final)) != 0;
    }

    List<Token> ReplaceDefined(List<Token> tokens)
    {
        var result = new List<Token>();
        for (int j = 0; j < tokens.Count; j++)
        {
            if (tokens[j].Kind == TokenKind.Identifier && tokens[j].Text == "defined")
            {
                string name;
                if (j + 1 < tokens.Count && tokens[j + 1].Kind == TokenKind.LParen)
                {
                    if (j + 3 >= tokens.Count || tokens[j + 3].Kind != TokenKind.RParen)
                        throw new GlslParseException("malformed 'defined(...)' in #if expression", tokens[j].Line);
                    name = tokens[j + 2].Text;   // defined ( NAME )
                    j += 3;
                }
                else
                {
                    if (j + 1 >= tokens.Count)
                        throw new GlslParseException("'defined' requires an operand in #if expression", tokens[j].Line);
                    name = tokens[j + 1].Text;   // defined NAME
                    j += 1;
                }
                result.Add(Int(_macros.ContainsKey(name) ? 1 : 0));
            }
            else result.Add(tokens[j]);
        }
        return result;
    }

    long Eval(Expr e) => e switch
    {
        Literal l => EvalLiteral(l),
        Ident => 0,
        Unary u => u.Op switch
        {
            "-" => -Eval(u.Operand),
            "+" => Eval(u.Operand),
            "!" => Eval(u.Operand) == 0 ? 1 : 0,
            "~" => ~Eval(u.Operand),
            _ => throw new GlslParseException($"invalid operator '{u.Op}' in #if"),
        },
        // && and || short-circuit: the untaken side is never evaluated, which is what makes the usual
        // guard idiom (`#if N != 0 && 100/N > 2`) safe when the guard is false.
        Binary { Op: "&&" } b => Eval(b.Left) == 0 ? 0 : Eval(b.Right) != 0 ? 1 : 0,
        Binary { Op: "||" } b => Eval(b.Left) != 0 ? 1 : Eval(b.Right) != 0 ? 1 : 0,
        Binary b => EvalBinary(b.Op, Eval(b.Left), Eval(b.Right)),
        // The preprocessor expression grammar has no conditional operator.
        Conditional => throw new GlslParseException("'#if' : unexpected tokens following directive"),
        _ => throw new GlslParseException("invalid #if expression"),
    };

    static long EvalBinary(string op, long a, long b) => op switch
    {
        "+" => a + b, "-" => a - b, "*" => a * b,
        "/" => b == 0 ? throw new GlslParseException("division by zero in #if") : a / b,
        "%" => b == 0 ? throw new GlslParseException("division by zero in #if") : a % b,
        "<<" => a << (int)b, ">>" => a >> (int)b,
        "<" => a < b ? 1 : 0, ">" => a > b ? 1 : 0, "<=" => a <= b ? 1 : 0, ">=" => a >= b ? 1 : 0,
        "==" => a == b ? 1 : 0, "!=" => a != b ? 1 : 0,
        "&" => a & b, "^" => a ^ b, "|" => a | b,
        "&&" => a != 0 && b != 0 ? 1 : 0, "||" => a != 0 || b != 0 ? 1 : 0,
        "^^" => (a != 0) ^ (b != 0) ? 1 : 0,
        _ => throw new GlslParseException($"invalid operator '{op}' in #if"),
    };

    static long EvalLiteral(Literal l) => l.Kind switch
    {
        TokenKind.BoolConst => l.Text == "true" ? 1 : 0,
        // Only plain integers: a u/l suffix or a float is outside the preprocessor's expression grammar.
        TokenKind.IntConst => ParseInt(l.Text),
        _ => throw new GlslParseException($"'preprocessor evaluation' : bad expression"),
    };

    // Integer literals in #if follow the same lexical rules as the language: 0x hex, a leading 0 is octal,
    // otherwise decimal. (Reading a leading-zero literal as decimal here would make `#if 010 == 8` disagree
    // with the compiler, which reads 010 as 8.) A malformed or out-of-range literal is a source error.
    static long ParseInt(string text)
    {
        string t = text.TrimEnd('u', 'U', 'l', 'L');
        try
        {
            return
                t.StartsWith("0x") || t.StartsWith("0X") ? (long)Convert.ToUInt64(t[2..], 16) :
                t.Length > 1 && t[0] == '0' ? (long)Convert.ToUInt64(t, 8) :
                (long)ulong.Parse(t, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new GlslParseException($"invalid integer literal '{Clip(text)}' in #if");
        }
    }

    // Diagnostics must not embed unbounded token text: a 100,000-digit literal or a megabyte macro
    // name would produce a megabyte error message (which also deadlocks naive hosts draining stderr
    // pipes). A 32-char prefix keeps the message identifiable without the megabyte.
    internal static string Clip(string s) => s.Length <= 32 ? s : s[..32] + "...";

    // ---- helpers ----

    // A backslash-newline joins the next physical line onto the current one. The newlines removed by the
    // join are re-emitted at the end of the joined line, so every later line keeps its original number --
    // otherwise diagnostics and __LINE__ drift by one per continuation for the rest of the file.
    static string Splice(string source)
    {
        if (source.IndexOf('\\') < 0) return source;   // nothing to join
        var sb = new System.Text.StringBuilder(source.Length);
        int pending = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\\')
            {
                int j = i + 1;
                if (j < source.Length && source[j] == '\r') j++;
                if (j < source.Length && source[j] == '\n') { pending++; i = j; continue; }
            }
            sb.Append(c);
            if (c == '\n') { for (; pending > 0; pending--) sb.Append('\n'); }
        }
        for (; pending > 0; pending--) sb.Append('\n');
        return sb.ToString();
    }
    static bool Adjacent(Token a, Token b) => a.Line == b.Line && b.Column == a.Column + a.Text.Length;
    static Token Int(long value) => new(TokenKind.IntConst, value.ToString(), 0, 0);
    static string Name(List<Token> dir, int index) =>
        index < dir.Count ? dir[index].Text : throw new GlslParseException("preprocessor directive is missing a name");
}
