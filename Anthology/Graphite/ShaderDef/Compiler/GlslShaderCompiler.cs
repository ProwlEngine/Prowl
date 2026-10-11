using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Silk.NET.Shaderc;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// A shader failed to compile. The message lists every diagnostic as file:line: error: text.
/// </summary>
public sealed class ShaderCompileException(string message) : Exception(message);


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
    /// Emits debug information for shader debuggers.
    /// </summary>
    public bool DebugInfo;

    /// <summary>
    /// Macros defined for every stage of every shader.
    /// </summary>
    public Dictionary<string, string> Defines = [];
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
/// Compiles GLSLPROGRAM passes to SPIR-V through shaderc. Thread safe, one instance can serve every shader.
/// </summary>
public sealed unsafe class GlslShaderCompiler : IShaderCompiler, IDisposable
{
    private const string LibraryPrefix = "Graphite/";

    private static readonly Shaderc s_api = Shaderc.GetApi();
    private static readonly PfnIncludeResolveFn s_resolve = new(ResolveCallback);
    private static readonly PfnIncludeResultReleaseFn s_release = new(ReleaseCallback);
    private static readonly Lazy<Dictionary<string, string>> s_library = new(LoadLibrary);

    private readonly GlslCompilerOptions _options;
    private readonly ThreadLocal<nint> _compilers = new(() => (nint)s_api.CompilerInitialize(), trackAllValues: true);
    private readonly ConcurrentDictionary<string, VariantSpace[]> _axes = new(StringComparer.Ordinal);
    private int _sessionVersion;
    private bool _disposed;


    /// <summary>
    /// Makes a compiler.
    /// </summary>
    public GlslShaderCompiler(GlslCompilerOptions? options = null)
    {
        _options = options ?? new GlslCompilerOptions();
    }


    /// <inheritdoc/>
    public int SessionVersion => _sessionVersion;


    /// <summary>
    /// Call after included files change, so cached axes are rescanned and failed variants retry.
    /// </summary>
    public void Invalidate()
    {
        _axes.Clear();
        Interlocked.Increment(ref _sessionVersion);
    }


    /// <inheritdoc/>
    public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass) => Axes(pass);


    /// <inheritdoc/>
    public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
    {
        if (backend != GraphicsBackend.Vulkan)
            throw new NotSupportedException($"The GLSL compiler targets Vulkan only, not {backend}.");

        return CompileProgram(pass, combo).Description;
    }


    /// <summary>
    /// Compiles one variant of a pass, keeping the compute thread group size and the warnings.
    /// </summary>
    public GlslProgram CompileProgram(ShaderPass pass, ReadOnlySpan<Keyword> combo)
    {
        StringBuilder warnings = new();
        (List<GlslStageSource> stages, GlslProgramLayout layout) = Prepare(pass, combo, warnings);

        string file = FileName(pass);
        Dictionary<string, string> defines = Defines(pass, combo);
        List<(GlslStage, byte[])> compiled = [];
        foreach (GlslStageSource stage in stages)
            compiled.Add((stage.Stage, Run(stage.Rewrite(layout), stage.Stage, file, defines, preprocess: false, warnings)));

        (ShaderDescription description, uint x, uint y, uint z) = SpirvReflector.Reflect(compiled, layout);
        return new GlslProgram { Description = description, ThreadGroupSize = (x, y, z), Warnings = warnings.ToString() };
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
            string preprocessed = Encoding.UTF8.GetString(Run(source, stage, file, stageDefines, preprocess: true, warnings));
            stages.Add(new GlslStageSource(stage, preprocessed));
        }

        return (stages, new GlslProgramLayout(stages));
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


    /// <summary>
    /// Releases the native compilers.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (nint compiler in _compilers.Values)
            s_api.CompilerRelease((Silk.NET.Shaderc.Compiler*)compiler);
        _compilers.Dispose();
    }


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


    private VariantSpace[] Axes(ShaderPass pass)
    {
        return _axes.GetOrAdd(pass.Source, _ =>
        {
            string file = FileName(pass);
            List<VariantSpace> axes = [];
            HashSet<string> visited = new(StringComparer.Ordinal);
            foreach (GlslSection section in GlslSection.Split(pass.Source, file, pass.SourceLine))
                GlslPragmas.Collect(section.Body, file, axes, visited, (name, from) => Resolve(name, from));
            return [.. axes];
        });
    }


    private string? Resolve(string name, string from)
    {
        if (name.StartsWith(LibraryPrefix, StringComparison.Ordinal))
            return s_library.Value.TryGetValue(name, out string? source) ? source : null;

        return _options.ResolveInclude?.Invoke(name, from);
    }


    private static Dictionary<string, string> LoadLibrary()
    {
        Dictionary<string, string> library = new(StringComparer.Ordinal);
        Assembly assembly = typeof(GlslShaderCompiler).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(LibraryPrefix, StringComparison.Ordinal))
                continue;

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);
            library[resource] = reader.ReadToEnd();
        }

        return library;
    }


    private byte[] Run(string source, GlslStage stage, string file, Dictionary<string, string> defines, bool preprocess, StringBuilder warnings)
    {
        CompileOptions* options = s_api.CompileOptionsInitialize();
        IncludeContext context = new() { Resolve = Resolve };
        GCHandle handle = GCHandle.Alloc(context);
        CompilationResult* result = null;
        try
        {
            s_api.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (1u << 22) | (2u << 12));
            s_api.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc15);
            s_api.CompileOptionsSetOptimizationLevel(options, _options.Optimize ? OptimizationLevel.Performance : OptimizationLevel.Zero);
            if (_options.DebugInfo)
                s_api.CompileOptionsSetGenerateDebugInfo(options);

            foreach ((string name, string value) in defines)
                s_api.CompileOptionsAddMacroDefinition(options, name, (nuint)Encoding.UTF8.GetByteCount(name), value, (nuint)Encoding.UTF8.GetByteCount(value));

            s_api.CompileOptionsSetIncludeCallbacks(options, s_resolve, s_release, (void*)GCHandle.ToIntPtr(handle));

            ShaderKind kind = stage switch
            {
                GlslStage.Vertex => ShaderKind.VertexShader,
                GlslStage.Fragment => ShaderKind.FragmentShader,
                _ => ShaderKind.ComputeShader,
            };

            Silk.NET.Shaderc.Compiler* compiler = (Silk.NET.Shaderc.Compiler*)_compilers.Value;
            nuint length = (nuint)Encoding.UTF8.GetByteCount(source);
            result = preprocess
                ? s_api.CompileIntoPreprocessedText(compiler, source, length, kind, file, "main", options)
                : s_api.CompileIntoSpv(compiler, source, length, kind, file, "main", options);

            string messages = s_api.ResultGetErrorMessageS(result) ?? "";
            if (s_api.ResultGetCompilationStatus(result) != CompilationStatus.Success)
                throw new ShaderCompileException(messages.TrimEnd());

            if (!preprocess && s_api.ResultGetNumWarnings(result) > 0)
                warnings.Append(messages);

            byte[] bytes = new byte[(int)s_api.ResultGetLength(result)];
            fixed (byte* destination = bytes)
                Buffer.MemoryCopy(s_api.ResultGetBytes(result), destination, bytes.Length, bytes.Length);
            return bytes;
        }
        finally
        {
            if (result != null)
                s_api.ResultRelease(result);
            s_api.CompileOptionsRelease(options);
            handle.Free();
        }
    }


    private sealed class IncludeContext
    {
        public required Func<string, string, string?> Resolve;
        public readonly HashSet<string> Once = new(StringComparer.Ordinal);
    }


    private static IncludeResult* ResolveCallback(void* userData, byte* requested, int type, byte* requesting, nuint depth)
    {
        IncludeContext context = (IncludeContext)GCHandle.FromIntPtr((nint)userData).Target!;
        string name = Marshal.PtrToStringUTF8((nint)requested) ?? "";
        string from = Marshal.PtrToStringUTF8((nint)requesting) ?? "";

        string resolvedName = name;
        string content;
        string? source = null;
        try
        {
            source = context.Resolve(name, from);
        }
        catch (Exception exception)
        {
            content = $"resolving '{name}' threw {exception.GetType().Name}: {exception.Message}";
            return Allocate("", content);
        }

        if (source == null)
            return Allocate("", $"'{name}' was not found");

        content = GlslPragmas.StripOnce(source, out bool once);
        if (once && !context.Once.Add(name))
            content = "";

        return Allocate(resolvedName, content);
    }


    private static IncludeResult* Allocate(string name, string content)
    {
        IncludeResult* result = (IncludeResult*)NativeMemory.Alloc((nuint)sizeof(IncludeResult));
        result->SourceName = (byte*)Marshal.StringToCoTaskMemUTF8(name);
        result->SourceNameLength = (nuint)Encoding.UTF8.GetByteCount(name);
        result->Content = (byte*)Marshal.StringToCoTaskMemUTF8(content);
        result->ContentLength = (nuint)Encoding.UTF8.GetByteCount(content);
        result->UserData = null;
        return result;
    }


    private static void ReleaseCallback(void* userData, IncludeResult* result)
    {
        Marshal.FreeCoTaskMem((nint)result->SourceName);
        Marshal.FreeCoTaskMem((nint)result->Content);
        NativeMemory.Free(result);
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
    public static bool IsVariantPragma(string directive)
    {
        string[] parts = Words(directive);
        return parts.Length >= 2 && parts[0] == "pragma" && parts[1] == "variant";
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
    /// Collects every variant pragma of a source and of everything it includes, whether or not the include is active.
    /// </summary>
    public static void Collect(string source, string file, List<VariantSpace> axes, HashSet<string> visited, Func<string, string, string?> resolve)
    {
        foreach (string line in StripComments(source).Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith('#'))
                continue;

            string[] parts = Words(trimmed);
            if (parts.Length >= 2 && parts[0] == "include")
            {
                string name = trimmed[(trimmed.IndexOf("include", StringComparison.Ordinal) + 7)..].Trim().Trim('"', '<', '>');
                if (!visited.Add(name))
                    continue;

                string? included = resolve(name, file);
                if (included != null)
                    Collect(included, name, axes, visited, resolve);
                continue;
            }

            if (parts.Length < 2 || parts[0] != "pragma" || parts[1] != "variant")
                continue;

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
                    throw new ShaderCompileException($"{file}: error: '{word}' in '{trimmed}' is not a valid name");
            }

            if (values.Length == 1)
                throw new ShaderCompileException($"{file}: error: variant '{axis}' has a single value, give it two or more, or none for an on or off toggle");

            VariantSpace space = values.Length == 0 ? VariantSpace.Toggle(axis, scope) : new VariantSpace(axis, values, scope);
            int existing = axes.FindIndex(a => a.Name == axis);
            if (existing < 0)
            {
                axes.Add(space);
                continue;
            }

            VariantSpace other = axes[existing];
            if (other.IsToggle != space.IsToggle || other.Scope != space.Scope || !SameValues(other.Values, space.Values))
                throw new ShaderCompileException($"{file}: error: variant '{axis}' is declared twice with different values");
        }
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


    private static string StripComments(string source)
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
