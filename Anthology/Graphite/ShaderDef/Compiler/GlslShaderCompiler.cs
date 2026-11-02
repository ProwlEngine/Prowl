using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Prowl.Prism.Compiler;
using Prowl.Prism.Glsl;
using Prowl.Prism.SpirV;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// A shader failed to compile. The message lists every diagnostic as file:line: error: text.
/// </summary>
public sealed class ShaderCompileException(string message) : Exception(message)
{
    /// <summary>The file the error is in, as its #line directive or include named it, when known.</summary>
    public string? File { get; init; }

    /// <summary>1-based line of the error in <see cref="File"/>, 0 when unknown.</summary>
    public int Line { get; init; }

    /// <summary>1-based column of the error on <see cref="Line"/>, 0 when only the line is known.</summary>
    public int Column { get; init; }

    /// <summary>Length of the source text the error points at, 0 when unknown.</summary>
    public int Length { get; init; }
}


/// <summary>
/// Settings for <see cref="GlslShaderCompiler"/>.
/// </summary>
public sealed class GlslCompilerOptions
{
    /// <summary>
    /// Resolves an include that is not part of the Graphite library: (name, including file) to source, or null when missing.
    /// </summary>
    public Func<string, string, string?>? ResolveInclude;

    /// <summary>
    /// Runs the SPIR-V optimizer.
    /// </summary>
    public bool Optimize = true;

    /// <summary>
    /// Macros defined for every stage of every shader.
    /// </summary>
    public Dictionary<string, string> Defines = [];

    /// <summary>
    /// A folder finished variants are kept in across runs, keyed by the pass, the variant, the included files and the
    /// settings, so a variant compiles once per machine and a cached one skips the compiler entirely. Null keeps nothing.
    /// </summary>
    public string? CacheDirectory;
}


/// <summary>
/// A compiled program variant.
/// </summary>
public sealed class GlslProgram
{
    /// <summary>
    /// Stages, vertex inputs and resource layouts. Render state is applied by the pass at bind time.
    /// </summary>
    public required ShaderDescription Description { get; init; }

    /// <summary>
    /// Thread group size of a compute program.
    /// </summary>
    public (uint X, uint Y, uint Z) ThreadGroupSize { get; init; } = (1, 1, 1);

    /// <summary>
    /// Compiler warnings, empty when there were none.
    /// </summary>
    public string Warnings { get; init; } = "";

    /// <summary>
    /// The compute description of a compute program.
    /// </summary>
    public ComputeDescription ToCompute() => new(Description.Stages[0], Description.ResourceLayouts, ThreadGroupSize.X, ThreadGroupSize.Y, ThreadGroupSize.Z)
    {
        PushConstants = Description.PushConstants,
    };
}


/// <summary>
/// Compiles GLSLPROGRAM passes to SPIR-V through Prism. Thread safe, one instance can serve every shader.
/// </summary>
public sealed class GlslShaderCompiler : IShaderCompiler, IDisposable
{
    private readonly GlslCompilerOptions _options;
    private readonly ConcurrentDictionary<(string File, string Source), (VariantSpace[] Axes, ShaderGlobal[] Globals, string Includes)> _pragmas = new();

    // Variants whose preprocessed stages match compile once
    private readonly ConcurrentDictionary<string, Lazy<GlslProgram>> _aliases = new(StringComparer.Ordinal);
    private int _sessionVersion;
    private bool _disposed;


    /// <summary>
    /// Makes a compiler.
    /// </summary>
    public GlslShaderCompiler(GlslCompilerOptions? options = null)
    {
        _options = options ?? new GlslCompilerOptions();
        ShaderLibrary.Register(typeof(GlslShaderCompiler).Assembly);
    }


    /// <inheritdoc/>
    public int SessionVersion => _sessionVersion;


    /// <summary>
    /// Call after included files change, so cached axes are rescanned and failed variants retry.
    /// </summary>
    public void Invalidate()
    {
        _pragmas.Clear();
        _aliases.Clear();
        Interlocked.Increment(ref _sessionVersion);
    }


    /// <inheritdoc/>
    public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass) => Axes(pass);


    /// <inheritdoc/>
    public IReadOnlyList<ShaderGlobal> GetGlobals(ShaderPass pass) => Pragmas(pass).Globals;


    /// <inheritdoc/>
    public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
    {
        RequireVulkan(backend);
        return CompileProgram(pass, combo).Description;
    }


    /// <inheritdoc/>
    public ComputeDescription CompileCompute(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
    {
        RequireVulkan(backend);
        return CompileProgram(pass, combo).ToCompute();
    }


    /// <summary>
    /// Compiles one variant of a pass, keeping the compute thread group size and the warnings. Variants whose preprocessed
    /// stages match compile once.
    /// </summary>
    public GlslProgram CompileProgram(ShaderPass pass, ReadOnlySpan<Keyword> combo)
    {
        string key = CacheKey(pass, combo);
        if (Load(key) is { } cached)
            return cached;

        StringBuilder warnings = new();
        (List<GlslStageSource> stages, GlslProgramLayout layout) = Prepare(pass, combo, warnings);
        List<(GlslStage Stage, string Source)> sources = [];
        StringBuilder text = new();
        foreach (GlslStageSource stage in stages)
        {
            string source = stage.Rewrite(layout);
            sources.Add((stage.Stage, source));
            text.Append(stage.Stage).Append('|').Append(source).Append('|');
        }
        string file = FileName(pass);
        Lazy<GlslProgram> program = _aliases.GetOrAdd(Hash(text.ToString()), _ => new Lazy<GlslProgram>(() =>
        {
            List<(GlslStage, byte[])> compiled = [];
            foreach ((GlslStage stage, string source) in sources)
                compiled.Add((stage, Invoke(source, stage, file, [], preprocess: false, warnings)));
            (ShaderDescription description, uint x, uint y, uint z) = SpirvReflector.Reflect(compiled, layout);
            return new GlslProgram { Description = description, ThreadGroupSize = (x, y, z), Warnings = warnings.ToString() };
        }));
        return Store(key, program.Value);
    }


    private static void RequireVulkan(GraphicsBackend backend)
    {
        if (backend != GraphicsBackend.Vulkan)
            throw new NotSupportedException($"The GLSL compiler targets Vulkan only, not {backend}.");
    }


    // Preprocesses every stage and lays out the program, everything before the SPIR-V compile.
    internal (List<GlslStageSource> Stages, GlslProgramLayout Layout) Prepare(ShaderPass pass, ReadOnlySpan<Keyword> combo, StringBuilder warnings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string file = FileName(pass);
        List<GlslSection> sections = GlslSection.Split(pass.Source, file, pass.SourceLine);
        GlslSection? shared = sections.Find(s => s.Name == "Shared");
        List<(GlslStage Stage, GlslSection Section)> stageSections = [];
        foreach (GlslSection section in sections)
        {
            if (section.Name != "Shared")
                stageSections.Add((section.Stage, section));
        }

        bool compute = stageSections.Exists(s => s.Stage == GlslStage.Compute);
        if (compute && stageSections.Count > 1)
            throw new ShaderCompileException($"{file}: error: a Compute section must be the only stage of its pass");
        if (!compute && !stageSections.Exists(s => s.Stage == GlslStage.Vertex))
            throw new ShaderCompileException($"{file}: error: a pass needs a Vertex section, or a Compute section");

        Dictionary<string, string> defines = Defines(pass, combo);
        List<GlslStageSource> stages = [];
        foreach ((GlslStage stage, GlslSection section) in stageSections)
        {
            string source = StageSource(file, shared, section);
            Dictionary<string, string> stageDefines = new(defines) { [StageDefine(stage)] = "1" };
            string preprocessed = Encoding.UTF8.GetString(Invoke(source, stage, file, stageDefines, preprocess: true, warnings));
            stages.Add(new GlslStageSource(stage, preprocessed));
        }

        if (pass.Material is { } material)
            ApplyMaterial(file, material, Pragmas(pass).Globals, stages);
        else if (Pragmas(pass).Globals.Length > 0)
            DeclareGlobals(Pragmas(pass).Globals, stages);

        // The preprocessor drops pragmas it does not know, so the default set is read from the pass's own source
        System.Text.RegularExpressions.Match resourceSet = s_resourceSet.Match(pass.Source ?? "");
        uint? defaultSet = resourceSet.Success ? uint.Parse(resourceSet.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : pass.Material?.ResourceSet;
        return (stages, new GlslProgramLayout(stages, defaultSet));
    }


    // Outside a material a global is a plain uniform the caller sets, so one include serves both kinds of pass.
    private static void DeclareGlobals(IReadOnlyList<ShaderGlobal> globals, List<GlslStageSource> stages)
    {
        for (int i = 0; i < stages.Count; i++)
            stages[i] = new GlslStageSource(stages[i].Stage, stages[i].DeclareGlobals(globals));
    }

    private static void ApplyMaterial(string file, MaterialAbi material, IReadOnlyList<ShaderGlobal> globals, List<GlslStageSource> stages)
    {
        bool fragmentReads = false;
        for (int i = 0; i < stages.Count; i++)
        {
            string text = stages[i].ApplyMaterial(material, globals, out bool reads);
            fragmentReads |= reads && stages[i].Stage == GlslStage.Fragment;
            stages[i] = new GlslStageSource(stages[i].Stage, text);
        }

        GlslStageSource? vertex = stages.Find(s => s.Stage == GlslStage.Vertex);
        if (fragmentReads && vertex != null && !vertex.Declarations.Exists(d => d.Declarators.Exists(m => m.Name == material.Slot.Name)))
            throw new ShaderCompileException($"{file}: error: the fragment stage reads material properties but the vertex stage does not write '{material.Slot.Name}'");
    }


    private Dictionary<string, string> Defines(ShaderPass pass, ReadOnlySpan<Keyword> combo)
    {
        Dictionary<string, string> defines = new(_options.Defines);
        VariantSpace[] axes = Axes(pass);
        foreach (Keyword keyword in combo)
        {
            int axis = Array.FindIndex(axes, a => a.Name == keyword.Name);
            if (axis < 0)
                continue;

            if (axes[axis].IsToggle)
            {
                if (keyword.Value == "true")
                    defines[keyword.Name] = "1";
            }
            else
            {
                defines[$"{keyword.Name}_{keyword.Value}"] = "1";
            }
        }

        return defines;
    }


    public void Dispose() => _disposed = true;


    private static string FileName(ShaderPass pass)
    {
        string shader = pass.SourceName.Length > 0 ? pass.SourceName : "shader";
        return pass.Name.Length > 0 ? $"{shader}/{pass.Name}" : shader;
    }


    private static string StageDefine(GlslStage stage) => stage switch
    {
        GlslStage.Vertex => "GRAPHITE_VERTEX",
        GlslStage.Fragment => "GRAPHITE_FRAGMENT",
        _ => "GRAPHITE_COMPUTE",
    };


    private static string StageSource(string file, GlslSection? shared, GlslSection stage)
    {
        StringBuilder sb = new();
        sb.Append("#version 460\n");
        sb.Append("#extension GL_GOOGLE_cpp_style_line_directive : require\n");
        if (shared != null)
            sb.Append($"#line {shared.Line} \"{file}\"\n").Append(shared.Body).Append('\n');
        sb.Append($"#line {stage.Line} \"{file}\"\n").Append(stage.Body).Append('\n');
        return sb.ToString();
    }


    private VariantSpace[] Axes(ShaderPass pass) => Pragmas(pass).Axes;


    // The pass's axes and globals, and a hash of every file it can include
    private (VariantSpace[] Axes, ShaderGlobal[] Globals, string Includes) Pragmas(ShaderPass pass)
    {
        string file = FileName(pass);
        return _pragmas.GetOrAdd((file, pass.Source), _ =>
        {
            Dictionary<(string, string), string?> resolved = [];
            StringBuilder included = new();
            string? Include(string name, string from)
            {
                if (resolved.TryGetValue((name, from), out string? text))
                    return text;
                text = resolved[(name, from)] = Resolve(name, from);
                included.Append(name).Append('|').Append(text).Append('|');
                return text;
            }

            List<GlslSection> sections = GlslSection.Split(pass.Source, file, pass.SourceLine);
            string shared = sections.Find(s => s.Name == "Shared")?.Body ?? "";
            List<(string?, string)> stages = [];
            foreach (GlslSection section in sections)
            {
                if (section.Name != "Shared")
                    stages.Add((StageDefine(section.Stage), shared + "\n" + section.Body));
            }
            if (stages.Count == 0)
                stages.Add((null, shared));

            (VariantSpace[] axes, ShaderGlobal[] globals) = GlslPragmaScan.Scan(file, stages, _options.Defines, Include);
            return (axes, globals, Hash(included.ToString()));
        });
    }


    private string? Resolve(string name, string from)
    {
        if (name.StartsWith(ShaderLibrary.Prefix, StringComparison.Ordinal))
            return ShaderLibrary.Get(name);

        return _options.ResolveInclude?.Invoke(name, from);
    }


    private static readonly System.Text.RegularExpressions.Regex s_resourceSet = new(@"#\s*pragma\s+resource_set\s+(\d+)");

    // Bump when the compile settings below change in a way the key does not show
    private const string CacheSalt = "graphite-prism-3";

    private const uint CacheMagic = 0x47505643;

    private string CacheKey(ShaderPass pass, ReadOnlySpan<Keyword> combo)
    {
        StringBuilder text = new(pass.Source.Length + 256);
        text.Append(CacheSalt).Append('|').Append(_options.Optimize).Append('|').Append(FileName(pass)).Append('|').Append(pass.SourceLine).Append('|').Append(Pragmas(pass).Includes);
        Dictionary<string, string> defines = Defines(pass, combo);
        List<string> names = [.. defines.Keys];
        names.Sort(StringComparer.Ordinal);
        foreach (string name in names)
            text.Append('|').Append(name).Append('=').Append(defines[name]);
        text.Append('|').Append(pass.Fingerprint);
        return Hash(text.ToString());
    }

    private static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private string? CachePath(string key) => _options.CacheDirectory is { Length: > 0 } directory ? Path.Combine(directory, key + ".variant") : null;

    private GlslProgram? Load(string key)
    {
        if (CachePath(key) is not { } path || !File.Exists(path))
            return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new(stream, Encoding.UTF8);
            if (reader.ReadUInt32() != CacheMagic)
                return null;
            ShaderDescription description = BakedVariants.ReadDescription(reader);
            return new GlslProgram { Description = description, ThreadGroupSize = (reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private GlslProgram Store(string key, GlslProgram program)
    {
        if (CachePath(key) is not { } path)
            return program;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = $"{path}.{Environment.CurrentManagedThreadId}.tmp";
            using (FileStream stream = File.Create(temporary))
            using (BinaryWriter writer = new(stream, Encoding.UTF8))
            {
                writer.Write(CacheMagic);
                BakedVariants.WriteDescription(writer, program.Description);
                writer.Write(program.ThreadGroupSize.X);
                writer.Write(program.ThreadGroupSize.Y);
                writer.Write(program.ThreadGroupSize.Z);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return program;
    }

    private byte[] Invoke(string source, GlslStage stage, string file, Dictionary<string, string> defines, bool preprocess, StringBuilder warnings)
    {
        IncludeResult? Include(string name, bool system, string from)
            => Resolve(name, from) is { } text ? new IncludeResult(name, GlslPragmas.StripOnce(text, out _)) : null;
        try
        {
            if (preprocess)
                return Encoding.UTF8.GetBytes(GlslCompiler.Preprocess(source, defines, Include).Text);
            ShaderStage kind = stage switch
            {
                GlslStage.Vertex => ShaderStage.Vertex,
                GlslStage.Fragment => ShaderStage.Fragment,
                _ => ShaderStage.Compute,
            };
            return GlslCompiler.CompileToBytes(source, kind, SpirVVersion.V1_5, defines, _options.Optimize, includes: Include);
        }
        catch (GlslParseException e)
        {
            // Prism reports the file and line its #line directives and includes gave the error; the pass is the fallback.
            string where = e.File ?? file;
            throw new ShaderCompileException(e.Line > 0 ? $"{where}:{e.Line}: error: {e.Text}" : $"{where}: error: {e.Text}")
            {
                File = where,
                Line = e.Line,
                Column = e.Column,
                Length = e.Length,
            };
        }
    }
}


/// <summary>
/// A Shared, Vertex, Fragment or Compute section of a GLSLPROGRAM block.
/// </summary>
internal sealed class GlslSection
{
    public required string Name;
    public required string Body;
    public required int Line;

    public GlslStage Stage => Name switch
    {
        "Vertex" => GlslStage.Vertex,
        "Fragment" => GlslStage.Fragment,
        _ => GlslStage.Compute,
    };


    public static List<GlslSection> Split(string source, string file, int firstLine)
    {
        List<GlslSection> sections = [];
        int i = 0;
        while (true)
        {
            i = SkipSpace(source, i);
            if (i >= source.Length)
                break;

            int start = i;
            while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                i++;

            string name = source[start..i];
            if (name is not ("Shared" or "Vertex" or "Fragment" or "Compute"))
                throw new ShaderCompileException($"{file}:{LineAt(source, start, firstLine)}: error: expected a Shared, Vertex, Fragment or Compute section, found '{(name.Length > 0 ? name : source[start].ToString())}'");

            if (sections.Exists(s => s.Name == name))
                throw new ShaderCompileException($"{file}:{LineAt(source, start, firstLine)}: error: the {name} section is declared twice");

            i = SkipSpace(source, i);
            if (i >= source.Length || source[i] != '{')
                throw new ShaderCompileException($"{file}:{LineAt(source, start, firstLine)}: error: expected '{{' after {name}");

            int open = i;
            int close = MatchBrace(source, open);
            if (close < 0)
                throw new ShaderCompileException($"{file}:{LineAt(source, open, firstLine)}: error: the {name} section has no closing '}}'");

            string body = source[(open + 1)..close];
            if (GlslPragmas.HasVersion(body))
                throw new ShaderCompileException($"{file}:{LineAt(source, open, firstLine)}: error: remove #version, the compiler sets it");

            sections.Add(new GlslSection { Name = name, Body = body, Line = LineAt(source, open, firstLine) });
            i = close + 1;
        }

        return sections;
    }


    private static int LineAt(string source, int offset, int firstLine)
    {
        int line = firstLine;
        for (int k = 0; k < offset && k < source.Length; k++)
        {
            if (source[k] == '\n')
                line++;
        }
        return line;
    }


    private static int SkipSpace(string s, int i)
    {
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i]))
                i++;
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
                while (i < s.Length && s[i] != '\n') i++;
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
            }
            else
                break;
        }
        return i;
    }


    private static int MatchBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    return -1;
                i = end + 1;
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
                return i;
        }

        return -1;
    }
}


/// <summary>
/// The pragmas and directives the compiler handles itself.
/// </summary>
internal static class GlslPragmas
{
    /// <summary>The GLSL types a global pragma can declare.</summary>
    public static readonly string[] GlobalTypes = ["float", "int", "ivec2", "ivec3", "ivec4", "vec2", "vec3", "vec4", "mat4", "sampler2D", "sampler2DArray", "sampler3D", "samplerCube", "samplerCubeArray"];

    /// <summary>True for the variant and global pragmas, which only Graphite reads and which are removed before compiling.</summary>
    public static bool IsGraphitePragma(string directive)
    {
        string[] parts = Words(directive);
        return parts.Length >= 2 && parts[0] == "pragma" && parts[1] is "variant" or "global";
    }


    public static bool HasVersion(string source)
    {
        foreach (string line in StripComments(source).Split('\n'))
        {
            string[] parts = Words(line);
            if (parts.Length > 0 && parts[0] == "version" && line.TrimStart().StartsWith('#'))
                return true;
        }
        return false;
    }


    // Removes "#pragma once" while keeping the line count, so the include's line numbers stay right.
    public static string StripOnce(string source, out bool once)
    {
        once = false;
        string[] lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string[] parts = Words(lines[i]);
            if (lines[i].TrimStart().StartsWith('#') && parts.Length == 2 && parts[0] == "pragma" && parts[1] == "once")
            {
                lines[i] = "";
                once = true;
            }
        }
        return once ? string.Join('\n', lines) : source;
    }


    /// <summary>
    /// Adds the axis or globals of a variant or global pragma line, ignoring any other directive.
    /// </summary>
    public static void Read(string directive, string file, List<VariantSpace> axes, List<ShaderGlobal> globals)
    {
        string[] parts = Words(directive);
        if (parts.Length >= 2 && parts[0] == "pragma" && parts[1] == "global")
        {
            if (parts.Length < 4)
                throw new ShaderCompileException($"{file}: error: '#pragma global' needs a type and one or more names, like '#pragma global float _Wetness'");
            if (!GlobalTypes.Contains(parts[2]))
                throw new ShaderCompileException($"{file}: error: '{parts[2]}' in '{directive}' is not a global type, use one of {string.Join(" ", GlobalTypes)}");
            foreach (string name in parts[3..])
            {
                if (!IsIdentifier(name))
                    throw new ShaderCompileException($"{file}: error: '{name}' in '{directive}' is not a valid name");
                int declared = globals.FindIndex(g => g.Name == name);
                if (declared < 0)
                    globals.Add(new ShaderGlobal(name, parts[2]));
                else if (globals[declared].Type != parts[2])
                    throw new ShaderCompileException($"{file}: error: global '{name}' is declared as both {globals[declared].Type} and {parts[2]}");
            }
            return;
        }

        if (parts.Length < 2 || parts[0] != "pragma" || parts[1] != "variant")
            return;

        int at = 2;
        VariantScope scope = VariantScope.Material;
        if (parts.Length > at && parts[at] == "global")
        {
            scope = VariantScope.Global;
            at++;
        }

        if (parts.Length <= at)
            throw new ShaderCompileException($"{file}: error: '#pragma variant' needs a name");

        string axis = parts[at];
        string[] values = parts[(at + 1)..];
        foreach (string word in (string[])[axis, .. values])
        {
            if (!IsIdentifier(word))
                throw new ShaderCompileException($"{file}: error: '{word}' in '{directive}' is not a valid name");
        }

        if (values.Length == 1)
            throw new ShaderCompileException($"{file}: error: variant '{axis}' has a single value, give it two or more, or none for an on or off toggle");

        VariantSpace space = values.Length == 0 ? VariantSpace.Toggle(axis, scope) : new VariantSpace(axis, values, scope);
        int existing = axes.FindIndex(a => a.Name == axis);
        if (existing < 0)
        {
            axes.Add(space);
            return;
        }

        VariantSpace other = axes[existing];
        if (other.IsToggle != space.IsToggle || other.Scope != space.Scope || !SameValues(other.Values, space.Values))
            throw new ShaderCompileException($"{file}: error: variant '{axis}' is declared twice with different values");
    }


    private static bool SameValues(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }


    private static bool IsIdentifier(string word)
    {
        if (word.Length == 0 || !(char.IsLetter(word[0]) || word[0] == '_'))
            return false;
        foreach (char c in word)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }
        return true;
    }


    private static string[] Words(string directive) =>
        directive.Trim().TrimStart('#').Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);


    public static string StripComments(string source)
    {
        StringBuilder sb = new(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                if (i < source.Length) sb.Append('\n');
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int stop = end < 0 ? source.Length : end + 2;
                for (int k = i; k < stop; k++)
                {
                    if (source[k] == '\n')
                        sb.Append('\n');
                }
                i = stop - 1;
                continue;
            }

            sb.Append(source[i]);
        }
        return sb.ToString();
    }
}
