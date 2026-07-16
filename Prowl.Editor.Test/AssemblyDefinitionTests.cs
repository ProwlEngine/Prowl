// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Tests for the assembly definition (.asmdef) feature: the reference graph, dependency-order
/// compilation, platform scoping, and the planner's rejection of invalid graphs. The pure-planner
/// cases run without the SDK; the compile/build cases are opt-in under the "Build" category.
/// </summary>
public class AssemblyDefinitionTests : EditorTestHarness
{
    public AssemblyDefinitionTests()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();
    }

    // ---- Fast: the planner rejects invalid graphs before invoking the compiler ----

    [Fact]
    public void CyclicAssemblyReferences_FailCompileWithClearError()
    {
        WriteAssemblyDefinition("A", new AssemblyDefinition { Name = "Cyc.A", References = { "Cyc.B" } });
        WriteScript(Path.Combine("A", "a.cs"), "namespace Cyc { public class A { } }");
        WriteAssemblyDefinition("B", new AssemblyDefinition { Name = "Cyc.B", References = { "Cyc.A" } });
        WriteScript(Path.Combine("B", "b.cs"), "namespace Cyc { public class B { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("Cyclic", result.Errors, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateAssemblyNames_FailCompileWithClearError()
    {
        WriteAssemblyDefinition("A", new AssemblyDefinition { Name = "Same.Name" });
        WriteAssemblyDefinition("B", new AssemblyDefinition { Name = "Same.Name" });

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("Duplicate", result.Errors, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AsmdefNamedLikeDefaultAssembly_FailsWithClearError()
    {
        WriteAssemblyDefinition("X", new AssemblyDefinition { Name = $"{Project.Name}.Game" });

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("reserved", result.Errors, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Build: real compilation / execution ----

    [Fact]
    [Trait("Category", "Build")]
    public void CompileInDependencyOrder_AndRun()
    {
        const string marker = "ASMDEF_OK:8";

        WriteAssemblyDefinition("Core", new AssemblyDefinition { Name = "Acme.Core" });
        WriteScript(Path.Combine("Core", "CoreMath.cs"), """
            namespace Acme.Core { public static class CoreMath { public static int Add(int a, int b) => a + b; } }
            """);

        WriteAssemblyDefinition("Gameplay", new AssemblyDefinition { Name = "Acme.Gameplay", References = { "Acme.Core" } });
        WriteScript(Path.Combine("Gameplay", "Calculator.cs"), """
            using Acme.Core;
            namespace Acme.Gameplay { public static class Calculator { public static int DoubleSum(int a, int b) => CoreMath.Add(a, b) * 2; } }
            """);

        // Loose script (default game assembly) uses the Gameplay assembly via auto-reference.
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

        string outDir = RunBuild(AuthorSceneWithComponent("Main"));
        try
        {
            Assert.True(File.Exists(Path.Combine(outDir, "Acme.Core.dll")), "Core assembly not shipped.");
            Assert.True(File.Exists(Path.Combine(outDir, "Acme.Gameplay.dll")), "Gameplay assembly not shipped.");
            Assert.Contains(marker, RunPlayerHeadless(outDir));
        }
        finally { TryDeleteDir(outDir); }
    }

    // A self-reference must be ignored, not reported as a dependency cycle.
    [Fact]
    [Trait("Category", "Build")]
    public void SelfReferencingAsmdef_CompilesWithoutCycleError()
    {
        WriteAssemblyDefinition("S", new AssemblyDefinition { Name = "SelfRef", References = { "SelfRef" } });
        WriteScript(Path.Combine("S", "S.cs"), "namespace SelfNs { public class S { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, $"Self-reference should be ignored, not a cycle:\n{result.Errors}");
    }

    // A platform-restricted asmdef ships only to that platform, so the default Game (which ships
    // everywhere) must NOT auto-reference it - otherwise excluded-platform builds dangle.
    [Fact]
    [Trait("Category", "Build")]
    public void PlatformRestrictedAsmdef_IsNotAutoReferencedByDefaultGame()
    {
        WriteAssemblyDefinition("Win", new AssemblyDefinition { Name = "WinOnly", IncludePlatforms = { BuildPlatforms.Windows } });
        WriteScript(Path.Combine("Win", "W.cs"), "namespace WinNs { public class W { } }");
        WriteScript("Loose.cs", "public class Loose { }");

        var result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, $"Compile failed:\n{result.Errors}");

        string gameProj = File.ReadAllText(Project.GameCsprojPath);
        Assert.DoesNotContain("Include=\"WinOnly\"", gameProj);
    }
}
