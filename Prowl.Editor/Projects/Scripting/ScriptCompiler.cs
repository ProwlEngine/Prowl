using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Prowl.Editor.Projects.Settings;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Generates .csproj files for user scripts and compiles them via `dotnet build`.
///
/// User code is split into one assembly per <see cref="AssemblyDefinition"/> (.asmdef), plus two
/// default assemblies for scripts not covered by any asmdef: <c>{Project}.Game</c> (runtime) and
/// <c>{Project}.Editor</c> (editor-only). Managed plugins under <c>Plugins/</c> folders are
/// referenced automatically (or explicitly via an asmdef's precompiled references).
/// </summary>
public static class ScriptCompiler
{
    public struct CompileResult
    {
        public bool Success;
        public string Output;
        public string Errors;
    }

    /// <summary>A single user assembly to be generated and compiled.</summary>
    internal sealed class CompilationUnit
    {
        public required string Name;
        public bool IsEditorOnly;   // editor-side: gets editor-only packages/plugins, never shipped in a build
        public bool AllowUnsafe = true;
        public bool NoEngineReferences;
        public AsmDefFile? Source;  // null for the default Game/Editor assemblies
        public readonly List<string> Scripts = new();
        public readonly List<string> AssemblyReferences = new();   // other unit names
        public readonly List<string> ManagedPluginPaths = new();   // absolute .dll paths

        public string CsprojPath = "";
        public string OutputDllPath = "";
    }

    // ================================================================
    //  Public entry point
    // ================================================================

    /// <summary>Generate .csproj files for all user assemblies and compile them in dependency order.</summary>
    public static CompileResult CompileAll(Project project)
    {
        var (units, error) = BuildPlan(project);
        if (error != null)
            return new CompileResult { Success = false, Errors = error };

        var compileUnits = units.Where(u => u.Scripts.Count > 0).ToList();
        if (compileUnits.Count == 0)
            return new CompileResult { Success = true, Output = "No scripts to compile." };

        foreach (var unit in compileUnits)
            GenerateCsproj(project, unit, units);

        var output = new StringBuilder();
        var errors = new StringBuilder();

        // Compile in dependency order so each unit's referenced DLLs already exist on disk.
        // `-t:Rebuild` forces a full recompile even when MSBuild thinks inputs are unchanged,
        // avoiding the stale-DLL bug where edits appear to compile but the old code still runs.
        foreach (var unit in compileUnits)
        {
            Runtime.Debug.Log($"[ScriptCompiler] Compiling {unit.Name} ({unit.Scripts.Count} scripts)...");
            var result = RunDotnetCommand($"build \"{unit.CsprojPath}\" --configuration Release -t:Rebuild", project.RootPath);
            output.AppendLine(result.stdout);
            if (!string.IsNullOrEmpty(result.stderr))
                errors.AppendLine(result.stderr);

            if (result.exitCode != 0)
            {
                Runtime.Debug.LogError($"[ScriptCompiler] {unit.Name} compilation failed.");
                LogBuildOutput(result.stdout, result.stderr);
                return new CompileResult { Success = false, Output = output.ToString(), Errors = errors.ToString() };
            }
            Runtime.Debug.Log($"[ScriptCompiler] {unit.Name} compiled successfully.");
        }

        return new CompileResult { Success = true, Output = output.ToString(), Errors = errors.ToString() };
    }

    /// <summary>
    /// Output DLL paths for every user assembly that has scripts, in dependency order.
    /// Used by the assembly manager to load them all into the editor.
    /// </summary>
    public static List<string> GetEditorAssemblyPaths(Project project)
    {
        var (units, _) = BuildPlan(project);
        return units.Where(u => u.Scripts.Count > 0).Select(u => u.OutputDllPath).ToList();
    }

    /// <summary>A user assembly destined for a player build.</summary>
    public readonly record struct BuildAssembly(string Name, string DllPath);

    /// <summary>
    /// Game-side user assemblies (not editor-only, included for the target platform) in dependency
    /// order. These are copied next to the player and loaded at startup.
    /// </summary>
    public static List<BuildAssembly> GetBuildAssemblies(Project project, string targetPlatform)
    {
        var (units, _) = BuildPlan(project);
        var result = new List<BuildAssembly>();
        foreach (var unit in units)
        {
            if (unit.IsEditorOnly || unit.Scripts.Count == 0) continue;
            if (unit.Source != null && !unit.Source.Definition.IncludedFor(targetPlatform)) continue;
            result.Add(new BuildAssembly(unit.Name, unit.OutputDllPath));
        }
        return result;
    }

    // ================================================================
    //  Build plan
    // ================================================================

    private static (List<CompilationUnit> units, string? error) BuildPlan(Project project)
    {
        var asmdefs = AssemblyDefinitionDatabase.LoadAll(project);

        // Reject duplicate assembly names early - they would clobber each other's output.
        var dupes = asmdefs.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (dupes.Count > 0)
            return (new(), $"Duplicate assembly definition name(s): {string.Join(", ", dupes.Select(d => d.Key))}");

        // Reject asmdef names reserved for the default assemblies - they would clobber the defaults.
        var reserved = new[] { $"{project.Name}.Game", $"{project.Name}.Editor" };
        var clashing = asmdefs.Where(a => reserved.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
            .Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (clashing.Count > 0)
            return (new(), $"Assembly definition name(s) reserved for the default assemblies: {string.Join(", ", clashing)}");

        string defaultDefines = $"PROWL;PROWL_EDITOR;{GetVersionDefine()}";
        var activeDefines = new HashSet<string>(defaultDefines.Split(';'), StringComparer.OrdinalIgnoreCase);

        var units = new List<CompilationUnit>();
        var byName = new Dictionary<string, CompilationUnit>(StringComparer.OrdinalIgnoreCase);

        // Default assemblies (scripts not owned by any asmdef).
        var defaultGame = new CompilationUnit { Name = $"{project.Name}.Game", IsEditorOnly = false };
        var defaultEditor = new CompilationUnit { Name = $"{project.Name}.Editor", IsEditorOnly = true };
        units.Add(defaultGame);
        units.Add(defaultEditor);

        // Asmdef-defined assemblies (only those whose define constraints are satisfied).
        var asmdefUnits = new List<(AsmDefFile file, CompilationUnit unit)>();
        foreach (var file in asmdefs)
        {
            if (!EvaluateDefineConstraints(file.Definition.DefineConstraints, activeDefines))
                continue;

            var unit = new CompilationUnit
            {
                Name = file.Name,
                IsEditorOnly = file.Definition.IsEditorOnly,
                AllowUnsafe = file.Definition.AllowUnsafeCode,
                NoEngineReferences = file.Definition.NoEngineReferences,
                Source = file,
            };
            units.Add(unit);
            asmdefUnits.Add((file, unit));
        }

        foreach (var unit in units)
            byName[unit.Name] = unit;

        // Classify scripts into their owning assembly.
        if (Directory.Exists(project.AssetsPath))
        {
            foreach (var script in Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
            {
                var owner = AssemblyDefinitionDatabase.FindOwner(script, asmdefs);
                if (owner != null)
                {
                    // Owned by an asmdef. If that asmdef was excluded by define constraints it has no
                    // unit, so the script is simply not compiled (it must NOT leak into the defaults).
                    if (byName.TryGetValue(owner.Name, out var ownerUnit))
                        ownerUnit.Scripts.Add(script);
                }
                else
                {
                    (IsEditorPath(project, script) ? defaultEditor : defaultGame).Scripts.Add(script);
                }
            }
        }

        // Resolve plugin and assembly references.
        var plugins = PluginScanner.ScanAll(project);
        var autoManaged = plugins.Where(p => p.IsManaged && p.AutoReferenced).ToList();
        // TryAdd (not ToDictionary) so duplicate plugin file names don't throw; first one wins.
        var managedByFile = new Dictionary<string, PluginInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plugins.Where(p => p.IsManaged))
            managedByFile.TryAdd(p.FileName, p);

        // Set of unit names that actually produce a DLL (have scripts) - only these are referenceable.
        var producing = new HashSet<string>(units.Where(u => u.Scripts.Count > 0).Select(u => u.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var unit in units)
        {
            // Managed plugin references.
            IEnumerable<PluginInfo> pluginRefs;
            if (unit.Source is { Definition.OverrideReferences: true } src)
            {
                pluginRefs = src.Definition.PrecompiledReferences
                    .Select(name => managedByFile.GetValueOrDefault(name))
                    .Where(p => p != null)!;
            }
            else
            {
                // Game assemblies may only reference non-editor-only plugins (build separation);
                // editor-only assemblies can reference everything.
                pluginRefs = autoManaged.Where(p => unit.IsEditorOnly || !p.EditorOnly);
            }
            foreach (var p in pluginRefs)
                unit.ManagedPluginPaths.Add(p!.AbsolutePath);

            // Assembly references.
            if (unit.Source != null)
            {
                foreach (var refName in unit.Source.Definition.References)
                {
                    if (refName.Equals(unit.Name, StringComparison.OrdinalIgnoreCase))
                        continue; // ignore self-reference
                    if (producing.Contains(refName))
                        unit.AssemblyReferences.Add(refName);
                    else if (byName.ContainsKey(refName))
                        { /* referenced asmdef has no scripts -> nothing to link */ }
                    else
                        Runtime.Debug.LogWarning($"[ScriptCompiler] {unit.Name} references unknown assembly '{refName}'.");
                }
            }
        }

        // Default assemblies auto-reference auto-referenced asmdef assemblies, so loose scripts can
        // use asmdef code without an explicit reference.
        foreach (var (file, unit) in asmdefUnits)
        {
            if (unit.Scripts.Count == 0 || !file.Definition.AutoReferenced) continue;

            // The default Game ships to every player platform, so it may only auto-reference an asmdef
            // that also ships everywhere - otherwise a build for an excluded platform would carry a
            // dangling reference. The default Editor never ships, so it can reference anything.
            bool shipsEverywhere = BuildPlatforms.Players.All(p => file.Definition.IncludedFor(p));
            if (!unit.IsEditorOnly && shipsEverywhere)
                defaultGame.AssemblyReferences.Add(unit.Name);
            defaultEditor.AssemblyReferences.Add(unit.Name);
        }
        if (defaultGame.Scripts.Count > 0)
            defaultEditor.AssemblyReferences.Add(defaultGame.Name);

        // Fill in generated file paths.
        foreach (var unit in units)
        {
            unit.CsprojPath = Path.Combine(project.RootPath, $"{unit.Name}.csproj");
            unit.OutputDllPath = Path.Combine(project.ScriptAssemblyPath, $"{unit.Name}.dll");
        }

        // Order by dependency (referenced assemblies build first).
        var (ordered, cycle) = TopologicalSort(units, byName);
        if (cycle != null)
            return (new(), $"Cyclic assembly references detected: {cycle}");

        return (ordered, null);
    }

    private static (List<CompilationUnit> ordered, string? cycle) TopologicalSort(
        List<CompilationUnit> units, Dictionary<string, CompilationUnit> byName)
    {
        var ordered = new List<CompilationUnit>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=unseen,1=visiting,2=done
        string? cycle = null;

        void Visit(CompilationUnit u, List<string> stack)
        {
            if (cycle != null) return;
            state.TryGetValue(u.Name, out int s);
            if (s == 2) return;
            if (s == 1)
            {
                cycle = string.Join(" -> ", stack.SkipWhile(n => !n.Equals(u.Name, StringComparison.OrdinalIgnoreCase)).Append(u.Name));
                return;
            }

            state[u.Name] = 1;
            stack.Add(u.Name);
            foreach (var dep in u.AssemblyReferences)
                if (byName.TryGetValue(dep, out var depUnit))
                    Visit(depUnit, stack);
            stack.RemoveAt(stack.Count - 1);
            state[u.Name] = 2;
            ordered.Add(u);
        }

        foreach (var u in units)
            Visit(u, new List<string>());

        return (cycle == null ? ordered : new(), cycle);
    }

    // ================================================================
    //  csproj generation
    // ================================================================

    private static void GenerateCsproj(Project project, CompilationUnit unit, List<CompilationUnit> allUnits)
    {
        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string outputDir = Path.GetRelativePath(project.RootPath, project.ScriptAssemblyPath);

        // Only the user's own output assemblies must be excluded from the engine-reference sweep -
        // match them by exact name, never by a name prefix (a project named "Prowl" must still get
        // Prowl.Echo etc.).
        var unitNames = new HashSet<string>(allUnits.Select(u => u.Name), StringComparer.OrdinalIgnoreCase);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        // EnableDefaultItems=false stops MSBuild from auto-globbing DLLs sitting next to the
        // project; every reference is added explicitly below.
        sb.AppendLine("    <EnableDefaultItems>false</EnableDefaultItems>");
        sb.AppendLine($"    <AllowUnsafeBlocks>{(unit.AllowUnsafe ? "true" : "false")}</AllowUnsafeBlocks>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine($"    <OutputPath>{Xml(outputDir)}</OutputPath>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
        sb.AppendLine($"    <AssemblyName>{Xml(unit.Name)}</AssemblyName>");
        sb.AppendLine($"    <DefineConstants>PROWL;PROWL_EDITOR;{GetVersionDefine()}</DefineConstants>");
        sb.AppendLine("  </PropertyGroup>");

        sb.AppendLine("  <ItemGroup>");

        // Engine references (Private=true forces use of the HintPath instead of local probing).
        if (!unit.NoEngineReferences)
        {
            AppendReference(sb, emitted, "Prowl.Runtime", Path.Combine(engineDir, "Prowl.Runtime.dll"), copyLocal: true);
            AppendReference(sb, emitted, "Prowl.Editor", Path.Combine(engineDir, "Prowl.Editor.dll"), copyLocal: true);

            foreach (var dll in Directory.EnumerateFiles(engineDir, "*.dll"))
            {
                string name = Path.GetFileNameWithoutExtension(dll);
                if (name == "Prowl.Runtime" || name == "Prowl.Editor" || unitNames.Contains(name)) continue;
                AppendReference(sb, emitted, name, dll, copyLocal: true);
            }
        }

        // Managed plugin references.
        foreach (var pluginPath in unit.ManagedPluginPaths)
            AppendReference(sb, emitted, Path.GetFileNameWithoutExtension(pluginPath), pluginPath, copyLocal: true);

        // Other user assemblies (built earlier; share OutputPath so they must not be copied over).
        foreach (var refName in unit.AssemblyReferences)
        {
            string dll = Path.Combine(project.ScriptAssemblyPath, $"{refName}.dll");
            AppendReference(sb, emitted, refName, dll, copyLocal: false);
        }

        sb.AppendLine("  </ItemGroup>");

        // NuGet packages: non-editor packages flow to every assembly; editor-only packages only to
        // editor-side assemblies (preserving "any script may use any non-editor package").
        AppendNuGetPackages(sb, project, isEditorAssembly: false);
        if (unit.IsEditorOnly)
            AppendNuGetPackages(sb, project, isEditorAssembly: true);

        // Compile items.
        sb.AppendLine("  <ItemGroup>");
        foreach (var script in unit.Scripts)
            sb.AppendLine($"    <Compile Include=\"{Xml(Path.GetRelativePath(project.RootPath, script))}\" />");
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("</Project>");

        File.WriteAllText(unit.CsprojPath, sb.ToString());
    }

    /// <summary>Appends a Reference item, skipping duplicates (by Include name) and XML-escaping values.</summary>
    private static void AppendReference(StringBuilder sb, HashSet<string> emitted, string include, string hintPath, bool copyLocal)
    {
        if (!emitted.Add(include)) return; // a later reference with the same name (e.g. plugin vs engine DLL) is dropped
        sb.AppendLine($"    <Reference Include=\"{Xml(include)}\">");
        sb.AppendLine($"      <HintPath>{Xml(hintPath)}</HintPath>");
        sb.AppendLine($"      <Private>{(copyLocal ? "true" : "false")}</Private>");
        sb.AppendLine("    </Reference>");
    }

    /// <summary>XML-escapes a value for safe interpolation into a generated .csproj.</summary>
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    // ================================================================
    //  Helpers (kept stable for callers outside this class)
    // ================================================================

    /// <summary>Classify every .cs file into game vs editor scripts (by an "Editor" folder segment).</summary>
    internal static (List<string> game, List<string> editor) ClassifyScripts(Project project)
    {
        var game = new List<string>();
        var editor = new List<string>();
        if (!Directory.Exists(project.AssetsPath)) return (game, editor);

        foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
            (IsEditorPath(project, file) ? editor : game).Add(file);

        return (game, editor);
    }

    private static bool IsEditorPath(Project project, string absolutePath)
    {
        string relative = Path.GetRelativePath(project.AssetsPath, absolutePath);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => s.Equals("Editor", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Evaluates asmdef define constraints (supports "SYMBOL" and "!SYMBOL").</summary>
    private static bool EvaluateDefineConstraints(List<string> constraints, HashSet<string> defined)
    {
        foreach (var raw in constraints)
        {
            string c = raw.Trim();
            if (c.Length == 0) continue;
            if (c.StartsWith('!'))
            {
                if (defined.Contains(c[1..])) return false;
            }
            else if (!defined.Contains(c))
            {
                return false;
            }
        }
        return true;
    }

    internal static string GetVersionDefine()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        return "PROWL_" + version.Replace('.', '_').Replace('-', '_').ToUpperInvariant();
    }

    internal static void AppendNuGetPackages(StringBuilder sb, Project project, bool isEditorAssembly)
    {
        try
        {
            var pkgSettings = EditorRegistries.GetSettings<PackageSettings>();
            if (pkgSettings.Packages.Count == 0) return;

            var filtered = pkgSettings.Packages.Where(p => p.EditorOnly == isEditorAssembly).ToList();
            if (filtered.Count == 0) return;

            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in filtered)
                sb.AppendLine($"    <PackageReference Include=\"{Xml(pkg.Name)}\" Version=\"{Xml(pkg.Version)}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        catch
        {
            // Fallback: no packages
        }
    }

    internal static (int exitCode, string stdout, string stderr) RunDotnetCommand(string args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return (-1, "", "Failed to start dotnet process");

            // Read both streams concurrently: draining stdout then stderr sequentially deadlocks
            // if the child fills the other stream's pipe buffer first and blocks waiting on us.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            bool exited = process.WaitForExit(120_000); // 120s timeout (multiple assemblies may build)
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                process.WaitForExit();
            }

            Task.WaitAll(stdoutTask, stderrTask);

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", $"Failed to run dotnet build: {ex.Message}. Is the .NET SDK installed?");
        }
    }

    internal static void LogBuildOutput(string stdout, string stderr)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Contains(": error "))
                Runtime.Debug.LogError(trimmed);
            else if (trimmed.Contains(": warning "))
                Runtime.Debug.LogWarning(trimmed);
            else if (!string.IsNullOrWhiteSpace(trimmed))
                Runtime.Debug.Log(trimmed);
        }

        if (!string.IsNullOrEmpty(stderr))
            Runtime.Debug.LogError(stderr);
    }
}
