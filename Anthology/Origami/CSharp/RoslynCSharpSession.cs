// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

using RoslynTextChange = Microsoft.CodeAnalysis.Text.TextChange;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>Roslyn features for one open editor. Its document lives in a <see cref="CSharpWorkspace"/> until disposed.</summary>
public sealed partial class RoslynCSharpSession : CodeLanguageSession
{
    private sealed record CompletionState(Document Document, TextSpan Span, int TextLength);

    private readonly CSharpWorkspace _workspace;
    private readonly ConditionalWeakTable<CodeCompletionList, CompletionState> _lists = new();
    private SourceText _text;
    private bool _disposed;

    public DocumentId DocumentId { get; }

    public RoslynCSharpSession(CSharpWorkspace workspace, string? documentPath, string text)
    {
        _workspace = workspace;
        _text = SourceText.From(text);
        DocumentId = workspace.OpenDocument(documentPath, _text);
    }

    /// <summary>This session's document as of now. Null once disposed.</summary>
    public Document? Document => _disposed ? null : _workspace.GetDocument(DocumentId);

    public override void OnTextReset(string text)
    {
        _text = SourceText.From(text);
        _workspace.UpdateOpenDocument(DocumentId, _text);
    }

    public override void OnTextChanged(Prowl.OrigamiUI.TextChange change)
    {
        _text = _text.WithChanges(new RoslynTextChange(new TextSpan(change.StartOffset, change.OldText.Length), change.NewText));
        _workspace.UpdateOpenDocument(DocumentId, _text);
    }

    private Task<T> Run<T>(T fallback, Func<Document, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        var document = Document;
        if (document == null) return Task.FromResult(fallback);
        return Task.Run(() => work(document, ct), ct);
    }

    public override Task<CodeCompletionList?> GetCompletionsAsync(int offset, CodeCompletionTrigger trigger, CancellationToken ct)
        => Run<CodeCompletionList?>(null, async (document, ct) =>
        {
            var service = CompletionService.GetService(document);
            if (service == null) return null;

            var text = await document.GetTextAsync(ct).ConfigureAwait(false);
            offset = Math.Clamp(offset, 0, text.Length);
            var roslynTrigger = trigger.Kind switch
            {
                CodeCompletionTriggerKind.Character => CompletionTrigger.CreateInsertionTrigger(trigger.Character),
                CodeCompletionTriggerKind.Deletion => CompletionTrigger.CreateDeletionTrigger(trigger.Character),
                _ => CompletionTrigger.Invoke,
            };
            if (trigger.Kind == CodeCompletionTriggerKind.Character && !service.ShouldTriggerCompletion(text, offset, roslynTrigger))
                return null;

            var list = await service.GetCompletionsAsync(document, offset, roslynTrigger, cancellationToken: ct).ConfigureAwait(false);
            if (list.ItemsList.Count == 0) return null;

            var defaultCommit = list.Rules.DefaultCommitCharacters;
            var items = new List<CodeCompletionItem>(list.ItemsList.Count);
            foreach (var item in list.ItemsList)
            {
                ct.ThrowIfCancellationRequested();
                items.Add(new CodeCompletionItem
                {
                    Label = item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix,
                    Kind = RoslynMapping.CompletionKind(item.Tags),
                    Detail = string.IsNullOrEmpty(item.InlineDescription) ? null : item.InlineDescription,
                    FilterText = item.FilterText,
                    SortText = item.SortText,
                    Preselect = item.Rules.MatchPriority >= MatchPriority.Preselect,
                    CommitCharacters = CommitCharacters(defaultCommit, item.Rules.CommitCharacterRules),
                    Data = item,
                });
            }

            var result = new CodeCompletionList
            {
                Items = items,
                SpanStart = list.Span.Start,
                SuggestionMode = list.SuggestionModeItem != null,
            };
            _lists.AddOrUpdate(result, new CompletionState(document, list.Span, text.Length));
            return result;
        }, ct);

    private static string CommitCharacters(ImmutableArray<char> defaults, ImmutableArray<CharacterSetModificationRule> rules)
    {
        if (rules.IsDefaultOrEmpty) return new string(defaults.AsSpan());
        var set = new List<char>(defaults);
        foreach (var rule in rules)
        {
            switch (rule.Kind)
            {
                case CharacterSetModificationKind.Add: set.AddRange(rule.Characters); break;
                case CharacterSetModificationKind.Remove: set.RemoveAll(rule.Characters.Contains); break;
                case CharacterSetModificationKind.Replace: set = new List<char>(rule.Characters); break;
            }
        }
        return new string(set.Distinct().ToArray());
    }

    public override Task<CodeQuickInfo?> GetCompletionDescriptionAsync(CodeCompletionItem item, CancellationToken ct)
        => Run<CodeQuickInfo?>(null, async (document, ct) =>
        {
            if (item.Data is not CompletionItem roslynItem) return null;
            var service = CompletionService.GetService(document);
            if (service == null) return null;

            var description = await service.GetDescriptionAsync(document, roslynItem, ct).ConfigureAwait(false);
            if (description == null || description.TaggedParts.IsDefaultOrEmpty) return null;

            var parts = description.TaggedParts;
            int lineBreak = parts.IndexOf(parts.FirstOrDefault(p => p.Tag == TextTags.LineBreak));
            var signature = lineBreak < 0 ? parts : parts.Take(lineBreak);
            var rest = lineBreak < 0 ? Enumerable.Empty<TaggedText>() : parts.Skip(lineBreak + 1);
            return new CodeQuickInfo
            {
                Signature = RoslynMapping.Tagged(signature),
                Documentation = Paragraphs(rest),
            };
        }, ct);

    private static List<string> Paragraphs(IEnumerable<TaggedText> parts)
    {
        var paragraphs = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            string s = current.ToString().Trim();
            if (s.Length > 0) paragraphs.Add(s);
            current.Clear();
        }
        foreach (var part in parts)
        {
            if (part.Tag == TextTags.LineBreak) Flush();
            else current.Append(part.Text);
        }
        Flush();
        return paragraphs;
    }

    public override CodeCompletionCommit? ResolveCompletion(CodeCompletionList list, CodeCompletionItem item, int caretOffset, char commitChar)
    {
        if (item.Data is not CompletionItem roslynItem || !_lists.TryGetValue(list, out var state)) return null;
        var service = CompletionService.GetService(state.Document);
        if (service == null) return null;

        // The list was computed before the user typed more filter text, so its edits are moved onto the current text.
        int delta = _text.Length - state.TextLength;
        int spanStart = state.Span.Start, spanEnd = state.Span.End;
        if (spanEnd + delta < spanStart) return null;

        char? commit = commitChar == '\0' ? null : commitChar;
        var change = Task.Run(() => service.GetChangeAsync(state.Document, roslynItem, commit)).GetAwaiter().GetResult();

        var edits = new List<CodeTextEdit>();
        foreach (var tc in change.TextChanges)
        {
            int start = tc.Span.Start, end = tc.Span.End;
            if (start > spanStart && start >= spanEnd) { start += delta; end += delta; }
            else if (end >= spanEnd) end += delta;
            edits.Add(new CodeTextEdit(start, end - start, tc.NewText ?? ""));
        }
        return new CodeCompletionCommit { Edits = edits, CaretOffset = change.NewPosition };
    }

    public override Task<CodeQuickInfo?> GetQuickInfoAsync(int offset, CancellationToken ct)
        => Run<CodeQuickInfo?>(null, async (document, ct) =>
        {
            var service = QuickInfoService.GetService(document);
            if (service == null) return null;
            var info = await service.GetQuickInfoAsync(document, offset, ct).ConfigureAwait(false);
            if (info == null) return null;

            var signature = new List<CodeTaggedText>();
            var documentation = new List<string>();
            foreach (var section in info.Sections)
            {
                if (section.Kind == QuickInfoSectionKinds.Description)
                    signature.AddRange(RoslynMapping.Tagged(section.TaggedParts));
                else
                    documentation.AddRange(Paragraphs(section.TaggedParts));
            }
            if (signature.Count == 0 && documentation.Count == 0) return null;
            return new CodeQuickInfo
            {
                SpanStart = info.Span.Start,
                SpanLength = info.Span.Length,
                Signature = signature,
                Documentation = documentation,
            };
        }, ct);

    public override Task<CodeSignatureHelp?> GetSignatureHelpAsync(int offset, CancellationToken ct)
        => Run<CodeSignatureHelp?>(null, async (document, ct) =>
        {
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            return model == null ? null : SignatureHelpBuilder.Compute(model, offset, ct);
        }, ct);

    public override Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(CancellationToken ct)
        => Run<IReadOnlyList<CodeDiagnostic>>(Array.Empty<CodeDiagnostic>(), async (document, ct) =>
        {
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (model == null) return Array.Empty<CodeDiagnostic>();

            var tree = model.SyntaxTree;
            var seen = new HashSet<(string, TextSpan)>();
            var result = new List<CodeDiagnostic>();
            var all = tree.GetDiagnostics(ct).Concat(model.GetDiagnostics(cancellationToken: ct))
                .Concat(await AnalyzerDiagnosticsAsync(model, ct).ConfigureAwait(false));
            foreach (var d in all)
            {
                if (d.Severity == DiagnosticSeverity.Hidden || d.IsSuppressed || d.Location.SourceTree != tree) continue;
                var span = d.Location.SourceSpan;
                if (!seen.Add((d.Id, span))) continue;
                result.Add(new CodeDiagnostic
                {
                    Start = span.Start,
                    Length = span.Length,
                    Severity = d.Severity switch
                    {
                        DiagnosticSeverity.Error => CodeDiagnosticSeverity.Error,
                        DiagnosticSeverity.Warning => CodeDiagnosticSeverity.Warning,
                        _ => CodeDiagnosticSeverity.Info,
                    },
                    Id = d.Id,
                    Message = d.GetMessage(),
                });
            }
            return result;
        }, ct);

    private async Task<IEnumerable<Diagnostic>> AnalyzerDiagnosticsAsync(SemanticModel model, CancellationToken ct)
    {
        var analyzers = _workspace.Analyzers;
        if (analyzers.IsDefaultOrEmpty) return Array.Empty<Diagnostic>();

        // A faulty analyzer must not take the compiler's own diagnostics down with it.
        var options = new CompilationWithAnalyzersOptions(new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false);
        var withAnalyzers = model.Compilation.WithAnalyzers(analyzers, options);
        try
        {
            var syntax = await withAnalyzers.GetAnalyzerSyntaxDiagnosticsAsync(model.SyntaxTree, ct).ConfigureAwait(false);
            var semantic = await withAnalyzers.GetAnalyzerSemanticDiagnosticsAsync(model, null, ct).ConfigureAwait(false);
            return syntax.Concat(semantic);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Array.Empty<Diagnostic>();
        }
    }

    public override Task<IReadOnlyList<CodeClassifiedSpan>> GetClassificationsAsync(int start, int length, CancellationToken ct)
        => Run<IReadOnlyList<CodeClassifiedSpan>>(Array.Empty<CodeClassifiedSpan>(), async (document, ct) =>
        {
            var text = await document.GetTextAsync(ct).ConfigureAwait(false);
            start = Math.Clamp(start, 0, text.Length);
            length = Math.Clamp(length, 0, text.Length - start);

            var spans = await Classifier.GetClassifiedSpansAsync(document, new TextSpan(start, length), ct).ConfigureAwait(false);
            var result = new List<CodeClassifiedSpan>();
            foreach (var span in spans)
            {
                if (span.TextSpan.IsEmpty || !RoslynMapping.TryClassification(span.ClassificationType, out var kind)) continue;
                var mapped = new CodeClassifiedSpan(span.TextSpan.Start, span.TextSpan.Length, kind);
                if (result.Count > 0 && result[^1].Start == mapped.Start && result[^1].Length == mapped.Length)
                    result[^1] = mapped;
                else
                    result.Add(mapped);
            }
            return result;
        }, ct);

    private static async Task<ISymbol?> FindSymbolAsync(Document document, int offset, CancellationToken ct)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset, ct).ConfigureAwait(false);
        if (symbol == null && offset > 0)
            symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset - 1, ct).ConfigureAwait(false);
        return symbol;
    }

    public override Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(int offset, CancellationToken ct)
        => Run<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>(), async (document, ct) =>
        {
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            if (symbol == null) return Array.Empty<CodeLocation>();

            var tree = (await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false))!;
            var definition = await SymbolFinder.FindSourceDefinitionAsync(symbol, document.Project.Solution, ct).ConfigureAwait(false) ?? symbol;
            var kind = RoslynMapping.SymbolKind(definition);
            string display = definition.ToDisplayString();

            var result = new List<CodeLocation>();
            foreach (var location in definition.Locations)
                if (location.IsInSource)
                    result.Add(ToLocation(document, tree, location, display, kind));

            if (result.Count == 0)
                result.Add(new CodeLocation { Display = display, Kind = kind });
            return result;
        }, ct);

    public override Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(int offset, CancellationToken ct)
        => Run<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>(), async (document, ct) =>
        {
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            if (symbol == null) return Array.Empty<CodeLocation>();

            var tree = (await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false))!;
            var solution = document.Project.Solution;
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
            var seen = new HashSet<(SyntaxTree?, TextSpan)>();
            var result = new List<CodeLocation>();
            void Add(Location location, ISymbol owner)
            {
                if (!location.IsInSource || !seen.Add((location.SourceTree, location.SourceSpan))) return;
                result.Add(ToLocation(document, tree, location, LineText(location), RoslynMapping.SymbolKind(owner)));
            }

            foreach (var referenced in references)
            {
                // A property's accessors come back as symbols of their own, declared at the get and set keywords.
                bool accessor = referenced.Definition is IMethodSymbol { AssociatedSymbol: not null };
                if (!accessor)
                    foreach (var location in referenced.Definition.Locations) Add(location, referenced.Definition);
                foreach (var reference in referenced.Locations) Add(reference.Location, referenced.Definition);
            }
            return result;
        }, ct);

    public override Task<IReadOnlyList<CodeClassifiedSpan>> GetHighlightsAsync(int offset, CancellationToken ct)
        => Run<IReadOnlyList<CodeClassifiedSpan>>(Array.Empty<CodeClassifiedSpan>(), async (document, ct) =>
        {
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            if (symbol == null) return Array.Empty<CodeClassifiedSpan>();

            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            var references = await SymbolFinder.FindReferencesAsync(symbol, document.Project.Solution,
                ImmutableHashSet.Create(document), ct).ConfigureAwait(false);

            var spans = new SortedSet<TextSpan>();
            foreach (var referenced in references)
            {
                foreach (var location in referenced.Definition.Locations)
                    if (location.SourceTree == tree) spans.Add(location.SourceSpan);
                foreach (var reference in referenced.Locations)
                    if (reference.Location.SourceTree == tree) spans.Add(reference.Location.SourceSpan);
            }
            return spans.Select(s => new CodeClassifiedSpan(s.Start, s.Length, CodeTokenKind.Identifier)).ToList();
        }, ct);

    public override Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(int start, int length, CancellationToken ct)
        => Run<IReadOnlyList<CodeAction>>(Array.Empty<CodeAction>(), async (document, ct) =>
        {
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (model == null) return Array.Empty<CodeAction>();

            var span = new TextSpan(start, Math.Max(0, length));
            var actions = new List<CodeAction>(AddImportProvider.GetActions(model, span, ct));
            var titles = new HashSet<string>(actions.Select(a => a.Title), StringComparer.Ordinal);
            foreach (var action in await RoslynCodeActions.GetAsync(document, span, ct).ConfigureAwait(false))
                if (titles.Add(action.Title)) actions.Add(action);
            return actions;
        }, ct);

    public override Task<IReadOnlyList<CodeTextEdit>> FormatAsync(int start, int length, CancellationToken ct)
        => Run<IReadOnlyList<CodeTextEdit>>(Array.Empty<CodeTextEdit>(), async (document, ct) =>
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null) return Array.Empty<CodeTextEdit>();

            var span = length < 0 ? root.FullSpan : TextSpan.FromBounds(Math.Clamp(start, 0, root.FullSpan.End), Math.Clamp(start + length, 0, root.FullSpan.End));
            return FormatSpan(document, root, span, ct);
        }, ct);

    public override Task<IReadOnlyList<CodeTextEdit>> FormatOnTypeAsync(int offset, char typed, CancellationToken ct)
        => Run<IReadOnlyList<CodeTextEdit>>(Array.Empty<CodeTextEdit>(), async (document, ct) =>
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null || offset <= 0 || offset > root.FullSpan.End) return Array.Empty<CodeTextEdit>();

            var token = root.FindToken(offset - 1);
            var kind = typed == ';' ? SyntaxKind.SemicolonToken : SyntaxKind.CloseBraceToken;
            if (!token.IsKind(kind) || token.IsMissing || token.Span.End != offset || token.Parent == null) return Array.Empty<CodeTextEdit>();

            // A '}' formats the whole statement or member its block belongs to, a ';' the statement it ends.
            var node = token.Parent;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax { Parent: { } owner and not Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax }) node = owner;
            return FormatSpan(document, root, TextSpan.FromBounds(node.FullSpan.Start, token.Span.End), ct);
        }, ct);

    public override Task<IReadOnlyList<CodeTextEdit>> OrganizeUsingsAsync(CancellationToken ct)
        => Run<IReadOnlyList<CodeTextEdit>>(Array.Empty<CodeTextEdit>(), async (document, ct) =>
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root == null || model == null) return Array.Empty<CodeTextEdit>();

            // CS8019 is the compiler's own "unnecessary using directive".
            var unused = model.GetDiagnostics(cancellationToken: ct)
                .Where(d => d.Id == "CS8019")
                .Select(d => root.FindNode(d.Location.SourceSpan))
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                .ToList();
            var cleaned = unused.Count > 0 ? root.RemoveNodes(unused, SyntaxRemoveOptions.KeepDirectives)! : root;

            var organized = await Formatter.OrganizeImportsAsync(document.WithSyntaxRoot(cleaned), ct).ConfigureAwait(false);
            var changes = await organized.GetTextChangesAsync(document, ct).ConfigureAwait(false);
            return changes.Select(c => new CodeTextEdit(c.Span.Start, c.Span.Length, c.NewText ?? "")).ToList();
        }, ct);

    private static IReadOnlyList<CodeTextEdit> FormatSpan(Document document, SyntaxNode root, TextSpan span, CancellationToken ct)
    {
        var workspace = document.Project.Solution.Workspace;
        var options = document.Project.Solution.Options.WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, "\n");
        var changes = Formatter.GetFormattedTextChanges(root, span, workspace, options, ct);
        return changes.Select(c => new CodeTextEdit(c.Span.Start, c.Span.Length, c.NewText ?? "")).ToList();
    }

    public override Task<CodeRenameResult?> RenameAsync(int offset, string newName, CancellationToken ct)
        => Run<CodeRenameResult?>(null, async (document, ct) =>
        {
            if (!IsValidIdentifier(newName)) return null;
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            if (symbol == null || !symbol.Locations.Any(l => l.IsInSource)) return null;

            var solution = document.Project.Solution;
            var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, ct).ConfigureAwait(false);

            var edits = new List<CodeTextEdit>();
            var others = new Dictionary<string, IReadOnlyList<CodeTextEdit>>();
            foreach (var projectChanges in renamed.GetChanges(solution).GetProjectChanges())
            {
                foreach (var id in projectChanges.GetChangedDocuments())
                {
                    var oldDocument = solution.GetDocument(id)!;
                    var newDocument = renamed.GetDocument(id)!;
                    var changes = await newDocument.GetTextChangesAsync(oldDocument, ct).ConfigureAwait(false);
                    var mapped = changes.Select(c => new CodeTextEdit(c.Span.Start, c.Span.Length, c.NewText ?? "")).ToList();
                    if (id == document.Id) edits.AddRange(mapped);
                    else others[oldDocument.FilePath ?? oldDocument.Name] = mapped;
                }
            }
            return new CodeRenameResult { Edits = edits, OtherDocuments = others };
        }, ct);

    private static bool IsValidIdentifier(string name)
    {
        if (name.StartsWith('@')) return SyntaxFacts.IsValidIdentifier(name[1..]);
        return SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
    }

    private static CodeLocation ToLocation(Document current, SyntaxTree currentTree, Location location, string display, CodeSymbolKind kind)
    {
        var tree = location.SourceTree!;
        var line = location.GetLineSpan().StartLinePosition;
        bool isCurrent = tree == currentTree;
        string? path = null;
        if (!isCurrent)
        {
            var other = current.Project.Solution.GetDocument(tree);
            path = other?.FilePath ?? other?.Name ?? tree.FilePath;
        }
        return new CodeLocation
        {
            IsCurrentDocument = isCurrent,
            Path = path,
            Start = location.SourceSpan.Start,
            Length = location.SourceSpan.Length,
            Line = line.Line,
            Column = line.Character,
            Display = display,
            Kind = kind,
        };
    }

    private static string LineText(Location location)
    {
        var text = location.SourceTree!.GetText();
        int line = location.GetLineSpan().StartLinePosition.Line;
        return text.Lines[line].ToString();
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace.CloseDocument(DocumentId);
    }
}
