using System.Globalization;
using System.Text;
using Prowl.Prism.Compiler;
namespace Prowl.Prism.Glsl;

/// <summary>
/// The GLSL preprocessor. Runs before the parser: it handles <c>#define</c> (object and
/// function-like macros), <c>#undef</c>, the conditional family
/// (<c>#if/#ifdef/#ifndef/#elif/#else/#endif</c>) with <c>#if</c> constant-expression
/// evaluation and the <c>defined</c> operator, <c>##</c> token pasting, the predefined
/// macros <c>__LINE__ / __FILE__ / __VERSION__</c> and glslang's preamble (<c>VULKAN</c>,
/// <c>GL_ES</c>, the supported-extension macros, ...), records <c>#version</c>, and resolves
/// <c>#include</c> through an <see cref="IncludeCallback"/>. Callers may inject macros up front
/// (this is how shader variants are compiled).
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
    int _directiveLine;   // location code of the directive currently being processed, for diagnostics
    int _site;   // location code of the source token whose macro expansion is being read, for diagnostics

    // A location is a segment and a line packed into one int. Every #line and every included file starts a new
    // segment, and segment 0 is the main source. Segment names are kept by position, so the parser and lowerer
    // can carry a bare int and the compile entry point resolves it into "file:line" (see GlslParseException).
    internal const int SegmentStride = 1 << 20;
    readonly List<string?> _segmentNames = [null];
    int _segment;   // the segment the source being read belongs to

    readonly IncludeCallback? _includes;
    readonly HashSet<string> _included = [];   // include-once: the Name of every file entered so far
    readonly List<string> _includedFiles = [];   // the same names, in the order they were entered
    string _currentFile = "";   // Name of the file being processed; "" is the main source
    int _file;   // __FILE__ of the file being processed: 0 for the main source, then 1, 2, ... in include order
    int _includeDepth;
    const int MaxIncludeDepth = 64;   // nested #include depth; cycles end sooner, through include-once
    int _condFloor;   // #if depth when the current file began; its directives may not close an outer #if
    bool _sawVersion;

    int _invariantAllFrom = -1;   // output index of the first `#pragma STDGL invariant(all)`, or -1

    Preprocessor(IncludeCallback? includes, ShaderStage? stage)
    {
        _includes = includes;
        _stage = stage;
    }

    /// <summary>
    /// Preprocesses a shader source into a token stream. The returned list is owned by the caller.
    /// </summary>
    public static (List<Token> Tokens, int Version, bool IsEs, List<string> Extensions, List<string> Pragmas) Process(string source, IReadOnlyDictionary<string, string>? defines = null, IncludeCallback? includes = null)
    {
        var r = ProcessPooled(source, defines, includes);
        // The pooled buffer is reused by the next call on this thread, so a public caller gets a copy. Public callers
        // get the line within the file; the segment only matters to diagnostics.
        var tokens = r.Tokens.Select(t => t with { Line = t.Line % SegmentStride }).ToList();
        return (tokens, r.Version, r.IsEs, r.Extensions, r.Pragmas);
    }

    /// <summary>
    /// Preprocesses a shader source and returns the result as GLSL text, along with the state the preprocessor found.
    /// Unlike <see cref="Process"/>, this does not run the version, reserved-word, or ES checks, so it can return text
    /// for source that the compiler would reject.
    /// </summary>
    public static PreprocessResult Preprocess(string source, IReadOnlyDictionary<string, string>? defines = null, IncludeCallback? includes = null)
    {
        var output = new List<Token>();
        var pp = Execute(source, defines, includes, new List<Token>(), output, null);
        var macros = pp._macros.Where(m => !pp._predefined.Contains(m.Key)).Select(m => new MacroDefinition(m.Key, m.Value.IsFunction ? m.Value.Parameters : null, Spell(m.Value.Body)));
        return new PreprocessResult(pp.Emit(output), pp._version, pp._isEs, pp._extensions, pp._pragmas, macros.ToList(), pp._includedFiles);
    }

    // The internal form returns the pooled token buffer directly; the parser consumes it before the next
    // compile on this thread can reuse it.
    internal static (List<Token> Tokens, int Version, bool IsEs, List<string> Extensions, List<string> Pragmas, List<string?> SourceNames, int InvariantAllFrom) ProcessPooled(string source, IReadOnlyDictionary<string, string>? defines = null, IncludeCallback? includes = null, ShaderStage? stage = null)
    {
        // The main input/output token lists are the two biggest front-end allocations (~18K tokens each);
        // pool them per-thread. Both are fully consumed within one compile (input by Run, output by the
        // parser) before the next compile reuses them, so reuse is safe. Nested macro/paste tokenizations
        // use the allocating Tokenize overload and never touch these buffers.
        var output = _outputBuf ??= [];
        var pp = Execute(source, defines, includes, _inputBuf ??= [], output, stage);
        try
        {
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
        }
        catch (GlslParseException e) when (!e.Located)
        {
            throw e.Resolve(pp._segmentNames);
        }
        output.Add(new Token(TokenKind.End, "", 0, 0));
        return (output, pp._version, pp._isEs, pp._extensions, pp._pragmas, pp._segmentNames, pp._invariantAllFrom);
    }

    static Preprocessor Execute(string source, IReadOnlyDictionary<string, string>? defines, IncludeCallback? includes, List<Token> input, List<Token> output, ShaderStage? stage)
    {
        var pp = new Preprocessor(includes, stage);
        if (defines is not null)
            foreach (var (name, value) in defines) pp.Define(name, value);

        try
        {
            Lexer.Tokenize(Splice(source, pp._joins), input);
            output.Clear();
            pp.Run(input, output);
            if (pp._cond.Count > 0) throw new GlslParseException("unterminated #if / #ifdef");
        }
        catch (GlslParseException e) when (!e.Located)
        {
            throw e.Resolve(pp._segmentNames);
        }
        return pp;
    }

    bool Emitting => _cond.Count == 0 || _cond[^1].Emitting;

    void Run(List<Token> input, List<Token> output)
    {
        int i = 0;
        var stream = new PpStream(input);
        while (input[i].Kind != TokenKind.End)
        {
            bool lineStart = i == 0 || input[i].Line != input[i - 1].Line;
            if (lineStart && input[i].Kind == TokenKind.Hash) i = Directive(input, i, output);
            else if (Emitting)
            {
                _sawCode = true;
                EnsurePreamble();
                var token = input[i];
                // glslang's scanner (outside any #if) requires a decimal point or exponent before a float suffix.
                if (_cond.Count == 0 && token.Kind is TokenKind.FloatConst or TokenKind.HalfConst or TokenKind.DoubleConst
                    && token.Text.IndexOfAny(['.', 'e', 'E']) < 0 && !token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    throw new GlslParseException("'' : float literal needs a decimal point or exponent", Locate(token.Line));
                int start = output.Count;
                _site = Locate(token.Line);
                stream.Pos = i;
                ExpandSourceToken(stream, output);
                i = stream.Pos;
                Place(output, start, token, Locate(token.Line));
            }
            else i++;
        }
    }

    // The location code of a physical line of the source being read.
    int Locate(int physical) => physical <= 0 ? 0 : _segment * SegmentStride + Math.Clamp(physical + _lineOffset, 1, SegmentStride - 1);

    // Stamps the tokens one source token produced with its location. A token that came through unchanged keeps its
    // column, so the emitter can keep the source spacing. A macro's output takes the location of the invocation.
    static void Place(List<Token> output, int start, Token source, int location)
    {
        bool direct = output.Count == start + 1 && output[start].Kind == source.Kind && output[start].Text == source.Text
            && output[start].Line == source.Line && output[start].Column == source.Column;
        for (int k = start; k < output.Count; k++)
            output[k] = direct ? output[k] with { Line = location } : output[k] with { Line = location, Macro = true };
    }

    // ---- directives ----

    int Directive(List<Token> input, int i, List<Token> output)
    {
        int line = input[i].Line;
        _directiveLine = _site = Locate(line);
        i++;   // past '#'
        var dir = new List<Token>();
        while (input[i].Kind != TokenKind.End && input[i].Line == line) dir.Add(input[i++]);

        string name = dir.Count > 0 ? dir[0].Text : "";
        if (name != "version") EnsurePreamble();
        switch (name)
        {
            case "if": PushIf(() => EvalIf(dir, "#if")); break;
            case "ifdef": PushIf(() => _macros.ContainsKey(Name(dir, 1))); break;
            case "ifndef": PushIf(() => !_macros.ContainsKey(Name(dir, 1))); break;
            case "elif": Elif(() => EvalIf(dir, "#elif")); break;
            case "else": Else(); break;
            case "endif": PopIf(); break;
            case "include": if (Emitting) IncludeDirective(dir, output); break;
            default:
                if (Emitting) Active(name, dir, output);
                break;
        }
        return i;
    }

    void Active(string name, List<Token> dir, List<Token> output)
    {
        switch (name)
        {
            case "define": DefineDirective(dir); break;
            case "undef": _macros.Remove(Name(dir, 1)); break;
            case "version": VersionDirective(dir); break;
            case "error": throw new GlslParseException("#error: " + string.Join(' ', dir.Skip(1).Select(t => t.Text)), _directiveLine);
            case "extension": ExtensionDirective(dir); break;
            case "pragma": PragmaDirective(dir, output); break;
            case "line": LineDirective(dir); break;
            case "": _sawCode = true; break;   // a bare '#' is inert content, but like glslang it still
                                               // counts as code, so a later '#version' is too late
            default: throw new GlslParseException($"unknown preprocessor directive '#{name}'", _directiveLine);
        }
    }

    // `#version N [profile]`. The core profile (explicit, or the default from 150 on) predefines
    // GL_core_profile, which shaders test with #ifdef.
    // `#line N` makes the following source line report line number N (so __LINE__ on it yields N).
    // Like glslang, both numbers are macro-expanded preprocessor expressions: `#line N F` also sets the source-string
    // number __FILE__ reports, and `#line N "name"` (GL_GOOGLE_cpp_style_line_directive) names the file instead.
    // Before GLSL 330 (desktop) the line AFTER the next one is N, as glslang's lineDirectiveShouldSetNextLine says.
    void LineDirective(List<Token> dir)
    {
        var s = new PpStream(dir) { Pos = 1 };
        var tok = Scan(s);
        if (tok.T.Kind == TokenKind.End)
            throw new GlslParseException("#line must be followed by a line number", _directiveLine);
        int n = Eval(s, ref tok, 0, false, 0);
        // A file name starts a segment of its own. A bare #line keeps the file it was already in.
        string? name = _segmentNames[_segment];
        int? file = null;
        if (tok.T.Kind == TokenKind.StringConst) { name = tok.T.Text; tok = Scan(s); }
        else if (tok.T.Kind != TokenKind.End) file = Eval(s, ref tok, 0, false, 0);
        if (tok.T.Kind != TokenKind.End)
            throw new GlslParseException("'#line' : unexpected tokens following directive", _directiveLine);
        if (file is int f) _file = f;
        bool setsNextLine = _isEs || _version >= 330;
        int next = dir[0].Line + 1 + (_joins.TryGetValue(dir[0].Line, out var joined) ? joined.Count : 0);
        _lineOffset = unchecked(n - next + (setsNextLine ? 0 : 1));
        NewSegment(name);
    }

    // `#pragma STDGL invariant(all)` makes every user output declared after it invariant (glslang's
    // setInvariantAll, applied in globalQualifierFixCheck as each `out` declaration is parsed). The position is
    // recorded as an index into the output token stream, where the parser picks it up.
    void PragmaDirective(List<Token> dir, List<Token> output)
    {
        if (dir.Count > 1) _pragmas.Add(dir[1].Text);
        if (dir.Count > 4 && dir[1].Text == "STDGL" && dir[2].Text == "invariant" && dir[4].Text == "all" && _invariantAllFrom < 0)
            _invariantAllFrom = output.Count;
    }

    void NewSegment(string? name)
    {
        if (_segmentNames.Count >= int.MaxValue / SegmentStride)
            throw new GlslParseException("too many #line directives and includes", _directiveLine);
        _segmentNames.Add(name);
        _segment = _segmentNames.Count - 1;
    }

    // `#include "name"` or `#include <name>`. Reached only while the directive is active, so a header inside a
    // false #if is never resolved. The file is spliced in at this point, sharing the macro and #if state, and
    // each file is entered once: the first resolution of its Name enters it, later ones are skipped.
    void IncludeDirective(List<Token> dir, List<Token> output)
    {
        int line = _directiveLine;
        if (_includes is null)
            throw new GlslParseException("#include requires an include callback", line);
        var (header, isSystem) = HeaderName(dir, line);
        var file = _includes(header, isSystem, _currentFile)
            ?? throw new GlslParseException($"cannot open include file '{Clip(header)}'", line);
        if (_included.Contains(file.Name)) return;
        if (_includeDepth >= MaxIncludeDepth)
            throw new GlslParseException($"#include nested too deeply (limit {MaxIncludeDepth})", line);

        _included.Add(file.Name);
        _includedFiles.Add(file.Name);
        var saved = (_currentFile, _file, _lineOffset, _condFloor, _segment, _joins);
        (_currentFile, _file, _lineOffset, _condFloor) = (file.Name, _includedFiles.Count, 0, _cond.Count);
        NewSegment(file.Name);
        _includeDepth++;
        try
        {
            List<Token> tokens;
            _joins = [];
            try { tokens = Lexer.Tokenize(Splice(file.Content, _joins)); }
            catch (GlslParseException e) when (!e.Located) { throw e.WithCode(Locate(e.Code)); }
            Run(tokens, output);
            if (_cond.Count != _condFloor)
                throw new GlslParseException($"unterminated #if / #ifdef in included file '{Clip(header)}'", line);
        }
        finally
        {
            _includeDepth--;
            (_currentFile, _file, _lineOffset, _condFloor, _segment, _joins) = saved;
            _directiveLine = line;
        }
    }

    static (string Name, bool IsSystem) HeaderName(List<Token> dir, int line)
    {
        if (dir.Count > 1 && dir[1].Kind == TokenKind.StringConst && dir[1].Text.Length > 0)
            return (dir[1].Text, false);
        if (dir.Count > 1 && dir[1].Kind == TokenKind.Operator && dir[1].Text == "<")
        {
            var name = new StringBuilder();
            for (int j = 2; j < dir.Count && dir[j].Text != ">"; j++) name.Append(dir[j].Text);
            if (name.Length > 0 && dir.Any(t => t.Kind == TokenKind.Operator && t.Text == ">"))
                return (name.ToString(), true);
        }
        throw new GlslParseException("'#include' : must be followed by a header name", line);
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
        if (_sawCode) throw new GlslParseException("#version must occur first in the shader", _directiveLine);
        if (dir.Count < 2 || !int.TryParse(dir[1].Text, out int v))
            throw new GlslParseException("#version must be followed by version number", _directiveLine);
        if (!SupportedVersions.Contains(v))
            throw new GlslParseException($"version not supported: {v}", _directiveLine);
        _version = v;
        _sawVersion = true;
        // GLSL 100 only exists as ES (desktop GLSL never had a version 100); glslang classifies it as
        // ES even without the profile keyword.
        if (v == 100) _isEs = true;
        if (dir.Count > 2 && dir[2].Text is not ("core" or "compatibility" or "es"))
            throw new GlslParseException($"invalid profile '{dir[2].Text}' in #version directive", _directiveLine);
        _isEs = dir.Count > 2 && dir[2].Text == "es";
        // glslang's DeduceVersionProfile: the ES-only versions need the profile spelled out.
        if (v is 300 or 310 or 320 && dir.Count <= 2)
            throw new GlslParseException("#version: versions 300, 310, and 320 require specifying the 'es' profile");
        _compatibility = dir.Count > 2 && dir[2].Text == "compatibility";
    }

    // `#extension NAME : behavior` records the extension (it becomes an OpSourceExtension) and predefines
    // NAME so `#ifdef NAME` sees it; 'disable' does neither.
    void ExtensionDirective(List<Token> dir)
    {
        string ext = Name(dir, 1);
        // The form is `#extension <name> : <behavior>`; the colon and behavior are mandatory.
        if (dir.Count < 4 || dir[2].Kind != TokenKind.Colon)
            throw new GlslParseException("malformed #extension (expected '#extension <name> : <behavior>')", _directiveLine);
        string behavior = dir[3].Text;
        if (behavior is not ("require" or "enable" or "warn" or "disable"))
            throw new GlslParseException($"invalid #extension behavior '{behavior}'", _directiveLine);
        // glslang defines no macro for an #extension: the supported extensions' macros come from the preamble.
        if (behavior == "disable") return;
        if (ext != "all" && !_extensions.Contains(ext)) _extensions.Add(ext);
    }

    void PushIf(Func<bool> condition)
    {
        bool parent = Emitting;
        bool taken = parent && condition();
        _cond.Add(new CondFrame { ParentEmitting = parent, Emitting = taken, AnyTaken = taken });
    }

    void Elif(Func<bool> condition)
    {
        if (_cond.Count <= _condFloor) throw new GlslParseException("#elif without #if", _directiveLine);
        var f = _cond[^1];
        bool take = f.ParentEmitting && !f.AnyTaken && condition();
        f.Emitting = take;
        f.AnyTaken |= take;
    }

    void Else()
    {
        if (_cond.Count <= _condFloor) throw new GlslParseException("#else without #if", _directiveLine);
        var f = _cond[^1];
        f.Emitting = f.ParentEmitting && !f.AnyTaken;
        f.AnyTaken = true;
    }

    void PopIf()
    {
        if (_cond.Count <= _condFloor) throw new GlslParseException("#endif without #if", _directiveLine);
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
    //
    // Expansion reads from a stack of inputs, like glslang's TPpContext: the source tokens at the bottom and, above
    // them, the replacement list of each macro being expanded. A macro stays hidden (glslang's 'busy') while its
    // replacement list is on the stack, and a list is popped only when a read goes past its end. So the last name a
    // replacement produces can take its arguments from the source that follows the invocation (`#define A B` makes
    // `A(1)` call B), while a macro's own name inside its expansion is never expanded again.

    // A token on the expansion stack. Painted marks a token of an argument that was already macro-expanded, which
    // glslang does not expand a second time (its fullyExpanded flag).
    readonly record struct PTok(Token T, bool Painted = false);

    sealed class Frame(List<PTok> tokens, string? macro)
    {
        public readonly List<PTok> Tokens = tokens;
        public readonly string? Macro = macro;   // the macro hidden while this replacement list is read
        public int Pos;
    }

    sealed class PpStream(IReadOnlyList<Token> source, bool tracksLine = true)
    {
        public readonly IReadOnlyList<Token> Source = source;
        public readonly bool TracksLine = tracksLine;   // reading a source token moves the current line
        public int Pos;   // the next source token
        public readonly List<Frame> Frames = [];
        public bool FromFrame, FromMacro;   // where the last token read came from
    }

    enum Expansion { NotStarted, Started, Undefined }

    int _curLine;   // the physical line of the last source token read: what __LINE__ in a macro body reports
    Dictionary<int, List<int>> _joins = [];   // line continuations of the file being read (see Splice)

    // The physical line of a token of the file being read: its line plus the continuations joined before it.
    int Physical(Token t)
    {
        if (_joins.Count == 0 || !_joins.TryGetValue(t.Line, out var cols)) return t.Line;
        int line = t.Line;
        foreach (int c in cols) if (t.Column >= c) line++;
        return line;
    }
    int _prescanDepth;

    bool Next(PpStream s, out PTok tok)
    {
        while (s.Frames.Count > 0)
        {
            var f = s.Frames[^1];
            if (f.Pos < f.Tokens.Count)
            {
                tok = f.Tokens[f.Pos++];
                s.FromFrame = true;
                s.FromMacro = f.Macro is not null;
                return true;
            }
            PopFrame(s);
        }
        s.FromFrame = s.FromMacro = false;
        if (s.Pos < s.Source.Count && s.Source[s.Pos].Kind != TokenKind.End)
        {
            var t = s.Source[s.Pos++];
            if (s.TracksLine) _curLine = Physical(t);
            tok = new PTok(t);
            return true;
        }
        tok = default;
        return false;
    }

    PTok Scan(PpStream s) => Next(s, out var tok) ? tok : new PTok(new Token(TokenKind.End, "", 0, 0));

    // Puts back the token the last Next returned. Next pops only exhausted lists before reading, so the list it
    // read from is still on top.
    static void Unget(PpStream s)
    {
        if (s.FromFrame) s.Frames[^1].Pos--;
        else s.Pos--;
    }

    void PushFrame(PpStream s, List<PTok> tokens, string? macro)
    {
        if (s.Frames.Count >= MaxExpansionDepth)
            throw new GlslParseException($"macro expansion of '{Clip(macro ?? s.Frames[^1].Macro ?? "")}' is nested too deeply (limit {MaxExpansionDepth})", _site);
        if ((_expansionBudget -= tokens.Count) < 0)
            throw new GlslParseException($"macro expansion of '{Clip(macro ?? "")}' is too large (limit {MaxExpansionTokens} tokens)", _site);
        if (macro is not null) _hidden.Add(macro);
        s.Frames.Add(new Frame(tokens, macro));
    }

    void PopFrame(PpStream s)
    {
        var f = s.Frames[^1];
        s.Frames.RemoveAt(s.Frames.Count - 1);
        if (f.Macro is not null) _hidden.Remove(f.Macro);
    }

    // Whether an expansion still has tokens to deliver; exhausted lists are popped (and their macros unhidden).
    bool Pending(PpStream s)
    {
        while (s.Frames.Count > 0)
        {
            var f = s.Frames[^1];
            if (f.Pos < f.Tokens.Count) return true;
            PopFrame(s);
        }
        return false;
    }

    // Reads one source token and, if it starts a macro expansion, everything that expansion produces (which may
    // consume further source tokens as arguments).
    void ExpandSourceToken(PpStream s, List<Token> output)
    {
        if (!Next(s, out var tok)) return;
        Expand(s, tok, output);
        while (Pending(s) && Next(s, out tok)) Expand(s, tok, output);
    }

    void Expand(PpStream s, PTok tok, List<Token> output)
    {
        if (IsName(tok.T) && MacroExpand(s, tok, false) != Expansion.NotStarted) return;
        output.Add(tok.T);
    }

    static bool IsName(Token t) => t.Kind is TokenKind.Identifier or TokenKind.BoolConst;

    // glslang's MacroExpand: pushes the expansion of the name just read, if it has one. A function-like macro
    // expands only when the next token is '('. With expandUndefined (#if), an undefined name reads as 0.
    Expansion MacroExpand(PpStream s, PTok tok, bool expandUndefined)
    {
        if (tok.Painted) return Expansion.NotStarted;
        var t = tok.T;
        switch (t.Text)
        {
            case "__LINE__":
                // A source token is on the line just read; one from a replacement list or an argument carries the
                // line it was given when the invocation was read.
                int line = s.TracksLine && !s.FromFrame ? _curLine : t.Line;
                PushFrame(s, [new PTok(Int(unchecked(line + _lineOffset)))], null);
                return Expansion.Started;
            case "__FILE__": PushFrame(s, [new PTok(Int(_file))], null); return Expansion.Started;
            case "__VERSION__": PushFrame(s, [new PTok(Int(_version))], null); return Expansion.Started;
        }
        if (!_macros.TryGetValue(t.Text, out var macro))
        {
            if (!expandUndefined) return Expansion.NotStarted;
            PushFrame(s, [new PTok(Int(0))], null);
            return Expansion.Undefined;
        }
        if (_hidden.Contains(t.Text)) return Expansion.NotStarted;
        List<List<Token>>? args = null;
        if (macro.IsFunction)
        {
            if (!Next(s, out var paren)) return Expansion.NotStarted;
            if (paren.T.Kind != TokenKind.LParen) { Unget(s); return Expansion.NotStarted; }
            args = CollectArgs(s, t.Text, macro.Parameters.Count);
        }
        PushFrame(s, Substitute(macro, args), t.Text);
        return Expansion.Started;
    }

    // The _hidden set stops a macro expanding itself, but nothing bounds how much a chain of distinct macros
    // can produce -- a doubling ladder (#define A1 A0 A0 ...) grows exponentially. Cap the total tokens one
    // preprocessor run may materialize so a pathological source fails fast instead of stalling the process.
    const int MaxExpansionTokens = 4_000_000;
    int _expansionBudget = MaxExpansionTokens;

    // A ladder of distinct macros (#define M1 (M0+M0); #define M2 (M1+M1); ...) stacks one replacement list per
    // link; glslang rejects such sources with a clean "macro expansion too deep". Cap the stack at 256, mirroring
    // the parser's expression-nesting limit; no real shader expands macros anywhere near that deep.
    const int MaxExpansionDepth = 256;

    // The arguments of a function-like macro call, read raw (unexpanded) after its '('. A __LINE__ in an argument
    // takes the line being read, as in glslang.
    List<List<Token>> CollectArgs(PpStream s, string name, int parameterCount)
    {
        var args = new List<List<Token>>();
        var current = new List<Token>();
        int depth = 0;
        while (true)
        {
            if (!Next(s, out var tok))
                throw new GlslParseException($"'macro expansion' : End of input in macro {Clip(name)}", _site);
            var t = tok.T;
            if (t.Kind == TokenKind.RParen && depth == 0) break;
            if (t.Kind == TokenKind.Comma && depth == 0) { args.Add(current); current = []; continue; }
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) depth--;
            if (t.Text == "__LINE__") t = t with { Line = _curLine };
            current.Add(t);
        }
        // `F()` passes one empty argument to a one-parameter macro.
        if (current.Count > 0 || args.Count > 0 || parameterCount == 1) args.Add(current);
        if (args.Count != parameterCount)
            throw new GlslParseException($"'{Clip(name)}' : macro expects {parameterCount} argument(s), but got {args.Count}", _site);
        return args;
    }

    // The replacement list with the arguments substituted. A parameter next to ## takes its argument's raw tokens;
    // anywhere else it takes the argument macro-expanded on its own beforehand (glslang's PrescanMacroArg). A
    // __LINE__ in the body reports the line the invocation ended on.
    List<PTok> Substitute(Macro macro, List<List<Token>>? args)
    {
        var body = macro.Body;
        List<PTok>[]? expanded = null;
        if (args is not null)
        {
            expanded = new List<PTok>[args.Count];
            for (int p = 0; p < args.Count; p++) expanded[p] = Prescan(args[p]);
        }
        var result = new List<PTok>(body.Count);
        for (int j = 0; j < body.Count; j++)
        {
            var bt = body[j];
            int p = args is not null && bt.Kind == TokenKind.Identifier ? macro.Parameters.LastIndexOf(bt.Text) : -1;
            if (p >= 0 && p < args!.Count)
            {
                if (IsPaste(body, j - 2) || IsPaste(body, j + 1))
                    foreach (var a in args[p]) result.Add(new PTok(a));
                else result.AddRange(expanded![p]);
            }
            else result.Add(new PTok(bt.Text == "__LINE__" ? bt with { Line = _curLine } : bt));
        }
        return Paste(result);
    }

    static bool IsPaste(List<Token> body, int k) =>
        k >= 0 && k + 1 < body.Count && body[k].Kind == TokenKind.Hash && body[k + 1].Kind == TokenKind.Hash;

    // Macro-expands one argument in isolation: a function-like macro at its end does not reach past it for a '('.
    // The result is not expanded again, except a trailing function-like macro name, which the replacement list
    // may still call.
    List<PTok> Prescan(List<Token> arg)
    {
        if (++_prescanDepth > Parser.MaxNestingDepth)
            throw new GlslParseException($"macro arguments are nested too deeply (limit {Parser.MaxNestingDepth})", _site);
        try
        {
            var s = new PpStream(arg, tracksLine: false);
            var output = new List<Token>();
            while (Next(s, out var tok)) Expand(s, tok, output);
            var result = new List<PTok>(output.Count);
            for (int k = 0; k < output.Count; k++)
            {
                var t = output[k];
                bool callable = k == output.Count - 1 && t.Kind == TokenKind.Identifier
                    && _macros.TryGetValue(t.Text, out var m) && m.IsFunction;
                result.Add(new PTok(t, !callable));
            }
            return result;
        }
        finally { _prescanDepth--; }
    }

    static List<PTok> Paste(List<PTok> body)
    {
        bool any = false;
        foreach (var t in body) if (t.T.Kind == TokenKind.Hash) { any = true; break; }
        if (!any) return body;
        var result = new List<PTok>();
        for (int j = 0; j < body.Count; j++)
        {
            if (body[j].T.Kind == TokenKind.Hash && j + 1 < body.Count && body[j + 1].T.Kind == TokenKind.Hash)
            {
                string left = result.Count > 0 ? result[^1].T.Text : "";
                string right = j + 2 < body.Count ? body[j + 2].T.Text : "";
                if (result.Count > 0) result.RemoveAt(result.Count - 1);
                foreach (var t in Lexer.Tokenize(left + right))
                    if (t.Kind != TokenKind.End) result.Add(new PTok(t));
                j += 2;
            }
            else result.Add(body[j]);
        }
        return result;
    }

    // ---- #if evaluation ----
    //
    // A port of glslang's TPpContext::eval: 32-bit int arithmetic on plain integer literals, names macro-expanded
    // as they are reached (an undefined name, `true` included, reads as 0), `defined NAME` / `defined(NAME)`, and
    // no conditional operator. INT_MIN / -1 and INT_MIN % -1 are 0, and -INT_MIN is INT_MIN, as in glslang.

    const int PrecUnary = 12;

    static int BinaryPrecedence(Token t) => t.Kind != TokenKind.Operator ? -1 : t.Text switch
    {
        "||" => 2, "&&" => 3, "|" => 4, "^" => 5, "&" => 6, "==" or "!=" => 7,
        "<" or ">" or "<=" or ">=" => 8, "<<" or ">>" => 9, "+" or "-" => 10, "*" or "/" or "%" => 11,
        _ => -1,
    };

    bool EvalIf(List<Token> dir, string label)
    {
        var s = new PpStream(dir) { Pos = 1 };
        var tok = Scan(s);
        int value = Eval(s, ref tok, 0, false, 0);
        if (tok.T.Kind != TokenKind.End)
            throw new GlslParseException($"'{label}' : unexpected tokens following directive", _directiveLine);
        return value != 0;
    }

    GlslParseException EvalError(string message) => new($"'preprocessor evaluation' : {message}", _directiveLine);

    int Eval(PpStream s, ref PTok tok, int precedence, bool shortCircuit, int depth)
    {
        if (depth > Parser.MaxNestingDepth)
            throw new GlslParseException($"expression nesting is too deep (limit {Parser.MaxNestingDepth})", _directiveLine);
        int res;
        var t = tok.T;
        if (t.Kind == TokenKind.Identifier && t.Text == "defined")
        {
            if (s.FromMacro)
                throw new GlslParseException("'defined' : cannot use in preprocessor expression when expanded from macros", _directiveLine);
            bool close = false;
            tok = Scan(s);
            if (tok.T.Kind == TokenKind.LParen) { close = true; tok = Scan(s); }
            if (!IsName(tok.T)) throw EvalError("incorrect directive, expected identifier");
            res = _macros.ContainsKey(tok.T.Text) ? 1 : 0;
            tok = Scan(s);
            if (close)
            {
                if (tok.T.Kind != TokenKind.RParen) throw EvalError("expected ')'");
                tok = Scan(s);
            }
        }
        else if (IsName(t))
        {
            tok = EvalToToken(s, tok, shortCircuit);
            return Eval(s, ref tok, precedence, shortCircuit, depth + 1);
        }
        else if (t.Kind == TokenKind.IntConst)
        {
            res = ParseInt(t.Text);
            tok = Scan(s);
        }
        else if (t.Kind == TokenKind.LParen)
        {
            tok = Scan(s);
            res = Eval(s, ref tok, 0, shortCircuit, depth + 1);
            if (tok.T.Kind != TokenKind.RParen) throw EvalError("expected ')'");
            tok = Scan(s);
        }
        else if (t.Kind == TokenKind.Operator && t.Text is "+" or "-" or "~" or "!")
        {
            tok = Scan(s);
            res = Eval(s, ref tok, PrecUnary, shortCircuit, depth + 1);
            res = t.Text switch
            {
                "-" => res == int.MinValue ? int.MinValue : -res,
                "~" => ~res,
                "!" => res == 0 ? 1 : 0,
                _ => res,
            };
        }
        else throw EvalError("bad expression");

        tok = EvalToToken(s, tok, shortCircuit);
        while (tok.T.Kind is not (TokenKind.RParen or TokenKind.End))
        {
            int prec = BinaryPrecedence(tok.T);
            if (prec < 0 || prec <= precedence) break;
            string op = tok.T.Text;
            int left = res;
            // Once a || with a true (1) left side or a && with a false left side is seen, the rest of this
            // subexpression is short-circuited, which only relaxes the ES undefined-name error in glslang.
            if (!shortCircuit && ((op == "||" && left == 1) || (op == "&&" && left == 0))) shortCircuit = true;
            tok = Scan(s);
            res = Eval(s, ref tok, prec, shortCircuit, depth + 1);
            if (op is "/" or "%" && res == 0)
            {
                // glslang reports a division by zero even on a short-circuited side; Prism keeps the guard idiom
                // (`#if N != 0 && 100/N > 2`) working there and reads the divisor as 1, like glslang's recovery.
                if (!shortCircuit) throw EvalError("division by 0");
                res = 1;
            }
            res = Apply(op, left, res);
        }
        return res;
    }

    // Expands names until the first token that is not one (glslang's evalToToken).
    PTok EvalToToken(PpStream s, PTok tok, bool shortCircuit)
    {
        while (IsName(tok.T) && tok.T.Text != "defined")
        {
            switch (MacroExpand(s, tok, true))
            {
                case Expansion.NotStarted:
                    throw EvalError("can't evaluate expression");
                case Expansion.Undefined when !shortCircuit && _isEs:
                    throw EvalError($"undefined macro in expression not allowed in es profile {Clip(tok.T.Text)}");
            }
            tok = Scan(s);
        }
        return tok;
    }

    static int Apply(string op, int a, int b) => unchecked(op switch
    {
        "||" => a != 0 || b != 0 ? 1 : 0,
        "&&" => a != 0 && b != 0 ? 1 : 0,
        "|" => a | b, "^" => a ^ b, "&" => a & b,
        "==" => a == b ? 1 : 0, "!=" => a != b ? 1 : 0,
        ">" => a > b ? 1 : 0, ">=" => a >= b ? 1 : 0, "<" => a < b ? 1 : 0, "<=" => a <= b ? 1 : 0,
        "<<" => a << b, ">>" => a >> b,
        "+" => a + b, "-" => a - b, "*" => a * b,
        "/" => a == int.MinValue && b == -1 ? 0 : a / b,
        "%" => a == int.MinValue && b == -1 ? 0 : a % b,
        _ => throw new GlslParseException($"invalid operator '{op}' in #if"),
    });

    // An integer literal in #if follows the language's lexical rules (0x hex, a leading 0 octal, otherwise decimal)
    // and holds 32 bits, as in glslang's scanner: a larger value is an error, and 0x80000000..0xFFFFFFFF wrap to
    // negative ints.
    int ParseInt(string text)
    {
        string t = text.TrimEnd('u', 'U', 'l', 'L');
        bool hex = t.StartsWith("0x") || t.StartsWith("0X"), octal = !hex && t.Length > 1 && t[0] == '0';
        ulong v;
        try
        {
            v = hex ? Convert.ToUInt64(t[2..], 16) : octal ? Convert.ToUInt64(t, 8) : ulong.Parse(t, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (OverflowException) { v = ulong.MaxValue; }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new GlslParseException($"invalid integer literal '{Clip(text)}' in #if", _directiveLine);
        }
        if (v > uint.MaxValue)
            throw new GlslParseException($"'' : {(hex ? "hexadecimal" : octal ? "octal" : "numeric")} literal too big", _directiveLine);
        return unchecked((int)(uint)v);
    }

    // ---- predefined macros ----

    // glslang's system preamble for a Vulkan (-V) compile (TParseVersions::getPreamble): the extensions it supports,
    // the profile macros, GL_FRAGMENT_PRECISION_HIGH, VULKAN 100 and, for desktop GLSL, the stage. It depends on
    // the version, so it is defined once #version has been seen (at the first other directive or code). A macro
    // the caller already defined keeps the caller's value.
    bool _preambleDone, _compatibility;
    readonly HashSet<string> _predefined = [];
    readonly ShaderStage? _stage;

    void EnsurePreamble()
    {
        if (_preambleDone) return;
        _preambleDone = true;
        int v = _version;
        if (_isEs)
        {
            Predefine(EsPreamble);
            if (v >= 300) Predefine("GL_NV_shader_noperspective_interpolation");
            if (v >= 310) Predefine("GL_EXT_null_initializer", "GL_EXT_subgroup_uniform_control_flow", "GL_EXT_maximal_reconvergence");
        }
        else
        {
            Predefine(DesktopPreamble);
            if (v >= 150)
            {
                Predefine("GL_core_profile");
                if (_compatibility) Predefine("GL_compatibility_profile");
            }
            if (v >= 140) Predefine("GL_EXT_null_initializer", "GL_EXT_subgroup_uniform_control_flow", "GL_EXT_maximal_reconvergence");
            if (v >= 130) Predefine("GL_FRAGMENT_PRECISION_HIGH");
        }
        if ((!_isEs && v >= 140) || (_isEs && v >= 310))
            Predefine("GL_EXT_device_group", "GL_EXT_multiview", "GL_NV_shader_sm_builtins");
        if ((!_isEs && v >= 130) || (_isEs && v >= 300))
            Predefine("GL_EXT_texture_offset_non_const");
        if (v >= 300) Predefine("GL_OVR_multiview", "GL_OVR_multiview2");
        Predefine("GL_GOOGLE_cpp_style_line_directive", "GL_GOOGLE_include_directive", "GL_KHR_blend_equation_advanced",
            "GL_EXT_terminate_invocation");
        PredefineValue("VULKAN", "100");
        if (!_isEs && _stage is ShaderStage stage)
        {
            string? name = stage switch
            {
                ShaderStage.Vertex => "GL_VERTEX_SHADER",
                ShaderStage.TessControl => "GL_TESSELLATION_CONTROL_SHADER",
                ShaderStage.TessEvaluation => "GL_TESSELLATION_EVALUATION_SHADER",
                ShaderStage.Geometry => "GL_GEOMETRY_SHADER",
                ShaderStage.Fragment => "GL_FRAGMENT_SHADER",
                ShaderStage.Compute => "GL_COMPUTE_SHADER",
                ShaderStage.RayGen => "GL_RAY_GENERATION_SHADER_EXT",
                ShaderStage.Intersection => "GL_INTERSECTION_SHADER_EXT",
                ShaderStage.AnyHit => "GL_ANY_HIT_SHADER_EXT",
                ShaderStage.ClosestHit => "GL_CLOSEST_HIT_SHADER_EXT",
                ShaderStage.Miss => "GL_MISS_SHADER_EXT",
                ShaderStage.Callable => "GL_CALLABLE_SHADER_EXT",
                ShaderStage.Task => "GL_TASK_SHADER_NV",
                ShaderStage.Mesh => "GL_MESH_SHADER_NV",
                _ => null,
            };
            if (name is not null) Predefine(name);
        }
    }

    void Predefine(params string[] names)
    {
        foreach (var name in names) PredefineValue(name, "1");
    }

    void PredefineValue(string name, string value)
    {
        if (_macros.ContainsKey(name)) return;
        _macros[name] = new Macro(false, [], [new Token(TokenKind.IntConst, value, 0, 0)]);
        _predefined.Add(name);
    }

    static readonly string[] EsPreamble =
    [
        "GL_ES", "GL_FRAGMENT_PRECISION_HIGH", "GL_OES_texture_3D", "GL_OES_standard_derivatives", "GL_EXT_frag_depth",
        "GL_OES_EGL_image_external", "GL_OES_EGL_image_external_essl3", "GL_EXT_YUV_target", "GL_EXT_shader_texture_lod",
        "GL_EXT_shadow_samplers", "GL_EXT_fragment_shading_rate",
        "GL_ANDROID_extension_pack_es31a", "GL_OES_sample_variables", "GL_OES_shader_image_atomic",
        "GL_OES_shader_multisample_interpolation", "GL_OES_texture_storage_multisample_2d_array", "GL_EXT_geometry_shader",
        "GL_EXT_geometry_point_size", "GL_EXT_gpu_shader5", "GL_EXT_primitive_bounding_box", "GL_EXT_shader_io_blocks",
        "GL_EXT_tessellation_shader", "GL_EXT_tessellation_point_size", "GL_EXT_texture_buffer", "GL_EXT_texture_cube_map_array",
        "GL_EXT_shader_implicit_conversions", "GL_EXT_shader_integer_mix", "GL_EXT_blend_func_extended",
        "GL_OES_geometry_shader", "GL_OES_geometry_point_size", "GL_OES_gpu_shader5", "GL_OES_primitive_bounding_box",
        "GL_OES_shader_io_blocks", "GL_OES_tessellation_shader", "GL_OES_tessellation_point_size", "GL_OES_texture_buffer",
        "GL_OES_texture_cube_map_array", "GL_EXT_shader_non_constant_global_initializers",
        "GL_QCOM_image_processing", "GL_QCOM_image_processing2", "GL_QCOM_tile_shading", "GL_QCOM_cooperative_matrix_conversion",
    ];

    static readonly string[] DesktopPreamble =
    [
        "GL_ARB_texture_rectangle", "GL_ARB_shading_language_420pack", "GL_ARB_texture_gather", "GL_ARB_gpu_shader5",
        "GL_ARB_separate_shader_objects", "GL_ARB_compute_shader", "GL_ARB_tessellation_shader", "GL_ARB_enhanced_layouts",
        "GL_ARB_texture_cube_map_array", "GL_ARB_texture_multisample", "GL_ARB_shader_texture_lod",
        "GL_ARB_explicit_attrib_location", "GL_ARB_explicit_uniform_location", "GL_ARB_shader_image_load_store",
        "GL_ARB_shader_atomic_counters", "GL_ARB_shader_draw_parameters", "GL_ARB_shader_group_vote",
        "GL_ARB_derivative_control", "GL_ARB_shader_texture_image_samples", "GL_ARB_viewport_array",
        "GL_ARB_gpu_shader_int64", "GL_ARB_gpu_shader_fp64", "GL_ARB_shader_ballot", "GL_ARB_sparse_texture2",
        "GL_ARB_sparse_texture_clamp", "GL_ARB_shader_stencil_export", "GL_ARB_sample_shading", "GL_ARB_shader_image_size",
        "GL_ARB_shading_language_packing", "GL_ARB_post_depth_coverage", "GL_ARB_fragment_shader_interlock",
        "GL_ARB_uniform_buffer_object", "GL_ARB_shader_bit_encoding", "GL_ARB_shader_storage_buffer_object",
        "GL_ARB_texture_query_lod", "GL_ARB_vertex_attrib_64bit", "GL_NV_gpu_shader5", "GL_ARB_draw_instanced",
        "GL_ARB_fragment_coord_conventions",
        "GL_EXT_shader_non_constant_global_initializers", "GL_EXT_shader_image_load_formatted", "GL_EXT_post_depth_coverage",
        "GL_EXT_control_flow_attributes", "GL_EXT_nonuniform_qualifier", "GL_EXT_shader_16bit_storage",
        "GL_EXT_shader_8bit_storage", "GL_EXT_samplerless_texture_functions", "GL_EXT_scalar_block_layout",
        "GL_EXT_fragment_invocation_density", "GL_EXT_buffer_reference", "GL_EXT_buffer_reference2",
        "GL_EXT_buffer_reference_uvec2", "GL_EXT_demote_to_helper_invocation", "GL_EXT_debug_printf",
        "GL_EXT_fragment_shading_rate", "GL_EXT_shared_memory_block", "GL_EXT_shader_integer_mix",
        "GL_EXT_spec_constant_composites",
        "GL_KHR_shader_subgroup_basic", "GL_KHR_shader_subgroup_vote", "GL_KHR_shader_subgroup_arithmetic",
        "GL_KHR_shader_subgroup_ballot", "GL_KHR_shader_subgroup_shuffle", "GL_KHR_shader_subgroup_shuffle_relative",
        "GL_KHR_shader_subgroup_clustered", "GL_KHR_shader_subgroup_quad",
        "GL_KHR_cooperative_matrix",
        "GL_EXT_shader_image_int64", "GL_EXT_shader_atomic_int64", "GL_EXT_shader_realtime_clock", "GL_EXT_ray_tracing",
        "GL_EXT_ray_query", "GL_EXT_ray_flags_primitive_culling", "GL_EXT_ray_cull_mask", "GL_EXT_ray_tracing_position_fetch",
        "GL_EXT_spirv_intrinsics", "GL_EXT_mesh_shader",
        "GL_AMD_shader_ballot", "GL_AMD_shader_trinary_minmax", "GL_AMD_shader_explicit_vertex_parameter",
        "GL_AMD_gcn_shader", "GL_AMD_gpu_shader_half_float", "GL_AMD_texture_gather_bias_lod", "GL_AMD_gpu_shader_int16",
        "GL_AMD_shader_image_load_store_lod", "GL_AMD_shader_fragment_mask", "GL_AMD_gpu_shader_half_float_fetch",
        "GL_INTEL_shader_integer_functions2",
        "GL_NV_sample_mask_override_coverage", "GL_NV_geometry_shader_passthrough", "GL_NV_viewport_array2",
        "GL_NV_shader_atomic_int64", "GL_NV_conservative_raster_underestimation", "GL_NV_shader_subgroup_partitioned",
        "GL_NV_shading_rate_image", "GL_NV_ray_tracing", "GL_NV_ray_tracing_motion_blur", "GL_NV_fragment_shader_barycentric",
        "GL_NV_compute_shader_derivatives", "GL_NV_shader_texture_footprint", "GL_NV_mesh_shader", "GL_NV_cooperative_matrix",
        "GL_NV_integer_cooperative_matrix", "GL_NV_shader_invocation_reorder", "GL_NV_cooperative_matrix2",
        "GL_QCOM_image_processing", "GL_QCOM_image_processing2", "GL_QCOM_tile_shading", "GL_QCOM_cooperative_matrix_conversion",
        "GL_EXT_shader_explicit_arithmetic_types", "GL_EXT_shader_explicit_arithmetic_types_int8",
        "GL_EXT_shader_explicit_arithmetic_types_int16", "GL_EXT_shader_explicit_arithmetic_types_int32",
        "GL_EXT_shader_explicit_arithmetic_types_int64", "GL_EXT_shader_explicit_arithmetic_types_float16",
        "GL_EXT_shader_explicit_arithmetic_types_float32", "GL_EXT_shader_explicit_arithmetic_types_float64",
        "GL_EXT_shader_subgroup_extended_types_int8", "GL_EXT_shader_subgroup_extended_types_int16",
        "GL_EXT_shader_subgroup_extended_types_int64", "GL_EXT_shader_subgroup_extended_types_float16",
        "GL_EXT_shader_atomic_float", "GL_EXT_shader_atomic_float2",
        "GL_EXT_fragment_shader_barycentric", "GL_EXT_shader_quad_control", "GL_EXT_texture_array",
        "GL_EXT_control_flow_attributes2",
        "GL_EXT_integer_dot_product", "GL_EXT_bfloat16", "GL_EXT_float_e5m2", "GL_EXT_float_e4m3",
    ];

    // Diagnostics must not embed unbounded token text: a 100,000-digit literal or a megabyte macro
    // name would produce a megabyte error message (which also deadlocks naive hosts draining stderr
    // pipes). A 32-char prefix keeps the message identifiable without the megabyte.
    internal static string Clip(string s) => s.Length <= 32 ? s : s[..32] + "...";

    // ---- helpers ----

    // A backslash-newline joins the next physical line onto the current one. The newlines removed by the
    // join are re-emitted at the end of the joined line, so every later line keeps its original number --
    // otherwise diagnostics and __LINE__ drift by one per continuation for the rest of the file.
    // Each join is recorded in `joins` (joined line -> the column at which each further physical line starts), so
    // __LINE__ can still report the physical line, as glslang does.
    static string Splice(string source, Dictionary<int, List<int>>? joins = null)
    {
        if (source.IndexOf('\\') < 0) return source;   // nothing to join
        var sb = new System.Text.StringBuilder(source.Length);
        int pending = 0, line = 1, lineStart = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\\')
            {
                int j = i + 1;
                if (j < source.Length && source[j] == '\r') j++;
                if (j < source.Length && source[j] == '\n')
                {
                    pending++;
                    i = j;
                    if (joins is not null)
                    {
                        if (!joins.TryGetValue(line, out var cols)) joins[line] = cols = [];
                        cols.Add(sb.Length - lineStart + 1);
                    }
                    continue;
                }
            }
            sb.Append(c);
            if (c == '\n')
            {
                line++;
                for (; pending > 0; pending--) { sb.Append('\n'); line++; }
                lineStart = sb.Length;
            }
        }
        for (; pending > 0; pending--) sb.Append('\n');
        return sb.ToString();
    }
    static bool Adjacent(Token a, Token b) => a.Line == b.Line && b.Column == a.Column + a.Text.Length;
    static Token Int(long value) => new(TokenKind.IntConst, value.ToString(), 0, 0);

    // Renders the preprocessed tokens as GLSL that keeps the source's layout, like glslang -E: a token on the same
    // source line keeps the spacing it had, a new line keeps its indentation, and a change of line that is not the
    // next one, or of file, gets a #line marker. The #version and #extension directives stay up front.
    string Emit(List<Token> tokens)
    {
        var sb = new StringBuilder();
        if (_sawVersion) sb.Append("#version ").Append(_version).Append(_isEs ? " es" : "").Append('\n');
        foreach (var ext in _extensions) sb.Append("#extension ").Append(ext).Append(" : enable\n");

        int segment = -1, line = -1;
        bool open = false;   // a line of tokens is in progress and has no newline yet
        Token previous = default;
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.End) continue;
            int s = t.Line / SegmentStride, l = t.Line % SegmentStride;
            if (s == segment && l == line)
                sb.Append(Gap(previous, t));
            else
            {
                if (open) sb.Append('\n');
                if (s == segment && l == line + 1)
                    sb.Append(Indent(t));
                else
                {
                    string name = s < _segmentNames.Count && _segmentNames[s] is { } file ? $" \"{file}\"" : "";
                    sb.Append("#line ").Append(l).Append(name).Append('\n').Append(Indent(t));
                    segment = s;
                }
            }
            sb.Append(Spelling(t));
            line = l;
            previous = t;
            open = true;
        }
        return (open ? sb.Append('\n') : sb).ToString();
    }

    // The whitespace between two tokens on the same source line: the gap they had in the source. Tokens a macro made
    // have no source spacing, so they are separated by one space.
    static string Gap(Token previous, Token t)
    {
        if (previous.Macro || t.Macro || previous.Column <= 0 || t.Column <= 0) return " ";
        int end = previous.Column + (previous.Kind == TokenKind.StringConst ? previous.Text.Length + 2 : previous.Text.Length);
        if (t.Column < end) return " ";
        return new string(' ', t.Column - end);
    }

    // The indentation of a token that starts a line.
    static string Indent(Token t) => t.Macro || t.Column <= 1 ? "" : new string(' ', t.Column - 1);

    static string Spell(IEnumerable<Token> tokens) => string.Join(' ', tokens.Where(t => t.Kind != TokenKind.End).Select(Spelling));

    static string Spelling(Token t) => t.Kind == TokenKind.StringConst
        ? "\"" + t.Text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r") + "\""
        : t.Text;

    static string Name(List<Token> dir, int index) =>
        index < dir.Count ? dir[index].Text : throw new GlslParseException("preprocessor directive is missing a name");
}
