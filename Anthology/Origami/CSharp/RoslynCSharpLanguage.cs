// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.OrigamiUI.CSharp;

/// <summary>C# with Roslyn behind it. Each editor using it gets a <see cref="RoslynCSharpSession"/> in <see cref="Workspace"/>.</summary>
public class RoslynCSharpLanguage : CSharpLanguage
{
    public CSharpWorkspace Workspace { get; }

    /// <summary>Path or name the next session's document is registered under. Null picks an untitled name.</summary>
    public string? DocumentPath { get; set; }

    public RoslynCSharpLanguage(CSharpWorkspace workspace, string? documentPath = null)
    {
        Workspace = workspace;
        DocumentPath = documentPath;
    }

    public override bool IsCompletionTrigger(char c) => c == '.';
    public override bool IsFormatOnTypeTrigger(char c) => c is ';' or '}';

    public override CodeLanguageSession? CreateSession(CodeDocument document)
        => new RoslynCSharpSession(Workspace, DocumentPath, document.GetText("\n"));
}
