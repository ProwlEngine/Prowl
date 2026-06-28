// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;

using Prowl.Editor.Build;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// End-to-end coverage for the plugin and assembly-definition pipeline: managed plugins compiled
/// against and shipped, an asmdef reference graph compiled in dependency order, editor-only plugins
/// kept out of builds, and native plugins placed into the platform runtimes layout.
///
/// These shell out to `dotnet build` / `dotnet publish` and (for the run cases) launch the produced
/// win-x64 player, so they are opt-in under the "Build" category like the other pipeline tests.
/// </summary>
[Trait("Category", "Build")]
public class PluginAndAssemblyTests : EditorTestHarness
{
    public PluginAndAssemblyTests()
    {
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
    }

    [Fact]
    public void ManagedPlugin_IsReferenced_Shipped_AndUsedAtRuntime()
    {
        const string marker = "MANAGED_PLUGIN_OK";

        // A managed plugin compiled completely outside the engine.
        string pluginDll = CompileManagedPlugin("Acme.Greeter", """
            namespace Acme.Greeter
            {
                public static class Greeter
                {
                    public static string Message => "MANAGED_PLUGIN_OK";
                }
            }
            """);
        CopyToAssets(pluginDll, Path.Combine("Plugins", "Acme.Greeter.dll"));

        // A game script that uses a type from the plugin.
        WriteScript("PluginUser.cs", $$"""
            using Prowl.Runtime;
            using Acme.Greeter;

            public class PluginUser : MonoBehaviour
            {
                public override void Start() => System.Console.WriteLine(Greeter.Message);
            }
            """);

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}\n{compile.Output}");
        Assert.True(File.Exists(Project.GameAssemblyPath), "Game assembly was not produced.");

        Guid sceneGuid = AuthorSceneWith("PluginUser");

        RunBuildAndAssertMarker(sceneGuid, marker, outDir =>
        {
            string shipped = Path.Combine(outDir, "runtimes", "Acme.Greeter.dll");
            Assert.True(File.Exists(shipped), "Managed plugin was not shipped into runtimes/.");
        });
    }

    [Fact]
    public void AssemblyDefinitions_CompileInDependencyOrder_AndRun()
    {
        const string marker = "ASMDEF_OK:8";

        // Core assembly.
        WriteAsmDef(Path.Combine("Core"), new AssemblyDefinition { Name = "Acme.Core" });
        WriteScript(Path.Combine("Core", "CoreMath.cs"), """
            namespace Acme.Core
            {
                public static class CoreMath
                {
                    public static int Add(int a, int b) => a + b;
                }
            }
            """);

        // Gameplay assembly that references Core.
        WriteAsmDef(Path.Combine("Gameplay"), new AssemblyDefinition
        {
            Name = "Acme.Gameplay",
            References = { "Acme.Core" },
        });
        WriteScript(Path.Combine("Gameplay", "Calculator.cs"), """
            using Acme.Core;

            namespace Acme.Gameplay
            {
                public static class Calculator
                {
                    public static int DoubleSum(int a, int b) => CoreMath.Add(a, b) * 2;
                }
            }
            """);

        // Loose script (default Game assembly) that consumes the Gameplay assembly via auto-reference.
        WriteScript("Main.cs", """
            using Prowl.Runtime;
            using Acme.Gameplay;

            public class Main : MonoBehaviour
            {
                public override void Start() => System.Console.WriteLine("ASMDEF_OK:" + Calculator.DoubleSum(1, 3));
            }
            """);

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}\n{compile.Output}");

        string asmDir = Project.ScriptAssemblyPath;
        Assert.True(File.Exists(Path.Combine(asmDir, "Acme.Core.dll")), "Core assembly missing.");
        Assert.True(File.Exists(Path.Combine(asmDir, "Acme.Gameplay.dll")), "Gameplay assembly missing.");
        Assert.True(File.Exists(Project.GameAssemblyPath), "Default game assembly missing.");

        Guid sceneGuid = AuthorSceneWith("Main");

        RunBuildAndAssertMarker(sceneGuid, marker, outDir =>
        {
            Assert.True(File.Exists(Path.Combine(outDir, "Acme.Core.dll")), "Core assembly not shipped.");
            Assert.True(File.Exists(Path.Combine(outDir, "Acme.Gameplay.dll")), "Gameplay assembly not shipped.");
        });
    }

    [Fact]
    public void EditorOnlyPlugin_IsNotShippedInBuild()
    {
        // Plugin under Editor/Plugins is editor-only by folder convention.
        string editorPlugin = CompileManagedPlugin("Acme.EditorTool", """
            namespace Acme.EditorTool { public static class Tool { public static int X => 1; } }
            """);
        CopyToAssets(editorPlugin, Path.Combine("Editor", "Plugins", "Acme.EditorTool.dll"));

        // A regular managed plugin for contrast - this one should ship.
        string runtimePlugin = CompileManagedPlugin("Acme.Runtime", """
            namespace Acme.Runtime { public static class R { public static int X => 2; } }
            """);
        CopyToAssets(runtimePlugin, Path.Combine("Plugins", "Acme.Runtime.dll"));

        Guid sceneGuid = AuthorEmptyScene();
        string outDir = RunBuild(sceneGuid);

        Assert.False(File.Exists(Path.Combine(outDir, "runtimes", "Acme.EditorTool.dll")),
            "Editor-only plugin must not be shipped.");
        Assert.True(File.Exists(Path.Combine(outDir, "runtimes", "Acme.Runtime.dll")),
            "Runtime plugin should be shipped.");

        try { Directory.Delete(outDir, true); } catch { }
    }

    [Fact]
    public void NativePlugin_IsPlacedInPlatformRuntimes()
    {
        // A fake native library: a .dll whose bytes are not a managed assembly is detected as native.
        string nativeDir = Path.Combine(Project.AssetsPath, "Plugins");
        Directory.CreateDirectory(nativeDir);
        string nativePath = Path.Combine(nativeDir, "acme_native.dll");
        File.WriteAllBytes(nativePath, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05]); // not a valid PE assembly

        Assert.True(PluginScanner.ScanAll(Project).Single(p => p.FileName == "acme_native.dll").IsNative,
            "Fake native .dll should be classified as native.");

        Guid sceneGuid = AuthorEmptyScene();
        string outDir = RunBuild(sceneGuid);

        // Default desktop profile targets win-x64, so the native lib lands under runtimes/win-x64/native.
        string placed = Path.Combine(outDir, "runtimes", "win-x64", "native", "acme_native.dll");
        Assert.True(File.Exists(placed), $"Native plugin not placed at {placed}.");

        try { Directory.Delete(outDir, true); } catch { }
    }

    // ================================================================
    //  Helpers
    // ================================================================

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

    private Guid AuthorSceneWith(string componentTypeName)
    {
        var gameAsm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var compType = gameAsm.GetType(componentTypeName);
        Assert.NotNull(compType);

        var scene = new Scene();
        var go = new GameObject("Root");
        go.AddComponent(compType!);
        scene.Add(go);
        Guid guid = CreateSceneAsset(scene, "Main.scene");
        Assert.NotEqual(Guid.Empty, guid);
        return guid;
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

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlPluginBuildOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        var pipeline = new DesktopBuildPipeline();
        var result = pipeline.BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
        Assert.True(result.Success, $"Build failed: {result.Errors}");
        return result.OutputPath;
    }

    private void RunBuildAndAssertMarker(Guid sceneGuid, string marker, Action<string> assertOutput)
    {
        string outDir = RunBuild(sceneGuid);
        try
        {
            var pipeline = new DesktopBuildPipeline();
            var build = ProjectSettingsRegistry.Get<BuildSettings>();
            string exe = pipeline.GetExecutablePath(outDir, build);
            Assert.True(File.Exists(exe), $"Expected executable at {exe}");

            assertOutput(outDir);

            var psi = new ProcessStartInfo(exe, "--headless --frames 30 --fps 0")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = outDir,
            };
            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            bool exited = proc.WaitForExit(90_000);
            if (!exited) { try { proc.Kill(true); } catch { } }

            Assert.True(exited, "Headless player did not exit within the timeout.");
            Assert.Equal(0, proc.ExitCode);
            Assert.Contains(marker, stdout);
        }
        finally
        {
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
        }
    }

    private static string CompileManagedPlugin(string assemblyName, string code)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ProwlPluginSrc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Plugin.cs"), code);
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
        Assert.True(File.Exists(dll), $"Plugin '{assemblyName}' build failed:\n{stdout}\n{stderr}");
        return dll;
    }
}
