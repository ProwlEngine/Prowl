// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>Signature help from the semantic model, for invocations, object creations, indexers, attributes and type arguments.</summary>
internal static class SignatureHelpBuilder
{
    private static readonly SymbolDisplayFormat s_typeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat s_parameterFormat = s_typeFormat.WithParameterOptions(
        SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName
        | SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut
        | SymbolDisplayParameterOptions.IncludeOptionalBrackets);

    private readonly record struct ArgumentList(SyntaxNode Node, SyntaxToken Open, int ArgumentCount, int ActiveIndex, string? ActiveName);

    public static CodeSignatureHelp? Compute(SemanticModel model, int position, CancellationToken ct)
    {
        var root = model.SyntaxTree.GetRoot(ct);
        var token = root.FindToken(Math.Min(position, root.FullSpan.End));
        if (token.Parent == null) return null;

        foreach (var node in token.Parent.AncestorsAndSelf())
        {
            ct.ThrowIfCancellationRequested();
            if (FindList(node, position) is not { } list) continue;

            var (candidates, resolved) = Candidates(model, list.Node, position, ct);
            if (candidates.Count == 0) continue;
            return Build(model, candidates, resolved, list, ct);
        }
        return null;
    }

    private static ArgumentList? FindList(SyntaxNode node, int position)
    {
        switch (node)
        {
            case ArgumentListSyntax a when Inside(a.OpenParenToken, a.CloseParenToken, position):
                return Describe(a, a.OpenParenToken, a.Arguments.GetSeparators(), a.Arguments.Count, position,
                    i => a.Arguments[i].NameColon?.Name.Identifier.ValueText);
            case BracketedArgumentListSyntax b when Inside(b.OpenBracketToken, b.CloseBracketToken, position):
                return Describe(b, b.OpenBracketToken, b.Arguments.GetSeparators(), b.Arguments.Count, position,
                    i => b.Arguments[i].NameColon?.Name.Identifier.ValueText);
            case AttributeArgumentListSyntax at when Inside(at.OpenParenToken, at.CloseParenToken, position):
                return Describe(at, at.OpenParenToken, at.Arguments.GetSeparators(), at.Arguments.Count, position,
                    i => at.Arguments[i].NameColon?.Name.Identifier.ValueText);
            case TypeArgumentListSyntax t when Inside(t.LessThanToken, t.GreaterThanToken, position):
                return Describe(t, t.LessThanToken, t.Arguments.GetSeparators(), t.Arguments.Count, position, _ => null);
            default:
                return null;
        }
    }

    private static bool Inside(SyntaxToken open, SyntaxToken close, int position)
        => !open.IsMissing && position >= open.Span.End && (close.IsMissing || position <= close.SpanStart);

    private static ArgumentList Describe(SyntaxNode node, SyntaxToken open, IEnumerable<SyntaxToken> separators, int count, int position, Func<int, string?> nameAt)
    {
        var commas = separators.ToList();
        int index = commas.Count(c => c.SpanStart < position);
        int argumentCount = Math.Max(count, commas.Count + 1);
        string? name = index < count ? nameAt(index) : null;
        return new ArgumentList(node, open, argumentCount, index, name);
    }

    private static (List<ISymbol> Candidates, ISymbol? Resolved) Candidates(SemanticModel model, SyntaxNode list, int position, CancellationToken ct)
    {
        var parent = list.Parent;
        var symbols = new List<ISymbol>();
        ISymbol? resolved = null;

        switch (parent)
        {
            case InvocationExpressionSyntax invocation:
                symbols.AddRange(model.GetMemberGroup(invocation.Expression, ct).OfType<IMethodSymbol>());
                var info = model.GetSymbolInfo(invocation, ct);
                resolved = info.Symbol;
                if (symbols.Count == 0)
                {
                    var target = model.GetSymbolInfo(invocation.Expression, ct).Symbol;
                    if (TypeOf(target) is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }) symbols.Add(invoke);
                    else symbols.AddRange(info.CandidateSymbols);
                }
                break;

            case BaseObjectCreationExpressionSyntax creation:
                resolved = model.GetSymbolInfo(creation, ct).Symbol;
                if (model.GetTypeInfo(creation, ct).Type is INamedTypeSymbol created)
                    symbols.AddRange(Constructors(model, created, position));
                break;

            case ConstructorInitializerSyntax initializer:
                resolved = model.GetSymbolInfo(initializer, ct).Symbol;
                if (initializer.Parent is ConstructorDeclarationSyntax ctor && model.GetDeclaredSymbol(ctor, ct)?.ContainingType is { } self)
                {
                    var type = initializer.IsKind(SyntaxKind.ThisConstructorInitializer) ? self : self.BaseType;
                    if (type != null) symbols.AddRange(Constructors(model, type, position));
                }
                break;

            case PrimaryConstructorBaseTypeSyntax primary:
                resolved = model.GetSymbolInfo(primary, ct).Symbol;
                if (model.GetTypeInfo(primary.Type, ct).Type is INamedTypeSymbol baseType)
                    symbols.AddRange(Constructors(model, baseType, position));
                break;

            case ElementAccessExpressionSyntax access:
                resolved = model.GetSymbolInfo(access, ct).Symbol;
                for (var type = model.GetTypeInfo(access.Expression, ct).Type; type != null; type = type.BaseType)
                    symbols.AddRange(type.GetMembers().OfType<IPropertySymbol>().Where(p => p.IsIndexer && model.IsAccessible(position, p)));
                break;

            case AttributeSyntax attribute:
                resolved = model.GetSymbolInfo(attribute, ct).Symbol;
                if (model.GetTypeInfo(attribute, ct).Type is INamedTypeSymbol attributeType)
                    symbols.AddRange(Constructors(model, attributeType, position));
                break;

            case GenericNameSyntax generic:
                var genericInfo = model.GetSymbolInfo(generic, ct);
                resolved = genericInfo.Symbol;
                if (genericInfo.Symbol != null) symbols.Add(genericInfo.Symbol);
                symbols.AddRange(genericInfo.CandidateSymbols);
                if (symbols.Count == 0)
                    symbols.AddRange(model.LookupSymbols(generic.SpanStart, name: generic.Identifier.ValueText));
                symbols.RemoveAll(s => TypeParameters(s).Count == 0);
                break;
        }

        var distinct = symbols.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(ParameterCount)
            .ToList();
        return (distinct, resolved);
    }

    private static IEnumerable<IMethodSymbol> Constructors(SemanticModel model, INamedTypeSymbol type, int position)
        => type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length == 0).Where(c => model.IsAccessible(position, c));

    private static ITypeSymbol? TypeOf(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol l => l.Type,
        IParameterSymbol p => p.Type,
        IFieldSymbol f => f.Type,
        IPropertySymbol p => p.Type,
        IEventSymbol e => e.Type,
        _ => null,
    };

    private static IReadOnlyList<ISymbol> Parameters(ISymbol symbol) => symbol switch
    {
        IMethodSymbol m => m.Parameters,
        IPropertySymbol p => p.Parameters,
        _ => Array.Empty<ISymbol>(),
    };

    private static IReadOnlyList<ITypeParameterSymbol> TypeParameters(ISymbol symbol) => symbol switch
    {
        IMethodSymbol m => m.TypeParameters,
        INamedTypeSymbol t => t.TypeParameters,
        _ => Array.Empty<ITypeParameterSymbol>(),
    };

    private static int ParameterCount(ISymbol symbol) => Parameters(symbol).Count;

    private static CodeSignatureHelp Build(SemanticModel model, List<ISymbol> candidates, ISymbol? resolved, ArgumentList list, CancellationToken ct)
    {
        bool typeArguments = list.Node is TypeArgumentListSyntax;
        if (Arguments(list.Node).Any(a => a.Expression.IsMissing)) resolved = null;
        int active = resolved == null ? -1 : candidates.FindIndex(c => SymbolEqualityComparer.Default.Equals(c.OriginalDefinition, resolved.OriginalDefinition));
        if (active < 0 && typeArguments)
            active = candidates.FindIndex(c => TypeParameters(c).Count >= list.ArgumentCount);
        if (active < 0)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                int score = Score(model, candidates[i], list, ct);
                if (score > best) (best, active) = (score, i);
            }
        }
        if (active < 0) active = 0;

        var signatures = candidates.Select(c => BuildSignature(c, typeArguments, ct)).ToList();
        return new CodeSignatureHelp
        {
            Signatures = signatures,
            ActiveSignature = active,
            ActiveParameter = ActiveParameter(signatures[active], candidates[active], list, typeArguments),
            ArgumentListStart = list.Open.SpanStart,
        };
    }

    private static IEnumerable<(string? Name, ExpressionSyntax Expression)> Arguments(SyntaxNode list) => list switch
    {
        BaseArgumentListSyntax b => b.Arguments.Select(a => (a.NameColon?.Name.Identifier.ValueText, a.Expression)),
        AttributeArgumentListSyntax at => at.Arguments.Where(a => a.NameEquals == null).Select(a => (a.NameColon?.Name.Identifier.ValueText, a.Expression)),
        _ => Enumerable.Empty<(string?, ExpressionSyntax)>(),
    };

    // How well the typed arguments fit a candidate: two per identity conversion, one per implicit one, negative when it cannot apply.
    private static int Score(SemanticModel model, ISymbol candidate, ArgumentList list, CancellationToken ct)
    {
        var parameters = Parameters(candidate).Cast<IParameterSymbol>().ToList();
        bool isParams = IsParams(candidate);
        if (parameters.Count < list.ArgumentCount && !isParams) return -1;

        int score = 0, index = 0;
        foreach (var (name, expression) in Arguments(list.Node))
        {
            ct.ThrowIfCancellationRequested();
            var parameter = name != null ? parameters.FirstOrDefault(p => p.Name == name)
                : index < parameters.Count ? parameters[index]
                : isParams ? parameters[^1] : null;
            index++;
            if (parameter == null) return -1;
            if (expression.IsMissing) continue;

            var type = parameter.IsParams && parameter.Type is IArrayTypeSymbol array && index > parameters.Count ? array.ElementType : parameter.Type;
            var conversion = model.ClassifyConversion(expression, type);
            if (conversion.IsIdentity) score += 2;
            else if (conversion.IsImplicit) score += 1;
        }
        return score;
    }

    private static bool IsParams(ISymbol symbol)
        => Parameters(symbol) is { Count: > 0 } p && p[^1] is IParameterSymbol { IsParams: true };

    private static int ActiveParameter(CodeSignature signature, ISymbol symbol, ArgumentList list, bool typeArguments)
    {
        if (list.ActiveName != null)
        {
            int named = signature.Parameters.ToList().FindIndex(p => p.Name == list.ActiveName);
            if (named >= 0) return named;
        }
        int count = signature.Parameters.Count;
        if (!typeArguments && IsParams(symbol) && list.ActiveIndex >= count) return count - 1;
        return list.ActiveIndex;
    }

    private static CodeSignature BuildSignature(ISymbol symbol, bool typeArguments, CancellationToken ct)
    {
        var parts = new List<CodeTaggedText>();
        var parameters = new List<CodeSignatureParameter>();
        int length = 0;

        void Add(string text, CodeTokenKind kind)
        {
            parts.Add(new CodeTaggedText(text, kind));
            length += text.Length;
        }
        void AddDisplay(ISymbol displayed, SymbolDisplayFormat format)
        {
            foreach (var part in displayed.ToDisplayParts(format)) Add(part.ToString(), RoslynMapping.DisplayPart(part.Kind));
        }

        var docs = Documentation.Parse(symbol, ct);

        if (typeArguments)
        {
            if (symbol is IMethodSymbol genericMethod)
            {
                AddDisplay(genericMethod.ContainingType, s_typeFormat);
                Add(".", CodeTokenKind.Punctuation);
            }
            Add(symbol.Name, symbol is INamedTypeSymbol named ? RoslynMapping.SymbolKind(named) switch
            {
                CodeSymbolKind.Struct => CodeTokenKind.Struct,
                CodeSymbolKind.Interface => CodeTokenKind.Interface,
                CodeSymbolKind.Delegate => CodeTokenKind.Delegate,
                CodeSymbolKind.Record => CodeTokenKind.Record,
                _ => CodeTokenKind.Class,
            } : CodeTokenKind.Method);
            Add("<", CodeTokenKind.Punctuation);
            var typeParameters = TypeParameters(symbol);
            for (int i = 0; i < typeParameters.Count; i++)
            {
                if (i > 0) Add(", ", CodeTokenKind.Punctuation);
                int start = length;
                Add(typeParameters[i].Name, CodeTokenKind.TypeParameter);
                parameters.Add(new CodeSignatureParameter
                {
                    Name = typeParameters[i].Name,
                    Documentation = docs.TypeParameter(typeParameters[i].Name),
                    LabelStart = start,
                    LabelLength = length - start,
                });
            }
            Add(">", CodeTokenKind.Punctuation);
            return new CodeSignature { Parts = parts, Parameters = parameters, Documentation = docs.Summary };
        }

        bool indexer = symbol is IPropertySymbol { IsIndexer: true };
        switch (symbol)
        {
            case IMethodSymbol { MethodKind: MethodKind.Constructor } ctor:
                AddDisplay(ctor.ContainingType, s_typeFormat);
                break;
            case IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } invoke:
                AddDisplay(invoke.ReturnType, s_typeFormat);
                Add(" ", CodeTokenKind.Plain);
                AddDisplay(invoke.ContainingType, s_typeFormat);
                break;
            case IMethodSymbol method:
                AddDisplay(method.ReturnType, s_typeFormat);
                Add(" ", CodeTokenKind.Plain);
                AddDisplay(method.ContainingType, s_typeFormat);
                Add(".", CodeTokenKind.Punctuation);
                Add(method.Name, method.IsExtensionMethod ? CodeTokenKind.ExtensionMethod : CodeTokenKind.Method);
                if (method.TypeParameters.Length > 0)
                    Add("<" + string.Join(", ", method.TypeParameters.Select(t => t.Name)) + ">", CodeTokenKind.TypeParameter);
                break;
            case IPropertySymbol property:
                AddDisplay(property.Type, s_typeFormat);
                Add(" ", CodeTokenKind.Plain);
                Add("this", CodeTokenKind.Keyword);
                break;
        }

        Add(indexer ? "[" : "(", CodeTokenKind.Punctuation);
        var symbolParameters = Parameters(symbol);
        for (int i = 0; i < symbolParameters.Count; i++)
        {
            if (i > 0) Add(", ", CodeTokenKind.Punctuation);
            var parameter = (IParameterSymbol)symbolParameters[i];
            int start = length;
            AddDisplay(parameter, s_parameterFormat);
            parameters.Add(new CodeSignatureParameter
            {
                Name = parameter.Name,
                Documentation = docs.Parameter(parameter.Name),
                LabelStart = start,
                LabelLength = length - start,
            });
        }
        Add(indexer ? "]" : ")", CodeTokenKind.Punctuation);
        return new CodeSignature { Parts = parts, Parameters = parameters, Documentation = docs.Summary };
    }

    /// <summary>The summary, param and typeparam text of a symbol's xml doc comment.</summary>
    internal sealed class Documentation
    {
        private static readonly Documentation s_empty = new(null);
        private readonly XElement? _root;

        private Documentation(XElement? root) => _root = root;

        public string? Summary => Text(_root?.Element("summary"));

        public string? Parameter(string name) => Text(_root?.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == name));

        public string? TypeParameter(string name) => Text(_root?.Elements("typeparam").FirstOrDefault(e => (string?)e.Attribute("name") == name));

        public static Documentation Parse(ISymbol symbol, CancellationToken ct)
        {
            var source = symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol.OriginalDefinition;
            string? xml = source.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(xml)) return s_empty;
            try
            {
                var root = XElement.Parse(xml.TrimStart().StartsWith("<member", StringComparison.Ordinal) ? xml : "<member>" + xml + "</member>");
                return new Documentation(root);
            }
            catch (System.Xml.XmlException)
            {
                return s_empty;
            }
        }

        public static string? Text(XElement? element)
        {
            if (element == null) return null;
            var sb = new StringBuilder();
            Append(element, sb);
            string text = string.Join(" ", sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length == 0 ? null : text;
        }

        private static void Append(XElement element, StringBuilder sb)
        {
            foreach (var node in element.Nodes())
            {
                if (node is XText text) sb.Append(text.Value);
                else if (node is XElement child)
                {
                    string? reference = (string?)child.Attribute("cref") ?? (string?)child.Attribute("name") ?? (string?)child.Attribute("langword");
                    if (child.Name.LocalName is "see" or "seealso" or "paramref" or "typeparamref" && reference != null && !child.Nodes().Any())
                        sb.Append(CrefName(reference));
                    else
                        Append(child, sb);
                }
            }
        }

        private static string CrefName(string cref)
        {
            if (cref.Length > 2 && cref[1] == ':') cref = cref[2..];
            int paren = cref.IndexOf('(');
            if (paren >= 0) cref = cref[..paren];
            int dot = cref.LastIndexOf('.');
            return dot >= 0 ? cref[(dot + 1)..] : cref;
        }
    }
}
