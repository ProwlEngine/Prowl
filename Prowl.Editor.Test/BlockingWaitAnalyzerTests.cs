// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Analyzers;
using Prowl.Editor.Projects.Scripting;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Verifies the analyzer that warns about blocking the main thread on a task, which never returns for
/// the asset loads and frame waits the main thread itself finishes.
/// </summary>
[Trait("Category", "Build")]
public class BlockingWaitAnalyzerTests : EditorTestHarness
{
    private const string Loader =
        "public static class Loader { public static System.Threading.Tasks.Task<int> Load() => System.Threading.Tasks.Task.FromResult(1); }";

    private Projects.Scripting.ScriptCompiler.CompileResult Compile(string component)
    {
        WriteScript("Loader.cs", Loader);
        WriteScript("Comp.cs", component);
        return ScriptCompiler.CompileAll(Project);
    }

    [Theory]
    [InlineData("int v = Loader.Load().Result;")]
    [InlineData("Loader.Load().Wait();")]
    [InlineData("int v = Loader.Load().GetAwaiter().GetResult();")]
    [InlineData("System.Threading.Tasks.Task.WaitAll(Loader.Load());")]
    public void WarnsOnEachWayOfBlockingInAComponent(string body)
    {
        var result = Compile($"public class Comp : Prowl.Runtime.MonoBehaviour {{ public override void Start() {{ {body} }} }}");

        Assert.True(result.Success, result.Errors); // a warning, so the compile still succeeds
        Assert.Contains(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAwait()
    {
        var result = Compile("public class Comp : Prowl.Runtime.MonoBehaviour { public override async void Start() { int v = await Loader.Load(); } }");

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
    }

    /// <summary>A lambda usually runs on some other thread, where blocking is fine.</summary>
    [Fact]
    public void SaysNothingInsideALambda()
    {
        var result = Compile(
            "public class Comp : Prowl.Runtime.MonoBehaviour { public override void Start() " +
            "{ System.Threading.Tasks.Task.Run(() => Loader.Load().Result); } }");

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
    }

    [Fact]
    public void SaysNothingOutsideAComponent()
    {
        var result = Compile("public class Plain { public int Get() => Loader.Load().Result; }");

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
    }
}
