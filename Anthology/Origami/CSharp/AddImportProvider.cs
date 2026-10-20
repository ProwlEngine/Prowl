// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>"using" and fully qualify fixes for names that do not resolve.</summary>
internal static class AddImportProvider
{
    private sealed class NameIndex
    {
        public readonly Dictionary<string, List<(string Namespace, int Arity)>> Types = new();

        // Extension method name to the metadata names of the static classes declaring it.
        public readonly Dictionary<string, List<string>> Extensions = new();
    }

    private static readonly ConditionalWeakTable<MetadataReference, NameIndex> s_referenceIndex = new();

    public static IReadOnlyList<CodeAction> GetActions(SemanticModel model, TextSpan span, CancellationToken ct)
    {
        var root = (CompilationUnitSyntax)model.SyntaxTree.GetRoot(ct);
        var text = model.SyntaxTree.GetText(ct);
        var actions = new List<CodeAction>();
        var titles = new HashSet<string>();
        List<NameIndex>? indexes = null;

        foreach (var diagnostic in model.GetDiagnostics(cancellationToken: ct))
        {
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            if (!diagnosticSpan.IntersectsWith(span)) continue;
            if (diagnostic.Id is not ("CS0246" or "CS0103" or "CS1061")) continue;

            indexes ??= BuildIndexes(model.Compilation, ct);
            var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

            if (diagnostic.Id == "CS1061")
            {
                if (node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>() is not { } access) continue;
                foreach (string ns in ExtensionNamespaces(model, indexes, access, ct))
                    AddUsing(actions, titles, root, text, ns);
                continue;
            }

            if (node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().FirstOrDefault() is not { } name) continue;
            string identifier = name.Identifier.ValueText;
            int arity = name is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count : 0;
            bool inAttribute = name.FirstAncestorOrSelf<AttributeSyntax>()?.Name.Span.Contains(name.Span) == true;

            var namespaces = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var index in indexes)
            {
                Collect(index, identifier, arity, namespaces);
                if (inAttribute) Collect(index, identifier + "Attribute", arity, namespaces);
            }

            foreach (string ns in namespaces) AddUsing(actions, titles, root, text, ns);
            foreach (string ns in namespaces)
            {
                string title = ns + "." + name;
                if (titles.Add(title))
                    actions.Add(new CodeAction { Title = title, IsQuickFix = true, Edits = new[] { new CodeTextEdit(name.SpanStart, name.Span.Length, title) } });
            }
        }
        return actions;
    }

    private static void Collect(NameIndex index, string name, int arity, SortedSet<string> namespaces)
    {
        if (!index.Types.TryGetValue(name, out var entries)) return;
        foreach (var (ns, typeArity) in entries)
            if (typeArity == arity) namespaces.Add(ns);
    }

    private static IEnumerable<string> ExtensionNamespaces(SemanticModel model, List<NameIndex> indexes, MemberAccessExpressionSyntax access, CancellationToken ct)
    {
        string methodName = access.Name.Identifier.ValueText;
        if (model.GetTypeInfo(access.Expression, ct).Type is not { } receiver || receiver.TypeKind == TypeKind.Error) yield break;

        var seen = new HashSet<string>();
        foreach (var index in indexes)
        {
            if (!index.Extensions.TryGetValue(methodName, out var containers)) continue;
            foreach (string container in containers)
            {
                if (model.Compilation.GetTypeByMetadataName(container) is not { } type) continue;
                bool applies = type.GetMembers(methodName).OfType<IMethodSymbol>()
                    .Any(m => m.IsExtensionMethod && m.ReduceExtensionMethod(receiver) != null);
                string ns = type.ContainingNamespace.ToDisplayString();
                if (applies && seen.Add(ns)) yield return ns;
            }
        }
    }

    private static List<NameIndex> BuildIndexes(Compilation compilation, CancellationToken ct)
    {
        var indexes = new List<NameIndex>();
        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                indexes.Add(s_referenceIndex.GetValue(reference, _ => Build(assembly.GlobalNamespace, null)));
        }
        indexes.Add(Build(compilation.Assembly.GlobalNamespace, compilation.Assembly));
        return indexes;
    }

    // Public types only for references, everything accessible for the source assembly.
    private static NameIndex Build(INamespaceSymbol global, IAssemblySymbol? source)
    {
        var index = new NameIndex();
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(global);
        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            foreach (var child in ns.GetNamespaceMembers()) stack.Push(child);
            if (ns.IsGlobalNamespace) continue;

            string nsName = ns.ToDisplayString();
            foreach (var type in ns.GetTypeMembers())
            {
                bool visible = source != null ? type.DeclaredAccessibility != Accessibility.Private : type.DeclaredAccessibility == Accessibility.Public;
                if (!visible) continue;

                if (!index.Types.TryGetValue(type.Name, out var list))
                    index.Types[type.Name] = list = new List<(string, int)>();
                list.Add((nsName, type.Arity));

                if (!type.MightContainExtensionMethods) continue;
                string metadataName = nsName + "." + type.MetadataName;
                foreach (var member in type.GetMembers())
                {
                    if (member is not IMethodSymbol { IsExtensionMethod: true } method) continue;
                    if (source == null && method.DeclaredAccessibility != Accessibility.Public) continue;
                    if (!index.Extensions.TryGetValue(method.Name, out var containers))
                        index.Extensions[method.Name] = containers = new List<string>();
                    if (!containers.Contains(metadataName)) containers.Add(metadataName);
                }
            }
        }
        return index;
    }

    private static void AddUsing(List<CodeAction> actions, HashSet<string> titles, CompilationUnitSyntax root, SourceText text, string ns)
    {
        string title = $"using {ns};";
        if (!titles.Add(title)) return;
        if (root.Usings.Any(u => u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.Name?.ToString() == ns)) return;
        actions.Add(new CodeAction { Title = title, IsQuickFix = true, Edits = new[] { UsingEdit(root, text, ns) } });
    }

    private static CodeTextEdit UsingEdit(CompilationUnitSyntax root, SourceText text, string ns)
    {
        string directive = $"using {ns};";
        var usings = root.Usings;
        if (usings.Count == 0)
        {
            var first = root.GetFirstToken(includeZeroWidth: true);
            int headerEnd = -1;
            foreach (var trivia in first.LeadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) headerEnd = trivia.Span.End;
                else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)) break;
            }
            if (headerEnd >= 0) return new CodeTextEdit(text.Lines.GetLineFromPosition(headerEnd).End, 0, "\n\n" + directive);
            return new CodeTextEdit(0, 0, first.IsKind(SyntaxKind.EndOfFileToken) ? directive + "\n" : directive + "\n\n");
        }

        foreach (var existing in usings)
        {
            if (existing.Alias != null || !existing.StaticKeyword.IsKind(SyntaxKind.None) || existing.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)) continue;
            if (CompareNamespaces(ns, existing.Name?.ToString() ?? "") < 0)
                return new CodeTextEdit(text.Lines.GetLineFromPosition(existing.SpanStart).Start, 0, directive + "\n");
        }
        int end = text.Lines.GetLineFromPosition(usings[^1].Span.End).End;
        return new CodeTextEdit(end, 0, "\n" + directive);
    }

    private static int CompareNamespaces(string a, string b)
    {
        bool aSystem = a == "System" || a.StartsWith("System.", StringComparison.Ordinal);
        bool bSystem = b == "System" || b.StartsWith("System.", StringComparison.Ordinal);
        if (aSystem != bSystem) return aSystem ? -1 : 1;
        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }
}
