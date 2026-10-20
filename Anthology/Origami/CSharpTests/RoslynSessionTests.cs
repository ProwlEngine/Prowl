// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Microsoft.CodeAnalysis;

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;

using Xunit;

namespace Prowl.Origami.CSharp.Tests;

public class RoslynSessionTests
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> s_references = new(CSharpWorkspace.GetPlatformReferences);

    private static (RoslynCSharpSession Session, int Caret) Open(string markedCode, CSharpWorkspace? workspace = null, string? path = null)
    {
        int caret = markedCode.IndexOf("$$", StringComparison.Ordinal);
        string code = markedCode.Replace("$$", "");
        workspace ??= new CSharpWorkspace(s_references.Value);
        return (new RoslynCSharpSession(workspace, path, code), caret);
    }

    private static string Apply(string text, IEnumerable<CodeTextEdit> edits)
    {
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText);
        return text;
    }

    private static string Text(RoslynCSharpSession session) => session.Document!.GetTextAsync().Result.ToString();

    [Fact]
    public async Task CompletionAfterDotListsMembers()
    {
        var (session, caret) = Open("using System;\nclass C\n{\n    void M()\n    {\n        Console.$$\n    }\n}");
        var list = await session.GetCompletionsAsync(caret, new CodeCompletionTrigger(CodeCompletionTriggerKind.Character, '.'), default);

        Assert.NotNull(list);
        Assert.Contains(list!.Items, i => i.Label == "WriteLine");
        Assert.Equal(caret, list.SpanStart);
    }

    [Fact]
    public async Task CompletionIsNotTriggeredBySpace()
    {
        var (session, caret) = Open("class C\n{\n    void M()\n    {\n        int x = 1 $$\n    }\n}");
        var list = await session.GetCompletionsAsync(caret, new CodeCompletionTrigger(CodeCompletionTriggerKind.Character, ' '), default);
        Assert.Null(list);
    }

    [Fact]
    public async Task CompletionDescriptionHasSignature()
    {
        var (session, caret) = Open("using System;\nclass C\n{\n    void M()\n    {\n        Console.$$\n    }\n}");
        var list = await session.GetCompletionsAsync(caret, CodeCompletionTrigger.Invoke, default);
        var item = list!.Items.First(i => i.Label == "WriteLine");

        var description = await session.GetCompletionDescriptionAsync(item, default);

        Assert.NotNull(description);
        Assert.NotEmpty(description!.Signature);
        Assert.Contains("WriteLine", string.Concat(description.Signature.Select(p => p.Text)));
    }

    [Fact]
    public async Task ResolveCompletionReplacesTypedFilterText()
    {
        var document = new CodeDocument("using System;\nclass C\n{\n    void M()\n    {\n        Console.Wr\n    }\n}");
        var session = new RoslynCSharpSession(new CSharpWorkspace(s_references.Value), null, document.GetText("\n"));
        int lineLength = document.GetLineLength(5);
        int caret = document.GetText("\n").IndexOf("Wr", StringComparison.Ordinal) + 2;

        var list = await session.GetCompletionsAsync(caret, CodeCompletionTrigger.Invoke, default);
        var item = list!.Items.First(i => i.Label == "WriteLine");

        session.OnTextChanged(document.Insert(new TextPosition(5, lineLength), "it"));
        var commit = session.ResolveCompletion(list, item, caret + 2, '\0');

        Assert.NotNull(commit);
        string result = Apply(document.GetText("\n"), commit!.Edits);
        Assert.Contains("        Console.WriteLine\n", result);
        var last = commit.Edits.OrderBy(e => e.Start).Last();
        int caretAfter = commit.CaretOffset ?? last.Start + last.NewText.Length;
        Assert.Equal(result.IndexOf("WriteLine", StringComparison.Ordinal) + "WriteLine".Length, caretAfter);
    }

    [Fact]
    public async Task QuickInfoOnType()
    {
        var (session, caret) = Open("class C\n{\n    System.Text.String$$Builder sb;\n}");
        var info = await session.GetQuickInfoAsync(caret, default);

        Assert.NotNull(info);
        Assert.Contains("StringBuilder", string.Concat(info!.Signature.Select(p => p.Text)));
    }

    [Fact]
    public async Task SignatureHelpActiveParameterAfterComma()
    {
        string code = "using System;\nclass C\n{\n    void M()\n    {\n        Math.Max(1, $$);\n    }\n}";
        var (session, caret) = Open(code);
        var help = await session.GetSignatureHelpAsync(caret, default);

        Assert.NotNull(help);
        Assert.NotEmpty(help!.Signatures);
        Assert.Equal(1, help.ActiveParameter);
        Assert.Equal(code.IndexOf("Max(", StringComparison.Ordinal) + 3, help.ArgumentListStart);

        var signature = help.Signatures[help.ActiveSignature];
        string joined = string.Concat(signature.Parts.Select(p => p.Text));
        var second = signature.Parameters[1];
        Assert.Equal("int val2", joined.Substring(second.LabelStart, second.LabelLength));
    }

    [Fact]
    public async Task SignatureHelpNamedArgument()
    {
        var (session, caret) = Open("class C\n{\n    void Go(int a, string b) { }\n    void M()\n    {\n        Go(b: $$);\n    }\n}");
        var help = await session.GetSignatureHelpAsync(caret, default);

        Assert.NotNull(help);
        Assert.Equal(1, help!.ActiveParameter);
    }

    [Fact]
    public async Task SignatureHelpForConstructor()
    {
        var (session, caret) = Open("class P\n{\n    /// <summary>Makes a P.</summary>\n    public P(int x, int y) { }\n    void M()\n    {\n        var p = new P($$\n    }\n}");
        var help = await session.GetSignatureHelpAsync(caret, default);

        Assert.NotNull(help);
        Assert.Equal(0, help!.ActiveParameter);
        Assert.Equal("Makes a P.", help.Signatures[help.ActiveSignature].Documentation);
    }

    [Fact]
    public async Task DiagnosticsReportUndefinedName()
    {
        string code = "class C\n{\n    void M()\n    {\n        int x = missing;\n    }\n}";
        var (session, _) = Open(code);
        var diagnostics = await session.GetDiagnosticsAsync(default);

        var d = Assert.Single(diagnostics, d => d.Id == "CS0103");
        Assert.Equal(CodeDiagnosticSeverity.Error, d.Severity);
        Assert.Equal(code.IndexOf("missing", StringComparison.Ordinal), d.Start);
        Assert.Equal("missing".Length, d.Length);
    }

    [Fact]
    public async Task ClassificationMarksClassName()
    {
        string code = "class Foo\n{\n    Foo other;\n}";
        var (session, _) = Open(code);
        var spans = await session.GetClassificationsAsync(0, code.Length, default);

        int usage = code.LastIndexOf("Foo", StringComparison.Ordinal);
        Assert.Contains(spans, s => s.Start == usage && s.Length == 3 && s.Kind == CodeTokenKind.Class);
        Assert.Contains(spans, s => s.Start == 0 && s.Length == 5 && s.Kind == CodeTokenKind.Keyword);
    }

    [Fact]
    public async Task DefinitionOfLocalFunction()
    {
        string code = "class C\n{\n    void M()\n    {\n        Lo$$cal();\n        void Local() { }\n    }\n}";
        var (session, caret) = Open(code);
        var locations = await session.GetDefinitionAsync(caret, default);

        var location = Assert.Single(locations);
        Assert.True(location.IsCurrentDocument);
        Assert.Equal(code.Replace("$$", "").IndexOf("Local() { }", StringComparison.Ordinal), location.Start);
    }

    [Fact]
    public async Task DefinitionInOtherDocument()
    {
        var workspace = new CSharpWorkspace(s_references.Value);
        string helperPath = Path.Combine(Path.GetTempPath(), "Helper.cs");
        workspace.AddOrUpdateDocument(helperPath, "public static class Helper\n{\n    public static void Run() { }\n}");
        var (session, caret) = Open("class C\n{\n    void M() => Helper.R$$un();\n}", workspace);

        var location = Assert.Single(await session.GetDefinitionAsync(caret, default));
        Assert.False(location.IsCurrentDocument);
        Assert.Equal(helperPath, location.Path);
        Assert.Equal(2, location.Line);

        var diagnostics = await session.GetDiagnosticsAsync(default);
        Assert.DoesNotContain(diagnostics, d => d.Severity == CodeDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DefinitionOfMetadataSymbol()
    {
        var (session, caret) = Open("class C\n{\n    void M() => System.Console.Write$$Line();\n}");
        var location = Assert.Single(await session.GetDefinitionAsync(caret, default));
        Assert.Null(location.Path);
        Assert.Contains("Console.WriteLine", location.Display);
    }

    [Fact]
    public async Task FindReferencesAndHighlights()
    {
        string code = "class C\n{\n    int value;\n    void M() { value = 1; value++; }\n}";
        var (session, _) = Open(code);
        int offset = code.IndexOf("value", StringComparison.Ordinal);

        var references = await session.FindReferencesAsync(offset, default);
        Assert.Equal(3, references.Count);
        Assert.Contains(references, r => r.Display == "int value;");

        var highlights = await session.GetHighlightsAsync(offset, default);
        Assert.Equal(3, highlights.Count);
    }

    [Fact]
    public async Task RenameEditsEveryUse()
    {
        string code = "class C\n{\n    int value;\n    void M() { value = 1; }\n}";
        var (session, _) = Open(code);
        var result = await session.RenameAsync(code.IndexOf("value", StringComparison.Ordinal), "count", default);

        Assert.NotNull(result);
        Assert.Equal("class C\n{\n    int count;\n    void M() { count = 1; }\n}", Apply(code, result!.Edits));
        Assert.Empty(result.OtherDocuments);
    }

    [Fact]
    public async Task RenameRejectsKeyword()
    {
        string code = "class C\n{\n    int value;\n}";
        var (session, _) = Open(code);
        Assert.Null(await session.RenameAsync(code.IndexOf("value", StringComparison.Ordinal), "class", default));
    }

    [Fact]
    public async Task AddUsingForStringBuilder()
    {
        string code = "class C\n{\n    void M()\n    {\n        var sb = new StringBuilder();\n    }\n}";
        var (session, _) = Open(code);
        int at = code.IndexOf("StringBuilder", StringComparison.Ordinal);
        var actions = await session.GetCodeActionsAsync(at, 0, default);

        var addUsing = Assert.Single(actions, a => a.Title == "using System.Text;");
        Assert.True(addUsing.IsQuickFix);
        Assert.StartsWith("using System.Text;\n\nclass C", Apply(code, addUsing.Edits));

        var qualify = Assert.Single(actions, a => a.Title == "System.Text.StringBuilder");
        Assert.Contains("new System.Text.StringBuilder()", Apply(code, qualify.Edits));
    }

    [Fact]
    public async Task AddUsingIsSortedIntoExistingBlock()
    {
        string code = "// Header\n\nusing System;\nusing System.Threading;\n\nclass C\n{\n    StringBuilder sb;\n}";
        var (session, _) = Open(code);
        var actions = await session.GetCodeActionsAsync(code.IndexOf("StringBuilder", StringComparison.Ordinal), 0, default);

        var addUsing = Assert.Single(actions, a => a.Title == "using System.Text;");
        Assert.StartsWith("// Header\n\nusing System;\nusing System.Text;\nusing System.Threading;\n", Apply(code, addUsing.Edits));
    }

    [Fact]
    public async Task AddUsingForExtensionMethod()
    {
        string code = "class C\n{\n    void M(int[] items)\n    {\n        var x = items.Select(i => i);\n    }\n}";
        var (session, _) = Open(code);
        var actions = await session.GetCodeActionsAsync(code.IndexOf("Select", StringComparison.Ordinal), 0, default);
        Assert.Contains(actions, a => a.Title == "using System.Linq;");
    }

    [Fact]
    public async Task FormatFixesIndentation()
    {
        string code = "class C\n{\nvoid M()\n{\nint x=1;\n}\n}";
        var (session, _) = Open(code);
        var edits = await session.FormatAsync(0, -1, default);

        Assert.NotEmpty(edits);
        string formatted = Apply(code, edits);
        Assert.Equal("class C\n{\n    void M()\n    {\n        int x = 1;\n    }\n}", formatted);
    }

    [Fact]
    public async Task EditsKeepOffsetsInSync()
    {
        var document = new CodeDocument("using System;\nclass C\n{\n    void M()\n    {\n        \n    }\n}");
        var session = new RoslynCSharpSession(new CSharpWorkspace(s_references.Value), null, document.GetText("\n"));

        session.OnTextChanged(document.Insert(new TextPosition(5, 8), "Console"));
        session.OnTextChanged(document.Insert(new TextPosition(5, 15), "."));
        Assert.Equal(document.GetText("\n"), Text(session));

        int caret = document.GetText("\n").IndexOf("Console.", StringComparison.Ordinal) + "Console.".Length;
        var list = await session.GetCompletionsAsync(caret, new CodeCompletionTrigger(CodeCompletionTriggerKind.Character, '.'), default);
        Assert.NotNull(list);
        Assert.Contains(list!.Items, i => i.Label == "WriteLine");
        Assert.Equal(caret, list.SpanStart);

        session.OnTextChanged(document.Delete(new TextRange(new TextPosition(5, 8), new TextPosition(5, 16))));
        Assert.Equal(document.GetText("\n"), Text(session));
    }

    [Fact]
    public void DisposeRemovesDocument()
    {
        var workspace = new CSharpWorkspace(s_references.Value);
        var (session, _) = Open("class C { }", workspace);
        var id = session.DocumentId;
        Assert.NotNull(workspace.GetDocument(id));

        session.Dispose();
        Assert.Null(workspace.GetDocument(id));
    }

    [Fact]
    public void ClosingAnExtraDocumentRestoresItsText()
    {
        var workspace = new CSharpWorkspace(s_references.Value);
        workspace.AddOrUpdateDocument("Script.cs", "class Script { }");
        var (session, _) = Open("class Script { int edited; }", workspace, "Script.cs");
        Assert.Single(workspace.Project.Documents);

        session.Dispose();
        var document = Assert.Single(workspace.Project.Documents);
        Assert.Equal("class Script { }", document.GetTextAsync().Result.ToString());
    }
}
