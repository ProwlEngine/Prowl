// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;

using Xunit;
using Xunit.Abstractions;

namespace Prowl.Origami.CSharp.Tests;

public class CodeActionTests
{
    private readonly ITestOutputHelper _output;

    public CodeActionTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<CSharpWorkspace> s_workspace = new(() => new CSharpWorkspace(CSharpWorkspace.GetPlatformReferences()));

    private async Task<(string Code, IReadOnlyList<CodeAction> Actions)> ActionsAt(string markedCode)
    {
        int caret = markedCode.IndexOf("$$", StringComparison.Ordinal);
        string code = markedCode.Replace("$$", "");
        var session = new RoslynCSharpSession(s_workspace.Value, $"Test{Guid.NewGuid():N}.cs", code);
        var actions = await session.GetCodeActionsAsync(caret, 0, default);
        _output.WriteLine(string.Join("\n", actions.Select(a => (a.IsQuickFix ? "[fix] " : "") + a.Title)));
        return (code, actions);
    }

    private static async Task<string> Apply(string code, CodeAction action)
    {
        var result = action.Resolve != null ? await action.Resolve(default) : new CodeRenameResult { Edits = action.Edits };
        Assert.NotNull(result);
        foreach (var edit in result!.Edits.OrderByDescending(e => e.Start))
            code = code.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
        return code;
    }

    [Fact]
    public void RoslynProvidersAreDiscovered()
    {
        _output.WriteLine($"fixers {RoslynCodeActions.FixerCount}, refactorings {RoslynCodeActions.RefactoringCount}");
        Assert.True(RoslynCodeActions.FixerCount > 50);
        Assert.True(RoslynCodeActions.RefactoringCount > 50);
    }

    [Fact]
    public async Task ImplementInterfaceFix()
    {
        var (code, actions) = await ActionsAt("class C : System.$$IDisposable { }");
        var action = Assert.Single(actions, a => a.IsQuickFix && a.Title == "Implement interface");
        string fixedCode = await Apply(code, action);
        Assert.Contains("public void Dispose()", fixedCode);
    }

    [Fact]
    public async Task GenerateMethodFix()
    {
        var (code, actions) = await ActionsAt("class C { void M() { $$Foo(1); } }");
        var action = actions.First(a => a.IsQuickFix && a.Title.Contains("Generate method", StringComparison.Ordinal));
        string fixedCode = await Apply(code, action);
        Assert.Contains("void Foo(int", fixedCode);
    }

    [Fact]
    public async Task ExpressionBodyRefactoring()
    {
        var (code, actions) = await ActionsAt("class C { int $$M() { return 1; } }");
        var action = actions.First(a => !a.IsQuickFix && a.Title.Contains("expression body", StringComparison.OrdinalIgnoreCase));
        string refactored = await Apply(code, action);
        Assert.Contains("=> 1;", refactored);
    }
}
