// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace Prowl.OrigamiUI.Graphite;

/// <summary>A file an include resolved to. <see cref="Path"/> is a disk path, or a library name such as Graphite/Core.glsl.</summary>
public sealed record ShaderSource(string Path, string Text);

/// <summary>
/// What every open shader editor shares: how includes resolve, the indexes of included files, and the compiler used for
/// diagnostics. Names starting with Graphite/ come from <see cref="ShaderLibrary"/>, everything else from the host.
/// </summary>
public sealed class ShaderWorkspace
{
    private readonly Func<string, string?, ShaderSource?>? _resolve;
    private readonly Func<string?, IEnumerable<string>>? _includeNames;
    private readonly ConcurrentDictionary<string, (string Text, ShaderIndex Index)> _indexes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SurfaceModel?> _surfaceModels = new(StringComparer.Ordinal);
    private GlslShaderCompiler? _compiler;

    /// <param name="resolveInclude">Resolves an include name seen in a file to its source, or null. Graphite/ names never reach it.</param>
    /// <param name="includeNames">Include names offered while typing #include in a file, besides the Graphite library.</param>
    public ShaderWorkspace(Func<string, string?, ShaderSource?>? resolveInclude = null, Func<string?, IEnumerable<string>>? includeNames = null)
    {
        _resolve = resolveInclude;
        _includeNames = includeNames;
    }

    /// <summary>
    /// Turns a shader with a Surface block into the passes the renderer would build, so they can be compiled for
    /// diagnostics. Without it those shaders are only checked for wrapper errors.
    /// </summary>
    public Func<ShaderDefinition, IEnumerable<ShaderPass>>? ExpandSurface { get; set; }

    /// <summary>Defines set for every compile, such as the engine's own.</summary>
    public Dictionary<string, string> Defines { get; } = new(StringComparer.Ordinal);

    public ShaderSource? Resolve(string name, string? fromPath)
    {
        if (name.StartsWith(ShaderLibrary.Prefix, StringComparison.Ordinal))
            return ShaderLibrary.Get(name) is { } text ? new ShaderSource(name, text) : null;
        try { return _resolve?.Invoke(name, fromPath); }
        catch (Exception) { return null; }
    }

    public IEnumerable<string> IncludeNames(string? fromPath)
    {
        var names = new SortedSet<string>(ShaderLibrary.Names.Where(n => n.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase)), StringComparer.Ordinal);
        if (_includeNames != null)
            foreach (var n in _includeNames(fromPath)) names.Add(n);
        return names;
    }

    internal ShaderIndex? IncludeIndex(string name, string? fromPath)
    {
        var source = Resolve(name, fromPath);
        if (source == null) return null;
        if (_indexes.TryGetValue(source.Path, out var cached) && (ReferenceEquals(cached.Text, source.Text) || cached.Text == source.Text))
            return cached.Index;
        var index = new ShaderIndex(source.Text, source.Path, isShaderFile: false);
        _indexes[source.Path] = (source.Text, index);
        return index;
    }

    /// <summary>
    /// The model shader of a Surface block and the place in it the block's code is spliced, the first "#pragma surface".
    /// What is visible there is visible to the block. Null when the model cannot be found.
    /// </summary>
    internal SurfaceModel? Surface(string model) => _surfaceModels.GetOrAdd(model, m =>
    {
        var source = Resolve($"{ShaderLibrary.Prefix}Standard/{m}.shader", null);
        if (source == null) return null;
        var index = new ShaderIndex(source.Text, source.Path, isShaderFile: true);
        var pragma = index.Directives.Find(d => d.Name == "pragma" && d.Args.Trim() == "surface");
        return pragma == null ? null : new SurfaceModel(index, pragma.Offset);
    });

    /// <summary>Drops cached include indexes, such as after an include file changed on disk.</summary>
    public void Invalidate()
    {
        _indexes.Clear();
        _surfaceModels.Clear();
        _compiler?.Invalidate();
    }

    internal GlslShaderCompiler Compiler => _compiler ??= new GlslShaderCompiler(new GlslCompilerOptions
    {
        ResolveInclude = (name, from) => Resolve(name, from)?.Text,
        Optimize = false,
        Defines = Defines,
    });

    /// <summary>The index of a file and of everything it includes, each once.</summary>
    internal IEnumerable<ShaderIndex> WithIncludes(ShaderIndex root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<ShaderIndex>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var index = stack.Pop();
            yield return index;
            foreach (var include in index.Includes)
            {
                var child = IncludeIndex(include.Name, index.Path);
                if (child?.Path != null && seen.Add(child.Path)) stack.Push(child);
            }
        }
    }
}

internal sealed record SurfaceModel(ShaderIndex Index, int SpliceOffset);

/// <summary>Which kind of shader file a document is.</summary>
public enum ShaderFileKind
{
    /// <summary>A .shader file: the ShaderDef wrapper around GLSLPROGRAM blocks.</summary>
    Shader,
    /// <summary>A .glsl include or program body.</summary>
    Glsl,
    /// <summary>A .compute file: GLSL with #pragma kernel entry points.</summary>
    Compute,
}

/// <summary>Graphite shaders for the code editor: highlighting, folding, completion, hover, navigation and compiler diagnostics.</summary>
public sealed class ShaderLanguage : CodeLanguage
{
    public ShaderWorkspace Workspace { get; }
    public ShaderFileKind Kind { get; }

    /// <summary>Path of the file the next session edits, used to resolve its relative includes.</summary>
    public string? DocumentPath { get; set; }

    public ShaderLanguage(ShaderWorkspace workspace, ShaderFileKind kind, string? documentPath = null)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Kind = kind;
        DocumentPath = documentPath;
    }

    /// <summary>The kind for a file extension, or null when it is not a shader file.</summary>
    public static ShaderFileKind? KindFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".shader" => ShaderFileKind.Shader,
        ".glsl" or ".hlsl" or ".vert" or ".frag" => ShaderFileKind.Glsl,
        ".compute" => ShaderFileKind.Compute,
        _ => null,
    };

    public override string Name => Kind switch { ShaderFileKind.Shader => "Graphite Shader", ShaderFileKind.Compute => "Compute", _ => "GLSL" };
    public override ICodeLexer? Lexer => Kind == ShaderFileKind.Shader ? ShaderLexer.ShaderFile : ShaderLexer.GlslFile;
    public override string? LineComment => "//";
    public override string? BlockCommentStart => "/*";
    public override string? BlockCommentEnd => "*/";

    public override bool IsCompletionTrigger(char c) => c is '.' or '"' or '#' or '<' or '[';
    public override bool IsSignatureHelpTrigger(char c) => c is '(' or ',';
    public override bool IsFormatOnTypeTrigger(char c) => c is ';' or '}';

    public override List<CodeFoldRange> ComputeFolds(string[] lines, System.Threading.CancellationToken ct)
    {
        var folds = CodeFolding.FromBraces(lines, Lexer, ct);
        int open = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.EndsWith("GLSLPROGRAM", StringComparison.Ordinal)) open = i;
            else if (t.StartsWith("ENDGLSL", StringComparison.Ordinal) && open >= 0 && i > open)
            {
                folds.Add(new CodeFoldRange(open, i, "GLSLPROGRAM ... ENDGLSL"));
                open = -1;
            }
        }
        folds.Sort((a, b) => a.StartLine != b.StartLine ? a.StartLine.CompareTo(b.StartLine) : b.EndLine.CompareTo(a.EndLine));
        return folds;
    }

    public override CodeLanguageSession? CreateSession(CodeDocument document)
        => new ShaderSession(Workspace, Kind, DocumentPath, document.GetText("\n"));
}
