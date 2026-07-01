// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for the project NuGet Packages feature (<see cref="PackageSettings"/> + ScriptCompiler
/// emission): the data model and disk round-trip, game/editor emission scoping, and an end-to-end
/// restore+compile against a real package served from a local feed.
/// </summary>
public class PackageSettingsTests : EditorTestHarness
{
    public PackageSettingsTests()
    {
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
        Packages().Packages.Clear();
    }

    private static PackageSettings Packages() => ProjectSettingsRegistry.Get<PackageSettings>();

    // ---- Fast: model + persistence ----

    [Fact]
    public void AddRemove_AndEditorOnlyFlag()
    {
        var pkgs = Packages();
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Game.Lib", Version = "1.0.0", EditorOnly = false });
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Editor.Lib", Version = "2.0.0", EditorOnly = true });

        Assert.Equal(2, pkgs.Packages.Count);
        Assert.Single(pkgs.Packages.Where(p => p.EditorOnly));

        pkgs.Packages.RemoveAll(p => p.Name == "Game.Lib");
        Assert.Single(pkgs.Packages);

        pkgs.ResetToDefaults();
        Assert.Empty(pkgs.Packages);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var pkgs = Packages();
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Foo.Bar", Version = "3.1.4", EditorOnly = false });
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Edit.Tool", Version = "9.9.9", EditorOnly = true });

        ProjectSettingsRegistry.SaveAll();
        Assert.True(File.Exists(Path.Combine(Project.ProjectSettingsPath, "Packages.yaml")));

        pkgs.Packages.Clear();          // wipe memory
        ProjectSettingsRegistry.LoadAll(); // reload from disk

        var reloaded = Packages();
        Assert.Equal(2, reloaded.Packages.Count);
        Assert.Equal("3.1.4", reloaded.Packages.Single(p => p.Name == "Foo.Bar").Version);
        Assert.True(reloaded.Packages.Single(p => p.Name == "Edit.Tool").EditorOnly);
    }

    // ---- Emission scoping (reads the generated csprojs; no restore needed) ----

    [Fact]
    [Trait("Category", "Build")]
    public void Packages_AreEmittedIntoCsprojs_WithGameEditorScoping()
    {
        var pkgs = Packages();
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Placeholder.GamePkg", Version = "1.2.3", EditorOnly = false });
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Placeholder.EditorPkg", Version = "4.5.6", EditorOnly = true });

        WriteScript("G.cs", "public class G { }");
        WriteScript(Path.Combine("Editor", "E.cs"), "public class E { }");

        ScriptCompiler.CompileAll(Project); // build fails restoring placeholders; we only read the csprojs

        string gameProj = File.ReadAllText(Project.GameCsprojPath);
        string editorProj = File.ReadAllText(Project.EditorCsprojPath);

        Assert.Contains("Placeholder.GamePkg", gameProj);
        Assert.DoesNotContain("Placeholder.EditorPkg", gameProj);
        Assert.Contains("Placeholder.GamePkg", editorProj);
        Assert.Contains("Placeholder.EditorPkg", editorProj);
    }

    [Fact]
    [Trait("Category", "Build")]
    public void EditorOnlyPackage_DoesNotLeakIntoGameAsmdef()
    {
        var pkgs = Packages();
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Placeholder.GamePkg", Version = "1.0.0", EditorOnly = false });
        pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "Placeholder.EditorPkg", Version = "1.0.0", EditorOnly = true });

        WriteAssemblyDefinition("Gameplay", new AssemblyDefinition { Name = "GameplayAsm" });
        WriteScript(Path.Combine("Gameplay", "G.cs"), "public class G { }");

        ScriptCompiler.CompileAll(Project);

        string asmProj = File.ReadAllText(Path.Combine(Project.RootPath, "GameplayAsm.csproj"));
        Assert.Contains("Placeholder.GamePkg", asmProj);          // game asmdef gets non-editor packages
        Assert.DoesNotContain("Placeholder.EditorPkg", asmProj);  // must NOT get editor-only packages
    }

    // ---- Real restore + compile against a local-feed package ----

    [Fact]
    [Trait("Category", "Build")]
    public void RestoreAndCompile_InGameAndEditorCode()
    {
        string feed = NewFeedDir();
        try
        {
            PackLib(feed, "GamePkgLib", "namespace GamePkgLib { public static class GameApi { public static int Value => 7; } }");
            PackLib(feed, "EditorPkgLib", "namespace EditorPkgLib { public static class EditorApi { public static int Value => 9; } }");
            WriteNugetConfig(feed);

            var pkgs = Packages();
            pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "GamePkgLib", Version = "1.0.0", EditorOnly = false });
            pkgs.Packages.Add(new PackageSettings.PackageEntry { Name = "EditorPkgLib", Version = "1.0.0", EditorOnly = true });

            WriteScript("UsesGamePackage.cs", "using GamePkgLib; public class UsesGamePackage { public static int X => GameApi.Value; }");
            WriteScript(Path.Combine("Editor", "UsesEditorPackage.cs"),
                "using GamePkgLib; using EditorPkgLib; public class UsesEditorPackage { public static int X => GameApi.Value + EditorApi.Value; }");

            var result = ScriptCompiler.CompileAll(Project);
            Assert.True(result.Success, $"Compile failed:\n{result.Errors}\n{result.Output}");
            Assert.True(File.Exists(Project.GameAssemblyPath));
            Assert.True(File.Exists(Project.EditorAssemblyPath));
        }
        finally { TryDeleteDir(feed); }
    }

    [Fact]
    [Trait("Category", "Build")]
    public void EditorOnlyPackage_IsNotAvailableToGameCode()
    {
        string feed = NewFeedDir();
        try
        {
            PackLib(feed, "EditorPkgLib", "namespace EditorPkgLib { public static class EditorApi { public static int Value => 9; } }");
            WriteNugetConfig(feed);

            Packages().Packages.Add(new PackageSettings.PackageEntry { Name = "EditorPkgLib", Version = "1.0.0", EditorOnly = true });
            WriteScript("GameUsesEditorPackage.cs", "using EditorPkgLib; public class GameUsesEditorPackage { public static int X => EditorApi.Value; }");

            var result = ScriptCompiler.CompileAll(Project);
            Assert.False(result.Success, "Game code must not see editor-only packages.");
        }
        finally { TryDeleteDir(feed); }
    }

    // ---- Package-specific helpers ----

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
