using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

using Prowl.Editor.AssetsDatabase;
using Prowl.Runtime;

namespace Prowl.Editor.Scripting;

/// <summary>
/// Generates .csproj files for user scripts and compiles them via `dotnet build`.
/// Produces two assemblies: Game (runtime scripts) and Editor (editor-only scripts).
/// </summary>
public static class ScriptCompiler
{
    public struct CompileResult
    {
        public bool Success;
        public string Output;
        public string Errors;
    }

    /// <summary>
    /// Generate .csproj files and compile both assemblies.
    /// Game assembly compiles first; Editor assembly depends on it.
    /// </summary>
    public static CompileResult CompileAll(Project project)
    {
        var (gameScripts, editorScripts) = ClassifyScripts(project);

        if (gameScripts.Count == 0 && editorScripts.Count == 0)
            return new CompileResult { Success = true, Output = "No scripts to compile." };

        // Generate .csproj files
        GenerateGameCsproj(project, gameScripts);
        GenerateEditorCsproj(project, editorScripts);

        var output = new StringBuilder();
        var errors = new StringBuilder();

        // Compile Game assembly first. `-t:Rebuild` forces a full recompile even when
        // MSBuild thinks inputs are unchanged avoids the stale-DLL bug where users
        // edit a script, see "compilation successful", restart, and the old code runs.
        // Incremental build can miss changes when the engine's Prowl.Runtime.dll is
        // updated but user sources weren't touched (cache references old symbols).
        if (gameScripts.Count > 0)
        {
            Runtime.Debug.Log($"[ScriptCompiler] Compiling {project.Name}.Game ({gameScripts.Count} scripts)...");
            var gameResult = RunDotnetCommand($"build \"{project.GameCsprojPath}\" --configuration Release -t:Rebuild", project.RootPath);
            output.AppendLine(gameResult.stdout);
            if (!string.IsNullOrEmpty(gameResult.stderr))
                errors.AppendLine(gameResult.stderr);

            if (gameResult.exitCode != 0)
            {
                Runtime.Debug.LogError($"[ScriptCompiler] Game assembly compilation failed.");
                LogBuildOutput(gameResult.stdout, gameResult.stderr);
                return new CompileResult { Success = false, Output = output.ToString(), Errors = errors.ToString() };
            }
            Runtime.Debug.Log($"[ScriptCompiler] Game assembly compiled successfully.");
        }

        // Compile Editor assembly (depends on Game).
        // BuildProjectReferences=false avoids redundantly rebuilding Game, which we
        // already built above and which -t:Rebuild would otherwise force again.
        if (editorScripts.Count > 0)
        {
            Runtime.Debug.Log($"[ScriptCompiler] Compiling {project.Name}.Editor ({editorScripts.Count} scripts)...");
            var editorResult = RunDotnetCommand($"build \"{project.EditorCsprojPath}\" --configuration Release -t:Rebuild -p:BuildProjectReferences=false", project.RootPath);
            output.AppendLine(editorResult.stdout);
            if (!string.IsNullOrEmpty(editorResult.stderr))
                errors.AppendLine(editorResult.stderr);

            if (editorResult.exitCode != 0)
            {
                Runtime.Debug.LogError($"[ScriptCompiler] Editor assembly compilation failed.");
                LogBuildOutput(editorResult.stdout, editorResult.stderr);
                return new CompileResult { Success = false, Output = output.ToString(), Errors = errors.ToString() };
            }
            Runtime.Debug.Log($"[ScriptCompiler] Editor assembly compiled successfully.");
        }

        return new CompileResult { Success = true, Output = output.ToString(), Errors = errors.ToString() };
    }

    /// <summary>Classify .cs files into game scripts and editor scripts.</summary>
    internal static (List<string> game, List<string> editor) ClassifyScripts(Project project)
    {
        var game = new List<string>();
        var editor = new List<string>();

        if (!Directory.Exists(project.AssetsPath)) return (game, editor);

        foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(project.AssetsPath, file);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            bool isEditor = segments.Any(s => s.Equals("Editor", StringComparison.OrdinalIgnoreCase));

            if (isEditor)
                editor.Add(file);
            else
                game.Add(file);
        }

        return (game, editor);
    }

    internal static string GetVersionDefine()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.1";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        // Convert "0.0.1" to "PROWL_0_0_1"
        return "PROWL_" + version.Replace('.', '_');
    }

    private static void GenerateGameCsproj(Project project, List<string> scripts)
    {
        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string runtimeDll = Path.Combine(engineDir, "Prowl.Runtime.dll");
        string editorDll = Path.Combine(engineDir, "Prowl.Editor.dll");
        string outputDir = Path.GetRelativePath(project.RootPath, project.ScriptAssemblyPath);
        string versionDefine = GetVersionDefine();

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine($"    <OutputPath>{outputDir}</OutputPath>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
        sb.AppendLine($"    <AssemblyName>{project.Name}.Game</AssemblyName>");
        sb.AppendLine($"    <DefineConstants>PROWL;PROWL_EDITOR;{versionDefine}</DefineConstants>");
        sb.AppendLine("  </PropertyGroup>");

        // References Game assembly can reference Editor when compiling in-editor (PROWL_EDITOR)
        // Private=true forces MSBuild to use HintPath instead of probing local directories
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Reference Include=\"Prowl.Runtime\">");
        sb.AppendLine($"      <HintPath>{runtimeDll}</HintPath>");
        sb.AppendLine("      <Private>true</Private>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine($"    <Reference Include=\"Prowl.Editor\">");
        sb.AppendLine($"      <HintPath>{editorDll}</HintPath>");
        sb.AppendLine("      <Private>true</Private>");
        sb.AppendLine("    </Reference>");

        // Add transitive dependencies from Prowl.Runtime
        foreach (var dll in Directory.EnumerateFiles(engineDir, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(dll);
            if (name == "Prowl.Runtime" || name == "Prowl.Editor" || name.StartsWith(project.Name)) continue;
            sb.AppendLine($"    <Reference Include=\"{name}\">");
            sb.AppendLine($"      <HintPath>{dll}</HintPath>");
            sb.AppendLine("      <Private>true</Private>");
            sb.AppendLine("    </Reference>");
        }
        sb.AppendLine("  </ItemGroup>");

        // NuGet packages (Game gets non-EditorOnly packages; Editor inherits these via ProjectReference)
        AppendNuGetPackages(sb, project, isEditorAssembly: false);

        // Compile items
        sb.AppendLine("  <ItemGroup>");
        foreach (var script in scripts)
        {
            string rel = Path.GetRelativePath(project.RootPath, script);
            sb.AppendLine($"    <Compile Include=\"{rel}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("</Project>");

        File.WriteAllText(project.GameCsprojPath, sb.ToString());
    }

    private static void GenerateEditorCsproj(Project project, List<string> scripts)
    {
        if (scripts.Count == 0) return;

        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string runtimeDll = Path.Combine(engineDir, "Prowl.Runtime.dll");
        string editorDll = Path.Combine(engineDir, "Prowl.Editor.dll");
        string gameCsprojRel = Path.GetFileName(project.GameCsprojPath);
        string outputDir = Path.GetRelativePath(project.RootPath, project.ScriptAssemblyPath);

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine($"    <OutputPath>{outputDir}</OutputPath>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
        sb.AppendLine($"    <AssemblyName>{project.Name}.Editor</AssemblyName>");
        sb.AppendLine($"    <DefineConstants>PROWL;PROWL_EDITOR;{GetVersionDefine()}</DefineConstants>");
        sb.AppendLine("  </PropertyGroup>");

        // References. Game is referenced via ProjectReference so its <PackageReference>s flow
        // here transitively (so the user only marks a package "Editor Only" when they want to
        // hide it from Game). Private=false avoids copying Game.dll on top of itself, since
        // both csprojs share the same OutputPath.
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Reference Include=\"Prowl.Runtime\">");
        sb.AppendLine($"      <HintPath>{runtimeDll}</HintPath>");
        sb.AppendLine("      <Private>true</Private>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine($"    <Reference Include=\"Prowl.Editor\">");
        sb.AppendLine($"      <HintPath>{editorDll}</HintPath>");
        sb.AppendLine("      <Private>true</Private>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine($"    <ProjectReference Include=\"{gameCsprojRel}\">");
        sb.AppendLine("      <Private>false</Private>");
        sb.AppendLine("    </ProjectReference>");

        // Transitive dependencies
        foreach (var dll in Directory.EnumerateFiles(engineDir, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(dll);
            if (name == "Prowl.Runtime" || name == "Prowl.Editor" || name.StartsWith(project.Name)) continue;
            sb.AppendLine($"    <Reference Include=\"{name}\">");
            sb.AppendLine($"      <HintPath>{dll}</HintPath>");
            sb.AppendLine("      <Private>true</Private>");
            sb.AppendLine("    </Reference>");
        }
        sb.AppendLine("  </ItemGroup>");

        // NuGet packages (Editor csproj only emits packages flagged EditorOnly; the rest flow
        // in transitively from the Game ProjectReference).
        AppendNuGetPackages(sb, project, isEditorAssembly: true);

        // Compile items
        sb.AppendLine("  <ItemGroup>");
        foreach (var script in scripts)
        {
            string rel = Path.GetRelativePath(project.RootPath, script);
            sb.AppendLine($"    <Compile Include=\"{rel}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("</Project>");

        File.WriteAllText(project.EditorCsprojPath, sb.ToString());
    }

    internal static void AppendNuGetPackages(StringBuilder sb, Project project, bool isEditorAssembly)
    {
        // Read packages from the PackageSettings (via ProjectSettingsRegistry)
        try
        {
            var pkgSettings = ProjectSettingsRegistry.Get<PackageSettings>();
            if (pkgSettings.Packages.Count == 0) return;

            // Editor csproj only declares EditorOnly packages; non-EditorOnly packages live
            // on the Game csproj and flow into Editor via the ProjectReference.
            var filtered = pkgSettings.Packages.Where(p => p.EditorOnly == isEditorAssembly).ToList();
            if (filtered.Count == 0) return;

            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in filtered)
                sb.AppendLine($"    <PackageReference Include=\"{pkg.Name}\" Version=\"{pkg.Version}\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        catch
        {
            // Fallback: no packages
        }
    }

    private class PackageRef
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
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

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(60_000); // 60s timeout

            return (process.ExitCode, stdout, stderr);
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
