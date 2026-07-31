using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

using Prowl.Analyzers;
using Prowl.Ember.Analyzers;

using CompilationUnit = Prowl.Editor.Projects.Scripting.ScriptCompiler.CompilationUnit;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Compiles a user assembly in process with Roslyn instead of shelling out to `dotnet build`. Emits to
/// a byte array with an embedded PDB (so stack traces and breakpoints work) which the caller writes to
/// disk and loads. References come from the running process (framework and engine assemblies via the
/// TPA list), plugins, peer user assemblies compiled earlier this run, and resolved NuGet packages.
///
/// Compilation is incremental across calls: each source file is content hashed, unchanged files reuse
/// their exact <see cref="SyntaxTree"/> instance, and the prior <see cref="CSharpCompilation"/> is
/// reused with only the changed trees swapped so Roslyn keeps its cached binding. A unit whose files
/// and references are all unchanged since its last successful compile is skipped entirely.
/// </summary>
internal static class RoslynScriptBackend
{
    public struct CompileOutcome
    {
        public byte[]? Image;                       // emitted assembly, or null on failure
        public IReadOnlyList<Diagnostic> Diagnostics;
        public MetadataReference? SelfReference;    // this unit's image as a reference, for dependents
        public bool Recompiled;                     // false when the unit was unchanged and skipped
    }

    private sealed class UnitState
    {
        public Dictionary<string, (ulong Hash, SyntaxTree Tree)> Trees = new(StringComparer.OrdinalIgnoreCase);
        public CSharpCompilation? Compilation;
        public MetadataReference[] References = Array.Empty<MetadataReference>();
        public bool AllowUnsafe;

        // Snapshot of the last SUCCESSFUL compile, used to decide the whole unit skip.
        public Dictionary<string, ulong> SuccessHashes = new(StringComparer.OrdinalIgnoreCase);
        public MetadataReference[] SuccessReferences = Array.Empty<MetadataReference>();
        public byte[]? Image;
        public MetadataReference? SelfReference;
        public IReadOnlyList<Diagnostic> Diagnostics = Array.Empty<Diagnostic>();
    }

    private static readonly Dictionary<string, MetadataReference> s_refCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, UnitState> s_states = new();
    private static readonly object s_lock = new();

    private static readonly CSharpParseOptions s_parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
        .WithPreprocessorSymbols("PROWL", "PROWL_EDITOR", ScriptCompiler.GetVersionDefine());

    /// <summary>Drops all cached compilation state (call when a project closes).</summary>
    public static void Reset()
    {
        lock (s_lock) s_states.Clear();
    }

    /// <summary>
    /// Compile one unit. <paramref name="stateKey"/> uniquely identifies the unit across projects,
    /// <paramref name="peerRefs"/> holds references to units compiled earlier this run, and
    /// <paramref name="nugetDllPaths"/> is the resolved package set.
    /// </summary>
    public static CompileOutcome Compile(
        string stateKey,
        CompilationUnit unit,
        IReadOnlyDictionary<string, MetadataReference> peerRefs,
        IReadOnlyList<string> nugetDllPaths)
    {
        lock (s_lock)
        {
            // 1. Read + hash every source file.
            var currentHashes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            var fileBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in unit.Scripts)
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file); }
                catch { bytes = Array.Empty<byte>(); }
                fileBytes[file] = bytes;
                currentHashes[file] = Fnv1a64(bytes);
            }

            var references = BuildReferences(unit, peerRefs, nugetDllPaths);
            s_states.TryGetValue(stateKey, out var state);

            // 2. Whole unit skip: unchanged source + references since the last successful compile.
            if (state?.Image != null
                && state.AllowUnsafe == unit.AllowUnsafe
                && SameHashes(state.SuccessHashes, currentHashes)
                && SameReferences(state.SuccessReferences, references))
            {
                return new CompileOutcome
                {
                    Image = state.Image,
                    Diagnostics = state.Diagnostics,
                    SelfReference = state.SelfReference,
                    Recompiled = false
                };
            }

            // 3. Build the current tree set, reusing unchanged trees by content hash.
            var newTrees = new Dictionary<string, (ulong Hash, SyntaxTree Tree)>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in unit.Scripts)
            {
                ulong hash = currentHashes[file];
                if (state != null && state.Trees.TryGetValue(file, out var prev) && prev.Hash == hash)
                {
                    newTrees[file] = prev; // unchanged, reuse the exact tree
                }
                else
                {
                    var text = SourceText.From(fileBytes[file], fileBytes[file].Length, Encoding.UTF8);
                    newTrees[file] = (hash, CSharpSyntaxTree.ParseText(text, s_parseOptions, path: file));
                }
            }

            // 4. Reuse the prior compilation, swapping only changed trees; otherwise create fresh.
            CSharpCompilation compilation;
            if (state?.Compilation != null && state.AllowUnsafe == unit.AllowUnsafe)
            {
                compilation = state.Compilation;

                foreach (var (path, entry) in state.Trees)
                    if (!newTrees.TryGetValue(path, out var ne) || !ReferenceEquals(ne.Tree, entry.Tree))
                        compilation = compilation.RemoveSyntaxTrees(entry.Tree);

                foreach (var (path, entry) in newTrees)
                    if (!state.Trees.TryGetValue(path, out var oe) || !ReferenceEquals(oe.Tree, entry.Tree))
                        compilation = compilation.AddSyntaxTrees(entry.Tree);

                if (!SameReferences(state.References, references))
                    compilation = compilation.WithReferences(references);
            }
            else
            {
                compilation = CSharpCompilation.Create(unit.Name, newTrees.Values.Select(t => t.Tree), references, BuildOptions(unit));
            }

            // 5. Emit with an embedded PDB.
            using var peStream = new MemoryStream();
            EmitResult result = compilation.Emit(peStream,
                options: new EmitOptions().WithDebugInformationFormat(DebugInformationFormat.Embedded));

            var diagnostics = result.Diagnostics
                .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                .ToList();
            diagnostics.AddRange(RunScriptAnalyzers(compilation));
            byte[]? image = result.Success ? peStream.ToArray() : null;

            // 6. Update state. Compilation/trees/refs reflect what we just built; the success snapshot
            //    only advances when the emit actually succeeded (so a repeat of failing source does not
            //    get skipped and silently return the last good image).
            var refArray = references.ToArray();
            state ??= new UnitState();
            state.Trees = newTrees;
            state.Compilation = compilation;
            state.References = refArray;
            state.AllowUnsafe = unit.AllowUnsafe;
            state.Diagnostics = diagnostics;

            if (image != null)
            {
                state.SuccessHashes = currentHashes;
                state.SuccessReferences = refArray;
                state.Image = image;
                state.SelfReference = MetadataReference.CreateFromImage(image);
            }
            s_states[stateKey] = state;

            return new CompileOutcome
            {
                Image = image,
                Diagnostics = diagnostics,
                SelfReference = image != null ? state.SelfReference : null,
                Recompiled = true
            };
        }
    }

    private static readonly ImmutableArray<DiagnosticAnalyzer> s_scriptAnalyzers =
        ImmutableArray.Create<DiagnosticAnalyzer>(new ReloadDiagnosticAnalyzer(), new EngineObjectNullAnalyzer());

    /// <summary>Run the Prowl script-safety analyzers over the compilation and surface their diagnostics.</summary>
    private static IEnumerable<Diagnostic> RunScriptAnalyzers(CSharpCompilation compilation)
    {
        try
        {
            return compilation.WithAnalyzers(s_scriptAnalyzers)
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
                .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[ScriptCompiler] Hot reload analyzer failed: {ex.Message}");
            return Array.Empty<Diagnostic>();
        }
    }

    private static CSharpCompilationOptions BuildOptions(CompilationUnit unit) =>
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithPlatform(Platform.AnyCpu)
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithAllowUnsafe(unit.AllowUnsafe)
            .WithNullableContextOptions(NullableContextOptions.Enable)
            .WithConcurrentBuild(true)
            // Referencing runtime implementation assemblies (from the TPA list) instead of ref
            // assemblies produces harmless version mismatch noise. Silence it.
            .WithSpecificDiagnosticOptions(new[]
            {
                new KeyValuePair<string, ReportDiagnostic>("CS1701", ReportDiagnostic.Suppress),
                new KeyValuePair<string, ReportDiagnostic>("CS1702", ReportDiagnostic.Suppress),
                new KeyValuePair<string, ReportDiagnostic>("CS1705", ReportDiagnostic.Suppress),
            });

    private static List<MetadataReference> BuildReferences(
        CompilationUnit unit,
        IReadOnlyDictionary<string, MetadataReference> peerRefs,
        IReadOnlyList<string> nugetDllPaths)
    {
        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // by simple assembly name
        var refs = new List<MetadataReference>();

        // Framework + engine assemblies: everything the editor process itself has loaded. An asmdef
        // that opts out of engine references excludes the assemblies living in the engine folder.
        string tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (unit.NoEngineReferences && IsUnderDirectory(path, engineDir)) continue;
            AddFileReference(refs, seen, Path.GetFileNameWithoutExtension(path), path);
        }

        // User scripts resolve [ReloadIgnore], [ReloadInitializer] and the reload interfaces out of
        // Prowl.Ember.Contracts. It reaches the editor as a lazily loaded transitive dependency, so it may not be
        // in the trusted-platform-assemblies list above and has to be referenced explicitly.
        if (!unit.NoEngineReferences)
        {
            string contractsPath = typeof(Prowl.Ember.IReloadAware).Assembly.Location;
            AddFileReference(refs, seen, Path.GetFileNameWithoutExtension(contractsPath), contractsPath);
        }

        // Managed plugins.
        foreach (var pluginPath in unit.ManagedPluginPaths)
            AddFileReference(refs, seen, Path.GetFileNameWithoutExtension(pluginPath), pluginPath);

        // Peer user assemblies compiled earlier this run (in dependency order), referenced in memory.
        foreach (var refName in unit.AssemblyReferences)
            if (peerRefs.TryGetValue(refName, out var mref) && seen.Add(refName))
                refs.Add(mref);

        // NuGet package assemblies.
        foreach (var dll in nugetDllPaths)
            AddFileReference(refs, seen, Path.GetFileNameWithoutExtension(dll), dll);

        return refs;
    }

    private static void AddFileReference(List<MetadataReference> refs, HashSet<string> seen, string name, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (!seen.Add(name)) return; // first definition of a given assembly name wins

        if (!s_refCache.TryGetValue(path, out var mref))
        {
            try { mref = MetadataReference.CreateFromFile(path); }
            catch { return; } // native library or unreadable file
            s_refCache[path] = mref;
        }
        refs.Add(mref);
    }

    private static bool SameHashes(Dictionary<string, ulong> a, Dictionary<string, ulong> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, hash) in b)
            if (!a.TryGetValue(key, out var h) || h != hash) return false;
        return true;
    }

    private static bool SameReferences(MetadataReference[] a, List<MetadataReference> b)
    {
        if (a.Length != b.Count) return false;
        for (int i = 0; i < b.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    private static ulong Fnv1a64(byte[] data)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static bool IsUnderDirectory(string path, string dir)
    {
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

/// <summary>
/// Resolves the NuGet package assemblies a unit compiles against. Packages come from the project's
/// Directory.Build.props, which the generated .csproj auto imports, so resolution runs `dotnet restore`
/// (only when the props file has changed) and reads the resulting <c>project.assets.json</c>. Results
/// are cached to disk keyed off the props file's timestamp so the common inner loop never restores.
/// </summary>
internal static class NuGetReferenceResolver
{
    /// <summary>Per unit list of absolute package assembly paths. Empty when the project has no packages.</summary>
    public static Dictionary<string, List<string>> Resolve(Project project, List<CompilationUnit> units)
    {
        var empty = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!ScriptCompiler.ProjectDeclaresPackages(project))
            return empty;

        string propsPath = Path.Combine(project.RootPath, "Directory.Build.props");
        string cachePath = Path.Combine(project.ScriptAssemblyPath, "nuget-refs.json");
        DateTime propsTime = File.Exists(propsPath) ? File.GetLastWriteTimeUtc(propsPath) : DateTime.MinValue;

        // Fresh cache: reuse it, no restore.
        if (File.Exists(cachePath) && File.GetLastWriteTimeUtc(cachePath) >= propsTime)
        {
            var cached = LoadCache(cachePath);
            if (cached != null) return cached;
        }

        // Refresh: restore each unit and parse its assets immediately (the units share one
        // obj/project.assets.json, so each is parsed before the next restore overwrites it).
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string assetsPath = Path.Combine(project.RootPath, "obj", "project.assets.json");

        foreach (var unit in units)
        {
            Runtime.Debug.Log($"[ScriptCompiler] Restoring NuGet packages for {unit.Name}...");
            var (exit, stdout, stderr) = ScriptCompiler.RunDotnetCommand($"restore \"{unit.CsprojPath}\"", project.RootPath);
            if (exit != 0)
            {
                Runtime.Debug.LogError($"[ScriptCompiler] Package restore failed for {unit.Name}.");
                ScriptCompiler.LogBuildOutput(stdout, stderr);
                result[unit.Name] = new();
                continue;
            }
            result[unit.Name] = ParseAssetsFile(assetsPath);
        }

        SaveCache(cachePath, result);
        return result;
    }

    private static List<string> ParseAssetsFile(string assetsPath)
    {
        var result = new List<string>();
        if (!File.Exists(assetsPath)) return result;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
            var root = doc.RootElement;

            var folders = new List<string>();
            if (root.TryGetProperty("packageFolders", out var pf))
                foreach (var f in pf.EnumerateObject())
                    folders.Add(f.Name);

            if (!root.TryGetProperty("targets", out var targets)) return result;

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Name.Contains('/')) continue; // skip runtime specific (RID) targets

                foreach (var pkg in target.Value.EnumerateObject())
                {
                    if (!pkg.Value.TryGetProperty("compile", out var compile)) continue;

                    int slash = pkg.Name.IndexOf('/');
                    if (slash < 0) continue;
                    string id = pkg.Name[..slash];
                    string version = pkg.Name[(slash + 1)..];

                    foreach (var item in compile.EnumerateObject())
                    {
                        string rel = item.Name;
                        if (rel.EndsWith("_._", StringComparison.Ordinal)) continue; // empty placeholder

                        string relNative = rel.Replace('/', Path.DirectorySeparatorChar);
                        foreach (var folder in folders)
                        {
                            string abs = Path.Combine(folder, id.ToLowerInvariant(), version, relNative);
                            if (File.Exists(abs)) { result.Add(abs); break; }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[ScriptCompiler] Failed to read package references: {ex.Message}");
        }

        return result;
    }

    private static Dictionary<string, List<string>>? LoadCache(string path)
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path));
            return data == null ? null : new Dictionary<string, List<string>>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch { return null; }
    }

    private static void SaveCache(string path, Dictionary<string, List<string>> data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[ScriptCompiler] Failed to cache package references: {ex.Message}");
        }
    }
}
