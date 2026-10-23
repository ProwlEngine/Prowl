using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;

namespace Prowl.Editor.GUI.ScriptEditor;

/// <summary>
/// Follows asset renames into the Script Editor, and offers to rename the type a script declares under its old file
/// name to the new one, with its uses in other scripts.
/// </summary>
internal static class ScriptRenames
{
    public static void OnAssetMoved(string oldRelative, string newRelative)
    {
        ScriptEditorWindow.OnAssetMoved(oldRelative, newRelative);

        if (!IsScript(oldRelative) || !IsScript(newRelative) || Project.Current is not { } project) return;
        string oldName = Path.GetFileNameWithoutExtension(oldRelative);
        string newName = Path.GetFileNameWithoutExtension(newRelative);
        if (oldName == newName || !SyntaxFacts.IsValidIdentifier(newName) || SyntaxFacts.GetKeywordKind(newName) != SyntaxKind.None) return;

        string oldPath = Path.GetFullPath(Path.Combine(project.AssetsPath, oldRelative));
        string newPath = Path.GetFullPath(Path.Combine(project.AssetsPath, newRelative));
        string? text = ScriptEditorWindow.EditorFor(newPath)?.Document.GetText("\n") ?? Read(newPath);
        if (text == null || DeclarationOffset(text, oldName) < 0) return;

        var dialog = new DialogModal { Title = "Rename class", Width = 420 };
        string message = $"{Path.GetFileName(newRelative)} declares {oldName}. Rename it to {newName} to match the file, along with its uses in other scripts?";
        dialog.DrawContent = paper => Origami.Label(paper, "sr_msg", message).Show();
        dialog.Button("Rename", () => { Modal.Remove(dialog); _ = RenameAsync(oldPath, newPath, oldName, newName); }, OrigamiVariant.Primary);
        dialog.Button("Keep Name", () => Modal.Remove(dialog));
        Modal.Push(dialog);
    }

    private static bool IsScript(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static string? Read(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static BaseTypeDeclarationSyntax? Declaration(string text, string name)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        return root.DescendantNodes(n => n is not BlockSyntax).OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(t => t.Identifier.ValueText == name);
    }

    // Offset of the name of the first type declared as name, or -1.
    private static int DeclarationOffset(string text, string name) => Declaration(text, name)?.Identifier.SpanStart ?? -1;

    // Saved scenes and assets name a component or serialized type by its class name, so a renamed class or struct
    // keeps answering to its old name.
    private static CodeTextEdit? FormerNameAttribute(string text, string oldName)
    {
        var type = Declaration(text, oldName);
        if (type is not (ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax)) return null;
        if (type.Modifiers.Any(m => m.RawKind == (int)SyntaxKind.StaticKeyword)) return null;

        int start = type.SpanStart;
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        string indent = text[lineStart..start];
        if (indent.Trim().Length > 0) indent = "";
        string newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        return new CodeTextEdit(start, 0, $"[Prowl.Echo.FormerlySerializedAs(\"{oldName}\")]{newLine}{indent}");
    }

    internal static async Task RenameAsync(string oldPath, string newPath, string oldName, string newName)
    {
        try
        {
            // An open script renames through its tab's session, any other through one of its own in the script's unit.
            var open = ScriptEditorWindow.EditorFor(newPath);
            CodeLanguageSession? session = open?.Session;
            RoslynCSharpSession? own = null;
            string? text;
            if (open != null && session != null) text = open.Document.GetText("\n");
            else
            {
                text = Read(newPath);
                if (text == null) return;
                var workspace = ScriptWorkspaces.Get(newPath, out bool fallback);
                if (fallback) workspace = ScriptWorkspaces.Get(oldPath, out _);
                workspace.RemoveDocument(oldPath);
                session = own = new RoslynCSharpSession(workspace, newPath, text);
            }

            int offset = DeclarationOffset(text, oldName);
            if (offset < 0) return;

            CodeRenameResult? result;
            try { result = await session.RenameAsync(offset, newName, CancellationToken.None); }
            finally { own?.Dispose(); }

            if (result == null)
            {
                EditorApplication.RunOnMainThread(() => Toasts.Warning("Rename class", $"{oldName} could not be renamed to {newName}."));
                return;
            }

            var fileEdits = new List<CodeTextEdit>(result.Edits);
            if (FormerNameAttribute(text, oldName) is { } attribute) fileEdits.Add(attribute);
            var edits = new Dictionary<string, IReadOnlyList<CodeTextEdit>>(result.OtherDocuments, StringComparer.OrdinalIgnoreCase) { [newPath] = fileEdits };
            EditorApplication.RunOnMainThread(() => ScriptEditorWindow.ApplyEditsAndSave(edits));
        }
        catch (Exception e)
        {
            Runtime.Debug.LogError($"[ScriptEditor] Renaming {oldName} to {newName} failed: {e.Message}");
        }
    }
}
