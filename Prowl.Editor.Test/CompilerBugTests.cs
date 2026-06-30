// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

using Prowl.Editor.Build;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Regression tests for bugs found by adversarial review of the multi-assembly compiler and plugin
/// build path. Each test pins behaviour that was previously wrong.
/// </summary>
public class CompilerBugTests : EditorTestHarness
{
    public CompilerBugTests()
    {
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
        ProjectSettingsRegistry.Get<PackageSettings>().Packages.Clear();
    }

    // BUG: an asmdef named like a default assembly silently clobbered it. Now a clear error.
    [Fact]
    public void AsmdefNamedLikeDefaultAssembly_FailsWithClearError()
    {
        WriteAsmDef("X", new AssemblyDefinition { Name = $"{Project.Name}.Game" });

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("reserved", result.Errors, StringComparison.OrdinalIgnoreCase);
    }

    // BUG: a platform-restricted asmdef was auto-referenced by the default Game (which ships to all
    // platforms), producing a dangling reference in builds for excluded platforms.
    [Fact]
    [Trait("Category", "Build")]
    public void PlatformRestrictedAsmdef_IsNotAutoReferencedByDefaultGame()
    {
        WriteAsmDef("Win", new AssemblyDefinition { Name = "WinOnly", IncludePlatforms = { BuildPlatforms.Windows } });
        WriteScript(Path.Combine("Win", "W.cs"), "namespace WinNs { public class W { } }");
        WriteScript("Loose.cs", "public class Loose { }");

        var result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, $"Compile failed:\n{result.Errors}");

        string gameProj = File.ReadAllText(Project.GameCsprojPath);
        Assert.DoesNotContain("Include=\"WinOnly\"", gameProj);
    }

    // BUG: managed plugins were copied to runtimes/{fileName}.dll, but the player resolves them by
    // assembly name. A plugin whose file name differs from its assembly name was unresolvable.
    [Fact]
    [Trait("Category", "Build")]
    public void ManagedPlugin_IsShippedUnderItsAssemblyName_NotFileName()
    {
        string realDll = CompileLib("RealAsm", "namespace RealNs { public class RealApi { public int V => 5; } }");
        CopyToAssets(realDll, Path.Combine("Plugins", "Renamed.dll")); // file name != assembly name

        WriteScript("UsesPlugin.cs", "using RealNs; public class UsesPlugin { public static int X() => new RealApi().V; }");

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}");

        string outDir = RunBuild(AuthorEmptyScene());
        try
        {
            Assert.True(File.Exists(Path.Combine(outDir, "runtimes", "RealAsm.dll")),
                "Managed plugin should be shipped under its assembly name (RealAsm.dll).");
            Assert.False(File.Exists(Path.Combine(outDir, "runtimes", "Renamed.dll")),
                "Plugin should not be shipped under its (different) file name.");
        }
        finally { TryDelete(outDir); }
    }

    // BUG: two managed plugins with the same file name threw ArgumentException from ToDictionary,
    // crashing the whole compile instead of degrading gracefully.
    [Fact]
    [Trait("Category", "Build")]
    public void DuplicateManagedPluginFilenames_DoNotCrashCompile()
    {
        string a = CompileLib("Common", "namespace CommonA { public class A { } }");
        string b = CompileLib("Common", "namespace CommonB { public class B { } }");
        CopyToAssets(a, Path.Combine("Plugins", "A", "Common.dll"));
        CopyToAssets(b, Path.Combine("Plugins", "B", "Common.dll"));

        var ex = Record.Exception(() => ScriptCompiler.CompileAll(Project));
        Assert.Null(ex); // must not throw, regardless of compile success/failure
    }

    // BUG: csproj values were interpolated without XML escaping, so a path/name containing '&' (e.g.
    // a folder named "R&D") produced a malformed project file.
    [Fact]
    [Trait("Category", "Build")]
    public void ScriptInAmpersandFolder_ProducesValidCsprojAndCompiles()
    {
        WriteScript(Path.Combine("R&D", "Tool.cs"), "public class Tool { }");

        var result = ScriptCompiler.CompileAll(Project);

        // The generated project must be well-formed XML...
        var ex = Record.Exception(() => XDocument.Load(Project.GameCsprojPath));
        Assert.Null(ex);
        // ...and actually compile.
        Assert.True(result.Success, $"Compile failed:\n{result.Errors}");
    }

    // BUG: the engine-reference sweep excluded engine DLLs by name PREFIX of the project name, so a
    // project named like an engine assembly prefix (e.g. "Origami") lost legitimate engine references.
    [Fact]
    [Trait("Category", "Build")]
    public void ProjectNamedLikeEnginePrefix_StillReferencesEngineDlls()
    {
        // "Origami" is a prefix of the engine assembly Origami.dll (Prowl.OrigamiUI). Build a project
        // literally named "Origami" so the old StartsWith(project.Name) exclusion would drop it.
        string parent = Path.Combine(Path.GetTempPath(), "ProwlPrefixTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var proj = Project.Create(parent, "Origami");
        try
        {
            File.WriteAllText(Path.Combine(proj.AssetsPath, "UsesEngine.cs"),
                "using Prowl.OrigamiUI; public class UsesEngine { public static System.Type T() => typeof(DockPanel); }");

            var result = ScriptCompiler.CompileAll(proj);
            Assert.True(result.Success, $"Engine references were dropped for a prefix-colliding project name:\n{result.Errors}");
        }
        finally { TryDelete(parent); }
    }

    // BUG: an asmdef listing itself in References was reported as a dependency cycle and aborted the build.
    [Fact]
    [Trait("Category", "Build")]
    public void SelfReferencingAsmdef_CompilesWithoutCycleError()
    {
        WriteAsmDef("S", new AssemblyDefinition { Name = "SelfRef", References = { "SelfRef" } });
        WriteScript(Path.Combine("S", "S.cs"), "namespace SelfNs { public class S { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, $"Self-reference should be ignored, not treated as a cycle:\n{result.Errors}");
    }

    // ----------------------------------------------------------------
    //  Helpers
    // ----------------------------------------------------------------

    private void WriteScript(string relativePath, string content)
    {
        string abs = Path.Combine(Project.AssetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private void WriteAsmDef(string relativeFolder, AssemblyDefinition def)
    {
        string dir = Path.Combine(Project.AssetsPath, relativeFolder);
        Directory.CreateDirectory(dir);
        def.WriteToFile(Path.Combine(dir, def.Name + AssemblyDefinitionDatabase.Extension));
    }

    private void CopyToAssets(string sourceFile, string relativeDest)
    {
        string abs = Path.Combine(Project.AssetsPath, relativeDest);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.Copy(sourceFile, abs, true);
    }

    private Guid AuthorEmptyScene()
    {
        var scene = new Scene();
        scene.Add(new GameObject("Root"));
        Guid guid = CreateSceneAsset(scene, "Main.scene");
        Assert.NotEqual(Guid.Empty, guid);
        return guid;
    }

    private string RunBuild(Guid sceneGuid)
    {
        var build = ProjectSettingsRegistry.Get<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlBugBuildOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        var pipeline = new DesktopBuildPipeline();
        var result = pipeline.BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
        Assert.True(result.Success, $"Build failed: {result.Errors}");
        return result.OutputPath;
    }

    private static string CompileLib(string assemblyName, string code)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ProwlLibSrc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Lib.cs"), code);
        File.WriteAllText(Path.Combine(dir, $"{assemblyName}.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{{assemblyName}}</AssemblyName>
                <Nullable>enable</Nullable>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
            </Project>
            """);

        var psi = new ProcessStartInfo("dotnet", $"build \"{Path.Combine(dir, assemblyName + ".csproj")}\" -c Release")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);

        string dll = Path.Combine(dir, "bin", "Release", $"{assemblyName}.dll");
        Assert.True(File.Exists(dll), $"Building lib '{assemblyName}' failed:\n{stdout}\n{stderr}");
        return dll;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}
