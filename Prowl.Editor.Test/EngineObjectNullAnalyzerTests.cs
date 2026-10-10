// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Analyzers;
using Prowl.Editor.Projects.Scripting;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The analyzer that refuses '?.', '??' and '??=' on engine objects, which test only for a null reference and miss a
/// destroyed object.
/// </summary>
[Trait("Category", "Build")]
public class EngineObjectNullAnalyzerTests : EditorTestHarness
{
    private ScriptCompiler.CompileResult Compile(string members)
    {
        WriteScript("Comp.cs", $"using Prowl.Runtime;\npublic class Comp : Component {{ {members} }}");
        return ScriptCompiler.CompileAll(Project);
    }

    [Theory]
    [InlineData("string? N(GameObject? go) => go?.Name;", EngineObjectNullAnalyzer.NullConditionalId)]
    [InlineData("GameObject N(GameObject? a, GameObject b) => a ?? b;", EngineObjectNullAnalyzer.NullCoalescingId)]
    [InlineData("GameObject? _go; void N(GameObject b) { _go ??= b; }", EngineObjectNullAnalyzer.NullCoalescingId)]
    [InlineData("void N<T>(T? asset) where T : Asset { asset?.Load(); }", EngineObjectNullAnalyzer.NullConditionalId)]
    [InlineData("T N<T>(T? a, T b) where T : Component => a ?? b;", EngineObjectNullAnalyzer.NullCoalescingId)]
    [InlineData("void N<T, U>(T? a) where T : U where U : Asset { a?.Load(); }", EngineObjectNullAnalyzer.NullConditionalId)]
    public void FlagsNullOperatorsOnEngineObjectsIncludingGenerics(string members, string expectedId)
    {
        var result = Compile(members);
        Assert.False(result.Success);
        Assert.Contains(expectedId, result.Errors);
    }

    [Theory]
    [InlineData("string? N(string? s) => s?.Trim();")]
    [InlineData("string N<T>(T? x) where T : class => x?.ToString() ?? \"\";")]
    [InlineData("bool N(GameObject? go) => go.IsValid();")]
    public void LeavesOtherTypesAlone(string members)
    {
        var result = Compile(members);
        Assert.True(result.Success, result.Errors);
    }
}
