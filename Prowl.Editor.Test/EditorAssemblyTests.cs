// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using System.Reflection;

using Prowl.Editor.Build;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for the multi-assembly compilation model: editor/game isolation, that the editor assembly
/// is never shipped, that recompilation reflects source edits (the core of hot-reload), and that the
/// assembly planner rejects invalid graphs (cycles, duplicate names) before invoking the compiler.
/// </summary>
public class EditorAssemblyTests : EditorTestHarness
{
    public EditorAssemblyTests()
    {
        ProjectSettingsRegistry.Initialize();
        ProjectSettingsRegistry.OnProjectOpened();
    }

    // ----------------------------------------------------------------
    //  Fast tests - planner rejects invalid graphs before any dotnet call
    // ----------------------------------------------------------------

    [Fact]
    public void CyclicAssemblyReferences_FailCompileWithClearError()
    {
        WriteAsmDef("A", new AssemblyDefinition { Name = "Cyc.A", References = { "Cyc.B" } });
        WriteScript(Path.Combine("A", "a.cs"), "namespace Cyc { public class A { } }");
        WriteAsmDef("B", new AssemblyDefinition { Name = "Cyc.B", References = { "Cyc.A" } });
        WriteScript(Path.Combine("B", "b.cs"), "namespace Cyc { public class B { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("Cyclic", result.Errors, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateAssemblyNames_FailCompileWithClearError()
    {
        WriteAsmDef("A", new AssemblyDefinition { Name = "Same.Name" });
        WriteAsmDef("B", new AssemblyDefinition { Name = "Same.Name" });

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("Duplicate", result.Errors, StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------
    //  Isolation - editor code may use game code, but not vice versa
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Build")]
    public void EditorScript_UsesGameType_AndAssembliesAreSeparate()
    {
        WriteScript("GameThing.cs", "public class GameThing { public static int Value => 42; }");
        WriteScript(Path.Combine("Editor", "EditorThing.cs"),
            "public class EditorThing { public static int Read() => GameThing.Value; }");

        var result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, $"Compile failed:\n{result.Errors}\n{result.Output}");

        var game = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var editor = Assembly.Load(File.ReadAllBytes(Project.EditorAssemblyPath));

        Assert.NotNull(game.GetType("GameThing"));
        Assert.Null(game.GetType("EditorThing"));        // game assembly must not contain editor code
        Assert.NotNull(editor.GetType("EditorThing"));
        Assert.Null(editor.GetType("GameThing"));        // editor code lives only in the editor assembly
    }

    [Fact]
    [Trait("Category", "Build")]
    public void GameScript_ReferencingEditorType_FailsToCompile()
    {
        // Game code trying to use a type that only exists in the editor assembly must not compile.
        WriteScript("GameThing.cs", "public class GameThing { public static int Read() => EditorThing.Value; }");
        WriteScript(Path.Combine("Editor", "EditorThing.cs"), "public class EditorThing { public static int Value => 1; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, "Game code must not be able to reference editor-only types.");
    }

    // ----------------------------------------------------------------
    //  Editor assembly is never shipped
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Build")]
    public void EditorAssembly_IsNotShippedInBuild()
    {
        WriteScript("PlayComponent.cs", """
            using Prowl.Runtime;
            public class PlayComponent : MonoBehaviour { }
            """);
        WriteScript(Path.Combine("Editor", "EditorOnlyTool.cs"), "public class EditorOnlyTool { public static int X => 1; }");

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}");
        Assert.True(File.Exists(Project.EditorAssemblyPath), "Editor assembly should be produced for editing.");

        Guid sceneGuid = AuthorSceneWith("PlayComponent");
        string outDir = RunBuild(sceneGuid);
        try
        {
            Assert.True(File.Exists(Path.Combine(outDir, $"{Project.Name}.Game.dll")), "Game assembly should ship.");
            Assert.False(File.Exists(Path.Combine(outDir, $"{Project.Name}.Editor.dll")), "Editor assembly must not ship.");
            Assert.False(File.Exists(Path.Combine(outDir, "runtimes", $"{Project.Name}.Editor.dll")), "Editor assembly must not ship to runtimes/.");
        }
        finally { try { Directory.Delete(outDir, true); } catch { } }
    }

    // ----------------------------------------------------------------
    //  Recompilation reflects source edits (the source-change half of hot-reload)
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Build")]
    public void Recompile_ReflectsSourceChanges()
    {
        WriteScript("Versioned.cs", "public static class Versioned { public static string Tag() => \"VERSION_ONE\"; }");
        Assert.True(ScriptCompiler.CompileAll(Project).Success);
        Assert.Equal("VERSION_ONE", InvokeTag());

        WriteScript("Versioned.cs", "public static class Versioned { public static string Tag() => \"VERSION_TWO\"; }");
        Assert.True(ScriptCompiler.CompileAll(Project).Success);
        Assert.Equal("VERSION_TWO", InvokeTag());
    }

    private string InvokeTag()
    {
        // Load by bytes so the file stays unlocked for the next recompile.
        var asm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        var type = asm.GetType("Versioned")!;
        return (string)type.GetMethod("Tag")!.Invoke(null, null)!;
    }

    // ----------------------------------------------------------------
    //  Editor scripts can register a new editor window
    // ----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Build")]
    public void EditorScript_CanDeclareEditorWindow_DiscoverableByEngineScan()
    {
        // An editor script declares a window exactly like the engine's own panels do.
        WriteScript(Path.Combine("Editor", "MyTestWindow.cs"), """
            using Prowl.Editor.GUI;
            using Prowl.OrigamiUI;
            using Prowl.PaperUI;

            [EditorWindow("Test/MyTestWindow")]
            public class MyTestWindow : DockPanel
            {
                public override string Title => "My Test Window";
                public override string Icon => "?";
                public override void OnGUI(Paper paper, float width, float height) { }
            }
            """);

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}\n{compile.Output}");
        Assert.True(File.Exists(Project.EditorAssemblyPath), "Editor assembly was not produced.");

        var editor = Assembly.Load(File.ReadAllBytes(Project.EditorAssemblyPath));

        // Apply the same predicate EditorApplication uses to discover/register editor windows:
        // a non-abstract DockPanel subclass carrying [EditorWindow]. (DockPanel is matched by name
        // to avoid taking a compile dependency on Prowl.PaperUI in the test project.)
        var windows = editor.GetTypes()
            .Where(t => !t.IsAbstract && DerivesFromDockPanel(t)
                        && t.GetCustomAttribute<EditorWindowAttribute>() != null)
            .ToList();

        var window = windows.SingleOrDefault(t => t.Name == "MyTestWindow");
        Assert.NotNull(window);
        Assert.Equal("Test/MyTestWindow", window!.GetCustomAttribute<EditorWindowAttribute>()!.Path);
    }

    private static bool DerivesFromDockPanel(Type t)
    {
        for (var b = t.BaseType; b != null; b = b.BaseType)
            if (b.Name == "DockPanel")
                return true;
        return false;
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

    private string RunBuild(Guid sceneGuid)
    {
        var build = ProjectSettingsRegistry.Get<BuildSettings>();
        build.Scenes.Clear();
        build.Scenes.Add(new SceneBuildEntry { Path = "Main.scene", SceneGuid = sceneGuid, Enabled = true });
        build.PackagingMode = AssetPackagingMode.LooseFiles;

        string buildOut = Path.Combine(Path.GetTempPath(), "ProwlEditorAsmBuildOut", Guid.NewGuid().ToString("N"));
        build.OutputDirectory = buildOut;

        var pipeline = new DesktopBuildPipeline();
        var result = pipeline.BuildAsync(Project.RootPath, build, buildOut).GetAwaiter().GetResult();
        Assert.True(result.Success, $"Build failed: {result.Errors}");
        return result.OutputPath;
    }
}
