// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;
using System.Linq;

using Prowl.Editor.Projects.Scripting;
using Prowl.Ember.Analyzers;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>Verifies the hot-reload-safety analyzer surfaces (and can be suppressed) during the script compile.</summary>
[Trait("Category", "Build")]
public class HotReloadAnalyzerTests : EditorTestHarness
{
    [Fact]
    public void Analyzer_WarnsOnStaticFieldOfGenericType()
    {
        WriteScript("Gen.cs", "public class Box<T> { public static int Count; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);                 // it's a warning, not an error
        Assert.Contains(ReloadDiagnosticAnalyzer.StaticOnGenericType.Id, result.Output);
    }

    [Fact]
    public void Analyzer_WarnsOnStaticAutoPropertyOfGenericType()
    {
        WriteScript("Gen.cs", "public class Box<T> { public static int Count { get; set; } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.Contains(ReloadDiagnosticAnalyzer.StaticOnGenericType.Id, result.Output);
    }

    [Fact]
    public void Analyzer_ReloadIgnoreSuppressesTheWarning()
    {
        WriteScript("Gen.cs", "public class Box<T> { [Prowl.Ember.ReloadIgnore] public static int Count; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(ReloadDiagnosticAnalyzer.StaticOnGenericType.Id, result.Output);
    }

    [Fact]
    public void Analyzer_IgnoresStaticFieldOfNonGenericType()
    {
        WriteScript("Plain.cs", "public class Box { public static int Count; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(ReloadDiagnosticAnalyzer.StaticOnGenericType.Id, result.Output);
    }

    // The generated csproj registers the analyzer as an <Analyzer>, so the IDE surfaces the warning too.
    [Fact]
    public void GeneratedCsproj_RegistersTheAnalyzer()
    {
        WriteScript("Gen.cs", "public class Box<T> { public static int Count; }");

        ScriptCompiler.CompileAll(Project);

        string? csproj = Directory.EnumerateFiles(Project.RootPath, "*.csproj")
            .Select(File.ReadAllText)
            .FirstOrDefault(t => t.Contains("<Compile"));
        Assert.NotNull(csproj);
        Assert.Contains("<Analyzer Include=", csproj);
        Assert.Contains("Prowl.Ember.Analyzers.dll", csproj);
    }
}
