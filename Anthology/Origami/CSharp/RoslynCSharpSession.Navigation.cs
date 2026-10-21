// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace Prowl.OrigamiUI.CSharp;

public sealed partial class RoslynCSharpSession
{
    public override Task<IReadOnlyList<CodeLocation>> GetImplementationsAsync(int offset, CancellationToken ct)
        => Run<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>(), async (document, ct) =>
        {
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            if (symbol == null) return Array.Empty<CodeLocation>();
            var solution = document.Project.Solution;

            var found = new List<ISymbol>();
            if (symbol is INamedTypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Interface)
                    found.AddRange(await SymbolFinder.FindImplementationsAsync(type, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false));
                else
                    found.AddRange(await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false));
            }
            else if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
            {
                found.AddRange(await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false));
                found.AddRange(await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false));
            }
            return await LocationsOfAsync(document, found, ct).ConfigureAwait(false);
        }, ct);

    public override Task<IReadOnlyList<CodeLocation>> GetBaseDefinitionsAsync(int offset, CancellationToken ct)
        => Run<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>(), async (document, ct) =>
        {
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            if (symbol == null) return Array.Empty<CodeLocation>();

            var found = new List<ISymbol>();
            if (symbol is INamedTypeSymbol type)
            {
                if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType) found.Add(baseType);
                found.AddRange(type.Interfaces);
            }
            else
            {
                ISymbol? overridden = symbol switch
                {
                    IMethodSymbol m => m.OverriddenMethod,
                    IPropertySymbol p => p.OverriddenProperty,
                    IEventSymbol e => e.OverriddenEvent,
                    _ => null,
                };
                if (overridden != null) found.Add(overridden);

                if (symbol.ContainingType is { } owner)
                {
                    foreach (var member in owner.AllInterfaces.SelectMany(i => i.GetMembers()))
                        if (SymbolEqualityComparer.Default.Equals(owner.FindImplementationForInterfaceMember(member), symbol))
                            found.Add(member);
                }
            }
            return await LocationsOfAsync(document, found, ct).ConfigureAwait(false);
        }, ct);

    public override Task<IReadOnlyList<CodeLocation>> GetTypeDefinitionAsync(int offset, CancellationToken ct)
        => Run<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>(), async (document, ct) =>
        {
            var symbol = await FindSymbolAsync(document, offset, ct).ConfigureAwait(false);
            ITypeSymbol? type = symbol switch
            {
                ILocalSymbol l => l.Type,
                IParameterSymbol p => p.Type,
                IFieldSymbol f => f.Type,
                IPropertySymbol p => p.Type,
                IEventSymbol e => e.Type,
                IMethodSymbol m => m.ReturnType,
                ITypeSymbol t => t,
                _ => null,
            };
            while (type is IArrayTypeSymbol array) type = array.ElementType;
            if (type is IPointerTypeSymbol pointer) type = pointer.PointedAtType;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) type = nullable.TypeArguments[0];
            if (type == null || type.TypeKind == TypeKind.Error) return Array.Empty<CodeLocation>();

            return await LocationsOfAsync(document, new[] { type.OriginalDefinition }, ct).ConfigureAwait(false);
        }, ct);

    // Source declarations of the symbols, or a name only entry for each one that lives in an assembly.
    private static async Task<IReadOnlyList<CodeLocation>> LocationsOfAsync(Document document, IEnumerable<ISymbol> symbols, CancellationToken ct)
    {
        var tree = (await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false))!;
        var result = new List<CodeLocation>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var symbol in symbols)
        {
            if (!seen.Add(symbol)) continue;
            var kind = RoslynMapping.SymbolKind(symbol);
            string display = symbol.ToDisplayString();
            bool any = false;
            foreach (var location in symbol.Locations)
            {
                if (!location.IsInSource) continue;
                result.Add(ToLocation(document, tree, location, display, kind));
                any = true;
            }
            if (!any) result.Add(new CodeLocation { Display = display, Kind = kind });
        }
        return result;
    }

    private const int MaxCodeLenses = 300;

    public override Task<IReadOnlyList<CodeLens>> GetCodeLensesAsync(int start, int length, CancellationToken ct)
        => Run<IReadOnlyList<CodeLens>>(Array.Empty<CodeLens>(), async (document, ct) =>
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root == null || model == null) return Array.Empty<CodeLens>();

            var span = new Microsoft.CodeAnalysis.Text.TextSpan(Math.Max(0, start), Math.Max(0, length));
            var solution = document.Project.Solution;
            var lenses = new List<CodeLens>();
            foreach (var node in root.DescendantNodes(span))
            {
                if (lenses.Count >= MaxCodeLenses) break;
                SyntaxToken name = node switch
                {
                    BaseTypeDeclarationSyntax t => t.Identifier,
                    DelegateDeclarationSyntax d => d.Identifier,
                    MethodDeclarationSyntax m => m.Identifier,
                    ConstructorDeclarationSyntax c => c.Identifier,
                    PropertyDeclarationSyntax p => p.Identifier,
                    EventDeclarationSyntax e => e.Identifier,
                    _ => default,
                };
                if (name == default || !span.IntersectsWith(name.Span)) continue;

                var symbol = model.GetDeclaredSymbol(node, ct);
                if (symbol == null) continue;

                var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                int count = references.Sum(r => r.Locations.Count());
                lenses.Add(new CodeLens { Offset = name.SpanStart, Text = count == 1 ? "1 reference" : $"{count} references" });
            }
            return lenses;
        }, ct);
}
