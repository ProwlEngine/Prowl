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
    [InlineData("Loader.Load().Wait(100);")]
    [InlineData("int v = Loader.Load().GetAwaiter().GetResult();")]
    [InlineData("int v = Loader.Load().ConfigureAwait(false).GetAwaiter().GetResult();")]
    [InlineData("int v = new System.Threading.Tasks.ValueTask<int>(Loader.Load()).Result;")]
    [InlineData("System.Threading.Tasks.Task.WaitAll(Loader.Load());")]
    [InlineData("int Local() => Loader.Load().Result; Local();")]
    [InlineData("var t = Loader.Load(); if (t.IsFaulted) { int v = t.Result; }")]
    [InlineData("var t = Loader.Load(); if (!t.IsCompleted) { int v = t.Result; }")]
    [InlineData("var t = Loader.Load(); if (t.IsCompleted || System.Environment.TickCount > 0) { int v = t.Result; }")]
    [InlineData("if (Loader.Load().IsCompleted) { int v = Loader.Load().Result; }")]
    [InlineData("var t = Loader.Load(); if (t.IsCompleted) { t = Loader.Load(); int v = t.Result; }")]
    [InlineData("var t = Loader.Load(); if (!t.IsCompleted) { } int v = t.Result;")]
    public void WarnsOnEachWayOfBlockingInAComponent(string body)
    {
        var result = Compile($"public class Comp : Prowl.Runtime.MonoBehaviour {{ public override void Start() {{ {body} }} }}");

        Assert.True(result.Success, result.Errors); // a warning, so the compile still succeeds
        Assert.Contains(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
    }

    [Theory]
    [InlineData("var t = Loader.Load(); if (t.IsCompleted) { int v = t.Result; }")]
    [InlineData("var t = Loader.Load(); int v = t.IsCompletedSuccessfully ? t.Result : 0;")]
    [InlineData("Loader.Load().Wait(0);")]
    [InlineData("Loader.Load().Wait(System.TimeSpan.Zero);")]
    [InlineData("var t = Loader.Load(); if (!t.IsCompleted) return; int v = t.Result;")]
    [InlineData("var t = Loader.Load(); if (t.IsCompleted) t.Wait();")]
    [InlineData("var t = Loader.Load(); if (t.IsCompleted) { int v = t.GetAwaiter().GetResult(); }")]
    [InlineData("var t = Loader.Load(); if (t.IsCompleted && t.Result > 0) { }")]
    [InlineData("System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { Loader.Load() }, 0);")]
    [InlineData("static int Local(System.Threading.Tasks.Task<int> t) => t.Result; Local(Loader.Load());")]
    public void SaysNothingAboutAReadThatCannotBlock(string body)
    {
        var result = Compile($"public class Comp : Prowl.Runtime.MonoBehaviour {{ public override void Start() {{ {body} }} }}");

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
    }

    /// <summary>A static helper is not tied to the component's thread, so it could be running anywhere.</summary>
    [Fact]
    public void SaysNothingInAStaticMethod()
    {
        var result = Compile("public class Comp : Prowl.Runtime.MonoBehaviour { static int Get() => Loader.Load().Result; }");

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(BlockingWaitAnalyzer.BlockingWaitId, result.Output);
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
