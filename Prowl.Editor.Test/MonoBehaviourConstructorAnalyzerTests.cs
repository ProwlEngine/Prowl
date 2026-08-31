// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Analyzers;
using Prowl.Editor.Projects.Scripting;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Verifies the analyzer that steers users away from constructors on components, and refuses one
/// the engine could never construct at all.
/// </summary>
[Trait("Category", "Build")]
public class MonoBehaviourConstructorAnalyzerTests : EditorTestHarness
{
    [Fact]
    public void WarnsOnAParameterlessConstructor()
    {
        WriteScript("Ctor.cs", "public class Ctor : Prowl.Runtime.MonoBehaviour { public Ctor() { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors); // a warning, so the compile still succeeds
        Assert.Contains(MonoBehaviourConstructorAnalyzer.DeclaredConstructorId, result.Output);
    }

    /// <summary>
    /// The shape behind the crash this came from: nothing can create it, so it could be dragged
    /// onto the inspector and silently do nothing.
    /// </summary>
    [Fact]
    public void ErrorsWhenNothingCanConstructIt()
    {
        WriteScript("NeedsArgs.cs", "public class NeedsArgs : Prowl.Runtime.MonoBehaviour { public NeedsArgs(int x) { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.Contains(MonoBehaviourConstructorAnalyzer.NoParameterlessConstructorId, result.Output);
    }

    [Fact]
    public void APrivateParameterlessConstructorStillCannotBeConstructed()
    {
        WriteScript("Hidden.cs", "public class Hidden : Prowl.Runtime.MonoBehaviour { private Hidden() { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.Contains(MonoBehaviourConstructorAnalyzer.NoParameterlessConstructorId, result.Output);
    }

    /// <summary>An overload the engine can reach is enough, even though the pair is still discouraged.</summary>
    [Fact]
    public void APublicParameterlessOverloadIsConstructableButStillWarns()
    {
        WriteScript("Both.cs",
            "public class Both : Prowl.Runtime.MonoBehaviour { public Both() { } public Both(int x) { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.Contains(MonoBehaviourConstructorAnalyzer.DeclaredConstructorId, result.Output);
        Assert.DoesNotContain(MonoBehaviourConstructorAnalyzer.NoParameterlessConstructorId, result.Output);
    }

    /// <summary>An abstract component is never constructed directly, so only the warning applies.</summary>
    [Fact]
    public void AnAbstractComponentIsNotAskedToBeConstructable()
    {
        WriteScript("Base.cs",
            "public abstract class BaseThing : Prowl.Runtime.MonoBehaviour { protected BaseThing(int x) { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.Contains(MonoBehaviourConstructorAnalyzer.DeclaredConstructorId, result.Output);
        Assert.DoesNotContain(MonoBehaviourConstructorAnalyzer.NoParameterlessConstructorId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAComponentWithNoConstructor()
    {
        WriteScript("Plain.cs", "public class Plain : Prowl.Runtime.MonoBehaviour { public int Speed = 5; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(MonoBehaviourConstructorAnalyzer.DeclaredConstructorId, result.Output);
        Assert.DoesNotContain(MonoBehaviourConstructorAnalyzer.NoParameterlessConstructorId, result.Output);
    }

    /// <summary>A plain class is nothing to do with the engine's construction, so it is left alone.</summary>
    [Fact]
    public void SaysNothingAboutANonComponent()
    {
        WriteScript("Helper.cs", "public class Helper { public Helper(int x) { } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(MonoBehaviourConstructorAnalyzer.DeclaredConstructorId, result.Output);
        Assert.DoesNotContain(MonoBehaviourConstructorAnalyzer.NoParameterlessConstructorId, result.Output);
    }
}
