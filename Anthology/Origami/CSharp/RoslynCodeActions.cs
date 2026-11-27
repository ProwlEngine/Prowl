// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

using ApplyChangesOperation = Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation;
using CodeActionWithOptions = Microsoft.CodeAnalysis.CodeActions.CodeActionWithOptions;
using RoslynCodeAction = Microsoft.CodeAnalysis.CodeActions.CodeAction;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>
/// Roslyn's own C# code fixes and refactorings, the ones Visual Studio offers on Ctrl+. The providers are found by
/// their public export attributes in the Features assemblies, since Roslyn exposes no public way to list them.
/// </summary>
internal static class RoslynCodeActions
{
    private static readonly string[] s_assemblies = { "Microsoft.CodeAnalysis.CSharp.Features", "Microsoft.CodeAnalysis.Features" };

    private static readonly Lazy<CodeFixProvider[]> s_fixers = new(() => Load<CodeFixProvider>(type =>
        type.GetCustomAttribute<ExportCodeFixProviderAttribute>() is { } export && export.Languages.Contains(LanguageNames.CSharp)));

    private static readonly Lazy<CodeRefactoringProvider[]> s_refactorings = new(() => Load<CodeRefactoringProvider>(type =>
        type.GetCustomAttribute<ExportCodeRefactoringProviderAttribute>() is { } export && export.Languages.Contains(LanguageNames.CSharp)));

    internal static int FixerCount => s_fixers.Value.Length;
    internal static int RefactoringCount => s_refactorings.Value.Length;

    private static T[] Load<T>(Func<Type, bool> exportedForCSharp) where T : class
    {
        var providers = new List<T>();
        foreach (string name in s_assemblies)
        {
            Assembly assembly;
            try { assembly = Assembly.Load(name); }
            catch (Exception) { continue; }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }

            foreach (Type type in types)
            {
                if (type.IsAbstract || type.ContainsGenericParameters || !typeof(T).IsAssignableFrom(type)) continue;
                if (!exportedForCSharp(type)) continue;
                if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) == null) continue;
                try { providers.Add((T)Activator.CreateInstance(type, nonPublic: true)!); }
                catch (Exception) { }
            }
        }
        return providers.ToArray();
    }

    public static async Task<IReadOnlyList<CodeAction>> GetAsync(Document document, TextSpan span, CancellationToken ct)
    {
        SemanticModel? model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        SourceText text = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (model == null) return Array.Empty<CodeAction>();

        // Fixes apply to diagnostics anywhere on the caret's lines, as Visual Studio's bulb does.
        TextLineCollection lines = text.Lines;
        int firstLine = lines.GetLineFromPosition(span.Start).LineNumber;
        int lastLine = lines.GetLineFromPosition(Math.Min(text.Length, span.End)).LineNumber;
        var lineSpan = TextSpan.FromBounds(lines[firstLine].Start, lines[lastLine].End);

        var diagnostics = model.SyntaxTree.GetDiagnostics(ct).Where(d => d.Location.SourceSpan.IntersectsWith(lineSpan))
            .Concat(model.GetDiagnostics(lineSpan, ct))
            .Where(d => !d.IsSuppressed && d.Location.IsInSource)
            .ToList();

        var fixes = new List<RoslynCodeAction>();
        foreach (Diagnostic? diagnostic in diagnostics)
        {
            foreach (CodeFixProvider fixer in s_fixers.Value)
            {
                if (!fixer.FixableDiagnosticIds.Contains(diagnostic.Id)) continue;
                try
                {
                    var context = new CodeFixContext(document, diagnostic, (action, _) => { lock (fixes) fixes.Add(action); }, ct);
                    await fixer.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
            }
        }

        var refactorings = new List<RoslynCodeAction>();
        foreach (CodeRefactoringProvider provider in s_refactorings.Value)
        {
            try
            {
                var context = new CodeRefactoringContext(document, span, action => { lock (refactorings) refactorings.Add(action); }, ct);
                await provider.ComputeRefactoringsAsync(context).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) { }
        }

        var result = new List<CodeAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (RoslynCodeAction action in fixes) Add(action, "", true);
        foreach (RoslynCodeAction action in refactorings) Add(action, "", false);
        return result;

        // Grouped actions, such as the variants of "Generate method", become "Group: Variant" entries.
        void Add(RoslynCodeAction action, string prefix, bool isFix)
        {
            // These ask the user through a Visual Studio dialog, such as picking members, which a plain workspace cannot show.
            if (action is CodeActionWithOptions) return;
            string title = prefix + action.Title;
            if (!action.NestedActions.IsDefaultOrEmpty)
            {
                foreach (RoslynCodeAction nested in action.NestedActions) Add(nested, title + ": ", isFix);
                return;
            }
            if (!seen.Add(title)) return;
            result.Add(new CodeAction { Title = title, IsQuickFix = isFix, Resolve = ct => ResolveAsync(document, action, ct) });
        }
    }

    private static async Task<CodeRenameResult?> ResolveAsync(Document document, RoslynCodeAction action, CancellationToken ct)
    {
        System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.CodeActions.CodeActionOperation> operations = await action.GetOperationsAsync(ct).ConfigureAwait(false);
        ApplyChangesOperation? apply = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (apply == null) return null;

        Solution before = document.Project.Solution;
        Solution after = apply.ChangedSolution;
        var edits = new List<CodeTextEdit>();
        var others = new Dictionary<string, IReadOnlyList<CodeTextEdit>>();
        foreach (ProjectChanges projectChanges in after.GetChanges(before).GetProjectChanges())
        {
            foreach (DocumentId id in projectChanges.GetChangedDocuments())
            {
                Document oldDocument = before.GetDocument(id)!;
                Document newDocument = after.GetDocument(id)!;
                IEnumerable<Microsoft.CodeAnalysis.Text.TextChange> changes = await newDocument.GetTextChangesAsync(oldDocument, ct).ConfigureAwait(false);
                var mapped = changes.Select(c => new CodeTextEdit(c.Span.Start, c.Span.Length, c.NewText ?? "")).ToList();
                if (id == document.Id) edits.AddRange(mapped);
                else others[oldDocument.FilePath ?? oldDocument.Name] = mapped;
            }
        }
        return new CodeRenameResult { Edits = edits, OtherDocuments = others };
    }
}
