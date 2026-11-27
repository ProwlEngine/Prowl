// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;

using Xunit;

namespace Prowl.Origami.CSharp.Tests;

/// <summary>Go to definition on symbols from referenced assemblies: their source through the PDB, or the assembly name.</summary>
public class AssemblySourceTests
{
    private static readonly Lazy<IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference>> s_references = new(CSharpWorkspace.GetPlatformReferences);

    private static async Task<CodeLocation> DefinitionAt(string markedCode)
    {
        int caret = markedCode.IndexOf("$$", StringComparison.Ordinal);
        string code = markedCode.Replace("$$", "");
        var workspace = new CSharpWorkspace(s_references.Value);
        workspace.AddReference(typeof(CodeEditorOptions).Assembly.Location);
        var session = new RoslynCSharpSession(workspace, "Test.cs", code);
        return Assert.Single(await session.GetDefinitionAsync(caret, default));
    }

    [Fact]
    public async Task TypeFromAnAssemblyWithSources_OpensItsDeclaration()
    {
        var location = await DefinitionAt("class C { Prowl.OrigamiUI.CodeEditor$$Options o; }");

        Assert.NotNull(location.Path);
        Assert.EndsWith("CodeEditorStyle.cs", location.Path);
        Assert.Equal(146, location.Line);
        Assert.Null(location.Assembly);
    }

    [Fact]
    public async Task MethodFromAnAssemblyWithSources_OpensItsDeclaration()
    {
        var location = await DefinitionAt("class C { void M(Prowl.OrigamiUI.CodeEditor e) { e.Format$$Document(); } }");

        Assert.NotNull(location.Path);
        Assert.EndsWith("CodeEditor.Popups.cs", location.Path);
        Assert.Equal(1135, location.Line);
    }

    [Fact]
    public async Task FrameworkType_HasNoSourceAndNamesItsAssembly()
    {
        var location = await DefinitionAt("class C { System.Str$$ing s; }");

        Assert.Null(location.Path);
        Assert.False(string.IsNullOrEmpty(location.Assembly));
    }
}
