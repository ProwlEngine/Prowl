// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using System.Reflection;
using System.Xml.Linq;

using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for how user scripts are compiled into the default Game/Editor assemblies: editor/game
/// isolation, recompilation picking up edits, editor-window discovery, and robust .csproj generation.
/// These invoke the .NET SDK, so they are opt-in under the "Build" category.
/// </summary>
[Trait("Category", "Build")]
public class ScriptCompilationTests : EditorTestHarness
{
    public ScriptCompilationTests()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();
    }

    // Editor code may use game code, and the two land in separate assemblies.
    [Fact]
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
        Assert.Null(game.GetType("EditorThing"));    // game assembly must not contain editor code
        Assert.NotNull(editor.GetType("EditorThing"));
        Assert.Null(editor.GetType("GameThing"));    // editor code lives only in the editor assembly
    }

    // Game code must not be able to reference editor-only types.
    [Fact]
    public void GameScript_ReferencingEditorType_FailsToCompile()
    {
        WriteScript("GameThing.cs", "public class GameThing { public static int Read() => EditorThing.Value; }");
        WriteScript(Path.Combine("Editor", "EditorThing.cs"), "public class EditorThing { public static int Value => 1; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, "Game code must not be able to reference editor-only types.");
    }

    [Fact]
    public void EditorAssembly_IsNotShippedInBuild()
    {
        WriteScript("PlayComponent.cs", "using Prowl.Runtime; public class PlayComponent : MonoBehaviour { }");
        WriteScript(Path.Combine("Editor", "EditorOnlyTool.cs"), "public class EditorOnlyTool { public static int X => 1; }");

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}");
        Assert.True(File.Exists(Project.EditorAssemblyPath), "Editor assembly should be produced for editing.");

        string outDir = RunBuild(AuthorSceneWithComponent("PlayComponent"));
        try
        {
            Assert.True(File.Exists(Path.Combine(outDir, $"{Project.Name}.Game.dll")), "Game assembly should ship.");
            Assert.False(File.Exists(Path.Combine(outDir, $"{Project.Name}.Editor.dll")), "Editor assembly must not ship.");
            Assert.False(File.Exists(Path.Combine(outDir, "runtimes", $"{Project.Name}.Editor.dll")), "Editor assembly must not ship to runtimes/.");
        }
        finally { TryDeleteDir(outDir); }
    }

    // Recompiling after an edit reflects the new source (the source-change half of hot-reload).
    [Fact]
    public void Recompile_ReflectsSourceChanges()
    {
        WriteScript("Versioned.cs", "public static class Versioned { public static string Tag() => \"VERSION_ONE\"; }");
        Assert.True(ScriptCompiler.CompileAll(Project).Success);
        Assert.Equal("VERSION_ONE", InvokeVersionedTag());

        WriteScript("Versioned.cs", "public static class Versioned { public static string Tag() => \"VERSION_TWO\"; }");
        Assert.True(ScriptCompiler.CompileAll(Project).Success);
        Assert.Equal("VERSION_TWO", InvokeVersionedTag());
    }

    // An editor script can declare a DockPanel with a [MenuItem]-attributed static opener method,
    // discoverable by the engine's scan (the unified menu system replaced the old [EditorWindow] attribute).
    [Fact]
    public void EditorScript_CanDeclareEditorWindow_DiscoverableByEngineScan()
    {
        WriteScript(Path.Combine("Editor", "MyTestWindow.cs"), """
            using Prowl.Editor;
            using Prowl.Editor.GUI;
            using Prowl.OrigamiUI;
            using Prowl.PaperUI;

            public class MyTestWindow : DockPanel
            {
                [MenuItem("Test/MyTestWindow")]
                static void Open() => EditorApplication.Instance?.OpenPanel(typeof(MyTestWindow));

                public override string Title => "My Test Window";
                public override string Icon => "?";
                public override void OnGUI(Paper paper, float width, float height) { }
            }
            """);

        var compile = ScriptCompiler.CompileAll(Project);
        Assert.True(compile.Success, $"Compile failed:\n{compile.Errors}\n{compile.Output}");

        var editor = Assembly.Load(File.ReadAllBytes(Project.EditorAssemblyPath));

        // (DockPanel matched by name to avoid a compile dependency on Prowl.PaperUI in the test project.)
        var window = editor.GetTypes()
            .Where(t => !t.IsAbstract && DerivesFromDockPanel(t))
            .SingleOrDefault(t => t.Name == "MyTestWindow");

        Assert.NotNull(window);

        var openMethod = window!.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(openMethod);
        var attr = openMethod!.GetCustomAttribute<MenuItemAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Test/MyTestWindow", attr!.Path);
    }

    // A script path containing '&' must produce a well-formed csproj and compile (XML escaping).
    [Fact]
    public void ScriptInAmpersandFolder_ProducesValidCsprojAndCompiles()
    {
        WriteScript(Path.Combine("R&D", "Tool.cs"), "public class Tool { }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.Null(Record.Exception(() => XDocument.Load(Project.GameCsprojPath))); // well-formed XML
        Assert.True(result.Success, $"Compile failed:\n{result.Errors}");
    }

    // A project named like an engine-assembly prefix (e.g. "Origami") must still get engine references.
    [Fact]
    public void ProjectNamedLikeEnginePrefix_StillReferencesEngineDlls()
    {
        string parent = Path.Combine(Path.GetTempPath(), "ProwlPrefixTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var proj = Project.Create(parent, "Origami"); // prefix of the engine assembly Origami.dll
        try
        {
            File.WriteAllText(Path.Combine(proj.AssetsPath, "UsesEngine.cs"),
                "using Prowl.OrigamiUI; public class UsesEngine { public static System.Type T() => typeof(DockPanel); }");

            var result = ScriptCompiler.CompileAll(proj);
            Assert.True(result.Success, $"Engine references were dropped for a prefix-colliding project name:\n{result.Errors}");
        }
        finally { TryDeleteDir(parent); }
    }

    private string InvokeVersionedTag()
    {
        // Load by bytes so the file stays unlocked for the next recompile.
        var asm = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath));
        return (string)asm.GetType("Versioned")!.GetMethod("Tag")!.Invoke(null, null)!;
    }

    private static bool DerivesFromDockPanel(Type t)
    {
        for (var b = t.BaseType; b != null; b = b.BaseType)
            if (b.Name == "DockPanel")
                return true;
        return false;
    }
}
