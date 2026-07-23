// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Projects.Scripting;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for the Directory.Build.props based NuGet workflow: packages declared there flow into the
/// generated user assemblies (auto-imported by MSBuild), editor-only scoping keeps a package out of
/// game code, and packages can be restored before the first script exists.
/// </summary>
public class DirectoryBuildPropsTests : EditorTestHarness
{
    // ---- Package detection (fast, no dotnet) ----

    [Fact]
    public void ProjectDeclaresPackages_IgnoresCommentedTemplate()
    {
        // A freshly created project ships the template, whose PackageReference lines are all commented.
        Assert.False(ScriptCompiler.ProjectDeclaresPackages(Project));

        WriteBuildProps("""
            <Project>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        Assert.True(ScriptCompiler.ProjectDeclaresPackages(Project));
    }

    // ---- Generation / restore without scripts ----

    [Fact]
    [Trait("Category", "Build")]
    public void NoScriptsNoPackages_GeneratesDefaultCsprojs_WithoutBuilding()
    {
        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success);
        Assert.False(result.RequiresReload);
        Assert.True(File.Exists(Project.GameCsprojPath));     // generated so an IDE has something to open
        Assert.True(File.Exists(Project.EditorCsprojPath));
        Assert.False(File.Exists(Project.GameAssemblyPath));  // nothing to compile
    }

    [Fact]
    [Trait("Category", "Build")]
    public void PackageAddedBeforeAnyScript_RestoresWithoutBuilding()
    {
        string feed = NewFeedDir();
        try
        {
            PackLib(feed, "GamePkgLib", "namespace GamePkgLib { public static class GameApi { public static int Value => 7; } }");
            WriteNugetConfig(feed);
            WriteBuildProps(PropsWithPackage("GamePkgLib", "1.0.0"));
            // No scripts written on purpose - this is the "add a package first" flow.

            var result = ScriptCompiler.CompileAll(Project);

            Assert.True(result.Success, $"Restore failed:\n{result.Errors}\n{result.Output}");
            Assert.False(result.RequiresReload);                 // restore only, no hot-reload
            Assert.False(File.Exists(Project.GameAssemblyPath)); // nothing was compiled
        }
        finally { TryDeleteDir(feed); }
    }

    // ---- Real restore + compile against a local-feed package ----

    [Fact]
    [Trait("Category", "Build")]
    public void DefaultGroupPackage_IsAvailableToGameAndEditorCode()
    {
        string feed = NewFeedDir();
        try
        {
            PackLib(feed, "GamePkgLib", "namespace GamePkgLib { public static class GameApi { public static int Value => 7; } }");
            WriteNugetConfig(feed);
            WriteBuildProps(PropsWithPackage("GamePkgLib", "1.0.0"));

            WriteScript("UsesGamePackage.cs", "using GamePkgLib; public class UsesGamePackage { public static int X => GameApi.Value; }");
            WriteScript(Path.Combine("Editor", "UsesFromEditor.cs"),
                "using GamePkgLib; public class UsesFromEditor { public static int X => GameApi.Value; }");

            var result = ScriptCompiler.CompileAll(Project);
            Assert.True(result.Success, $"Compile failed:\n{result.Errors}\n{result.Output}");
            Assert.True(result.RequiresReload);
            Assert.True(File.Exists(Project.GameAssemblyPath));
            Assert.True(File.Exists(Project.EditorAssemblyPath));
        }
        finally { TryDeleteDir(feed); }
    }

    [Fact]
    [Trait("Category", "Build")]
    public void EditorOnlyPackage_IsAvailableToEditorCode_ButNotGameCode()
    {
        string feed = NewFeedDir();
        try
        {
            PackLib(feed, "EditorPkgLib", "namespace EditorPkgLib { public static class EditorApi { public static int Value => 9; } }");
            WriteNugetConfig(feed);

            // Package scoped to the editor assembly only (never shipped, never seen by game code).
            WriteBuildProps("""
                <Project>
                  <ItemGroup Condition="$(MSBuildProjectName.EndsWith('.Editor'))">
                    <PackageReference Include="EditorPkgLib" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """);

            WriteScript(Path.Combine("Editor", "UsesEditorPackage.cs"),
                "using EditorPkgLib; public class UsesEditorPackage { public static int X => EditorApi.Value; }");

            var ok = ScriptCompiler.CompileAll(Project);
            Assert.True(ok.Success, $"Editor code should see editor-only packages:\n{ok.Errors}\n{ok.Output}");
            Assert.True(File.Exists(Project.EditorAssemblyPath));

            // Now let game code try to use it - the game assembly must not resolve the editor-only package.
            WriteScript("GameUsesEditorPackage.cs", "using EditorPkgLib; public class GameUsesEditorPackage { public static int X => EditorApi.Value; }");
            var fail = ScriptCompiler.CompileAll(Project);
            Assert.False(fail.Success, "Game code must not see editor-only packages.");
        }
        finally { TryDeleteDir(feed); }
    }

    [Fact]
    [Trait("Category", "Build")]
    public void DefaultGroupPackage_ShipsIntoPlayerBuild()
    {
        string feed = NewFeedDir();
        string outDir = Path.Combine(Path.GetTempPath(), "ProwlPlayerOut", Guid.NewGuid().ToString("N"));
        try
        {
            PackLib(feed, "GamePkgLib", "namespace GamePkgLib { public static class GameApi { public static int Value => 7; } }");
            WriteNugetConfig(feed);
            WriteBuildProps(PropsWithPackage("GamePkgLib", "1.0.0"));

            // Mirror the real pipeline layout: the player csproj lives under Temp/ inside the project
            // root, so MSBuild auto-imports the project-root Directory.Build.props into it.
            string playerDir = Path.Combine(Project.RootPath, "Temp", "PlayerBuild");
            Directory.CreateDirectory(playerDir);
            File.WriteAllText(Path.Combine(playerDir, "Program.cs"),
                "using GamePkgLib; class Program { static void Main() { System.Console.WriteLine(GameApi.Value); } }");
            File.WriteAllText(Path.Combine(playerDir, "TestPlayer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Program.cs" />
                  </ItemGroup>
                </Project>
                """);

            var (exit, stdout, stderr) = RunDotnet(
                $"publish \"{Path.Combine(playerDir, "TestPlayer.csproj")}\" -c Release -o \"{outDir}\"", playerDir);
            Assert.True(exit == 0, $"Player publish failed:\n{stdout}\n{stderr}");
            Assert.True(File.Exists(Path.Combine(outDir, "GamePkgLib.dll")),
                "A package declared in Directory.Build.props must ship next to the player.");
        }
        finally { TryDeleteDir(feed); TryDeleteDir(outDir); }
    }

    // ---- Helpers ----

    private void WriteBuildProps(string content)
        => File.WriteAllText(Path.Combine(Project.RootPath, "Directory.Build.props"), content);

    private static string PropsWithPackage(string name, string version) => $$"""
        <Project>
          <ItemGroup>
            <PackageReference Include="{{name}}" Version="{{version}}" />
          </ItemGroup>
        </Project>
        """;

    private static string NewFeedDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ProwlPkgFeed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void WriteNugetConfig(string feedDir)
    {
        File.WriteAllText(Path.Combine(Project.RootPath, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="ProwlLocalTestFeed" value="{feedDir}" />
              </packageSources>
            </configuration>
            """);
    }

    private static void PackLib(string feedDir, string packageId, string code)
    {
        string src = Path.Combine(Path.GetTempPath(), "ProwlPkgSrc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Lib.cs"), code);
        File.WriteAllText(Path.Combine(src, $"{packageId}.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>{{packageId}}</PackageId>
                <Version>1.0.0</Version>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        var (exit, stdout, stderr) = RunDotnet($"pack \"{Path.Combine(src, packageId + ".csproj")}\" -c Release -o \"{feedDir}\"", src);
        Assert.True(exit == 0, $"Packing '{packageId}' failed:\n{stdout}\n{stderr}");
        TryDeleteDir(src);
    }
}
