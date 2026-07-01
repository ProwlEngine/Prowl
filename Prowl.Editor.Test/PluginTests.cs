// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for the plugin system: managed/native libraries under <c>Plugins/</c> folders being
/// referenced, shipped, editor-only-excluded, and resolved at runtime. These shell out to the .NET
/// SDK and launch the built player, so they are opt-in under the "Build" category.
/// </summary>
[Trait("Category", "Build")]
public class PluginTests : EditorTestHarness
{
    public PluginTests()
    {
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
    }

    [Fact]
    public void ManagedPlugin_IsReferenced_Shipped_AndUsedAtRuntime()
    {
        const string marker = "MANAGED_PLUGIN_OK";

        string pluginDll = CompileLibrary("Acme.Greeter",
            "namespace Acme.Greeter { public static class Greeter { public static string Message => \"MANAGED_PLUGIN_OK\"; } }");
        CopyIntoAssets(pluginDll, Path.Combine("Plugins", "Acme.Greeter.dll"));

        WriteScript("PluginUser.cs", """
            using Prowl.Runtime;
            using Acme.Greeter;

            public class PluginUser : MonoBehaviour
            {
                public override void Start() => System.Console.WriteLine(Greeter.Message);
            }
            """);

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}\n{compile.Output}");

        string outDir = RunBuild(AuthorSceneWithComponent("PluginUser"));
        try
        {
            Assert.True(File.Exists(Path.Combine(outDir, "runtimes", "Acme.Greeter.dll")), "Managed plugin was not shipped.");
            Assert.Contains(marker, RunPlayerHeadless(outDir));
        }
        finally { TryDeleteDir(outDir); }
    }

    // A plugin whose file name differs from its assembly name must be shipped under its ASSEMBLY name
    // (that's what the player's managed resolver probes for).
    [Fact]
    public void ManagedPlugin_IsShippedUnderItsAssemblyName_NotFileName()
    {
        string realDll = CompileLibrary("RealAsm", "namespace RealNs { public class RealApi { public int V => 5; } }");
        CopyIntoAssets(realDll, Path.Combine("Plugins", "Renamed.dll")); // file name != assembly name

        WriteScript("UsesPlugin.cs", "using RealNs; public class UsesPlugin { public static int X() => new RealApi().V; }");

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}");

        string outDir = RunBuild(AuthorEmptyScene());
        try
        {
            Assert.True(File.Exists(Path.Combine(outDir, "runtimes", "RealAsm.dll")), "Plugin should ship under its assembly name.");
            Assert.False(File.Exists(Path.Combine(outDir, "runtimes", "Renamed.dll")), "Plugin should not ship under its file name.");
        }
        finally { TryDeleteDir(outDir); }
    }

    [Fact]
    public void EditorOnlyPlugin_IsNotShippedInBuild()
    {
        string editorPlugin = CompileLibrary("Acme.EditorTool",
            "namespace Acme.EditorTool { public static class Tool { public static int X => 1; } }");
        CopyIntoAssets(editorPlugin, Path.Combine("Editor", "Plugins", "Acme.EditorTool.dll")); // Editor/ => editor-only

        string runtimePlugin = CompileLibrary("Acme.Runtime",
            "namespace Acme.Runtime { public static class R { public static int X => 2; } }");
        CopyIntoAssets(runtimePlugin, Path.Combine("Plugins", "Acme.Runtime.dll"));

        string outDir = RunBuild(AuthorEmptyScene());
        try
        {
            Assert.False(File.Exists(Path.Combine(outDir, "runtimes", "Acme.EditorTool.dll")), "Editor-only plugin must not ship.");
            Assert.True(File.Exists(Path.Combine(outDir, "runtimes", "Acme.Runtime.dll")), "Runtime plugin should ship.");
        }
        finally { TryDeleteDir(outDir); }
    }

    [Fact]
    public void NativePlugin_IsPlacedInPlatformRuntimes()
    {
        string nativeDir = Path.Combine(Project.AssetsPath, "Plugins");
        Directory.CreateDirectory(nativeDir);
        string nativePath = Path.Combine(nativeDir, "acme_native.dll");
        File.WriteAllBytes(nativePath, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05]); // not a valid managed assembly

        Assert.True(PluginScanner.ScanAll(Project).Single(p => p.FileName == "acme_native.dll").IsNative,
            "A fake native .dll should be classified as native.");

        string outDir = RunBuild(AuthorEmptyScene());
        try
        {
            // Default desktop profile targets win-x64.
            string placed = Path.Combine(outDir, "runtimes", "win-x64", "native", "acme_native.dll");
            Assert.True(File.Exists(placed), $"Native plugin not placed at {placed}.");
        }
        finally { TryDeleteDir(outDir); }
    }

    // Two managed plugins with the same file name must not crash the compile (previously threw from ToDictionary).
    [Fact]
    public void DuplicateManagedPluginFilenames_DoNotCrashCompile()
    {
        CopyIntoAssets(CompileLibrary("Common", "namespace CommonA { public class A { } }"), Path.Combine("Plugins", "A", "Common.dll"));
        CopyIntoAssets(CompileLibrary("Common", "namespace CommonB { public class B { } }"), Path.Combine("Plugins", "B", "Common.dll"));

        var ex = Record.Exception(() => ScriptCompiler.CompileAll(Project));
        Assert.Null(ex);
    }
}
